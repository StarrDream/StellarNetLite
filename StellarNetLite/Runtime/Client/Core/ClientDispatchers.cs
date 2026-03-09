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

            if (_handlers.ContainsKey(msgId))
            {
                Debug.LogError($"[ClientGlobalDispatcher] 注册失败: MsgId {msgId} 已存在处理函数，禁止重复注册");
                return;
            }

            _handlers[msgId] = handler;
        }

        public void Dispatch(Packet packet)
        {
            if (!_handlers.TryGetValue(packet.MsgId, out var handler))
            {
                Debug.LogWarning($"[ClientGlobalDispatcher] 未找到 MsgId {packet.MsgId} 的处理函数，消息已忽略");
                return;
            }

            handler.Invoke(packet);
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

            if (_handlers.ContainsKey(msgId))
            {
                Debug.LogError($"[ClientRoomDispatcher] 注册失败: MsgId {msgId} 在房间 {_roomId} 中已存在处理函数");
                return;
            }

            _handlers[msgId] = handler;
        }

        public void Dispatch(Packet packet)
        {
            if (!_handlers.TryGetValue(packet.MsgId, out var handler))
            {
                Debug.LogWarning($"[ClientRoomDispatcher] 未找到 MsgId {packet.MsgId} 的处理函数，RoomId: {_roomId}，消息已忽略");
                return;
            }

            handler.Invoke(packet);
        }

        public void Clear()
        {
            _handlers.Clear();
        }
    }
}