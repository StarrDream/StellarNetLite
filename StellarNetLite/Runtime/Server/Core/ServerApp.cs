using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
using UnityEngine;

namespace StellarNet.Lite.Server.Core
{
    public sealed class ServerApp
    {
        public GlobalDispatcher GlobalDispatcher { get; } = new GlobalDispatcher();

        private readonly Dictionary<string, Session> _sessions = new Dictionary<string, Session>();
        private readonly Dictionary<int, Session> _connectionToSession = new Dictionary<int, Session>();
        private readonly Dictionary<string, Room> _rooms = new Dictionary<string, Room>();
        private readonly Action<int, Packet> _networkSender;

        private readonly List<string> _gcRoomCache = new List<string>();
        private readonly List<string> _gcSessionCache = new List<string>();

        public ServerApp(Action<int, Packet> networkSender)
        {
            _networkSender = networkSender;
        }

        public void Tick(NetConfig config)
        {
            if (config == null) return;

            _gcRoomCache.Clear();
            _gcSessionCache.Clear();
            DateTime now = DateTime.UtcNow;

            foreach (var kvp in _rooms)
            {
                var room = kvp.Value;
                room.Tick();

                if ((now - room.CreateTime).TotalHours >= config.MaxRoomLifetimeHours)
                {
                    _gcRoomCache.Add(room.RoomId);
                    continue;
                }

                if (room.MemberCount == 0 && (now - room.EmptySince).TotalMinutes >= config.EmptyRoomTimeoutMinutes)
                {
                    _gcRoomCache.Add(room.RoomId);
                }
            }

            for (int i = 0; i < _gcRoomCache.Count; i++)
            {
                string roomId = _gcRoomCache[i];
                Debug.LogWarning($"[ServerApp] 触发房间 GC: 销毁房间 {roomId} (可能因超时或长期空置)");
                DestroyRoom(roomId);
            }

            foreach (var kvp in _sessions)
            {
                var session = kvp.Value;
                if (!session.IsOnline)
                {
                    double offlineMinutes = (now - session.LastOfflineTime).TotalMinutes;
                    bool inRoom = !string.IsNullOrEmpty(session.CurrentRoomId);

                    if (inRoom && offlineMinutes >= config.OfflineTimeoutRoomMinutes)
                    {
                        _gcSessionCache.Add(session.SessionId);
                    }
                    else if (!inRoom && offlineMinutes >= config.OfflineTimeoutLobbyMinutes)
                    {
                        _gcSessionCache.Add(session.SessionId);
                    }
                }
            }

            for (int i = 0; i < _gcSessionCache.Count; i++)
            {
                string sessionId = _gcSessionCache[i];
                if (_sessions.TryGetValue(sessionId, out var session))
                {
                    if (!string.IsNullOrEmpty(session.CurrentRoomId))
                    {
                        var room = GetRoom(session.CurrentRoomId);
                        if (room != null)
                        {
                            room.RemoveMember(session);
                        }
                    }

                    RemoveSession(sessionId);
                    Debug.LogWarning($"[ServerApp] 触发 Session GC: 彻底回收长期离线会话 {sessionId}");
                }
            }
        }

        public void OnReceivePacket(int connectionId, Packet packet)
        {
            Session session = GetSessionByConnectionId(connectionId);
            if (session == null)
            {
                session = new Session(Guid.NewGuid().ToString("N"), "UNAUTH", connectionId);
                RegisterSession(session);
                Debug.Log($"[ServerApp] 接收到新连接，已分配匿名会话: {session.SessionId}");
            }

            // 架构说明：在路由分发前进行全局防重放拦截。
            // 客户端发出的 Seq 必定大于 0。若消费失败，说明是重复点击导致的重放包，直接丢弃，保护状态机纯净。
            if (packet.Seq > 0 && !session.TryConsumeSeq(packet.Seq))
            {
                Debug.LogWarning($"[ServerApp] 防重放拦截: 丢弃重复包 MsgId {packet.MsgId}, Seq {packet.Seq}, 当前记录 Seq {session.LastReceivedSeq}");
                return;
            }

            if (packet.Scope == NetScope.Global)
            {
                GlobalDispatcher.Dispatch(session, packet);
            }
            else if (packet.Scope == NetScope.Room)
            {
                if (string.IsNullOrEmpty(packet.RoomId) || packet.RoomId != session.CurrentRoomId)
                {
                    Debug.LogError($"[ServerApp] 路由阻断: 房间上下文不匹配。Packet.RoomId: {packet.RoomId}, Session.RoomId: {session.CurrentRoomId}");
                    return;
                }

                if (!_rooms.TryGetValue(packet.RoomId, out var room))
                {
                    Debug.LogError($"[ServerApp] 路由阻断: 目标房间不存在，RoomId: {packet.RoomId}");
                    return;
                }

                room.Dispatcher.Dispatch(session, packet);
            }
        }

        public Room CreateRoom(string roomId)
        {
            if (string.IsNullOrEmpty(roomId)) return null;
            if (_rooms.ContainsKey(roomId)) return null;

            var room = new Room(roomId, SendPacketToConnection);
            _rooms[roomId] = room;
            return room;
        }

        public void DestroyRoom(string roomId)
        {
            if (string.IsNullOrEmpty(roomId)) return;
            if (_rooms.TryGetValue(roomId, out var room))
            {
                room.Destroy();
                _rooms.Remove(roomId);
            }
        }

        public Room GetRoom(string roomId)
        {
            if (string.IsNullOrEmpty(roomId)) return null;
            _rooms.TryGetValue(roomId, out var room);
            return room;
        }

        public void RegisterSession(Session session)
        {
            if (session == null) return;
            _sessions[session.SessionId] = session;
            if (session.ConnectionId >= 0)
            {
                _connectionToSession[session.ConnectionId] = session;
            }
        }

        public void RemoveSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return;
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                _sessions.Remove(sessionId);
                if (session.ConnectionId >= 0)
                {
                    _connectionToSession.Remove(session.ConnectionId);
                }
            }
        }

        public void BindConnection(Session session, int connectionId)
        {
            if (session == null) return;

            if (_connectionToSession.TryGetValue(connectionId, out var oldSession) && oldSession != session)
            {
                Debug.LogWarning($"[ServerApp] 物理连接顶号: ConnectionId {connectionId} 原属 Session {oldSession.SessionId}，现被 {session.SessionId} 抢占");
                oldSession.MarkOffline();
            }

            if (session.ConnectionId >= 0 && session.ConnectionId != connectionId)
            {
                _connectionToSession.Remove(session.ConnectionId);
            }

            session.UpdateConnection(connectionId);
            if (connectionId >= 0)
            {
                _connectionToSession[connectionId] = session;
            }

            if (!string.IsNullOrEmpty(session.CurrentRoomId))
            {
                GetRoom(session.CurrentRoomId)?.NotifyMemberOnline(session);
            }
        }

        public void UnbindConnection(Session session)
        {
            if (session == null) return;
            if (session.ConnectionId >= 0)
            {
                _connectionToSession.Remove(session.ConnectionId);
                session.MarkOffline();

                if (!string.IsNullOrEmpty(session.CurrentRoomId))
                {
                    GetRoom(session.CurrentRoomId)?.NotifyMemberOffline(session);
                }
            }
        }

        private Session GetSessionByConnectionId(int connectionId)
        {
            _connectionToSession.TryGetValue(connectionId, out var session);
            return session;
        }

        private void SendPacketToConnection(int connectionId, Packet packet)
        {
            _networkSender?.Invoke(connectionId, packet);
        }
    }
}