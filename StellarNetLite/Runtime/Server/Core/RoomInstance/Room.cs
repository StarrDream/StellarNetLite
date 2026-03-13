using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Server.Infrastructure;

namespace StellarNet.Lite.Server.Core
{
    public enum RoomState
    {
        Waiting,
        Playing,
        Finished
    }

    public sealed class Room
    {
        public string RoomId { get; }

        // 核心架构：引入纯净的领域配置模型，统一管理房间业务属性
        public RoomConfigModel Config { get; } = new RoomConfigModel();

        // 保留快捷属性，兼容外部日志与面板查询
        public string RoomName => Config.RoomName;

        public RoomDispatcher Dispatcher { get; }
        public bool IsRecording { get; private set; }
        public int CurrentTick { get; private set; }
        public DateTime CreateTime { get; }
        public DateTime EmptySince { get; private set; }
        public int[] ComponentIds { get; private set; }
        public int MemberCount => _members.Count;
        public RoomState State { get; private set; } = RoomState.Waiting;
        public ReplayFile LastReplay { get; private set; }

        private readonly Dictionary<string, Session> _members = new Dictionary<string, Session>();
        public IReadOnlyDictionary<string, Session> Members => _members;
        private readonly List<RoomComponent> _components = new List<RoomComponent>();
        private readonly List<ReplayFrame> _recorder = new List<ReplayFrame>();

        private readonly Action<int, Packet> _sendToConnection;
        private readonly Func<object, byte[]> _serializeFunc;

        private const int MaxReplayFrames = 108000;
        private int _finishedTickCount = 0;

        public Room(string roomId, Action<int, Packet> sendToConnection, Func<object, byte[]> serializeFunc)
        {
            RoomId = roomId;
            Dispatcher = new RoomDispatcher(roomId);
            _sendToConnection = sendToConnection;
            _serializeFunc = serializeFunc;
            CreateTime = DateTime.UtcNow;
            EmptySince = DateTime.UtcNow;
            CurrentTick = 0;
            State = RoomState.Waiting;
        }

        public void SetComponentIds(int[] ids)
        {
            if (ids == null) return;
            ComponentIds = ids;
        }

        public void AddComponent(RoomComponent component)
        {
            if (component == null) return;
            component.Room = this;
            _components.Add(component);
        }

        public void InitializeComponents()
        {
            for (int i = 0; i < _components.Count; i++)
            {
                _components[i].OnInit();
            }
        }

        public void AddMember(Session session)
        {
            if (session == null || _members.ContainsKey(session.SessionId)) return;

            if (State == RoomState.Finished)
            {
                NetLogger.LogWarning("Room", "拦截加入: 房间已结束，拒绝加入", RoomId, session.SessionId);
                return;
            }

            _members[session.SessionId] = session;
            session.BindRoom(RoomId);
            EmptySince = DateTime.MaxValue;

            for (int i = 0; i < _components.Count; i++)
            {
                _components[i].OnMemberJoined(session);
            }
        }

        public void RemoveMember(Session session)
        {
            if (session == null || !_members.ContainsKey(session.SessionId)) return;

            for (int i = 0; i < _components.Count; i++)
            {
                _components[i].OnMemberLeft(session);
            }

            _members.Remove(session.SessionId);
            session.UnbindRoom();

            if (_members.Count == 0)
            {
                EmptySince = DateTime.UtcNow;
            }
        }

        public Session GetMember(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return null;
            _members.TryGetValue(sessionId, out var session);
            return session;
        }

        public void NotifyMemberOffline(Session session)
        {
            if (session == null || !_members.ContainsKey(session.SessionId)) return;
            for (int i = 0; i < _components.Count; i++)
            {
                _components[i].OnMemberOffline(session);
            }
        }

        public void NotifyMemberOnline(Session session)
        {
            if (session == null || !_members.ContainsKey(session.SessionId)) return;

            if (State == RoomState.Finished)
            {
                NetLogger.LogWarning("Room", "拦截重连: 房间已结束，强制将重连玩家移出房间", RoomId, session.SessionId);
                RemoveMember(session);
                return;
            }

            for (int i = 0; i < _components.Count; i++)
            {
                _components[i].OnMemberOnline(session);
            }
        }

        public void TriggerReconnectSnapshot(Session session)
        {
            if (session == null || !_members.ContainsKey(session.SessionId)) return;
            for (int i = 0; i < _components.Count; i++)
            {
                _components[i].OnSendSnapshot(session);
            }
        }

        public void StartGame()
        {
            if (State != RoomState.Waiting) return;
            State = RoomState.Playing;
            LastReplay = null;
            StartRecord();

            for (int i = 0; i < _components.Count; i++)
            {
                _components[i].OnGameStart();
            }
        }

        public void EndGame()
        {
            if (State != RoomState.Playing) return;
            State = RoomState.Finished;
            _finishedTickCount = 0;

            if (IsRecording)
            {
                LastReplay = StopRecordAndSave();
                ServerReplayStorage.SaveReplay(LastReplay, new NetConfig());
            }

            for (int i = 0; i < _components.Count; i++)
            {
                _components[i].OnGameEnd();
            }
        }

        public void BroadcastMessage<T>(T msg) where T : class
        {
            if (!NetMessageMapper.TryGetMeta(typeof(T), out var meta))
            {
                NetLogger.LogError("Room", $"广播失败: 未找到类型 {typeof(T).Name} 的网络元数据", RoomId);
                return;
            }

            if (meta.Dir != NetDir.S2C)
            {
                NetLogger.LogError("Room", $"广播阻断: 协议 {meta.Id} 方向为 {meta.Dir}，服务端只能发送 S2C", RoomId);
                return;
            }

            byte[] payload = _serializeFunc(msg);
            var packet = new Packet(0, meta.Id, meta.Scope, RoomId, payload);

            RecordPacket(packet);

            foreach (var kvp in _members)
            {
                var session = kvp.Value;
                if (session.IsOnline)
                {
                    _sendToConnection?.Invoke(session.ConnectionId, packet);
                }
            }
        }

        public void SendMessageTo<T>(Session session, T msg, bool recordToReplay = false) where T : class
        {
            if (session == null || !session.IsOnline) return;

            if (!NetMessageMapper.TryGetMeta(typeof(T), out var meta))
            {
                NetLogger.LogError("Room", $"发送失败: 未找到类型 {typeof(T).Name} 的网络元数据", RoomId, session.SessionId);
                return;
            }

            if (meta.Dir != NetDir.S2C)
            {
                NetLogger.LogError("Room", $"发送阻断: 协议 {meta.Id} 方向为 {meta.Dir}，服务端只能发送 S2C", RoomId,
                    session.SessionId);
                return;
            }

            byte[] payload = _serializeFunc(msg);
            var packet = new Packet(0, meta.Id, meta.Scope, RoomId, payload);

            if (recordToReplay)
            {
                RecordPacket(packet);
            }

            _sendToConnection?.Invoke(session.ConnectionId, packet);
        }

        private void RecordPacket(Packet packet)
        {
            if (!IsRecording) return;

            if (_recorder.Count < MaxReplayFrames)
            {
                byte[] payloadCopy;
                if (packet.Payload == null || packet.Payload.Length == 0)
                {
                    payloadCopy = new byte[0];
                }
                else
                {
                    payloadCopy = new byte[packet.Payload.Length];
                    Buffer.BlockCopy(packet.Payload, 0, payloadCopy, 0, packet.Payload.Length);
                }

                _recorder.Add(new ReplayFrame(CurrentTick, packet.MsgId, payloadCopy, RoomId));
            }
            else
            {
                IsRecording = false;
                NetLogger.LogWarning("Room", $"录像阻断: 录像帧数已达上限 {MaxReplayFrames}，自动停止录制", RoomId);
            }
        }

        public void StartRecord()
        {
            IsRecording = true;
            _recorder.Clear();
        }

        public ReplayFile StopRecordAndSave()
        {
            IsRecording = false;
            var replayFile = new ReplayFile
            {
                ReplayId = Guid.NewGuid().ToString("N"),
                RoomId = this.RoomId,
                ComponentIds = this.ComponentIds,
                Frames = new List<ReplayFrame>(_recorder)
            };
            _recorder.Clear();
            return replayFile;
        }

        public void Tick()
        {
            CurrentTick++;

            if (State == RoomState.Finished)
            {
                _finishedTickCount++;
                if (_finishedTickCount > 300 && _members.Count > 0)
                {
                    NetLogger.LogWarning("Room", $"僵尸清理: 结算已超时，强制清空残留的 {_members.Count} 名玩家", RoomId);
                    var sessionsToKick = new List<Session>(_members.Values);
                    foreach (var s in sessionsToKick)
                    {
                        RemoveMember(s);
                    }
                }
            }
        }

        public void Destroy()
        {
            for (int i = 0; i < _components.Count; i++)
            {
                _components[i].OnDestroy();
            }

            _components.Clear();

            foreach (var kvp in _members)
            {
                kvp.Value.UnbindRoom();
            }

            _members.Clear();
            Dispatcher.Clear();
        }
    }
}