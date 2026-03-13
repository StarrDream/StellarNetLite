using System;
using UnityEngine;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Client.Core.Events;

namespace StellarNet.Lite.Client.Modules
{
    [GlobalModule("ClientReplayModule", "客户端回放模块")]
    public sealed class ClientReplayModule
    {
        private readonly ClientApp _app;

        public ClientReplayModule(ClientApp app)
        {
            _app = app;
        }

        [NetHandler]
        public void OnS2C_ReplayList(S2C_ReplayList msg)
        {
            if (msg == null) return;
            GlobalTypeNetEvent.Broadcast(msg);
        }

        [NetHandler]
        public void OnS2C_DownloadReplayResult(S2C_DownloadReplayResult msg)
        {
            if (msg == null) return;

            if (!msg.Success)
            {
                NetLogger.LogError("ClientReplayModule", $"录像下载失败: {msg.Reason}");
            }
            else if (string.IsNullOrEmpty(msg.ReplayFileData))
            {
                NetLogger.LogError("ClientReplayModule", "录像下载失败: 服务端返回的录像数据为空");
            }
            else
            {
                NetLogger.LogInfo("ClientReplayModule", $"录像下载成功，准备派发给表现层解析");
            }

            GlobalTypeNetEvent.Broadcast(msg);
        }
    }
}