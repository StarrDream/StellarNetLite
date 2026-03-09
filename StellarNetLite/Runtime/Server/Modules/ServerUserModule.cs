using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Shared.Infrastructure;
using UnityEngine;

namespace StellarNet.Lite.Server.Modules
{
    public sealed class ServerUserModule
    {
        private readonly ServerApp _app;
        private readonly Action<int, Packet> _networkSender;
        private readonly Func<object, byte[]> _serializeFunc;
        private readonly NetConfig _config;
        private readonly Dictionary<string, Session> _accountToSession = new Dictionary<string, Session>();

        public ServerUserModule(ServerApp app, Action<int, Packet> networkSender, Func<object, byte[]> serializeFunc,
            NetConfig config)
        {
            _app = app;
            _networkSender = networkSender;
            _serializeFunc = serializeFunc;
            _config = config;
        }

        [NetHandler]
        public void OnC2S_Login(Session session, C2S_Login msg)
        {
            if (session == null || msg == null || string.IsNullOrEmpty(msg.AccountId))
            {
                LiteLogger.LogError("ServerUserModule", $"登录失败: 参数非法", "-", session?.SessionId);
                return;
            }

            if (string.IsNullOrEmpty(msg.ClientVersion))
            {
                LiteLogger.LogWarning("ServerUserModule", "登录拦截: 客户端未提供版本号", "-", session.SessionId);
                var rejectRes = new S2C_LoginResult { Success = false, Reason = "客户端版本过旧，请更新游戏" };
                _app.SendMessageToSession(session, rejectRes);
                return;
            }

            if (Version.TryParse(msg.ClientVersion, out Version clientVer) &&
                Version.TryParse(_config.MinClientVersion, out Version minVer))
            {
                if (clientVer < minVer)
                {
                    LiteLogger.LogWarning("ServerUserModule",
                        $"登录拦截: 客户端版本 {msg.ClientVersion} 低于最低要求 {_config.MinClientVersion}", "-", session.SessionId);
                    var rejectRes = new S2C_LoginResult
                        { Success = false, Reason = $"客户端版本过旧，请更新至 {_config.MinClientVersion} 或以上版本" };
                    _app.SendMessageToSession(session, rejectRes);
                    return;
                }
            }
            else if (msg.ClientVersion != _config.MinClientVersion)
            {
                LiteLogger.LogWarning("ServerUserModule",
                    $"登录拦截: 客户端版本 {msg.ClientVersion} 不匹配要求 {_config.MinClientVersion}", "-", session.SessionId);
                var rejectRes = new S2C_LoginResult
                    { Success = false, Reason = $"客户端版本不匹配，请更新至 {_config.MinClientVersion}" };
                _app.SendMessageToSession(session, rejectRes);
                return;
            }

            if (_accountToSession.TryGetValue(msg.AccountId, out var oldSession))
            {
                if (oldSession.IsOnline)
                {
                    LiteLogger.LogWarning("ServerUserModule", $"账号在其他设备登录，踢出旧连接", oldSession.CurrentRoomId,
                        oldSession.SessionId);
                    var kickMsg = new S2C_KickOut { Reason = "账号在其他设备登录" };
                    _app.SendMessageToSession(oldSession, kickMsg);
                    _app.UnbindConnection(oldSession);
                }

                _app.RemoveSession(session.SessionId);
                _app.BindConnection(oldSession, session.ConnectionId);

                // 核心修复：同步新物理连接的 Seq 到旧 Session，对齐防重放基线
                oldSession.ResetSeq(session.LastReceivedSeq);

                bool hasRoom = !string.IsNullOrEmpty(oldSession.CurrentRoomId) &&
                               _app.GetRoom(oldSession.CurrentRoomId) != null;

                var reconnectRes = new S2C_LoginResult
                {
                    Success = true,
                    SessionId = oldSession.SessionId,
                    HasReconnectRoom = hasRoom,
                    Reason = string.Empty
                };

                _app.SendMessageToSession(oldSession, reconnectRes);
                LiteLogger.LogInfo("ServerUserModule", $"玩家断线重连(顶号)成功，Seq 状态已重置对齐", oldSession.CurrentRoomId,
                    oldSession.SessionId);
                return;
            }

            _app.RemoveSession(session.SessionId);
            var authSession = new Session(session.SessionId, msg.AccountId, session.ConnectionId);
            // 首次登录，继承当前 UNAUTH session 的 Seq (即 1)
            authSession.ResetSeq(session.LastReceivedSeq);

            _accountToSession[msg.AccountId] = authSession;
            _app.RegisterSession(authSession);

            var res = new S2C_LoginResult
            {
                Success = true,
                SessionId = authSession.SessionId,
                HasReconnectRoom = false,
                Reason = string.Empty
            };

            _app.SendMessageToSession(authSession, res);
            LiteLogger.LogInfo("ServerUserModule", $"玩家全新登录成功", "-", authSession.SessionId);
        }

        [NetHandler]
        public void OnC2S_ConfirmReconnect(Session session, C2S_ConfirmReconnect msg)
        {
            if (session == null || msg == null) return;

            string roomId = session.CurrentRoomId;
            Room room = string.IsNullOrEmpty(roomId) ? null : _app.GetRoom(roomId);

            if (!msg.Accept)
            {
                if (room != null)
                {
                    room.RemoveMember(session);
                }

                session.UnbindRoom();
                var rejectRes = new S2C_ReconnectResult { Success = false, Reason = "已放弃重连" };
                _app.SendMessageToSession(session, rejectRes);
                return;
            }

            if (room == null)
            {
                session.UnbindRoom();
                var failRes = new S2C_ReconnectResult { Success = false, Reason = "房间已解散" };
                _app.SendMessageToSession(session, failRes);
                return;
            }

            var successRes = new S2C_ReconnectResult
            {
                Success = true,
                RoomId = room.RoomId,
                ComponentIds = room.ComponentIds,
                Reason = string.Empty
            };

            _app.SendMessageToSession(session, successRes);
        }

        [NetHandler]
        public void OnC2S_ReconnectReady(Session session, C2S_ReconnectReady msg)
        {
            if (session == null || msg == null) return;

            string roomId = session.CurrentRoomId;
            if (string.IsNullOrEmpty(roomId))
            {
                LiteLogger.LogError("ServerUserModule", "握手阻断: 尚未绑定房间，无法下发快照", "-", session.SessionId);
                return;
            }

            Room room = _app.GetRoom(roomId);
            if (room == null)
            {
                LiteLogger.LogError("ServerUserModule", "握手阻断: 房间不存在", roomId, session.SessionId);
                return;
            }

            room.TriggerReconnectSnapshot(session);
            LiteLogger.LogInfo("ServerUserModule", "客户端装配就绪，已下发房间全量快照", roomId, session.SessionId);
        }
    }
}