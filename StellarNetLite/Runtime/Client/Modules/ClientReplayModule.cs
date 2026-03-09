using System;
using UnityEngine;
using Newtonsoft.Json;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Shared.Infrastructure;

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
            GlobalEventBus<ReplayListEvent>.Fire(new ReplayListEvent { ReplayIds = msg.ReplayIds ?? new string[0] });
        }

        [NetHandler]
        public void OnS2C_DownloadReplayResult(S2C_DownloadReplayResult msg)
        {
            if (msg == null) return;

            // 核心修复 (Point 14)：前置拦截，拒绝深层嵌套与宽泛的 Try-Catch
            if (!msg.Success)
            {
                LiteLogger.LogError("ClientReplayModule", $"录像下载失败: {msg.Reason}");
                GlobalEventBus<ReplayDownloadedEvent>.Fire(new ReplayDownloadedEvent
                    { Success = false, Reason = msg.Reason });
                return;
            }

            if (string.IsNullOrEmpty(msg.ReplayFileData))
            {
                LiteLogger.LogError("ClientReplayModule", "录像下载失败: 服务端返回的录像数据为空");
                GlobalEventBus<ReplayDownloadedEvent>.Fire(new ReplayDownloadedEvent
                    { Success = false, Reason = "录像数据为空" });
                return;
            }

            try
            {
                // 仅在处理不可控的 JSON 反序列化时使用 try-catch
                var replayFile = JsonConvert.DeserializeObject<ReplayFile>(msg.ReplayFileData);
                if (replayFile != null)
                {
                    GlobalEventBus<ReplayDownloadedEvent>.Fire(new ReplayDownloadedEvent
                        { Success = true, File = replayFile });
                    LiteLogger.LogInfo("ClientReplayModule", $"录像下载并解析成功，帧数: {replayFile.Frames.Count}");
                }
                else
                {
                    LiteLogger.LogError("ClientReplayModule", "录像解析失败: 反序列化结果为 null");
                    GlobalEventBus<ReplayDownloadedEvent>.Fire(new ReplayDownloadedEvent
                        { Success = false, Reason = "录像数据反序列化为空" });
                }
            }
            catch (Exception e)
            {
                LiteLogger.LogError("ClientReplayModule", $"录像解析异常: {e.Message}");
                GlobalEventBus<ReplayDownloadedEvent>.Fire(new ReplayDownloadedEvent
                    { Success = false, Reason = "录像文件损坏或格式不匹配" });
            }
        }
    }
}