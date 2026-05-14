using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using UnityEngine;

namespace StellarNet.Lite.Client.Modules
{
    /// <summary>
    /// 客户端 Ping 模块。
    /// 负责把 Pong 转换为本地 RTT 结果。
    /// </summary>
    [ClientModule("ClientPingModule", "全局延迟心跳模块")]
    public sealed class ClientPingModule
    {
        private readonly ClientApp _app;

        public ClientPingModule(ClientApp app)
        {
            _app = app;
        }

        [NetHandler]
        public void OnS2C_Pong(S2C_Pong msg)
        {
            float now = Time.realtimeSinceStartup;
            float rtt = now - msg.ClientTime;
            GlobalTypeNetEvent.Broadcast(new Local_PingResult { RttMs = rtt * 1000f, ReceivedRealtime = now });
        }
    }
}
