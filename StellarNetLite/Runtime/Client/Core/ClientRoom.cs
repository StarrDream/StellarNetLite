using System.Collections.Generic;
using UnityEngine;

namespace StellarNet.Lite.Client.Core
{
    public sealed class ClientRoom
    {
        public string RoomId { get; }
        public ClientRoomDispatcher Dispatcher { get; }

        private readonly List<ClientRoomComponent> _components = new List<ClientRoomComponent>();

        public ClientRoom(string roomId)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                Debug.LogError("[ClientRoom] 房间创建失败: roomId 为空");
                return;
            }

            RoomId = roomId;
            Dispatcher = new ClientRoomDispatcher(roomId);
        }

        public void AddComponent(ClientRoomComponent component)
        {
            if (component == null)
            {
                Debug.LogError($"[ClientRoom] 添加组件失败: component 为空，RoomId: {RoomId}");
                return;
            }

            component.Room = this;
            _components.Add(component);
            component.OnInit();
        }

        public void Destroy()
        {
            for (int i = 0; i < _components.Count; i++)
            {
                _components[i].OnDestroy();
            }

            _components.Clear();
            Dispatcher.Clear();
        }
    }
}