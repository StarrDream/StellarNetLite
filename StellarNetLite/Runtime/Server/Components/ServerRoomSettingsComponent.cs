using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Server.Components
{
    [RoomComponent(1, "RoomSettings", "基础房间设置")]
    public sealed class ServerRoomSettingsComponent : RoomComponent
    {
        private readonly ServerApp _app;
        private readonly Dictionary<string, bool> _readyStates = new Dictionary<string, bool>();
        private string _ownerSessionId;

        public ServerRoomSettingsComponent(ServerApp app)
        {
            _app = app;
        }

        public override void OnInit()
        {
            _readyStates.Clear();
            _ownerSessionId = string.Empty;
        }

        // 核心工厂方法：将底层的 Session 转换为表现层的 MemberInfo
        private MemberInfo CreateMemberInfo(Session session, bool isReady, bool isOwner)
        {
            return new MemberInfo
            {
                SessionId = session.SessionId,
                Uid = session.Uid,
                // 这里假设 Session 中暂时没有 DisplayName，先用 Uid 兜底。
                // 后续如果你的 Session 扩展了 userData，可以直接写 DisplayName = session.DisplayName
                DisplayName = string.IsNullOrEmpty(session.Uid) ? "Unknown" : session.Uid,
                IsReady = isReady,
                IsOwner = isOwner
            };
        }

        public override void OnMemberJoined(Session session)
        {
            if (session == null) return;

            if (string.IsNullOrEmpty(_ownerSessionId))
            {
                _ownerSessionId = session.SessionId;
            }

            _readyStates[session.SessionId] = false;

            // 广播加入事件时，直接下发封装好的全量 MemberInfo
            var memberInfo = CreateMemberInfo(session, false, session.SessionId == _ownerSessionId);
            var msg = new S2C_MemberJoined { Member = memberInfo };
            Room.BroadcastMessage(msg);

            OnSendSnapshot(session);
        }

        public override void OnMemberLeft(Session session)
        {
            if (session == null) return;

            _readyStates.Remove(session.SessionId);

            var msg = new S2C_MemberLeft { SessionId = session.SessionId };
            Room.BroadcastMessage(msg);

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
                NetLogger.LogWarning("ServerRoomSettings", "房主异常离线，触发房主移交机制", Room.RoomId, session.SessionId);
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
                    if (string.IsNullOrEmpty(fallbackSessionId)) fallbackSessionId = kvp.Key;
                    if (memberSession.IsOnline)
                    {
                        _ownerSessionId = kvp.Key;
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(_ownerSessionId)) _ownerSessionId = fallbackSessionId;

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
                var memberSession = Room.GetMember(kvp.Key);
                if (memberSession != null)
                {
                    members.Add(CreateMemberInfo(memberSession, kvp.Value, kvp.Key == _ownerSessionId));
                }
            }

            var msg = new S2C_RoomSnapshot
            {
                RoomName = Room.Config.RoomName,
                MaxMembers = Room.Config.MaxMembers,
                IsPrivate = Room.Config.IsPrivate,
                Members = members.ToArray()
            };
            Room.SendMessageTo(session, msg);
        }

        private void BroadcastSnapshotToAll()
        {
            var members = new List<MemberInfo>();
            foreach (var kvp in _readyStates)
            {
                var memberSession = Room.GetMember(kvp.Key);
                if (memberSession != null)
                {
                    members.Add(CreateMemberInfo(memberSession, kvp.Value, kvp.Key == _ownerSessionId));
                }
            }

            var msg = new S2C_RoomSnapshot
            {
                RoomName = Room.Config.RoomName,
                MaxMembers = Room.Config.MaxMembers,
                IsPrivate = Room.Config.IsPrivate,
                Members = members.ToArray()
            };
            Room.BroadcastMessage(msg);
        }

        [NetHandler]
        public void OnC2S_SetReady(Session session, C2S_SetReady msg)
        {
            if (session == null || msg == null) return;
            if (!_readyStates.ContainsKey(session.SessionId)) return;

            _readyStates[session.SessionId] = msg.IsReady;

            var notify = new S2C_MemberReadyChanged { SessionId = session.SessionId, IsReady = msg.IsReady };
            Room.BroadcastMessage(notify);
        }

        [NetHandler]
        public void OnC2S_StartGame(Session session, C2S_StartGame msg)
        {
            if (session == null) return;
            if (session.SessionId != _ownerSessionId) return;
            if (Room.State != RoomState.Waiting) return;

            foreach (var kvp in _readyStates)
            {
                if (kvp.Key != _ownerSessionId && !kvp.Value) return;
            }

            Room.StartGame();
            var notify = new S2C_GameStarted { StartUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
            Room.BroadcastMessage(notify);
        }

        [NetHandler]
        public void OnC2S_EndGame(Session session, C2S_EndGame msg)
        {
            if (session == null) return;
            if (session.SessionId != _ownerSessionId) return;
            if (Room.State != RoomState.Playing) return;

            Room.EndGame();
            var notify = new S2C_GameEnded { WinnerSessionId = "房主强制中止" };
            Room.BroadcastMessage(notify);
        }
    }
}