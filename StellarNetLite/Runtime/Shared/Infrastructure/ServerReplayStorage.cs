using System;
using System.IO;
using System.Linq;
using UnityEngine;
using Newtonsoft.Json;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Server.Infrastructure
{
    /// <summary>
    /// 服务端录像存储服务。
    /// 职责：负责录像文件的序列化落地，并严格执行滚动淘汰（Rolling File Appender）策略，防止磁盘打满。
    /// </summary>
    public static class ServerReplayStorage
    {
        public const string ReplayFolderName = "Replays";

        public static void SaveReplay(ReplayFile replay, NetConfig config)
        {
            if (replay == null || config == null)
            {
                LiteLogger.LogError("[ServerReplayStorage] ",$" 保存失败: 传入的录像文件或配置为空");
                return;
            }

            if (string.IsNullOrEmpty(replay.ReplayId))
            {
                LiteLogger.LogError("[ServerReplayStorage] ",$" 保存失败: ReplayId 为空");
                return;
            }

            string basePath = Application.persistentDataPath;
            string folderPath = Path.Combine(basePath, ReplayFolderName).Replace("\\", "/");

            try
            {
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }

                string fileName = $"{replay.ReplayId}.json";
                string fullPath = Path.Combine(folderPath, fileName).Replace("\\", "/");

                string json = JsonConvert.SerializeObject(replay, Formatting.None);
                File.WriteAllText(fullPath, json);

                LiteLogger.LogInfo($"[ServerReplayStorage] ",$" 录像保存成功: {fileName}, 帧数: {replay.Frames.Count}");

                // 核心防御：保存成功后，立即触发滚动清理逻辑
                EnforceRollingLimit(folderPath, config.MaxReplayFiles);
            }
            catch (Exception e)
            {
                // 允许的 Try-Catch：底层 I/O 异常不可控，必须捕获防止主线程崩溃
                LiteLogger.LogError($"[ServerReplayStorage] ",$" 录像保存异常: {e.Message}");
            }
        }

        private static void EnforceRollingLimit(string folderPath, int maxFiles)
        {
            if (maxFiles <= 0) return;

            try
            {
                var dirInfo = new DirectoryInfo(folderPath);
                var files = dirInfo.GetFiles("*.json");

                if (files.Length <= maxFiles)
                {
                    return;
                }

                // 按创建时间降序排列（最新的在前）
                var sortedFiles = files.OrderByDescending(f => f.CreationTimeUtc).ToList();

                // 剔除超出阈值的旧文件
                for (int i = maxFiles; i < sortedFiles.Count; i++)
                {
                    sortedFiles[i].Delete();
                    LiteLogger.LogInfo($"[ServerReplayStorage] ",$" 滚动清理: 已删除过期录像文件 {sortedFiles[i].Name}");
                }
            }
            catch (Exception e)
            {
                LiteLogger.LogError($"[ServerReplayStorage] ",$" 滚动清理异常: {e.Message}");
            }
        }
    }
}