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

        // 核心修复：处理房主异常掉线（未离开房间，但物理连接断开）
        public override void OnMemberOffline(Session session)
        {
            if (session == null) return;

            if (_ownerSessionId == session.SessionId)
            {
                Debug.LogWarning($"[ServerRoomSettings] 房主 {session.SessionId} 异常离线，触发房主移交机制");
                MigrateHost();
            }
        }

        // 辅助方法：智能移交房主权限
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
                        fallbackSessionId = kvp.Key; // 记录第一个作为保底
                    }

                    if (memberSession.IsOnline)
                    {
                        _ownerSessionId = kvp.Key; // 优先移交给在线玩家
                        break;
                    }
                }
            }

            // 如果全员离线，则交由保底玩家（等待其重连或房间超时销毁）
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
            if (session == null)
            {
                Debug.LogError("[ServerRoomSettings] 准备失败: session 为空");
                return;
            }

            if (msg == null)
            {
                Debug.LogError($"[ServerRoomSettings] 准备失败: msg 为空, SessionId: {session.SessionId}");
                return;
            }

            if (!_readyStates.ContainsKey(session.SessionId))
            {
                Debug.LogError($"[ServerRoomSettings] 准备失败: 玩家不在房间中, SessionId: {session.SessionId}");
                return;
            }

            _readyStates[session.SessionId] = msg.IsReady;
            var notify = new S2C_MemberReadyChanged { SessionId = session.SessionId, IsReady = msg.IsReady };
            Broadcast(304, notify);
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