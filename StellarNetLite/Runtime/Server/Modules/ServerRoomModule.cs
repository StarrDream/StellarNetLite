using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Server.Core;
using UnityEngine;

namespace StellarNet.Lite.Server.Modules
{
    public sealed class ServerRoomModule
    {
        private readonly ServerApp _app;
        private readonly Action<int, Packet> _networkSender;
        private readonly Func<object, byte[]> _serializeFunc;

        private readonly Dictionary<string, S2C_CreateRoomResult> _idempotentCache = new Dictionary<string, S2C_CreateRoomResult>();
        private readonly Queue<string> _idempotentQueue = new Queue<string>();
        private const int MaxCacheSize = 10000;

        public ServerRoomModule(ServerApp app, Action<int, Packet> networkSender, Func<object, byte[]> serializeFunc)
        {
            _app = app;
            _networkSender = networkSender;
            _serializeFunc = serializeFunc;
        }

        [NetHandler]
        public void OnC2S_CreateRoom(Session session, C2S_CreateRoom msg)
        {
            if (session == null || msg == null) return;

            if (string.IsNullOrEmpty(msg.RequestToken))
            {
                Debug.LogError($"[ServerRoomModule] 建房阻断: RequestToken 为空, SessionId: {session.SessionId}");
                var failMsg = new S2C_CreateRoomResult { Success = false, Reason = "非法请求令牌" };
                SendGlobal(session, 201, failMsg);
                return;
            }

            if (_idempotentCache.TryGetValue(msg.RequestToken, out var cachedRes))
            {
                Debug.LogWarning($"[ServerRoomModule] 触发幂等拦截: Token {msg.RequestToken}，直接返回缓存结果");
                SendGlobal(session, 201, cachedRes);
                return;
            }

            if (!string.IsNullOrEmpty(session.CurrentRoomId))
            {
                var failMsg = new S2C_CreateRoomResult { Success = false, Reason = "已在房间中" };
                SendGlobal(session, 201, failMsg);
                return;
            }

            string roomId = Guid.NewGuid().ToString("N").Substring(0, 8);
            var room = _app.CreateRoom(roomId);
            if (room == null)
            {
                var failMsg = new S2C_CreateRoomResult { Success = false, Reason = "服务器内部错误" };
                SendGlobal(session, 201, failMsg);
                return;
            }

            int[] uniqueComponentIds = DeduplicateComponentIds(msg.ComponentIds);

            // 核心修复 2：强阻断装配。如果装配失败，立即销毁刚创建的房间，并返回失败结果
            bool buildSuccess = ServerRoomFactory.BuildComponents(room, uniqueComponentIds);
            if (!buildSuccess)
            {
                _app.DestroyRoom(roomId);
                Debug.LogError($"[ServerRoomModule] 房间 {roomId} 组件装配失败，已强制销毁该残缺实例");

                var failMsg = new S2C_CreateRoomResult { Success = false, Reason = "房间组件装配失败，存在非法组件" };
                SendGlobal(session, 201, failMsg);
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

            if (_idempotentQueue.Count >= MaxCacheSize)
            {
                string oldToken = _idempotentQueue.Dequeue();
                _idempotentCache.Remove(oldToken);
            }

            _idempotentCache[msg.RequestToken] = successMsg;
            _idempotentQueue.Enqueue(msg.RequestToken);

            session.AuthorizeRoom(roomId);
            SendGlobal(session, 201, successMsg);
        }

        [NetHandler]
        public void OnC2S_JoinRoom(Session session, C2S_JoinRoom msg)
        {
            if (session == null || msg == null) return;

            if (!string.IsNullOrEmpty(session.CurrentRoomId))
            {
                var failMsg = new S2C_JoinRoomResult { Success = false, Reason = "已在房间中" };
                SendGlobal(session, 203, failMsg);
                return;
            }

            Room room = _app.GetRoom(msg.RoomId);
            if (room == null)
            {
                var failMsg = new S2C_JoinRoomResult { Success = false, Reason = "房间不存在" };
                SendGlobal(session, 203, failMsg);
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
            SendGlobal(session, 203, successMsg);
        }

        [NetHandler]
        public void OnC2S_RoomSetupReady(Session session, C2S_RoomSetupReady msg)
        {
            if (session == null || msg == null) return;

            if (string.IsNullOrEmpty(msg.RoomId))
            {
                Debug.LogError($"[ServerRoomModule] 握手阻断: RoomId 为空, SessionId: {session.SessionId}");
                return;
            }

            if (string.IsNullOrEmpty(session.AuthorizedRoomId) || session.AuthorizedRoomId != msg.RoomId)
            {
                Debug.LogError($"[ServerRoomModule] 握手阻断: 越权访问或授权已失效。目标房间: {msg.RoomId}, 授权房间: {session.AuthorizedRoomId}");
                return;
            }

            if (!string.IsNullOrEmpty(session.CurrentRoomId))
            {
                Debug.LogError($"[ServerRoomModule] 握手阻断: 玩家已在房间 {session.CurrentRoomId} 中，非法调用首次加入握手");
                return;
            }

            Room room = _app.GetRoom(msg.RoomId);
            if (room == null)
            {
                Debug.LogError($"[ServerRoomModule] 握手阻断: 目标房间不存在, RoomId: {msg.RoomId}");
                return;
            }

            room.AddMember(session);

            session.ClearAuthorizedRoom();
            Debug.Log($"[ServerRoomModule] 客户端 {session.SessionId} 首次装配就绪，正式加入房间 {msg.RoomId} 并下发快照");
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
                    Debug.Log($"[ServerRoomModule] 房间 {roomId} 已空，执行自动销毁");
                }
            }

            var successMsg = new S2C_LeaveRoomResult { Success = true };
            SendGlobal(session, 205, successMsg);
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

        private void SendGlobal(Session session, int msgId, object msgObj)
        {
            byte[] payload = _serializeFunc(msgObj);
            var packet = new Packet(msgId, NetScope.Global, string.Empty, payload);
            _networkSender.Invoke(session.ConnectionId, packet);
        }
    }
}