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

        // 核心架构：提供合法的只读观测边界，彻底封死外部反射私有字段的后门
        public IReadOnlyDictionary<string, Room> Rooms => _rooms;
        public IReadOnlyDictionary<string, Session> Sessions => _sessions;

        private readonly Dictionary<string, Session> _sessions = new Dictionary<string, Session>();
        private readonly Dictionary<int, Session> _connectionToSession = new Dictionary<int, Session>();
        private readonly Dictionary<string, Room> _rooms = new Dictionary<string, Room>();

        private readonly Action<int, Packet> _networkSender;
        private readonly Func<object, byte[]> _serializeFunc;

        private readonly List<string> _gcRoomCache = new List<string>();
        private readonly List<string> _gcSessionCache = new List<string>();

        public ServerApp(Action<int, Packet> networkSender, Func<object, byte[]> serializeFunc)
        {
            _networkSender = networkSender;
            _serializeFunc = serializeFunc;
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
            Session session = TryGetSessionByConnectionId(connectionId);
            if (session == null)
            {
                session = new Session(Guid.NewGuid().ToString("N"), "UNAUTH", connectionId);
                RegisterSession(session);
                Debug.Log($"[ServerApp] 接收到新连接，已分配匿名会话: {session.SessionId}");
            }

            // 架构说明：在路由分发前进行全局防重放拦截。
            if (packet.Seq > 0 && !session.TryConsumeSeq(packet.Seq))
            {
                Debug.LogWarning(
                    $"[ServerApp] 防重放拦截: 丢弃重复包 MsgId {packet.MsgId}, Seq {packet.Seq}, 当前记录 Seq {session.LastReceivedSeq}");
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
                    Debug.LogError(
                        $"[ServerApp] 路由阻断: 房间上下文不匹配。Packet.RoomId: {packet.RoomId}, Session.RoomId: {session.CurrentRoomId}");
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

        /// <summary>
        /// 服务端强类型统一发送器 (定向发送)。
        /// </summary>
        public void SendMessageToSession<T>(Session session, T msg) where T : class
        {
            if (session == null || !session.IsOnline || msg == null) return;

            if (!NetMessageMapper.TryGetMeta(typeof(T), out var meta))
            {
                Debug.LogError($"[ServerApp] 发送失败: 未找到类型 {typeof(T).Name} 的网络元数据");
                return;
            }

            // 核心修复 (Point 1)：严格校验发包方向，服务端只能发 S2C
            if (meta.Dir != NetDir.S2C)
            {
                Debug.LogError($"[ServerApp] 发送阻断: 协议 {meta.Id} 的方向为 {meta.Dir}，服务端只能发送 S2C 协议");
                return;
            }

            byte[] payload = _serializeFunc(msg);
            string roomId = meta.Scope == NetScope.Room ? session.CurrentRoomId : string.Empty;
            var packet = new Packet(0, meta.Id, meta.Scope, roomId, payload);
            _networkSender?.Invoke(session.ConnectionId, packet);
        }

        public Room CreateRoom(string roomId)
        {
            if (string.IsNullOrEmpty(roomId)) return null;
            if (_rooms.ContainsKey(roomId)) return null;

            // 核心修复：将 _serializeFunc 注入 Room，支撑房间级的强类型广播
            var room = new Room(roomId, SendPacketToConnection, _serializeFunc);
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
                Debug.LogWarning(
                    $"[ServerApp] 物理连接顶号: ConnectionId {connectionId} 原属 Session {oldSession.SessionId}，现被 {session.SessionId} 抢占");
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

        // 核心修复 (Point 2)：提供合法的 internal 查询接口，彻底消灭外部反射
        internal Session TryGetSessionByConnectionId(int connectionId)
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