using System;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Shared.Infrastructure;
using UnityEngine;

namespace StellarNet.Lite.Client.Modules
{
    public sealed class ClientUserModule
    {
        private readonly ClientApp _app;
        private readonly Action<Packet> _networkSender;
        private readonly Func<object, byte[]> _serializeFunc;

        public ClientUserModule(ClientApp app, Action<Packet> networkSender, Func<object, byte[]> serializeFunc)
        {
            _app = app;
            _networkSender = networkSender;
            _serializeFunc = serializeFunc;
        }

        [NetHandler]
        public void OnS2C_LoginResult(S2C_LoginResult msg)
        {
            if (msg == null) return;

            if (msg.Success)
            {
                _app.Session.OnLoginSuccess(msg.SessionId, "UID_PLACEHOLDER");
                LiteLogger.LogInfo($"[ClientUserModule]",$"  登录成功, SessionId: {msg.SessionId}");

                if (msg.HasReconnectRoom)
                {
                    LiteLogger.LogInfo("[ClientUserModule] ",$" 发现可重连房间，等待玩家选择...");
                }
            }
            else
            {
                LiteLogger.LogError($"[ClientUserModule]",$"  登录失败: {msg.Reason}");
            }
        }

        [NetHandler]
        public void OnS2C_ReconnectResult(S2C_ReconnectResult msg)
        {
            if (msg == null) return;

            // 核心防御：状态机防御。如果玩家正在看回放，收到重连成功包必须丢弃，防止覆盖回放沙盒
            if (_app.State == ClientAppState.ReplayRoom)
            {
                LiteLogger.LogWarning("[ClientUserModule] ",$" 拦截: 当前处于回放模式，忽略重连结果");
                return;
            }

            if (msg.Success)
            {
                _app.EnterOnlineRoom(msg.RoomId);
                bool buildSuccess = ClientRoomFactory.BuildComponents(_app.CurrentRoom, msg.ComponentIds);

                if (!buildSuccess)
                {
                    LiteLogger.LogError($"[ClientUserModule]",$"  重连房间 {msg.RoomId} 本地装配失败，已强制销毁本地实例并终止重连握手");
                    _app.LeaveRoom();
                    return;
                }

                LiteLogger.LogInfo($"[ClientUserModule]",$"  重连房间 {msg.RoomId} 本地装配完毕，准备发送就绪握手");
                var readyMsg = new C2S_ReconnectReady();
                _app.SendMessage(readyMsg);
            }
            else
            {
                LiteLogger.LogInfo($"[ClientUserModule]",$"  重连结束: {msg.Reason}");
            }
        }

        [NetHandler]
        public void OnS2C_KickOut(S2C_KickOut msg)
        {
            if (msg == null) return;

            // 核心防御：踢下线时，必须先清理本地房间状态，防止 UI 和业务逻辑残留
            if (_app.State == ClientAppState.OnlineRoom)
            {
                LiteLogger.LogWarning("[ClientUserModule]",$"  被踢下线，强制退出当前在线房间");
                _app.LeaveRoom();
            }

            _app.Session.Clear();
            LiteLogger.LogError($"[ClientUserModule] ",$" 被踢下线: {msg.Reason}");
        }
    }
}