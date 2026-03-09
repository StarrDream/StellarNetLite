using System;
using System.Collections.Generic;
using UnityEngine;

namespace StellarNet.Lite.Client.Core
{
    public static class ClientRoomFactory
    {
        public static Action<ClientRoomComponent, ClientRoomDispatcher> ComponentBinder;
        private static readonly Dictionary<int, Func<ClientRoomComponent>> _registry = new Dictionary<int, Func<ClientRoomComponent>>();

        public static void Register(int componentId, Func<ClientRoomComponent> componentBuilder)
        {
            if (componentBuilder == null)
            {
                Debug.LogError($"[ClientRoomFactory] 注册失败: 传入的构造器为空, ComponentId: {componentId}");
                return;
            }

            if (_registry.ContainsKey(componentId))
            {
                Debug.LogError($"[ClientRoomFactory] 注册失败: ComponentId {componentId} 已存在，禁止重复注册");
                return;
            }

            _registry[componentId] = componentBuilder;
        }

        // 核心改造：将返回值改为 bool，实施强阻断策略。任何组件缺失都将导致整个房间装配失败
        public static bool BuildComponents(ClientRoom room, int[] componentIds)
        {
            if (room == null)
            {
                Debug.LogError("[ClientRoomFactory] 装配阻断: 传入的 room 为空");
                return false;
            }

            if (componentIds == null || componentIds.Length == 0)
            {
                Debug.LogWarning($"[ClientRoomFactory] 装配警告: 房间 {room.RoomId} 的组件清单为空");
                return true;
            }

            foreach (int id in componentIds)
            {
                if (_registry.TryGetValue(id, out var builder))
                {
                    var comp = builder.Invoke();
                    room.AddComponent(comp);
                    ComponentBinder?.Invoke(comp, room.Dispatcher);
                }
                else
                {
                    // 核心防御：发现未知组件，立即阻断后续装配，防止产生残缺的房间实例
                    Debug.LogError($"[ClientRoomFactory] 装配致命失败: 本地未注册 ComponentId {id}。客户端版本可能过旧，拒绝进入残缺房间");
                    return false;
                }
            }

            return true;
        }
    }
}