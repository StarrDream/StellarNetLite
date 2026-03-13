using System;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Client.Core.Events;

namespace StellarNet.Lite.Client.Modules
{
    [GlobalModule("ClientUserModule", "客户端用户模块")]
    public sealed class ClientUserModule
    {
        private readonly ClientApp _app;

        // 核心改造：统一极简构造函数
        public ClientUserModule(ClientApp app)
        {
            _app = app;
        }

        [NetHandler]
        public void OnS2C_LoginResult(S2C_LoginResult msg)
        {
            if (msg == null) return;

            if (msg.Success)
            {
                _app.Session.OnLoginSuccess(msg.SessionId, "UID_PLACEHOLDER");
                NetLogger.LogInfo($"[ClientUserModule]", $"登录成功, SessionId: {msg.SessionId}");

                if (msg.HasReconnectRoom)
                {
                    NetLogger.LogInfo("[ClientUserModule]", $"发现可重连房间，等待玩家选择...");
                }
            }
            else
            {
                NetLogger.LogError($"[ClientUserModule]", $"登录失败: {msg.Reason}");
            }

            GlobalTypeNetEvent.Broadcast(msg);
        }

        [NetHandler]
        public void OnS2C_ReconnectResult(S2C_ReconnectResult msg)
        {
            if (msg == null) return;

            if (_app.State == ClientAppState.ReplayRoom)
            {
                NetLogger.LogWarning("[ClientUserModule]", $"拦截: 当前处于回放模式，忽略重连结果");
                return;
            }

            if (msg.Success)
            {
                _app.EnterOnlineRoom(msg.RoomId);
                bool buildSuccess = ClientRoomFactory.BuildComponents(_app.CurrentRoom, msg.ComponentIds);

                if (!buildSuccess)
                {
                    NetLogger.LogError($"[ClientUserModule]", $"重连房间 {msg.RoomId} 本地装配失败，已强制销毁本地实例并终止重连握手");
                    _app.LeaveRoom();
                    return;
                }

                NetLogger.LogInfo($"[ClientUserModule]", $"重连房间 {msg.RoomId} 本地装配完毕，准备发送就绪握手");
                var readyMsg = new C2S_ReconnectReady();
                _app.SendMessage(readyMsg);
            }
            else
            {
                NetLogger.LogInfo($"[ClientUserModule]", $"重连结束: {msg.Reason}");
            }

            GlobalTypeNetEvent.Broadcast(msg);
        }

        [NetHandler]
        public void OnS2C_KickOut(S2C_KickOut msg)
        {
            if (msg == null) return;

            if (_app.State == ClientAppState.OnlineRoom)
            {
                NetLogger.LogWarning("[ClientUserModule]", $"被踢下线，强制退出当前在线房间");
                _app.LeaveRoom();
            }

            _app.Session.Clear();
            NetLogger.LogError($"[ClientUserModule]", $"被踢下线: {msg.Reason}");

            GlobalTypeNetEvent.Broadcast(msg);
        }
    }
}