using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;
using UnityEngine;

namespace StellarNet.Lite.Client.Core
{
    public sealed class ClientGlobalDispatcher
    {
        private readonly Dictionary<int, Action<Packet>> _handlers = new Dictionary<int, Action<Packet>>();

        public void Register(int msgId, Action<Packet> handler)
        {
            if (handler == null)
            {
                Debug.LogError($"[ClientGlobalDispatcher] 注册失败: 传入的 handler 为空，MsgId: {msgId}");
                return;
            }

            // 核心架构升级：支持多播委托。允许多个独立模块监听同一个全局协议
            if (_handlers.TryGetValue(msgId, out var existingHandler))
            {
                _handlers[msgId] = existingHandler + handler;
            }
            else
            {
                _handlers[msgId] = handler;
            }
        }

        public void Dispatch(Packet packet)
        {
            if (_handlers.TryGetValue(packet.MsgId, out var handler))
            {
                handler?.Invoke(packet);
            }
            else
            {
                Debug.LogWarning($"[ClientGlobalDispatcher] 未找到 MsgId {packet.MsgId} 的处理函数，消息已忽略");
            }
        }
    }

    public sealed class ClientRoomDispatcher
    {
        private readonly Dictionary<int, Action<Packet>> _handlers = new Dictionary<int, Action<Packet>>();
        private readonly string _roomId;

        public ClientRoomDispatcher(string roomId)
        {
            _roomId = roomId;
        }

        public void Register(int msgId, Action<Packet> handler)
        {
            if (handler == null)
            {
                Debug.LogError($"[ClientRoomDispatcher] 注册失败: 传入的 handler 为空，RoomId: {_roomId}, MsgId: {msgId}");
                return;
            }

            // 核心架构升级：支持多播委托。允许多个 RoomComponent 监听同一个房间协议
            if (_handlers.TryGetValue(msgId, out var existingHandler))
            {
                _handlers[msgId] = existingHandler + handler;
            }
            else
            {
                _handlers[msgId] = handler;
            }
        }

        public void Dispatch(Packet packet)
        {
            if (_handlers.TryGetValue(packet.MsgId, out var handler))
            {
                handler?.Invoke(packet);
            }
            else
            {
                Debug.LogWarning($"[ClientRoomDispatcher] 未找到 MsgId {packet.MsgId} 的处理函数，RoomId: {_roomId}，消息已忽略");
            }
        }

        public void Clear()
        {
            _handlers.Clear();
        }
    }
}