using System;
using StellarNet.Lite.Shared.Core;
using UnityEngine;

namespace StellarNet.Lite.Client.Core
{
    public enum ClientAppState
    {
        Idle,
        OnlineRoom,
        ReplayRoom
    }

    public sealed class ClientApp
    {
        public ClientSession Session { get; } = new ClientSession();
        public ClientGlobalDispatcher GlobalDispatcher { get; } = new ClientGlobalDispatcher();
        public ClientRoom CurrentRoom { get; private set; }
        public ClientAppState State { get; private set; } = ClientAppState.Idle;

        private readonly Action<Packet> _networkSender;

        public ClientApp(Action<Packet> networkSender)
        {
            _networkSender = networkSender;
        }

        public void OnReceivePacket(Packet packet)
        {
            if (packet.Scope == NetScope.Global)
            {
                GlobalDispatcher.Dispatch(packet);
            }
            else if (packet.Scope == NetScope.Room)
            {
                if (State == ClientAppState.ReplayRoom)
                {
                    Debug.LogWarning($"[ClientApp] 拦截: 回放模式下禁止接收真实网络房间包, MsgId: {packet.MsgId}");
                    return;
                }

                if (CurrentRoom == null)
                {
                    Debug.LogError($"[ClientApp] 路由阻断: 当前不在任何房间中，却收到 Room 消息，MsgId: {packet.MsgId}");
                    return;
                }

                if (packet.RoomId != CurrentRoom.RoomId)
                {
                    Debug.LogError($"[ClientApp] 路由阻断: 房间上下文不匹配。Packet.RoomId: {packet.RoomId}, CurrentRoom.RoomId: {CurrentRoom.RoomId}");
                    return;
                }

                CurrentRoom.Dispatcher.Dispatch(packet);
            }
        }

        public void EnterOnlineRoom(string roomId)
        {
            if (string.IsNullOrEmpty(roomId)) return;

            if (State != ClientAppState.Idle)
            {
                Debug.LogError($"[ClientApp] 进入在线房间失败: 当前状态为 {State}，必须先退出");
                return;
            }

            CurrentRoom = new ClientRoom(roomId);
            Session.BindRoom(roomId);
            State = ClientAppState.OnlineRoom;
        }

        public void EnterReplayRoom(string roomId)
        {
            if (string.IsNullOrEmpty(roomId)) return;

            if (State != ClientAppState.Idle)
            {
                Debug.LogError($"[ClientApp] 进入回放房间失败: 当前状态为 {State}，必须先退出");
                return;
            }

            CurrentRoom = new ClientRoom(roomId);
            State = ClientAppState.ReplayRoom;
        }

        public void LeaveRoom()
        {
            if (CurrentRoom != null)
            {
                CurrentRoom.Destroy();
                CurrentRoom = null;
            }

            Session.UnbindRoom();
            State = ClientAppState.Idle;
        }

        public void SendGlobal(Packet packet)
        {
            packet.Scope = NetScope.Global;
            packet.RoomId = string.Empty;
            _networkSender?.Invoke(packet);
        }

        public void SendRoom(Packet packet)
        {
            if (State == ClientAppState.ReplayRoom)
            {
                Debug.LogWarning("[ClientApp] 拦截: 回放模式下禁止发送网络包，已自动丢弃");
                return;
            }

            if (State != ClientAppState.OnlineRoom || CurrentRoom == null)
            {
                Debug.LogError("[ClientApp] 发送房间消息失败: 当前不在在线房间中");
                return;
            }

            packet.Scope = NetScope.Room;
            packet.RoomId = CurrentRoom.RoomId;
            _networkSender?.Invoke(packet);
        }
    }
}