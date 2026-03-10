using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Server.Components
{
    // 核心新增：添加组件元数据特性，驱动常量表生成
    [RoomComponent(1, "RoomSettings")]
    public sealed class ServerRoomSettingsComponent : RoomComponent
    {
        private readonly Dictionary<string, bool> _readyStates = new Dictionary<string, bool>();
        private string _ownerSessionId;

        public ServerRoomSettingsComponent(Func<object, byte[]> serializeFunc)
        {
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
                LiteLogger.LogWarning("ServerRoomSettings", "房主异常离线，触发房主移交机制", Room.RoomId, session.SessionId);
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
            Room.SendMessageTo(session, msg);
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

            if (session.SessionId != _ownerSessionId)
            {
                LiteLogger.LogWarning("ServerRoomSettings", "越权拦截: 非房主尝试开始游戏", Room.RoomId, session.SessionId);
                return;
            }

            if (Room.State != RoomState.Waiting)
            {
                LiteLogger.LogWarning("ServerRoomSettings", $"状态拦截: 房间当前状态为 {Room.State}，无法开始游戏", Room.RoomId,
                    session.SessionId);
                return;
            }

            foreach (var kvp in _readyStates)
            {
                if (kvp.Key != _ownerSessionId && !kvp.Value)
                {
                    LiteLogger.LogWarning("ServerRoomSettings", $"拦截: 玩家 {kvp.Key} 未准备，无法开始游戏", Room.RoomId,
                        session.SessionId);
                    return;
                }
            }

            Room.StartGame();

            var notify = new S2C_GameStarted { StartUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
            Room.BroadcastMessage(notify);
        }

        [NetHandler]
        public void OnC2S_EndGame(Session session, C2S_EndGame msg)
        {
            if (session == null) return;

            if (session.SessionId != _ownerSessionId)
            {
                LiteLogger.LogWarning("ServerRoomSettings", "越权拦截: 非房主尝试强制结束游戏", Room.RoomId, session.SessionId);
                return;
            }

            if (Room.State != RoomState.Playing)
            {
                LiteLogger.LogWarning("ServerRoomSettings", $"状态拦截: 房间当前状态为 {Room.State}，无法强制结束", Room.RoomId,
                    session.SessionId);
                return;
            }

            Room.EndGame();

            var notify = new S2C_GameEnded { WinnerSessionId = "房主强制中止" };
            Room.BroadcastMessage(notify);
        }
    }
}