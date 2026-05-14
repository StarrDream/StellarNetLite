using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Transports.Common;
using UnityEngine;

namespace StellarNet.Lite.Transports.TCP
{
    /// <summary>
    /// 基于 TcpClient/TcpListener 的 TCP 传输层实现。
    /// </summary>
    [DisallowMultipleComponent]
    public class TcpTransportProvider : MonoBehaviour, INetworkTransport, IServerTransportPump
    {
        public event Action OnServerStartedEvent;
        public event Action OnServerStoppedEvent;
        public event Action<int> OnServerClientConnectedEvent;
        public event Action<int> OnServerClientDisconnectedEvent;
        public event Action<int, Packet> OnServerReceivePacketEvent;

        public event Action OnClientStartedEvent;
        public event Action OnClientStoppedEvent;
        public event Action OnClientConnectedEvent;
        public event Action OnClientDisconnectedEvent;
        public event Action<Packet> OnClientReceivePacketEvent;

        private NetConfig _appConfig;
        private const int ClientConnectTimeoutMs = 3000;
        private const int SendQueueWarnFrameCount = 1024;
        private const int SendQueueCriticalFrameCount = 4096;
        private const long SendQueueWarnBytes = 2L * 1024L * 1024L;
        private const long SendQueueCriticalBytes = 8L * 1024L * 1024L;
        private long _serverTcpTotalPackets;
        private long _serverTcpDeserializeFailures;
        private long _serverTcpDisconnects;
        private long _serverTcpSendQueueAborts;
        private long _serverTcpWriteFailures;
        private long _clientTcpTotalPackets;
        private long _clientTcpDeserializeFailures;
        private long _clientTcpDisconnects;
        private long _clientTcpConnectFailures;
        private long _clientTcpSendQueueAborts;
        private long _clientTcpWriteFailures;
        private TcpListener _serverListener;
        private CancellationTokenSource _serverCts;
        private int _connectionIdCounter = 0;

        /// <summary>
        /// 服务端侧的 TCP 连接包装。
        /// </summary>
        private class TcpConnection
        {
            public int Id;
            public TcpClient Client;
            public TcpSendQueue SendQueue;
        }

        /// <summary>
        /// 单条 TCP 连接的发送上下文。
        /// 通过单发送循环串行写流，避免高负载下为每个包都创建独立异步任务。
        /// </summary>
        private sealed class TcpSendQueue
        {
            public int OwnerConnectionId;
            public bool IsServerSide;
            public NetworkStream Stream;
            public readonly ConcurrentQueue<PendingSendFrame> PendingFrames = new ConcurrentQueue<PendingSendFrame>();
            public int SendLoopRunning;
            public int PendingFrameCount;
            public long PendingBytes;
            public long TotalQueuedFrames;
            public long TotalSentFrames;
            public long SendFailures;
            public int WarningIssued;
            public int AbortIssued;
        }

        /// <summary>
        /// 已完成序列化、等待写入底层流的独立帧。
        /// </summary>
        private sealed class PendingSendFrame
        {
            public byte[] Buffer;
            public int Length;
        }

        private enum ServerEventKind
        {
            Connected,
            Disconnected,
            Packet
        }

        private sealed class ServerEvent
        {
            public ServerEventKind Kind;
            public int ConnectionId;
            public Packet Packet;
        }

        private readonly ConcurrentDictionary<int, TcpConnection> _serverConnections = new ConcurrentDictionary<int, TcpConnection>();
        private readonly ConcurrentQueue<ServerEvent> _serverEvents = new ConcurrentQueue<ServerEvent>();

        private TcpClient _client;
        private NetworkStream _clientStream;
        private CancellationTokenSource _clientCts;
        private TcpSendQueue _clientSendQueue;
        private int _clientConnectAttemptId;

        private bool _isServerActive;
        private bool _isClientActive;
        private bool _isClientConnecting;
        private bool _isPhysicalConnected;

        public void ApplyConfig(NetConfig config)
        {
            _appConfig = config;
        }

        private void Awake()
        {
            UnityPlayerLoopDispatcher.EnsureInstalled();
        }

        private void OnDestroy()
        {
            StopServer();
            StopClient();
        }

        #region 服务端

        public void PumpServer()
        {
            while (_serverEvents.TryDequeue(out ServerEvent serverEvent))
            {
                switch (serverEvent.Kind)
                {
                    case ServerEventKind.Connected:
                        OnServerClientConnectedEvent?.Invoke(serverEvent.ConnectionId);
                        break;
                    case ServerEventKind.Disconnected:
                        OnServerClientDisconnectedEvent?.Invoke(serverEvent.ConnectionId);
                        break;
                    case ServerEventKind.Packet:
                        OnServerReceivePacketEvent?.Invoke(serverEvent.ConnectionId, serverEvent.Packet);
                        break;
                }
            }
        }

        public void StartServer()
        {
            if (_isServerActive) return;
            if (_appConfig == null) return;

            try
            {
                DrainServerEvents();
                _serverListener = new TcpListener(IPAddress.Any, _appConfig.Port);
                _serverListener.Start();
                _serverCts = new CancellationTokenSource();
                _isServerActive = true;

                _ = AcceptClientsAsync(_serverCts.Token);

                NetLogger.LogInfo("TcpTransportProvider", $"TCP 服务端已启动，监听端口: {_appConfig.Port}");
                NetLogger.LogInfo("TcpTransportProvider", $"TCP runtime check. ServerPump:Enabled, AsyncIO:Background, NoDelay:True, SendQueueWarn:{SendQueueWarnFrameCount}/{SendQueueWarnBytes}B, SendQueueCritical:{SendQueueCriticalFrameCount}/{SendQueueCriticalBytes}B");
                OnServerStartedEvent?.Invoke();
            }
            catch (Exception ex)
            {
                NetLogger.LogError("TcpTransportProvider", $"TCP 服务端启动失败: {ex.Message}");
            }
        }

        public void StopServer()
        {
            if (!_isServerActive) return;
            _isServerActive = false;

            _serverCts?.Cancel();
            _serverListener?.Stop();
            _serverListener = null;

            foreach (var kvp in _serverConnections)
            {
                DisposeServerConnection(kvp.Value);
            }

            _serverConnections.Clear();
            DrainServerEvents();

            NetLogger.LogInfo("TcpTransportProvider", "TCP 服务端已停止");
            NetLogger.LogInfo("TcpTransportProvider", $"TCP server metrics. Recv:{Interlocked.Read(ref _serverTcpTotalPackets)}, DecodeFail:{Interlocked.Read(ref _serverTcpDeserializeFailures)}, Disconnects:{Interlocked.Read(ref _serverTcpDisconnects)}, WriteFailures:{Interlocked.Read(ref _serverTcpWriteFailures)}, QueueAborts:{Interlocked.Read(ref _serverTcpSendQueueAborts)}");
            OnServerStoppedEvent?.Invoke();
        }

        private async Task AcceptClientsAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    TcpClient client = await _serverListener.AcceptTcpClientAsync().ConfigureAwait(false);
                    client.NoDelay = true;

                    int connId = Interlocked.Increment(ref _connectionIdCounter);
                    NetworkStream stream = client.GetStream();
                    var connection = new TcpConnection
                    {
                        Id = connId,
                        Client = client,
                        SendQueue = new TcpSendQueue
                        {
                            OwnerConnectionId = connId,
                            IsServerSide = true,
                            Stream = stream
                        }
                    };
                    _serverConnections.TryAdd(connId, connection);

                    EnqueueServerConnected(connId);

                    _ = ReceiveDataAsync(connection, stream, token);
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                NetLogger.LogError("TcpTransportProvider", $"接受客户端异常: {ex.Message}");
            }
        }

        private async Task ReceiveDataAsync(TcpConnection connection, NetworkStream stream, CancellationToken token)
        {
            byte[] headerBuffer = new byte[4];
            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (!await ReadExactAsync(stream, headerBuffer, 4, token).ConfigureAwait(false)) break;
                    int length = BitConverter.ToInt32(headerBuffer, 0);

                    if (length <= 0 || length > 1024 * 1024 * 10) break;

                    byte[] payloadBuffer = ArrayPool<byte>.Shared.Rent(length);
                    try
                    {
                        if (!await ReadExactAsync(stream, payloadBuffer, length, token).ConfigureAwait(false)) break;

                        Interlocked.Increment(ref _serverTcpTotalPackets);
                        if (LitePacketFormatter.TryDeserialize(payloadBuffer, 0, length, out Packet packet))
                        {
                            byte[] safePayload = new byte[packet.PayloadLength];
                            Buffer.BlockCopy(packet.Payload, packet.PayloadOffset, safePayload, 0, packet.PayloadLength);
                            Packet safePacket = new Packet(packet.Seq, packet.MsgId, packet.Scope, packet.RoomId, safePayload, packet.PayloadLength);

                            EnqueueServerPacket(connection.Id, safePacket);
                        }
                        else
                        {
                            Interlocked.Increment(ref _serverTcpDeserializeFailures);
                            NetLogger.LogWarning("TcpTransportProvider", $"TCP 服务端解包失败，长度={length}，连接={connection.Id}");
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(payloadBuffer);
                    }
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                DisconnectClient(connection.Id);
            }
        }

        private void EnqueueServerConnected(int connectionId)
        {
            if (!_isServerActive)
            {
                return;
            }

            _serverEvents.Enqueue(new ServerEvent
            {
                Kind = ServerEventKind.Connected,
                ConnectionId = connectionId
            });
        }

        private void EnqueueServerDisconnected(int connectionId)
        {
            if (!_isServerActive)
            {
                return;
            }

            _serverEvents.Enqueue(new ServerEvent
            {
                Kind = ServerEventKind.Disconnected,
                ConnectionId = connectionId
            });
        }

        private void EnqueueServerPacket(int connectionId, Packet packet)
        {
            if (!_isServerActive)
            {
                return;
            }

            _serverEvents.Enqueue(new ServerEvent
            {
                Kind = ServerEventKind.Packet,
                ConnectionId = connectionId,
                Packet = packet
            });
        }

        private void DrainServerEvents()
        {
            while (_serverEvents.TryDequeue(out _))
            {
            }
        }

        #endregion

        #region 客户端

        public void StartClient()
        {
            if (_appConfig == null) return;
            UnityPlayerLoopDispatcher.EnsureInstalled();

            if (_isClientActive)
            {
                if (_isPhysicalConnected || _isClientConnecting) return;

                CleanupClientTransportOnly();
            }

            if (!_isClientActive)
            {
                _isClientActive = true;
                OnClientStartedEvent?.Invoke();
            }

            int attemptId = Interlocked.Increment(ref _clientConnectAttemptId);
            _isClientConnecting = true;
            _ = ConnectClientAsync(attemptId, _appConfig.Ip, _appConfig.Port);
        }

        private async Task ConnectClientAsync(int attemptId, string host, int port)
        {
            TcpClient client = new TcpClient();
            CancellationTokenSource timeoutCts = new CancellationTokenSource();

            try
            {
                client.NoDelay = true;
                Task connectTask = client.ConnectAsync(host, port);
                Task timeoutTask = Task.Delay(ClientConnectTimeoutMs, timeoutCts.Token);
                Task completedTask = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);
                if (!ReferenceEquals(completedTask, connectTask))
                {
                    throw new TimeoutException($"connect timeout after {ClientConnectTimeoutMs}ms");
                }

                await connectTask.ConfigureAwait(false);
                timeoutCts.Cancel();

                NetworkStream clientStream = client.GetStream();
                CancellationTokenSource clientCts = new CancellationTokenSource();
                TcpSendQueue sendQueue = new TcpSendQueue
                {
                    OwnerConnectionId = 0,
                    IsServerSide = false,
                    Stream = clientStream
                };

                if (!_isClientActive || attemptId != _clientConnectAttemptId)
                {
                    sendQueue.Stream = null;
                    DrainPendingFrames(sendQueue);
                    clientStream.Close();
                    client.Close();
                    clientCts.Dispose();
                    return;
                }

                _client = client;
                _clientStream = clientStream;
                _clientSendQueue = sendQueue;
                _clientCts = clientCts;
                _isPhysicalConnected = true;
                _isClientConnecting = false;

                NetLogger.LogInfo("TcpTransportProvider", $"TCP client connected -> {host}:{port}");
                UnityPlayerLoopDispatcher.ExecuteOrPost(() => OnClientConnectedEvent?.Invoke());

                _ = ReceiveClientDataAsync(clientStream, clientCts.Token);
            }
            catch (Exception ex)
            {
                client.Close();

                if (attemptId != _clientConnectAttemptId)
                {
                    return;
                }

                Interlocked.Increment(ref _clientTcpConnectFailures);
                _isClientConnecting = false;
                _isPhysicalConnected = false;
                NetLogger.LogWarning("TcpTransportProvider", $"TCP client connect failed -> {host}:{port}, Error:{ex.Message}, Failures:{Interlocked.Read(ref _clientTcpConnectFailures)}");

                if (_isClientActive)
                {
                    UnityPlayerLoopDispatcher.ExecuteOrPost(() => OnClientDisconnectedEvent?.Invoke());
                }
            }
            finally
            {
                timeoutCts.Dispose();
            }
        }

        public void StopClient()
        {
            if (!_isClientActive) return;
            Interlocked.Increment(ref _clientConnectAttemptId);
            _isClientActive = false;
            _isPhysicalConnected = false;
            _isClientConnecting = false;

            TcpSendQueue sendQueue = _clientSendQueue;
            _clientSendQueue = null;
            if (sendQueue != null)
            {
                sendQueue.Stream = null;
                DrainPendingFrames(sendQueue);
            }

            _clientCts?.Cancel();
            _clientStream?.Close();
            _client?.Close();

            _clientStream = null;
            _clientCts = null;
            _client = null;

            NetLogger.LogInfo("TcpTransportProvider", "TCP 客户端已停止");
            NetLogger.LogInfo("TcpTransportProvider", $"TCP client metrics. Recv:{Interlocked.Read(ref _clientTcpTotalPackets)}, DecodeFail:{Interlocked.Read(ref _clientTcpDeserializeFailures)}, Disconnects:{Interlocked.Read(ref _clientTcpDisconnects)}, ConnectFailures:{Interlocked.Read(ref _clientTcpConnectFailures)}, WriteFailures:{Interlocked.Read(ref _clientTcpWriteFailures)}, QueueAborts:{Interlocked.Read(ref _clientTcpSendQueueAborts)}");
            UnityPlayerLoopDispatcher.ExecuteOrPost(() =>
            {
                OnClientDisconnectedEvent?.Invoke();
                OnClientStoppedEvent?.Invoke();
            });
        }

        private void HandlePhysicalDisconnect()
        {
            if (!_isPhysicalConnected && _client == null && !_isClientConnecting) return;
            Interlocked.Increment(ref _clientTcpDisconnects);
            _isPhysicalConnected = false;
            _isClientConnecting = false;

            TcpSendQueue sendQueue = _clientSendQueue;
            _clientSendQueue = null;
            if (sendQueue != null)
            {
                sendQueue.Stream = null;
                DrainPendingFrames(sendQueue);
            }

            _clientCts?.Cancel();
            _clientStream?.Close();
            _client?.Close();

            _clientStream = null;
            _clientCts = null;
            _client = null;

            UnityPlayerLoopDispatcher.ExecuteOrPost(() => { OnClientDisconnectedEvent?.Invoke(); });
        }

        private void CleanupClientTransportOnly()
        {
            _isPhysicalConnected = false;
            _isClientConnecting = false;

            TcpSendQueue sendQueue = _clientSendQueue;
            _clientSendQueue = null;
            if (sendQueue != null)
            {
                sendQueue.Stream = null;
                DrainPendingFrames(sendQueue);
            }

            _clientCts?.Cancel();
            _clientStream?.Close();
            _client?.Close();

            _clientStream = null;
            _clientCts = null;
            _client = null;
        }

        private async Task ReceiveClientDataAsync(NetworkStream stream, CancellationToken token)
        {
            byte[] headerBuffer = new byte[4];
            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (!await ReadExactAsync(stream, headerBuffer, 4, token).ConfigureAwait(false)) break;
                    int length = BitConverter.ToInt32(headerBuffer, 0);

                    if (length <= 0 || length > 1024 * 1024 * 10) break;

                    byte[] payloadBuffer = ArrayPool<byte>.Shared.Rent(length);
                    try
                    {
                        if (!await ReadExactAsync(stream, payloadBuffer, length, token).ConfigureAwait(false)) break;

                        Interlocked.Increment(ref _clientTcpTotalPackets);
                        if (LitePacketFormatter.TryDeserialize(payloadBuffer, 0, length, out Packet packet))
                        {
                            byte[] safePayload = new byte[packet.PayloadLength];
                            Buffer.BlockCopy(packet.Payload, packet.PayloadOffset, safePayload, 0, packet.PayloadLength);
                            Packet safePacket = new Packet(packet.Seq, packet.MsgId, packet.Scope, packet.RoomId, safePayload, packet.PayloadLength);

                            // 后台线程只负责解包，真正进入客户端逻辑仍通过统一调度器回主线程。
                            UnityPlayerLoopDispatcher.ExecuteOrPost(() => OnClientReceivePacketEvent?.Invoke(safePacket));
                        }
                        else
                        {
                            Interlocked.Increment(ref _clientTcpDeserializeFailures);
                            NetLogger.LogWarning("TcpTransportProvider", $"TCP 客户端解包失败，长度={length}");
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(payloadBuffer);
                    }
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                // 只有当前仍然挂在客户端字段上的那条物理连接，才允许触发真实断线清理。
                // 这样旧连接在重连过程中退出时，不会误把新建好的连接一起断掉。
                if (_isClientActive && ReferenceEquals(_clientStream, stream))
                {
                    HandlePhysicalDisconnect();
                }
            }
        }

        #endregion

        #region 混合与发送

        public void StartHost()
        {
            StartServer();
            StartClient();
        }

        public void SendToServer(Packet packet)
        {
            if (!_isPhysicalConnected || _clientSendQueue == null) return;
            QueueSerializedSend(_clientSendQueue, packet);
        }

        public void SendToClient(int connectionId, Packet packet)
        {
            if (_serverConnections.TryGetValue(connectionId, out TcpConnection conn) && conn.SendQueue != null)
            {
                QueueSerializedSend(conn.SendQueue, packet);
            }
        }

        private void QueueSerializedSend(TcpSendQueue sendQueue, Packet packet)
        {
            if (sendQueue == null || sendQueue.Stream == null)
            {
                return;
            }

            byte[] frameBuffer = null;
            int frameLength = 0;
            try
            {
                // 先在调用线程把 Packet 序列化成独立帧，确保上层即使立即归还 Payload 池化缓冲区，
                // TCP 后台发送任务也不会再读到被复用的旧载荷。
                int packetLength = LitePacketFormatter.GetSerializedLength(packet);
                frameLength = packetLength + 4;
                frameBuffer = ArrayPool<byte>.Shared.Rent(frameLength);
                int serializedLength = LitePacketFormatter.Serialize(packet, frameBuffer, 4);
                byte[] lengthBytes = BitConverter.GetBytes(serializedLength);
                Buffer.BlockCopy(lengthBytes, 0, frameBuffer, 0, 4);
            }
            catch (Exception ex)
            {
                if (frameBuffer != null)
                {
                    ArrayPool<byte>.Shared.Return(frameBuffer);
                }

                NetLogger.LogError("TcpTransportProvider", $"序列化待发送帧异常: {ex.Message}");
                return;
            }

            PendingSendFrame pendingFrame = new PendingSendFrame
            {
                Buffer = frameBuffer,
                Length = frameLength
            };

            // 每条连接同一时间只允许一个发送循环在跑，避免高负载下为每个包堆积一个等待锁的异步任务。
            if (!TryTrackQueuedFrame(sendQueue, frameLength))
            {
                ReleasePendingFrame(sendQueue, pendingFrame);
                return;
            }

            sendQueue.PendingFrames.Enqueue(pendingFrame);
            if (Interlocked.CompareExchange(ref sendQueue.SendLoopRunning, 1, 0) == 0)
            {
                _ = SendQueuedFramesAsync(sendQueue);
            }
        }

        private bool TryTrackQueuedFrame(TcpSendQueue sendQueue, int frameLength)
        {
            int pendingFrames = Interlocked.Increment(ref sendQueue.PendingFrameCount);
            long pendingBytes = Interlocked.Add(ref sendQueue.PendingBytes, frameLength);

            if (pendingFrames >= SendQueueCriticalFrameCount || pendingBytes >= SendQueueCriticalBytes)
            {
                AbortSendQueue(sendQueue, $"critical queue growth, PendingFrames:{pendingFrames}, PendingBytes:{pendingBytes}");
                return false;
            }

            Interlocked.Increment(ref sendQueue.TotalQueuedFrames);
            if ((pendingFrames >= SendQueueWarnFrameCount || pendingBytes >= SendQueueWarnBytes) &&
                Interlocked.CompareExchange(ref sendQueue.WarningIssued, 1, 0) == 0)
            {
                NetLogger.LogWarning("TcpTransportProvider", $"TCP send queue high. Side:{(sendQueue.IsServerSide ? "Server" : "Client")}, Conn:{sendQueue.OwnerConnectionId}, PendingFrames:{pendingFrames}, PendingBytes:{pendingBytes}, TotalQueued:{Interlocked.Read(ref sendQueue.TotalQueuedFrames)}, TotalSent:{Interlocked.Read(ref sendQueue.TotalSentFrames)}, ConsecutiveWriteFailures:{Interlocked.Read(ref sendQueue.SendFailures)}");
            }

            return true;
        }

        private async Task SendQueuedFramesAsync(TcpSendQueue sendQueue)
        {
            if (sendQueue == null)
            {
                return;
            }

            bool abortLoop = false;
            try
            {
                while (true)
                {
                    while (sendQueue.PendingFrames.TryDequeue(out PendingSendFrame frame))
                    {
                        try
                        {
                            if (sendQueue.Stream == null)
                            {
                                abortLoop = true;
                                break;
                            }

                            await sendQueue.Stream.WriteAsync(frame.Buffer, 0, frame.Length).ConfigureAwait(false);
                            Interlocked.Increment(ref sendQueue.TotalSentFrames);
                            Interlocked.Exchange(ref sendQueue.SendFailures, 0);
                        }
                        catch (Exception ex)
                        {
                            abortLoop = true;
                            long failures = Interlocked.Increment(ref sendQueue.SendFailures);
                            if (sendQueue.IsServerSide)
                            {
                                Interlocked.Increment(ref _serverTcpWriteFailures);
                            }
                            else
                            {
                                Interlocked.Increment(ref _clientTcpWriteFailures);
                            }

                            NetLogger.LogError("TcpTransportProvider", $"TCP write failed. Side:{(sendQueue.IsServerSide ? "Server" : "Client")}, Conn:{sendQueue.OwnerConnectionId}, ConsecutiveWriteFailures:{failures}, PendingFrames:{Interlocked.CompareExchange(ref sendQueue.PendingFrameCount, 0, 0)}, PendingBytes:{Interlocked.Read(ref sendQueue.PendingBytes)}, Error:{ex.Message}");
                            AbortSendQueue(sendQueue, $"write failure, ConsecutiveWriteFailures:{failures}");
                            NetLogger.LogError("TcpTransportProvider", $"发送数据异常: {ex.Message}");
                            break;
                        }
                        finally
                        {
                            ReleasePendingFrame(sendQueue, frame);
                        }
                    }

                    if (abortLoop)
                    {
                        break;
                    }

                    Interlocked.Exchange(ref sendQueue.SendLoopRunning, 0);
                    if (sendQueue.PendingFrames.IsEmpty ||
                        Interlocked.CompareExchange(ref sendQueue.SendLoopRunning, 1, 0) != 0)
                    {
                        break;
                    }
                }
            }
            finally
            {
                if (abortLoop)
                {
                    Interlocked.Exchange(ref sendQueue.SendLoopRunning, 0);
                    DrainPendingFrames(sendQueue);
                }
            }
        }

        private static void ReleasePendingFrame(TcpSendQueue sendQueue, PendingSendFrame frame)
        {
            if (frame == null)
            {
                return;
            }

            if (frame.Buffer != null)
            {
                ArrayPool<byte>.Shared.Return(frame.Buffer);
                frame.Buffer = null;
            }

            int pendingFrames = DecrementPendingFrameCount(sendQueue);
            long pendingBytes = SubtractPendingBytes(sendQueue, frame.Length);
            if (pendingFrames < SendQueueWarnFrameCount / 2 && pendingBytes < SendQueueWarnBytes / 2)
            {
                Interlocked.Exchange(ref sendQueue.WarningIssued, 0);
            }
        }

        private static int DecrementPendingFrameCount(TcpSendQueue sendQueue)
        {
            while (true)
            {
                int current = Volatile.Read(ref sendQueue.PendingFrameCount);
                int next = current > 0 ? current - 1 : 0;
                if (Interlocked.CompareExchange(ref sendQueue.PendingFrameCount, next, current) == current)
                {
                    return next;
                }
            }
        }

        private static long SubtractPendingBytes(TcpSendQueue sendQueue, int frameLength)
        {
            while (true)
            {
                long current = Interlocked.Read(ref sendQueue.PendingBytes);
                long next = current > frameLength ? current - frameLength : 0;
                if (Interlocked.CompareExchange(ref sendQueue.PendingBytes, next, current) == current)
                {
                    return next;
                }
            }
        }

        private void AbortSendQueue(TcpSendQueue sendQueue, string reason)
        {
            if (sendQueue == null || Interlocked.Exchange(ref sendQueue.AbortIssued, 1) != 0)
            {
                return;
            }

            NetLogger.LogError("TcpTransportProvider", $"TCP send queue aborted. Side:{(sendQueue.IsServerSide ? "Server" : "Client")}, Conn:{sendQueue.OwnerConnectionId}, Reason:{reason}, PendingFrames:{Interlocked.CompareExchange(ref sendQueue.PendingFrameCount, 0, 0)}, PendingBytes:{Interlocked.Read(ref sendQueue.PendingBytes)}, TotalQueued:{Interlocked.Read(ref sendQueue.TotalQueuedFrames)}, TotalSent:{Interlocked.Read(ref sendQueue.TotalSentFrames)}, ConsecutiveWriteFailures:{Interlocked.Read(ref sendQueue.SendFailures)}");
            sendQueue.Stream = null;

            if (sendQueue.IsServerSide)
            {
                Interlocked.Increment(ref _serverTcpSendQueueAborts);
                DisconnectClient(sendQueue.OwnerConnectionId);
            }
            else
            {
                Interlocked.Increment(ref _clientTcpSendQueueAborts);
                HandlePhysicalDisconnect();
            }
        }

        private static void DrainPendingFrames(TcpSendQueue sendQueue)
        {
            if (sendQueue == null)
            {
                return;
            }

            while (sendQueue.PendingFrames.TryDequeue(out PendingSendFrame frame))
            {
                ReleasePendingFrame(sendQueue, frame);
            }
        }

        public void DisconnectClient(int connectionId)
        {
            if (_serverConnections.TryRemove(connectionId, out TcpConnection conn))
            {
                Interlocked.Increment(ref _serverTcpDisconnects);
                DisposeServerConnection(conn);
                EnqueueServerDisconnected(connectionId);
            }
        }

        private static void DisposeServerConnection(TcpConnection connection)
        {
            if (connection == null)
            {
                return;
            }

            NetworkStream stream = connection.SendQueue != null ? connection.SendQueue.Stream : null;
            if (connection.SendQueue != null)
            {
                connection.SendQueue.Stream = null;
            }

            DrainPendingFrames(connection.SendQueue);
            stream?.Close();
            connection.Client?.Close();
        }

        public float GetRTT() => 0.05f; // 占位符，真实 RTT 需在业务层通过 PingPong 计算

        private async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, int length, CancellationToken token)
        {
            int totalRead = 0;
            while (totalRead < length)
            {
                int read = await stream.ReadAsync(buffer, totalRead, length - totalRead, token).ConfigureAwait(false);
                if (read == 0) return false;
                totalRead += read;
            }

            return true;
        }

        #endregion
    }
}
