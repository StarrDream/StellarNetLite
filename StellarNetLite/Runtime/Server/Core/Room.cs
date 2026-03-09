using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Server.Infrastructure;
using UnityEngine;

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

        // 核心新增：预留房间名称字段，供大厅列表展示
        public string RoomName { get; set; }
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
        private readonly List<RoomComponent> _components = new List<RoomComponent>();
        private readonly List<ReplayFrame> _recorder = new List<ReplayFrame>();
        private readonly Action<int, Packet> _sendToConnection;
        private const int MaxReplayFrames = 108000;

        // 核心新增：结算后的僵尸房间销毁倒计时
        private int _finishedTickCount = 0;

        public Room(string roomId, Action<int, Packet> sendToConnection)
        {
            RoomId = roomId;
            RoomName = "未命名房间"; // 默认名称
            Dispatcher = new RoomDispatcher(roomId);
            _sendToConnection = sendToConnection;
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
            component.OnInit();
        }

        public void AddMember(Session session)
        {
            if (session == null || _members.ContainsKey(session.SessionId)) return;

            // 核心防御：拒绝加入已结束的房间
            if (State == RoomState.Finished)
            {
                Debug.LogWarning($"[Room] 拦截加入: 房间 {RoomId} 已结束，拒绝玩家 {session.SessionId} 加入");
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

            // 核心防御：如果玩家断网重连时，房间已经处于结算结束状态，直接将其踢出房间，强制回大厅
            if (State == RoomState.Finished)
            {
                Debug.LogWarning($"[Room] 拦截重连: 房间 {RoomId} 已结束，强制将重连玩家 {session.SessionId} 移出房间");
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
            _finishedTickCount = 0; // 启动销毁倒计时

            if (IsRecording)
            {
                LastReplay = StopRecordAndSave();
                // 核心修复：将内存中的录像持久化到物理磁盘，并传入默认网络配置以触发滚动清理机制
                ServerReplayStorage.SaveReplay(LastReplay, new NetConfig());
            }

            for (int i = 0; i < _components.Count; i++)
            {
                _components[i].OnGameEnd();
            }
        }

        public void Broadcast(Packet packet)
        {
            if (IsRecording)
            {
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
                }
            }

            foreach (var kvp in _members)
            {
                var session = kvp.Value;
                if (session.IsOnline)
                {
                    _sendToConnection?.Invoke(session.ConnectionId, packet);
                }
            }
        }

        public void SendTo(Session session, Packet packet)
        {
            if (session == null || !session.IsOnline) return;
            _sendToConnection?.Invoke(session.ConnectionId, packet);
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

            // 核心防御：僵尸房间强制清理机制。结算后 10 秒（假设 30 Tick/s，约 300 Tick），强制踢出所有残留玩家，触发空房间销毁
            if (State == RoomState.Finished)
            {
                _finishedTickCount++;
                if (_finishedTickCount > 300 && _members.Count > 0)
                {
                    Debug.LogWarning($"[Room] 僵尸清理: 房间 {RoomId} 结算已超时，强制清空残留的 {_members.Count} 名玩家");
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