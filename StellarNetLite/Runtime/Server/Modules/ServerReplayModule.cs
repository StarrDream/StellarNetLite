using System;
using System.IO;
using System.Linq;
using UnityEngine;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Server.Infrastructure;

namespace StellarNet.Lite.Server.Modules
{
    /// <summary>
    /// 服务端录像模块。
    /// 职责：处理客户端拉取录像列表与下载录像文件的请求。
    /// </summary>
    public sealed class ServerReplayModule
    {
        private readonly ServerApp _app;
        private readonly Action<int, Packet> _networkSender;
        private readonly Func<object, byte[]> _serializeFunc;

        public ServerReplayModule(ServerApp app, Action<int, Packet> networkSender, Func<object, byte[]> serializeFunc)
        {
            _app = app;
            _networkSender = networkSender;
            _serializeFunc = serializeFunc;
        }

        [NetHandler]
        public void OnC2S_GetReplayList(Session session, C2S_GetReplayList msg)
        {
            if (session == null) return;

            string folderPath = Path.Combine(Application.persistentDataPath, ServerReplayStorage.ReplayFolderName).Replace("\\", "/");
            string[] replayIds = new string[0];

            try
            {
                if (Directory.Exists(folderPath))
                {
                    // 获取最新的 10 个录像文件
                    var files = new DirectoryInfo(folderPath).GetFiles("*.json")
                        .OrderByDescending(f => f.CreationTimeUtc)
                        .Take(10)
                        .ToArray();

                    replayIds = files.Select(f => Path.GetFileNameWithoutExtension(f.Name)).ToArray();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ServerReplayModule] 读取录像列表异常: {e.Message}");
            }

            var res = new S2C_ReplayList { ReplayIds = replayIds };
            SendGlobal(session, 601, res);
        }

        [NetHandler]
        public void OnC2S_DownloadReplay(Session session, C2S_DownloadReplay msg)
        {
            if (session == null || string.IsNullOrEmpty(msg.ReplayId)) return;

            string folderPath = Path.Combine(Application.persistentDataPath, ServerReplayStorage.ReplayFolderName).Replace("\\", "/");
            string fullPath = Path.Combine(folderPath, $"{msg.ReplayId}.json").Replace("\\", "/");

            try
            {
                if (File.Exists(fullPath))
                {
                    string json = File.ReadAllText(fullPath);
                    var res = new S2C_DownloadReplayResult
                    {
                        Success = true,
                        ReplayId = msg.ReplayId,
                        ReplayFileData = json,
                        Reason = string.Empty
                    };
                    SendGlobal(session, 603, res);
                    Debug.Log($"[ServerReplayModule] 已向客户端 {session.SessionId} 下发录像 {msg.ReplayId}");
                }
                else
                {
                    var res = new S2C_DownloadReplayResult
                    {
                        Success = false,
                        ReplayId = msg.ReplayId,
                        ReplayFileData = string.Empty,
                        Reason = "录像文件不存在或已被清理"
                    };
                    SendGlobal(session, 603, res);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ServerReplayModule] 读取录像文件异常: {e.Message}");
                var res = new S2C_DownloadReplayResult { Success = false, ReplayId = msg.ReplayId, Reason = "服务器读取文件失败" };
                SendGlobal(session, 603, res);
            }
        }

        private void SendGlobal(Session session, int msgId, object msgObj)
        {
            byte[] payload = _serializeFunc(msgObj);
            var packet = new Packet(msgId, NetScope.Global, string.Empty, payload);
            _networkSender.Invoke(session.ConnectionId, packet);
        }
    }
}