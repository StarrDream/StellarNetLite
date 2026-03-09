#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Editor
{
    public class NetConfigEditorWindow : EditorWindow
    {
        private NetConfig _currentConfig = new NetConfig();
        private ConfigRootPath _targetRoot = ConfigRootPath.StreamingAssets;

        [MenuItem("StellarNet/Lite 网络配置 (NetConfig)")]
        public static void ShowWindow()
        {
            var window = GetWindow<NetConfigEditorWindow>("NetConfig Editor");
            window.minSize = new Vector2(400, 420);
            window.Show();
        }

        private void OnEnable()
        {
            LoadFromCurrentRoot();
        }

        private void OnGUI()
        {
            GUILayout.Space(10);
            EditorGUILayout.LabelField("StellarNet Lite 全局网络配置", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("修改后点击保存，将自动写入到对应的 NetConfig/netconfig.json 文件中。", MessageType.Info);
            GUILayout.Space(10);

            EditorGUI.BeginChangeCheck();
            _targetRoot = (ConfigRootPath)EditorGUILayout.EnumPopup("存储目录 (Root Path):", _targetRoot);
            if (EditorGUI.EndChangeCheck())
            {
                LoadFromCurrentRoot();
            }

            GUILayout.Space(10);
            EditorGUILayout.BeginVertical("box");

            _currentConfig.Ip = EditorGUILayout.TextField("服务器 IP:", _currentConfig.Ip);
            int port = EditorGUILayout.IntField("端口 (Port):", _currentConfig.Port);
            _currentConfig.Port = (ushort)Mathf.Clamp(port, 0, 65535);
            _currentConfig.MaxConnections = EditorGUILayout.IntField("最大连接数:", _currentConfig.MaxConnections);
            _currentConfig.TickRate = EditorGUILayout.IntField("服务器帧率 (TickRate):", _currentConfig.TickRate);

            GUILayout.Space(5);
            EditorGUILayout.LabelField("生产环境防御配置 (GC & 熔断)", EditorStyles.boldLabel);
            _currentConfig.MaxRoomLifetimeHours = EditorGUILayout.IntField("房间最大存活(小时):", _currentConfig.MaxRoomLifetimeHours);
            _currentConfig.MaxReplayFiles = EditorGUILayout.IntField("最大录像保留数:", _currentConfig.MaxReplayFiles);

            _currentConfig.OfflineTimeoutLobbyMinutes = EditorGUILayout.IntField("大厅离线GC(分钟):", _currentConfig.OfflineTimeoutLobbyMinutes);
            _currentConfig.OfflineTimeoutRoomMinutes = EditorGUILayout.IntField("房间离线GC(分钟):", _currentConfig.OfflineTimeoutRoomMinutes);

            // 核心新增：暴露空房间防暴毙配置
            _currentConfig.EmptyRoomTimeoutMinutes = EditorGUILayout.IntField("空房间熔断(分钟):", _currentConfig.EmptyRoomTimeoutMinutes);

            EditorGUILayout.EndVertical();

            GUILayout.Space(20);
            GUI.color = Color.green;
            if (GUILayout.Button("保存配置 (Save Config)", GUILayout.Height(35)))
            {
                SaveToCurrentRoot();
            }

            GUI.color = Color.white;

            GUILayout.Space(10);
            if (GUILayout.Button("在资源管理器中打开目录"))
            {
                OpenFolderInExplorer();
            }
        }

        private void LoadFromCurrentRoot()
        {
            _currentConfig = NetConfigLoader.LoadEditorSync(_targetRoot);
        }

        private void SaveToCurrentRoot()
        {
            if (_currentConfig == null) return;

            string basePath = _targetRoot == ConfigRootPath.StreamingAssets
                ? Application.streamingAssetsPath
                : Application.persistentDataPath;
            string folderPath = Path.Combine(basePath, NetConfigLoader.ConfigFolderName).Replace("\\", "/");
            string fullPath = Path.Combine(folderPath, NetConfigLoader.ConfigFileName).Replace("\\", "/");

            try
            {
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }

                string json = JsonConvert.SerializeObject(_currentConfig, Formatting.Indented);
                File.WriteAllText(fullPath, json);
                AssetDatabase.Refresh();
                Debug.Log($"[NetConfigEditor] 配置保存成功! 路径: {fullPath}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[NetConfigEditor] 保存配置失败: {e.Message}");
            }
        }

        private void OpenFolderInExplorer()
        {
            string basePath = _targetRoot == ConfigRootPath.StreamingAssets
                ? Application.streamingAssetsPath
                : Application.persistentDataPath;
            string folderPath = Path.Combine(basePath, NetConfigLoader.ConfigFolderName).Replace("\\", "/");

            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            EditorUtility.RevealInFinder(folderPath);
        }
    }
}
#endif