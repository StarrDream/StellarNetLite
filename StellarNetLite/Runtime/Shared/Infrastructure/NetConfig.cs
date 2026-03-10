using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;

namespace StellarNet.Lite.Shared.Infrastructure
{
    public enum ConfigRootPath
    {
        StreamingAssets,
        PersistentDataPath
    }

    [Serializable]
    public sealed class NetConfig
    {
        public string Ip = "127.0.0.1";
        public ushort Port = 7777;
        public int MaxConnections = 200;
        public int TickRate = 60;
        public int MaxRoomLifetimeHours = 24;
        public int MaxReplayFiles = 100;
        public int OfflineTimeoutLobbyMinutes = 5;
        public int OfflineTimeoutRoomMinutes = 60;
        public int EmptyRoomTimeoutMinutes = 5;

        //最低允许的客户端版本号，用于阻断过旧版本的连接
        public string MinClientVersion = "0.0.1";
    }

    public static class NetConfigLoader
    {
        public const string ConfigFolderName = "NetConfig";
        public const string ConfigFileName = "netconfig.json";

        public static async Task<NetConfig> LoadAsync(ConfigRootPath rootPath)
        {
            string basePath = rootPath == ConfigRootPath.StreamingAssets
                ? Application.streamingAssetsPath
                : Application.persistentDataPath;

            string fullPath = Path.Combine(basePath, ConfigFolderName, ConfigFileName).Replace("\\", "/");
            string jsonContent = string.Empty;

            try
            {
                if (rootPath == ConfigRootPath.StreamingAssets && Application.platform == RuntimePlatform.Android)
                {
                    jsonContent = await ReadViaWebRequestAsync(fullPath);
                }
                else
                {
                    if (!File.Exists(fullPath))
                    {
                        return new NetConfig();
                    }

                    jsonContent = File.ReadAllText(fullPath);
                }

                if (string.IsNullOrEmpty(jsonContent)) return new NetConfig();

                var config = JsonConvert.DeserializeObject<NetConfig>(jsonContent);
                return config ?? new NetConfig();
            }
            catch (Exception e)
            {
                LiteLogger.LogError($"[NetConfigLoader]",$"  读取配置异常: {e.Message}");
                return new NetConfig();
            }
        }

        public static NetConfig LoadServerConfigSync(ConfigRootPath rootPath)
        {
            string basePath = rootPath == ConfigRootPath.StreamingAssets
                ? Application.streamingAssetsPath
                : Application.persistentDataPath;

            string fullPath = Path.Combine(basePath, ConfigFolderName, ConfigFileName).Replace("\\", "/");

            if (!File.Exists(fullPath))
            {
                LiteLogger.LogWarning($"[NetConfigLoader]",$"  未找到配置文件 {fullPath}，将使用默认配置启动服务器。");
                return new NetConfig();
            }

            try
            {
                string json = File.ReadAllText(fullPath);
                var config = JsonConvert.DeserializeObject<NetConfig>(json);
                return config ?? new NetConfig();
            }
            catch (Exception e)
            {
                LiteLogger.LogError($"[NetConfigLoader] ",$" 服务端同步读取配置异常: {e.Message}");
                return new NetConfig();
            }
        }

        private static Task<string> ReadViaWebRequestAsync(string url)
        {
            var tcs = new TaskCompletionSource<string>();
            var request = UnityWebRequest.Get(url);
            var operation = request.SendWebRequest();

            operation.completed += op =>
            {
                if (request.result == UnityWebRequest.Result.ConnectionError ||
                    request.result == UnityWebRequest.Result.ProtocolError)
                {
                    tcs.SetException(new Exception(request.error));
                }
                else
                {
                    tcs.SetResult(request.downloadHandler.text);
                }

                request.Dispose();
            };

            return tcs.Task;
        }

#if UNITY_EDITOR
        public static NetConfig LoadEditorSync(ConfigRootPath rootPath)
        {
            return LoadServerConfigSync(rootPath);
        }
#endif
    }
}