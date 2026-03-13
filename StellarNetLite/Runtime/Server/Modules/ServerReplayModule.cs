using System;
using System.IO;
using System.Linq;
using UnityEngine;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Server.Infrastructure;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Server.Modules
{
    [GlobalModule("ServerReplayModule", "录像下载与分发模块")]
    public sealed class ServerReplayModule
    {
        private readonly ServerApp _app;

        public ServerReplayModule(ServerApp app)
        {
            _app = app;
        }

        [NetHandler]
        public void OnC2S_GetReplayList(Session session, C2S_GetReplayList msg)
        {
            if (session == null) return;

            string folderPath = Path.Combine(Application.persistentDataPath, ServerReplayStorage.ReplayFolderName)
                .Replace("\\", "/");
            string[] replayIds = new string[0];

            if (Directory.Exists(folderPath))
            {
                try
                {
                    var files = new DirectoryInfo(folderPath).GetFiles("*.json")
                        .OrderByDescending(f => f.CreationTimeUtc)
                        .Take(10)
                        .ToArray();

                    replayIds = files.Select(f => Path.GetFileNameWithoutExtension(f.Name)).ToArray();
                }
                catch (Exception e)
                {
                    NetLogger.LogError("ServerReplayModule", $"读取录像列表异常: {e.Message}", "-", session.SessionId);
                }
            }

            var res = new S2C_ReplayList { ReplayIds = replayIds };
            _app.SendMessageToSession(session, res);
        }

        [NetHandler]
        public void OnC2S_DownloadReplay(Session session, C2S_DownloadReplay msg)
        {
            if (session == null || string.IsNullOrEmpty(msg.ReplayId)) return;

            string folderPath = Path.Combine(Application.persistentDataPath, ServerReplayStorage.ReplayFolderName)
                .Replace("\\", "/");
            string fullPath = Path.Combine(folderPath, $"{msg.ReplayId}.json").Replace("\\", "/");

            if (!File.Exists(fullPath))
            {
                NetLogger.LogWarning("ServerReplayModule", $"请求的录像文件不存在: {msg.ReplayId}", "-", session.SessionId);
                var notFoundRes = new S2C_DownloadReplayResult
                {
                    Success = false,
                    ReplayId = msg.ReplayId,
                    ReplayFileData = string.Empty,
                    Reason = "录像文件不存在或已被清理"
                };
                _app.SendMessageToSession(session, notFoundRes);
                return;
            }

            try
            {
                string json = File.ReadAllText(fullPath);
                var res = new S2C_DownloadReplayResult
                {
                    Success = true,
                    ReplayId = msg.ReplayId,
                    ReplayFileData = json,
                    Reason = string.Empty
                };
                _app.SendMessageToSession(session, res);
                NetLogger.LogInfo("ServerReplayModule", $"已向客户端下发录像 {msg.ReplayId}", "-", session.SessionId);
            }
            catch (Exception e)
            {
                NetLogger.LogError("ServerReplayModule", $"读取录像文件异常: {e.Message}", "-", session.SessionId);
                var errorRes = new S2C_DownloadReplayResult
                {
                    Success = false,
                    ReplayId = msg.ReplayId,
                    Reason = "服务器读取文件失败"
                };
                _app.SendMessageToSession(session, errorRes);
            }
        }
    }
}