using System.Collections.Generic;
using UnityEngine;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Client.Core
{
    public sealed class ClientRoom
    {
        public string RoomId { get; }
        public ClientRoomDispatcher Dispatcher { get; }
        public RoomEventBus EventBus { get; }

        private readonly List<ClientRoomComponent> _components = new List<ClientRoomComponent>();

        // 构造函数私有化，防止外部直接 new 出非法对象
        private ClientRoom(string roomId)
        {
            RoomId = roomId;
            Dispatcher = new ClientRoomDispatcher(roomId);
            EventBus = new RoomEventBus(roomId);
        }

        /// <summary>
        /// 安全创建客户端房间实例。
        /// 失败则返回 null，防止脏数据污染内存。
        /// </summary>
        public static ClientRoom Create(string roomId)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                LiteLogger.LogError("[ClientRoom] ",$" 房间创建阻断: 传入的 roomId 为空，拒绝实例化");
                return null;
            }

            return new ClientRoom(roomId);
        }

        public void AddComponent(ClientRoomComponent component)
        {
            if (component == null)
            {
                LiteLogger.LogError($"[ClientRoom] ",$" 添加组件失败: component 为空，RoomId: {RoomId}");
                return;
            }

            component.Room = this;
            _components.Add(component);
        }

        /// <summary>
        /// 核心新增：提供合法的组件查询接口，彻底封死外部反射 _components 的后门
        /// </summary>
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
            EventBus.Clear();
        }
    }
}