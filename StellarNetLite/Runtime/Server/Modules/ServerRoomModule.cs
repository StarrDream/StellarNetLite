using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Shared.Infrastructure;
using UnityEngine;

namespace StellarNet.Lite.Server.Modules
{
    [GlobalModule("ServerRoomModule", "房间生命周期模块")]
    public sealed class ServerRoomModule
    {
        private readonly ServerApp _app;

        public ServerRoomModule(ServerApp app)
        {
            _app = app;
        }

        [NetHandler]
        public void OnC2S_CreateRoom(Session session, C2S_CreateRoom msg)
        {
            if (session == null || msg == null) return;

            if (!string.IsNullOrEmpty(session.CurrentRoomId))
            {
                var failMsg = new S2C_CreateRoomResult { Success = false, Reason = "已在房间中" };
                _app.SendMessageToSession(session, failMsg);
                return;
            }

            string roomId = Guid.NewGuid().ToString("N").Substring(0, 8);
            var room = _app.CreateRoom(roomId);

            if (room == null)
            {
                var failMsg = new S2C_CreateRoomResult { Success = false, Reason = "服务器内部错误" };
                _app.SendMessageToSession(session, failMsg);
                return;
            }

            room.Config.RoomName = string.IsNullOrEmpty(msg.RoomName) ? $"房间_{roomId}" : msg.RoomName;
            room.Config.MaxMembers = msg.MaxMembers <= 0 ? 4 : msg.MaxMembers;
            room.Config.Password = msg.Password ?? string.Empty;

            int[] uniqueComponentIds = DeduplicateComponentIds(msg.ComponentIds);
            bool buildSuccess = ServerRoomFactory.BuildComponents(room, uniqueComponentIds);

            if (!buildSuccess)
            {
                _app.DestroyRoom(roomId);
                NetLogger.LogError($"[ServerRoomModule]", $"房间 {roomId} 组件装配失败，已强制销毁该残缺实例");
                var failMsg = new S2C_CreateRoomResult { Success = false, Reason = "房间组件装配失败，存在非法组件" };
                _app.SendMessageToSession(session, failMsg);
                return;
            }

            room.SetComponentIds(uniqueComponentIds);

            var successMsg = new S2C_CreateRoomResult
            {
                Success = true,
                RoomId = roomId,
                ComponentIds = uniqueComponentIds,
                Reason = string.Empty
            };

            session.AuthorizeRoom(roomId);
            _app.SendMessageToSession(session, successMsg);
        }

        [NetHandler]
        public void OnC2S_JoinRoom(Session session, C2S_JoinRoom msg)
        {
            if (session == null || msg == null) return;

            if (!string.IsNullOrEmpty(session.CurrentRoomId))
            {
                var failMsg = new S2C_JoinRoomResult { Success = false, Reason = "已在房间中" };
                _app.SendMessageToSession(session, failMsg);
                return;
            }

            Room room = _app.GetRoom(msg.RoomId);
            if (room == null)
            {
                var failMsg = new S2C_JoinRoomResult { Success = false, Reason = "房间不存在" };
                _app.SendMessageToSession(session, failMsg);
                return;
            }

            if (room.MemberCount >= room.Config.MaxMembers)
            {
                NetLogger.LogWarning("[ServerRoomModule]", $"加入拦截: 房间人数已满", msg.RoomId, session.SessionId);
                var failMsg = new S2C_JoinRoomResult { Success = false, Reason = "房间人数已满" };
                _app.SendMessageToSession(session, failMsg);
                return;
            }

            if (room.Config.IsPrivate && room.Config.Password != msg.Password)
            {
                NetLogger.LogWarning("[ServerRoomModule]", $"加入拦截: 密码错误", msg.RoomId, session.SessionId);
                var failMsg = new S2C_JoinRoomResult { Success = false, Reason = "房间密码错误" };
                _app.SendMessageToSession(session, failMsg);
                return;
            }

            var successMsg = new S2C_JoinRoomResult
            {
                Success = true,
                RoomId = room.RoomId,
                ComponentIds = room.ComponentIds,
                Reason = string.Empty
            };

            session.AuthorizeRoom(room.RoomId);
            _app.SendMessageToSession(session, successMsg);
        }

        [NetHandler]
        public void OnC2S_RoomSetupReady(Session session, C2S_RoomSetupReady msg)
        {
            if (session == null || msg == null) return;

            if (string.IsNullOrEmpty(msg.RoomId))
            {
                NetLogger.LogError($"[ServerRoomModule]", $"握手阻断: RoomId 为空, SessionId: {session.SessionId}");
                return;
            }

            if (string.IsNullOrEmpty(session.AuthorizedRoomId) || session.AuthorizedRoomId != msg.RoomId)
            {
                NetLogger.LogError(
                    $"[ServerRoomModule]", $"握手阻断: 越权访问或授权已失效。目标房间: {msg.RoomId}, 授权房间: {session.AuthorizedRoomId}");
                return;
            }

            if (!string.IsNullOrEmpty(session.CurrentRoomId))
            {
                NetLogger.LogError($"[ServerRoomModule]", $"握手阻断: 玩家已在房间 {session.CurrentRoomId} 中，非法调用首次加入握手");
                return;
            }

            Room room = _app.GetRoom(msg.RoomId);
            if (room == null)
            {
                NetLogger.LogError($"[ServerRoomModule]", $"握手阻断: 目标房间不存在, RoomId: {msg.RoomId}");
                return;
            }

            room.AddMember(session);
            session.ClearAuthorizedRoom();
            NetLogger.LogInfo($"[ServerRoomModule]", $"客户端 {session.SessionId} 首次装配就绪，正式加入房间 {msg.RoomId} 并下发快照");
        }

        [NetHandler]
        public void OnC2S_LeaveRoom(Session session, C2S_LeaveRoom msg)
        {
            if (session == null) return;

            string roomId = session.CurrentRoomId;
            if (string.IsNullOrEmpty(roomId)) return;

            Room room = _app.GetRoom(roomId);
            if (room != null)
            {
                room.RemoveMember(session);
                if (room.MemberCount == 0)
                {
                    _app.DestroyRoom(roomId);
                    NetLogger.LogInfo($"[ServerRoomModule]", $"房间 {roomId} 已空，执行自动销毁");
                }
            }

            var successMsg = new S2C_LeaveRoomResult { Success = true };
            _app.SendMessageToSession(session, successMsg);
        }

        private int[] DeduplicateComponentIds(int[] rawIds)
        {
            if (rawIds == null || rawIds.Length == 0) return new int[0];
            var set = new HashSet<int>();
            var list = new List<int>(rawIds.Length);
            for (int i = 0; i < rawIds.Length; i++)
            {
                if (set.Add(rawIds[i]))
                {
                    list.Add(rawIds[i]);
                }
            }

            return list.ToArray();
        }
    }
}