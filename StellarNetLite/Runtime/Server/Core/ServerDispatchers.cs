using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;
using UnityEngine;

namespace StellarNet.Lite.Server.Core
{
    public sealed class GlobalDispatcher
    {
        private readonly Dictionary<int, Action<Session, Packet>> _handlers = new Dictionary<int, Action<Session, Packet>>();

        public void Register(int msgId, Action<Session, Packet> handler)
        {
            if (handler == null)
            {
                Debug.LogError($"[GlobalDispatcher] 注册失败: 传入的 handler 为空，MsgId: {msgId}");
                return;
            }

            if (_handlers.ContainsKey(msgId))
            {
                Debug.LogError($"[GlobalDispatcher] 注册失败: MsgId {msgId} 已存在处理函数，禁止重复注册");
                return;
            }

            _handlers[msgId] = handler;
        }

        public void Dispatch(Session session, Packet packet)
        {
            if (session == null)
            {
                Debug.LogError($"[GlobalDispatcher] 分发失败: session 为空");
                return;
            }

            if (!_handlers.TryGetValue(packet.MsgId, out var handler))
            {
                Debug.LogError($"[GlobalDispatcher] 分发失败: 未找到 MsgId {packet.MsgId} 的处理函数");
                return;
            }

            handler.Invoke(session, packet);
        }
    }

    public sealed class RoomDispatcher
    {
        private readonly Dictionary<int, Action<Session, Packet>> _handlers = new Dictionary<int, Action<Session, Packet>>();
        private readonly string _roomId;

        public RoomDispatcher(string roomId)
        {
            _roomId = roomId;
        }

        public void Register(int msgId, Action<Session, Packet> handler)
        {
            if (handler == null)
            {
                Debug.LogError($"[RoomDispatcher] 注册失败: 传入的 handler 为空，RoomId: {_roomId}, MsgId: {msgId}");
                return;
            }

            if (_handlers.ContainsKey(msgId))
            {
                Debug.LogError($"[RoomDispatcher] 注册失败: MsgId {msgId} 在房间 {_roomId} 中已存在处理函数");
                return;
            }

            _handlers[msgId] = handler;
        }

        public void Dispatch(Session session, Packet packet)
        {
            if (session == null)
            {
                Debug.LogError($"[RoomDispatcher] 分发失败: session 为空，RoomId: {_roomId}");
                return;
            }

            if (!_handlers.TryGetValue(packet.MsgId, out var handler))
            {
                Debug.LogError($"[RoomDispatcher] 分发失败: 未找到 MsgId {packet.MsgId} 的处理函数，RoomId: {_roomId}");
                return;
            }

            handler.Invoke(session, packet);
        }

        public void Clear()
        {
            _handlers.Clear();
        }
    }
}