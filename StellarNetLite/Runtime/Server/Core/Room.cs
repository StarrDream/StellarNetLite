using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;
using UnityEngine;

namespace StellarNet.Lite.Server.Core
{
    public sealed class Room
    {
        public string RoomId { get; }
        public RoomDispatcher Dispatcher { get; }
        public bool IsRecording { get; private set; }

        public int CurrentTick { get; private set; }
        public DateTime CreateTime { get; }
        public DateTime EmptySince { get; private set; }

        public int[] ComponentIds { get; private set; }
        public int MemberCount => _members.Count;

        private readonly Dictionary<string, Session> _members = new Dictionary<string, Session>();
        private readonly List<RoomComponent> _components = new List<RoomComponent>();
        private readonly List<ReplayFrame> _recorder = new List<ReplayFrame>();
        private readonly Action<int, Packet> _sendToConnection;

        private const int MaxReplayFrames = 108000;

        public Room(string roomId, Action<int, Packet> sendToConnection)
        {
            RoomId = roomId;
            Dispatcher = new RoomDispatcher(roomId);
            _sendToConnection = sendToConnection;
            CreateTime = DateTime.UtcNow;
            EmptySince = DateTime.UtcNow;
            CurrentTick = 0;
        }

        public void SetComponentIds(int[] ids)
        {
            if (ids == null) return;
            ComponentIds = ids;
        }

        public void AddComponent(RoomComponent component)
        {
            if (component == null)
            {
                Debug.LogError($"[Room] 添加组件失败: component 为空，RoomId: {RoomId}");
                return;
            }

            component.Room = this;
            _components.Add(component);
            component.OnInit();
        }

        public void AddMember(Session session)
        {
            if (session == null)
            {
                Debug.LogError($"[Room] 添加成员失败: session 为空，RoomId: {RoomId}");
                return;
            }

            if (_members.ContainsKey(session.SessionId))
            {
                Debug.LogError($"[Room] 添加成员失败: SessionId {session.SessionId} 已在房间中，RoomId: {RoomId}");
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
            if (session == null || !_members.ContainsKey(session.SessionId))
            {
                return;
            }

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

        // 核心新增：提供给组件层的安全查询接口，用于遍历寻找在线玩家
        public Session GetMember(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return null;
            _members.TryGetValue(sessionId, out var session);
            return session;
        }

        // 核心新增：分发成员离线事件
        public void NotifyMemberOffline(Session session)
        {
            if (session == null || !_members.ContainsKey(session.SessionId)) return;
            for (int i = 0; i < _components.Count; i++)
            {
                _components[i].OnMemberOffline(session);
            }
        }

        // 核心新增：分发成员上线事件
        public void NotifyMemberOnline(Session session)
        {
            if (session == null || !_members.ContainsKey(session.SessionId)) return;
            for (int i = 0; i < _components.Count; i++)
            {
                _components[i].OnMemberOnline(session);
            }
        }

        public void TriggerReconnectSnapshot(Session session)
        {
            if (session == null || !_members.ContainsKey(session.SessionId))
            {
                Debug.LogError($"[Room] 触发重连快照失败: 目标 session 不在当前房间中，RoomId: {RoomId}");
                return;
            }

            for (int i = 0; i < _components.Count; i++)
            {
                _components[i].OnSendSnapshot(session);
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
                    Debug.LogError($"[Room] 录制阻断: 房间 {RoomId} 录制帧数达到上限 {MaxReplayFrames}，已强制终止录制防止 OOM");
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
            if (session == null)
            {
                Debug.LogError($"[Room] 单播失败: session 为空，RoomId: {RoomId}");
                return;
            }

            if (!session.IsOnline)
            {
                return;
            }

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