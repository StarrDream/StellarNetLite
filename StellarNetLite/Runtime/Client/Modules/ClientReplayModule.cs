using System;
using UnityEngine;
using Newtonsoft.Json;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Client.Core;

namespace StellarNet.Lite.Client.Modules
{
    /// <summary>
    /// 客户端录像模块。
    /// 职责：接收服务端下发的录像列表与录像文件数据，反序列化后派发给表现层。
    /// </summary>
    public sealed class ClientReplayModule
    {
        private readonly ClientApp _app;
        private readonly Action<Packet> _networkSender;
        private readonly Func<object, byte[]> _serializeFunc;

        public ClientReplayModule(ClientApp app, Action<Packet> networkSender, Func<object, byte[]> serializeFunc)
        {
            _app = app;
            _networkSender = networkSender;
            _serializeFunc = serializeFunc;
        }

        [NetHandler]
        public void OnS2C_ReplayList(S2C_ReplayList msg)
        {
            if (msg == null) return;
            LiteEventBus<ReplayListEvent>.Fire(new ReplayListEvent { ReplayIds = msg.ReplayIds ?? new string[0] });
        }

        [NetHandler]
        public void OnS2C_DownloadReplayResult(S2C_DownloadReplayResult msg)
        {
            if (msg == null) return;

            if (msg.Success)
            {
                try
                {
                    // 核心逻辑：将服务端传来的 JSON 字符串反序列化为完整的 ReplayFile 对象
                    var replayFile = JsonConvert.DeserializeObject<ReplayFile>(msg.ReplayFileData);
                    if (replayFile != null)
                    {
                        LiteEventBus<ReplayDownloadedEvent>.Fire(new ReplayDownloadedEvent { Success = true, File = replayFile });
                        Debug.Log($"[ClientReplayModule] 录像下载并解析成功，帧数: {replayFile.Frames.Count}");
                    }
                    else
                    {
                        LiteEventBus<ReplayDownloadedEvent>.Fire(new ReplayDownloadedEvent { Success = false, Reason = "录像数据反序列化为空" });
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ClientReplayModule] 录像解析异常: {e.Message}");
                    LiteEventBus<ReplayDownloadedEvent>.Fire(new ReplayDownloadedEvent { Success = false, Reason = "录像文件损坏或格式不匹配" });
                }
            }
            else
            {
                Debug.LogError($"[ClientReplayModule] 录像下载失败: {msg.Reason}");
                LiteEventBus<ReplayDownloadedEvent>.Fire(new ReplayDownloadedEvent { Success = false, Reason = msg.Reason });
            }
        }
    }
}