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

            // 核心架构升级：支持多播委托
            if (_handlers.TryGetValue(msgId, out var existingHandler))
            {
                _handlers[msgId] = existingHandler + handler;
            }
            else
            {
                _handlers[msgId] = handler;
            }
        }

        public void Dispatch(Session session, Packet packet)
        {
            if (session == null)
            {
                Debug.LogError($"[GlobalDispatcher] 分发失败: session 为空");
                return;
            }

            if (_handlers.TryGetValue(packet.MsgId, out var handler))
            {
                handler?.Invoke(session, packet);
            }
            else
            {
                Debug.LogWarning($"[GlobalDispatcher] 未找到 MsgId {packet.MsgId} 的处理函数");
            }
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

            // 核心架构升级：支持多播委托
            if (_handlers.TryGetValue(msgId, out var existingHandler))
            {
                _handlers[msgId] = existingHandler + handler;
            }
            else
            {
                _handlers[msgId] = handler;
            }
        }

        public void Dispatch(Session session, Packet packet)
        {
            if (session == null)
            {
                Debug.LogError($"[RoomDispatcher] 分发失败: session 为空，RoomId: {_roomId}");
                return;
            }

            if (_handlers.TryGetValue(packet.MsgId, out var handler))
            {
                handler?.Invoke(session, packet);
            }
            else
            {
                Debug.LogWarning($"[RoomDispatcher] 未找到 MsgId {packet.MsgId} 的处理函数，RoomId: {_roomId}");
            }
        }

        public void Clear()
        {
            _handlers.Clear();
        }
    }
}