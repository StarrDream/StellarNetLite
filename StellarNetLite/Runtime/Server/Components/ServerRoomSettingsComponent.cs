using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Server.Core;
using UnityEngine;

namespace StellarNet.Lite.Server.Components
{
    public sealed class ServerRoomSettingsComponent : RoomComponent
    {
        private readonly Dictionary<string, bool> _readyStates = new Dictionary<string, bool>();
        private string _ownerSessionId;
        private readonly Func<object, byte[]> _serializeFunc;

        public ServerRoomSettingsComponent(Func<object, byte[]> serializeFunc)
        {
            _serializeFunc = serializeFunc;
        }

        public override void OnInit()
        {
            _readyStates.Clear();
            _ownerSessionId = string.Empty;
        }

        public override void OnMemberJoined(Session session)
        {
            if (session == null) return;

            if (string.IsNullOrEmpty(_ownerSessionId))
            {
                _ownerSessionId = session.SessionId;
            }

            _readyStates[session.SessionId] = false;

            var msg = new S2C_MemberJoined { SessionId = session.SessionId };
            Broadcast(301, msg);

            OnSendSnapshot(session);
        }

        public override void OnMemberLeft(Session session)
        {
            if (session == null) return;

            _readyStates.Remove(session.SessionId);

            var msg = new S2C_MemberLeft { SessionId = session.SessionId };
            Broadcast(302, msg);

            if (_ownerSessionId == session.SessionId)
            {
                MigrateHost();
            }
        }

        public override void OnMemberOffline(Session session)
        {
            if (session == null) return;

            if (_ownerSessionId == session.SessionId)
            {
                Debug.LogWarning($"[ServerRoomSettings] 房主 {session.SessionId} 异常离线，触发房主移交机制");
                MigrateHost();
            }
        }

        private void MigrateHost()
        {
            _ownerSessionId = string.Empty;
            string fallbackSessionId = string.Empty;

            foreach (var kvp in _readyStates)
            {
                var memberSession = Room.GetMember(kvp.Key);
                if (memberSession != null)
                {
                    if (string.IsNullOrEmpty(fallbackSessionId))
                    {
                        fallbackSessionId = kvp.Key;
                    }

                    if (memberSession.IsOnline)
                    {
                        _ownerSessionId = kvp.Key;
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(_ownerSessionId))
            {
                _ownerSessionId = fallbackSessionId;
            }

            if (!string.IsNullOrEmpty(_ownerSessionId))
            {
                BroadcastSnapshotToAll();
            }
        }

        public override void OnSendSnapshot(Session session)
        {
            if (session == null) return;

            var members = new List<MemberInfo>();
            foreach (var kvp in _readyStates)
            {
                members.Add(new MemberInfo
                {
                    SessionId = kvp.Key,
                    IsReady = kvp.Value,
                    IsOwner = (kvp.Key == _ownerSessionId)
                });
            }

            var msg = new S2C_RoomSnapshot { Members = members.ToArray() };
            SendTo(session, 300, msg);
        }

        private void BroadcastSnapshotToAll()
        {
            var members = new List<MemberInfo>();
            foreach (var kvp in _readyStates)
            {
                members.Add(new MemberInfo
                {
                    SessionId = kvp.Key,
                    IsReady = kvp.Value,
                    IsOwner = (kvp.Key == _ownerSessionId)
                });
            }

            var msg = new S2C_RoomSnapshot { Members = members.ToArray() };
            Broadcast(300, msg);
        }

        [NetHandler]
        public void OnC2S_SetReady(Session session, C2S_SetReady msg)
        {
            if (session == null || msg == null) return;

            if (!_readyStates.ContainsKey(session.SessionId)) return;

            _readyStates[session.SessionId] = msg.IsReady;

            var notify = new S2C_MemberReadyChanged { SessionId = session.SessionId, IsReady = msg.IsReady };
            Broadcast(304, notify);
        }

        [NetHandler]
        public void OnC2S_StartGame(Session session, C2S_StartGame msg)
        {
            if (session == null) return;

            if (session.SessionId != _ownerSessionId)
            {
                Debug.LogWarning($"[ServerRoomSettings] 越权拦截: 非房主 {session.SessionId} 尝试开始游戏");
                return;
            }

            if (Room.State != RoomState.Waiting)
            {
                Debug.LogWarning($"[ServerRoomSettings] 状态拦截: 房间当前状态为 {Room.State}，无法开始游戏");
                return;
            }

            foreach (var kvp in _readyStates)
            {
                if (kvp.Key != _ownerSessionId && !kvp.Value)
                {
                    Debug.LogWarning($"[ServerRoomSettings] 拦截: 玩家 {kvp.Key} 未准备，无法开始游戏");
                    return;
                }
            }

            Room.StartGame();

            var notify = new S2C_GameStarted { StartUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
            Broadcast(501, notify);
        }

        // 核心新增：处理房主发起的强制结束游戏请求 (502)
        [NetHandler]
        public void OnC2S_EndGame(Session session, C2S_EndGame msg)
        {
            if (session == null) return;

            if (session.SessionId != _ownerSessionId)
            {
                Debug.LogWarning($"[ServerRoomSettings] 越权拦截: 非房主 {session.SessionId} 尝试强制结束游戏");
                return;
            }

            if (Room.State != RoomState.Playing)
            {
                Debug.LogWarning($"[ServerRoomSettings] 状态拦截: 房间当前状态为 {Room.State}，无法强制结束");
                return;
            }

            Room.EndGame();

            // 广播游戏结束事件 (503)
            var notify = new S2C_GameEnded { WinnerSessionId = "房主强制中止" };
            Broadcast(503, notify);
        }

        private void Broadcast(int msgId, object msgObj)
        {
            byte[] payload = _serializeFunc(msgObj);
            var packet = new Packet(msgId, NetScope.Room, Room.RoomId, payload);
            Room.Broadcast(packet);
        }

        private void SendTo(Session session, int msgId, object msgObj)
        {
            byte[] payload = _serializeFunc(msgObj);
            var packet = new Packet(msgId, NetScope.Room, Room.RoomId, payload);
            Room.SendTo(session, packet);
        }
    }
}