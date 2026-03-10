using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Infrastructure;
using UnityEngine;

namespace StellarNet.Lite.Client.Core
{
    public static class ClientRoomFactory
    {
        public static Action<ClientRoomComponent, ClientRoomDispatcher> ComponentBinder;

        private static readonly Dictionary<int, Func<ClientRoomComponent>> _registry =
            new Dictionary<int, Func<ClientRoomComponent>>();

        public static void Register(int componentId, Func<ClientRoomComponent> componentBuilder)
        {
            if (componentBuilder == null)
            {
                LiteLogger.LogError($"[ClientRoomFactory] ", $" 注册失败: 传入的构造器为空, ComponentId: {componentId}");
                return;
            }

            if (_registry.ContainsKey(componentId))
            {
                LiteLogger.LogError($"[ClientRoomFactory] ", $" 注册失败: ComponentId {componentId} 已存在，禁止重复注册");
                return;
            }

            _registry[componentId] = componentBuilder;
        }

        /// <summary>
        /// 原子化装配客户端房间组件。
        /// 架构意图：采用两阶段提交（Two-Phase Commit）策略。
        /// 核心修复 (Point 5)：引入 try-catch 事务回滚，确保装配失败时不产生半残房间。
        /// </summary>
        public static bool BuildComponents(ClientRoom room, int[] componentIds)
        {
            if (room == null)
            {
                LiteLogger.LogError("[ClientRoomFactory] ", $" 装配阻断: 传入的 room 为空");
                return false;
            }

            if (componentIds == null || componentIds.Length == 0)
            {
                LiteLogger.LogWarning($"[ClientRoomFactory]", $"  装配警告: 房间 {room.RoomId} 的组件清单为空");
                return true;
            }

            // 阶段一：全量校验与实例化 (All or Nothing)
            var pendingComponents = new List<ClientRoomComponent>(componentIds.Length);
            foreach (int id in componentIds)
            {
                if (_registry.TryGetValue(id, out var builder))
                {
                    pendingComponents.Add(builder.Invoke());
                }
                else
                {
                    LiteLogger.LogError($"[ClientRoomFactory]", $"  装配致命失败: 本地未注册 ComponentId {id}。客户端版本可能过旧，拒绝进入残缺房间");
                    return false;
                }
            }

            // 阶段二与阶段三：带事务回滚的装配与激活
            try
            {
                foreach (var comp in pendingComponents)
                {
                    room.AddComponent(comp);
                    ComponentBinder?.Invoke(comp, room.Dispatcher);
                }

                room.InitializeComponents();
                return true;
            }
            catch (Exception e)
            {
                LiteLogger.LogError($"[ClientRoomFactory]", $"  房间 {room.RoomId} 装配期间发生异常，触发原子回滚: {e.Message}\n{e.StackTrace}");
                room.Destroy();
                return false;
            }
        }
    }
}