using System;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Client.Core;
using UnityEngine;

namespace StellarNet.Lite.Client.Modules
{
    public sealed class ClientRoomModule
    {
        private readonly ClientApp _app;
        private readonly Action<Packet> _networkSender;
        private readonly Func<object, byte[]> _serializeFunc;

        public ClientRoomModule(ClientApp app, Action<Packet> networkSender, Func<object, byte[]> serializeFunc)
        {
            _app = app;
            _networkSender = networkSender;
            _serializeFunc = serializeFunc;
        }

        [NetHandler]
        public void OnS2C_CreateRoomResult(S2C_CreateRoomResult msg)
        {
            if (msg == null)
            {
                Debug.LogError("[ClientRoomModule] 处理建房结果失败: msg 为空");
                return;
            }

            // 核心修复 4：状态机防御
            if (_app.State == ClientAppState.ReplayRoom)
            {
                Debug.LogWarning("[ClientRoomModule] 拦截: 当前处于回放模式，忽略建房结果");
                return;
            }

            if (msg.Success)
            {
                _app.EnterOnlineRoom(msg.RoomId);

                bool buildSuccess = ClientRoomFactory.BuildComponents(_app.CurrentRoom, msg.ComponentIds);
                if (!buildSuccess)
                {
                    Debug.LogError($"[ClientRoomModule] 房间 {msg.RoomId} 本地装配失败，已强制销毁本地实例并终止握手");
                    _app.LeaveRoom();
                    return;
                }

                Debug.Log($"[ClientRoomModule] 建房成功, 本地装配完毕，准备发送就绪握手。房间: {msg.RoomId}");
                var readyMsg = new C2S_RoomSetupReady { RoomId = msg.RoomId };
                SendGlobal(206, readyMsg);
            }
            else
            {
                Debug.LogError($"[ClientRoomModule] 建房失败: {msg.Reason}");
            }
        }

        [NetHandler]
        public void OnS2C_JoinRoomResult(S2C_JoinRoomResult msg)
        {
            if (msg == null)
            {
                Debug.LogError("[ClientRoomModule] 处理加房结果失败: msg 为空");
                return;
            }

            // 核心修复 4：状态机防御
            if (_app.State == ClientAppState.ReplayRoom)
            {
                Debug.LogWarning("[ClientRoomModule] 拦截: 当前处于回放模式，忽略加房结果");
                return;
            }

            if (msg.Success)
            {
                _app.EnterOnlineRoom(msg.RoomId);

                bool buildSuccess = ClientRoomFactory.BuildComponents(_app.CurrentRoom, msg.ComponentIds);
                if (!buildSuccess)
                {
                    Debug.LogError($"[ClientRoomModule] 房间 {msg.RoomId} 本地装配失败，已强制销毁本地实例并终止握手");
                    _app.LeaveRoom();
                    return;
                }

                Debug.Log($"[ClientRoomModule] 加房成功, 本地装配完毕，准备发送就绪握手。房间: {msg.RoomId}");
                var readyMsg = new C2S_RoomSetupReady { RoomId = msg.RoomId };
                SendGlobal(206, readyMsg);
            }
            else
            {
                Debug.LogError($"[ClientRoomModule] 加房失败: {msg.Reason}");
            }
        }

        [NetHandler]
        public void OnS2C_LeaveRoomResult(S2C_LeaveRoomResult msg)
        {
            if (msg == null)
            {
                Debug.LogError("[ClientRoomModule] 处理离房结果失败: msg 为空");
                return;
            }

            if (_app.State == ClientAppState.ReplayRoom)
            {
                Debug.LogWarning("[ClientRoomModule] 拦截: 当前处于回放模式，忽略离房结果");
                return;
            }

            _app.LeaveRoom();
            Debug.Log("[ClientRoomModule] 已离开房间");
        }

        private void SendGlobal(int msgId, object msgObj)
        {
            byte[] payload = _serializeFunc(msgObj);
            var packet = new Packet(msgId, NetScope.Global, string.Empty, payload);
            _networkSender?.Invoke(packet);
        }
    }
}