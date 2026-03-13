using System.Collections.Generic;
using UnityEngine;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Client.Core.Events;

namespace StellarNet.Lite.Client.Core
{
    public sealed class ClientRoom
    {
        public string RoomId { get; }
        public ClientRoomDispatcher Dispatcher { get; }

        // 核心重构：挂载全新的实例化沙盒事件系统
        public RoomNetEventSystem NetEventSystem { get; }

        private readonly List<ClientRoomComponent> _components = new List<ClientRoomComponent>();

        private ClientRoom(string roomId)
        {
            RoomId = roomId;
            Dispatcher = new ClientRoomDispatcher(roomId);
            NetEventSystem = new RoomNetEventSystem(roomId);
        }

        public static ClientRoom Create(string roomId)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                NetLogger.LogError("[ClientRoom] ", $" 房间创建阻断: 传入的 roomId 为空，拒绝实例化");
                return null;
            }

            return new ClientRoom(roomId);
        }

        public void AddComponent(ClientRoomComponent component)
        {
            if (component == null)
            {
                NetLogger.LogError($"[ClientRoom] ", $" 添加组件失败: component 为空，RoomId: {RoomId}");
                return;
            }

            component.Room = this;
            _components.Add(component);
        }

        public T GetComponent<T>() where T : ClientRoomComponent
        {
            for (int i = 0; i < _components.Count; i++)
            {
                if (_components[i] is T target)
                {
                    return target;
                }
            }

            return null;
        }

        public void InitializeComponents()
        {
            for (int i = 0; i < _components.Count; i++)
            {
                _components[i].OnInit();
            }
        }

        public void Destroy()
        {
            for (int i = 0; i < _components.Count; i++)
            {
                _components[i].OnDestroy();
            }

            _components.Clear();
            Dispatcher.Clear();
            NetEventSystem.Clear();
        }
    }
}