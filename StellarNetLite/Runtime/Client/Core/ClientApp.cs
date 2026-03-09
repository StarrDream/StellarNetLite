using System;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
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
        private readonly Func<object, byte[]> _serializeFunc;
        private uint _sendSeq = 0;

        public ClientApp(Action<Packet> networkSender, Func<object, byte[]> serializeFunc)
        {
            _networkSender = networkSender;
            _serializeFunc = serializeFunc;
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
                    LiteLogger.LogWarning("ClientApp", $"拦截: 回放模式下禁止接收真实网络房间包, MsgId: {packet.MsgId}");
                    return;
                }

                if (CurrentRoom == null)
                {
                    LiteLogger.LogError("ClientApp", $"路由阻断: 当前不在任何房间中，却收到 Room 消息, MsgId: {packet.MsgId}");
                    return;
                }

                if (packet.RoomId != CurrentRoom.RoomId)
                {
                    LiteLogger.LogError("ClientApp",
                        $"路由阻断: 房间上下文不匹配。PacketRoom: {packet.RoomId}, CurrentRoom: {CurrentRoom.RoomId}");
                    return;
                }

                CurrentRoom.Dispatcher.Dispatch(packet);
            }
        }

        // 核心修复 (Point 11)：状态机统一封锁与审计矩阵
        private bool TryChangeState(ClientAppState targetState)
        {
            if (State == targetState) return true;

            bool isValidTransition = false;
            switch (State)
            {
                case ClientAppState.Idle:
                    // Idle 只能进入 OnlineRoom 或 ReplayRoom
                    isValidTransition = (targetState == ClientAppState.OnlineRoom ||
                                         targetState == ClientAppState.ReplayRoom);
                    break;
                case ClientAppState.OnlineRoom:
                case ClientAppState.ReplayRoom:
                    // 房间内只能退回 Idle，绝对禁止 Online <-> Replay 互跳
                    isValidTransition = (targetState == ClientAppState.Idle);
                    break;
            }

            if (!isValidTransition)
            {
                LiteLogger.LogError("ClientApp", $"非法状态迁移拦截: 无法从 {State} 直接切换到 {targetState}，必须先退回 Idle 状态。");
                return false;
            }

            LiteLogger.LogInfo("ClientApp", $"状态机迁移: {State} -> {targetState}");
            State = targetState;
            return true;
        }

        public void EnterOnlineRoom(string roomId)
        {
            if (string.IsNullOrEmpty(roomId)) return;

            if (!TryChangeState(ClientAppState.OnlineRoom)) return;

            CurrentRoom = ClientRoom.Create(roomId);
            if (CurrentRoom == null)
            {
                TryChangeState(ClientAppState.Idle); // 回滚状态
                return;
            }

            Session.BindRoom(roomId);
        }

        public void EnterReplayRoom(string roomId)
        {
            if (string.IsNullOrEmpty(roomId)) return;

            if (!TryChangeState(ClientAppState.ReplayRoom)) return;

            CurrentRoom = ClientRoom.Create(roomId);
            if (CurrentRoom == null)
            {
                TryChangeState(ClientAppState.Idle); // 回滚状态
                return;
            }
        }

        public void LeaveRoom()
        {
            if (CurrentRoom != null)
            {
                CurrentRoom.Destroy();
                CurrentRoom = null;
            }

            Session.UnbindRoom();
            TryChangeState(ClientAppState.Idle);
        }

        public void SendMessage<T>(T msg) where T : class
        {
            if (msg == null)
            {
                LiteLogger.LogError("ClientApp", "发送失败: 消息对象为空");
                return;
            }

            if (!NetMessageMapper.TryGetMeta(typeof(T), out var meta))
            {
                LiteLogger.LogError("ClientApp", $"发送失败: 未找到类型 {typeof(T).Name} 的网络元数据，请检查是否添加了 [NetMsg] 特性");
                return;
            }

            if (meta.Dir != NetDir.C2S)
            {
                LiteLogger.LogError("ClientApp", $"发送阻断: 协议 {meta.Id} 的方向为 {meta.Dir}，客户端只能发送 C2S 协议");
                return;
            }

            if (State == ClientAppState.ReplayRoom)
            {
                LiteLogger.LogWarning("ClientApp", $"拦截: 回放模式下禁止发送网络包，已自动丢弃协议 {meta.Id}");
                return;
            }

            if (meta.Scope == NetScope.Room && (State != ClientAppState.OnlineRoom || CurrentRoom == null))
            {
                LiteLogger.LogError("ClientApp", $"发送失败: 协议 {meta.Id} 作用域为 Room，但当前不在在线房间中");
                return;
            }

            _sendSeq++;
            byte[] payload = _serializeFunc(msg);
            string roomId = meta.Scope == NetScope.Room ? CurrentRoom.RoomId : string.Empty;
            var packet = new Packet(_sendSeq, meta.Id, meta.Scope, roomId, payload);
            _networkSender?.Invoke(packet);
        }
    }
}