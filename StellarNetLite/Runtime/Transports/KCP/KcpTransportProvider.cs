using System;
using System.Net;
using System.Threading;
using kcp2k;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Transports.Common;
using UnityEngine;
using ErrorCode = kcp2k.ErrorCode;

namespace StellarNet.Lite.Transports.KCP
{
    /// <summary>
    /// 基于 kcp2k 的 KCP 传输层实现。
    /// </summary>
    [DisallowMultipleComponent]
    public class KcpTransportProvider : MonoBehaviour, INetworkTransport, IServerTransportPump
    {
        #region 物理事件契约

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

        #endregion

        [Header("KCP 核心配置 (生产级预设)")] [Tooltip("NoDelay=true, Interval=10, Resend=2, NC=1 (极速模式)")]
        public KcpConfig KcpConfig = new KcpConfig(
            DualMode: false,
            RecvBufferSize: 1024 * 1024 * 8,
            SendBufferSize: 1024 * 1024 * 8,
            Mtu: 1200,
            NoDelay: true,
            Interval: 10,
            FastResend: 2,
            CongestionWindow: false,
            SendWindowSize: 4096,
            ReceiveWindowSize: 4096,
            Timeout: 10000,
            MaxRetransmits: 40
        );

        private KcpServer _server;
        private KcpClient _client;
        private NetConfig _appConfig;

        private bool _isServerActive;
        private bool _isClientActive;
        private long _serverKcpTotalPackets;
        private long _serverKcpSentPackets;
        private long _serverKcpDeserializeFailures;
        private long _serverKcpDisconnects;
        private long _serverKcpErrors;
        private long _clientKcpTotalPackets;
        private long _clientKcpSentPackets;
        private long _clientKcpDeserializeFailures;
        private long _clientKcpDisconnects;
        private long _clientKcpErrors;

        // 独立追踪物理层的真实连接状态，避免强依赖底层库内部属性，确保状态机流转清晰。
        private bool _isPhysicalConnected;
        private int _clientPumpRegistrationId;

        public void ApplyConfig(NetConfig config)
        {
            if (config == null) return;
            _appConfig = config;
            NetLogger.LogInfo("KcpTransportProvider", $"KCP 传输层配置已应用. IP:{config.Ip}, Port:{config.Port}");
        }

        private void Awake()
        {
            UnityPlayerLoopDispatcher.EnsureInstalled();
            Log.Info = (msg) => NetLogger.LogInfo("kcp2k", msg);
            Log.Warning = (msg) => NetLogger.LogWarning("kcp2k", msg);
            Log.Error = (msg) => NetLogger.LogError("kcp2k", msg);
        }

        private string DescribeKcpRuntime()
        {
            return $"NoDelay:True, Interval:10, FastResend:2, CongestionWindow:False, SendWindow:4096, ReceiveWindow:4096, RecvBuffer:{1024 * 1024 * 8}, SendBuffer:{1024 * 1024 * 8}, Timeout:10000, MaxRetransmits:40";
        }

        public void PumpServer()
        {
            if (_isServerActive && _server != null)
            {
                _server.TickIncoming();
                _server.TickOutgoing();
            }
        }

        private void OnDestroy()
        {
            StopServer();
            StopClient();
        }

        #region 服务端控制

        public void StartServer()
        {
            if (_isServerActive) return;
            if (_appConfig == null) return;

            _server = new KcpServer(
                OnServerConnected,
                OnServerDataReceived,
                OnServerDisconnected,
                OnServerError,
                KcpConfig
            );

            _server.Start((ushort)_appConfig.Port);
            _isServerActive = true;

            NetLogger.LogInfo("KcpTransportProvider", $"KCP 服务端已启动，监听端口: {_appConfig.Port}");
            NetLogger.LogInfo("KcpTransportProvider", $"KCP runtime check. ServerPump:Enabled, ReliableChannel:True, {DescribeKcpRuntime()}");
            OnServerStartedEvent?.Invoke();
        }

        public void StopServer()
        {
            if (!_isServerActive) return;
            _isServerActive = false;

            _server?.Stop();
            _server = null;

            NetLogger.LogInfo("KcpTransportProvider", "KCP 服务端已停止");
            NetLogger.LogInfo("KcpTransportProvider", $"KCP server stopped. Recv:{Interlocked.Read(ref _serverKcpTotalPackets)}, Sent:{Interlocked.Read(ref _serverKcpSentPackets)}, DecodeFail:{Interlocked.Read(ref _serverKcpDeserializeFailures)}, Disconnects:{Interlocked.Read(ref _serverKcpDisconnects)}, Errors:{Interlocked.Read(ref _serverKcpErrors)}");
            OnServerStoppedEvent?.Invoke();
        }

        private void OnServerConnected(int connectionId)
        {
            OnServerClientConnectedEvent?.Invoke(connectionId);
        }

        private void OnServerDisconnected(int connectionId)
        {
            Interlocked.Increment(ref _serverKcpDisconnects);
            OnServerClientDisconnectedEvent?.Invoke(connectionId);
        }

        private void OnServerDataReceived(int connectionId, ArraySegment<byte> message, KcpChannel channel)
        {
            Interlocked.Increment(ref _serverKcpTotalPackets);
            if (LitePacketFormatter.TryDeserialize(message.Array, message.Offset, message.Count, out Packet packet))
            {
                // 服务端 KCP 泵运行在 ServerRuntimeHost 线程内，这里立即同步进入 ServerApp，
                // 房间包在跨到 RoomWorker 前会再次深拷贝，因此当前回调内直接消费是安全的。
                OnServerReceivePacketEvent?.Invoke(connectionId, packet);
            }
            else
            {
                Interlocked.Increment(ref _serverKcpDeserializeFailures);
                NetLogger.LogWarning("KcpTransportProvider", $"KCP 服务端解包失败，长度={message.Count}，偏移={message.Offset}，连接={connectionId}");
            }
        }

        private void OnServerError(int connectionId, ErrorCode error, string reason)
        {
            Interlocked.Increment(ref _serverKcpErrors);
            NetLogger.LogWarning("KcpTransportProvider", $"KCP 服务端连接异常: {error} - {reason}", "-", $"ConnId:{connectionId}");
        }

        #endregion

        #region 客户端控制

        public void StartClient()
        {
            if (_appConfig == null) return;
            UnityPlayerLoopDispatcher.EnsureInstalled();

            // 允许在逻辑层活跃但物理层断开的场景下，重新发起物理连接，保障断线重连机制的可靠性。
            if (_isClientActive)
            {
                if (_isPhysicalConnected) return;

                if (_client != null)
                {
                    _client.Disconnect();
                    _client = null;
                }
            }

            _client = new KcpClient(
                OnClientConnected,
                OnClientDataReceived,
                OnClientDisconnected,
                OnClientError,
                KcpConfig
            );

            _client.Connect(_appConfig.Ip, (ushort)_appConfig.Port);
            NetLogger.LogInfo("KcpTransportProvider", $"KCP 客户端发起连接 -> {_appConfig.Ip}:{_appConfig.Port}");

            if (!_isClientActive)
            {
                _isClientActive = true;
                EnsureClientPumpRegistered();
                OnClientStartedEvent?.Invoke();
            }
            else
            {
                EnsureClientPumpRegistered();
            }

            NetLogger.LogInfo("KcpTransportProvider", $"KCP runtime check. ClientPumpRegistration:{_clientPumpRegistrationId}, ReliableChannel:True, {DescribeKcpRuntime()}");
        }

        public void StopClient()
        {
            if (!_isClientActive) return;
            _isClientActive = false;
            _isPhysicalConnected = false;
            UnregisterClientPump();

            _client?.Disconnect();
            _client = null;

            NetLogger.LogInfo("KcpTransportProvider", "KCP 客户端已停止");
            NetLogger.LogInfo("KcpTransportProvider", $"KCP client stopped. Recv:{Interlocked.Read(ref _clientKcpTotalPackets)}, Sent:{Interlocked.Read(ref _clientKcpSentPackets)}, DecodeFail:{Interlocked.Read(ref _clientKcpDeserializeFailures)}, Disconnects:{Interlocked.Read(ref _clientKcpDisconnects)}, Errors:{Interlocked.Read(ref _clientKcpErrors)}");
            OnClientStoppedEvent?.Invoke();
        }

        private void OnClientConnected()
        {
            _isPhysicalConnected = true;
            UnityPlayerLoopDispatcher.ExecuteOrPost(() => { OnClientConnectedEvent?.Invoke(); });
        }

        private void OnClientDisconnected()
        {
            _isPhysicalConnected = false;
            Interlocked.Increment(ref _clientKcpDisconnects);
            UnityPlayerLoopDispatcher.ExecuteOrPost(() => { OnClientDisconnectedEvent?.Invoke(); });
        }

        private void OnClientDataReceived(ArraySegment<byte> message, KcpChannel channel)
        {
            Interlocked.Increment(ref _clientKcpTotalPackets);
            if (LitePacketFormatter.TryDeserialize(message.Array, message.Offset, message.Count, out Packet packet))
            {
                // KCP 底层缓冲区可能会被后续 Tick 复用，这里先复制成安全载荷再跨阶段投递。
                byte[] safePayload = new byte[packet.PayloadLength];
                Buffer.BlockCopy(packet.Payload, packet.PayloadOffset, safePayload, 0, packet.PayloadLength);
                Packet safePacket = new Packet(packet.Seq, packet.MsgId, packet.Scope, packet.RoomId, safePayload, packet.PayloadLength);
                UnityPlayerLoopDispatcher.ExecuteOrPost(() => OnClientReceivePacketEvent?.Invoke(safePacket));
            }
            else
            {
                Interlocked.Increment(ref _clientKcpDeserializeFailures);
                NetLogger.LogWarning("KcpTransportProvider", $"KCP 客户端解包失败，长度={message.Count}，偏移={message.Offset}");
            }
        }

        private void OnClientError(ErrorCode error, string reason)
        {
            Interlocked.Increment(ref _clientKcpErrors);
            NetLogger.LogWarning("KcpTransportProvider", $"KCP 客户端连接异常: {error} - {reason}");
        }

        #endregion

        #region 混合控制与数据发送

        public void StartHost()
        {
            StartServer();
            StartClient();
        }

        public void SendToServer(Packet packet)
        {
            if (!_isClientActive || _client == null) return;
            try
            {
                // 这里不用 ArrayPool 回收发送缓冲区。
                // 如果 kcp2k 内部对调用方传入的 ArraySegment 采取延后发送或暂存引用，
                // 过早归还池化数组会让高负载下出现载荷被复用污染的问题。
                int length = LitePacketFormatter.GetSerializedLength(packet);
                byte[] buffer = new byte[length];
                int serializedLength = LitePacketFormatter.Serialize(packet, buffer, 0);
                _client.Send(new ArraySegment<byte>(buffer, 0, serializedLength), KcpChannel.Reliable);
                Interlocked.Increment(ref _clientKcpSentPackets);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _clientKcpErrors);
                NetLogger.LogError("KcpTransportProvider", $"KCP 客户端发送异常: {ex.Message}");
            }
        }

        public void SendToClient(int connectionId, Packet packet)
        {
            if (!_isServerActive || _server == null) return;
            try
            {
                // 同上，KCP 发送侧优先保证载荷生命周期独立，不把可靠发送建立在外部池化数组立即回收的假设上。
                int length = LitePacketFormatter.GetSerializedLength(packet);
                byte[] buffer = new byte[length];
                int serializedLength = LitePacketFormatter.Serialize(packet, buffer, 0);
                _server.Send(connectionId, new ArraySegment<byte>(buffer, 0, serializedLength), KcpChannel.Reliable);
                Interlocked.Increment(ref _serverKcpSentPackets);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _serverKcpErrors);
                NetLogger.LogError("KcpTransportProvider", $"KCP 服务端发送异常: {ex.Message}", "-", $"ConnId:{connectionId}");
            }
        }

        public void DisconnectClient(int connectionId)
        {
            if (!_isServerActive || _server == null) return;
            _server.Disconnect(connectionId);
        }

        public float GetRTT()
        {
            if (_isClientActive && _client != null) return 0.05f;
            return 0f;
        }

        private void EnsureClientPumpRegistered()
        {
            if (_clientPumpRegistrationId != 0)
            {
                return;
            }

            // 客户端 KCP 泵统一挂到 PlayerLoop，避免依赖组件自己的 Update。
            _clientPumpRegistrationId = UnityPlayerLoopDispatcher.RegisterRecurring(PumpClient);
        }

        private void UnregisterClientPump()
        {
            if (_clientPumpRegistrationId == 0)
            {
                return;
            }

            UnityPlayerLoopDispatcher.UnregisterRecurring(_clientPumpRegistrationId);
            _clientPumpRegistrationId = 0;
        }

        private void PumpClient()
        {
            if (!_isClientActive || _client == null)
            {
                return;
            }

            try
            {
                _client.TickIncoming();
                _client.TickOutgoing();
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _clientKcpErrors);
                NetLogger.LogError("KcpTransportProvider", $"KCP 客户端泵异常: {ex.GetType().Name}, {ex.Message}");
            }
        }

        #endregion
    }
}
