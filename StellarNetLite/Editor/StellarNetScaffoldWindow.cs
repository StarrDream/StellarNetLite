#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using StellarNet.Lite.Shared.Infrastructure;
using UnityEditor;
using UnityEngine;

namespace StellarNet.Lite.Editor.Tools
{
    /// <summary>
    /// StellarNet Lite 业务脚手架生成器。
    /// 职责：提供可视化的 UI 界面，一键生成符合 MSV 架构与防重放规范的模板代码。
    /// 架构说明：已对齐最新的无侵入式协议直抛架构，并全面适配 AutoRegistry 自动装配机制。
    /// </summary>
    public sealed class StellarNetScaffoldWindow : EditorWindow
    {
        #region ================= 数据结构与状态 =================

        private enum ModuleType
        {
            RoomComponent,
            GlobalModule
        }

        private ModuleType _currentType = ModuleType.RoomComponent;
        private string _moduleName = "NewFeature";
        private string _displayName = "新功能模块";
        private int _startProtocolId = 10000;
        private int _componentId = 10;
        private string _authorName = "Developer";
        private string _outputRootPath = "Assets/Scripts/Game";
        private string _baseNamespace = "Game";

        private bool _genProtocol = true;
        private bool _genServer = true;
        private bool _genClient = true;

        private GUIStyle _headerStyle;
        private GUIStyle _sectionStyle;
        private bool _stylesInitialized = false;

        #endregion

        [MenuItem("StellarNet/Lite 业务脚手架 (Scaffold)")]
        public static void ShowWindow()
        {
            var window = GetWindow<StellarNetScaffoldWindow>("StellarNet 脚手架");
            window.minSize = new Vector2(500, 600);
            window.Show();
        }

        #region ================= UI 绘制逻辑 =================

        private void OnGUI()
        {
            InitializeStyles();

            GUILayout.Space(10);
            GUILayout.Label("StellarNet Lite 业务代码生成器", _headerStyle);
            EditorGUILayout.HelpBox("输入模块名称与起始协议 ID，工具将自动在指定的业务目录下生成 Shared、Server、Client 脚本，并自动打上装配特性。",
                MessageType.Info);
            GUILayout.Space(10);

            DrawMainPanel();

            GUILayout.FlexibleSpace();
            DrawActionBar();
        }

        private void InitializeStyles()
        {
            if (_stylesInitialized) return;

            _headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter
            };

            _sectionStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.3f, 0.6f, 0.9f) }
            };

            _stylesInitialized = true;
        }

        private void DrawMainPanel()
        {
            EditorGUILayout.BeginVertical("box");
            GUILayout.Space(5);
            GUILayout.Label("目录与命名空间配置", _sectionStyle);
            GUILayout.Space(5);

            _outputRootPath = EditorGUILayout.TextField("输出根目录 (Root Path):", _outputRootPath);
            _baseNamespace = EditorGUILayout.TextField("业务命名空间 (Namespace):", _baseNamespace);

            GUILayout.Space(15);
            GUILayout.Label("业务模块配置", _sectionStyle);
            GUILayout.Space(5);

            _currentType = (ModuleType)EditorGUILayout.EnumPopup("模块类型 (Type):", _currentType);

            EditorGUI.BeginChangeCheck();
            _moduleName = EditorGUILayout.TextField("模块类名 (Name):", _moduleName);
            if (EditorGUI.EndChangeCheck())
            {
                // 防御性编程：强制剔除空格与非法字符，防止生成非法的 C# 类名
                _moduleName = System.Text.RegularExpressions.Regex.Replace(_moduleName, @"[^a-zA-Z0-9_]", "");
            }

            _displayName = EditorGUILayout.TextField("中文展示名 (DisplayName):", _displayName);
            _startProtocolId = EditorGUILayout.IntField("起始协议 ID:", _startProtocolId);

            if (_currentType == ModuleType.RoomComponent)
            {
                _componentId = EditorGUILayout.IntField("组件 ID (ComponentId):", _componentId);
            }

            _authorName = EditorGUILayout.TextField("开发者 (Author):", _authorName);

            GUILayout.Space(15);
            GUILayout.Label("生成选项", _sectionStyle);
            GUILayout.Space(5);

            _genProtocol = EditorGUILayout.Toggle("生成 Shared 协议定义", _genProtocol);
            _genServer = EditorGUILayout.Toggle("生成 Server 端逻辑", _genServer);
            _genClient = EditorGUILayout.Toggle("生成 Client 端逻辑", _genClient);

            EditorGUILayout.EndVertical();
        }

        private void DrawActionBar()
        {
            EditorGUILayout.BeginVertical("box");
            GUI.backgroundColor = new Color(0.2f, 0.8f, 0.2f);
            if (GUILayout.Button("一键生成业务代码 (Generate)", GUILayout.Height(40)))
            {
                ExecuteGeneration();
            }

            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndVertical();
        }

        #endregion

        #region ================= 代码生成核心逻辑 =================

        private void ExecuteGeneration()
        {
            if (string.IsNullOrEmpty(_moduleName))
            {
                NetLogger.LogError("[Scaffold]", $" 生成阻断: 模块名称不能为空");
                EditorUtility.DisplayDialog("错误", "模块名称不能为空！", "确定");
                return;
            }

            if (_startProtocolId <= 0)
            {
                NetLogger.LogError($"[Scaffold] ", $"生成阻断: 协议 ID {_startProtocolId} 非法，必须大于 0");
                EditorUtility.DisplayDialog("错误", "协议 ID 必须大于 0！", "确定");
                return;
            }

            if (_currentType == ModuleType.RoomComponent && _componentId <= 0)
            {
                NetLogger.LogError($"[Scaffold]", $" 生成阻断: 组件 ID {_componentId} 非法，必须大于 0");
                EditorUtility.DisplayDialog("错误", "组件 ID 必须大于 0！", "确定");
                return;
            }

            if (string.IsNullOrEmpty(_outputRootPath))
            {
                NetLogger.LogError("[Scaffold] ", $" 生成阻断: 输出根目录不能为空");
                EditorUtility.DisplayDialog("错误", "输出根目录不能为空！", "确定");
                return;
            }

            try
            {
                if (_genProtocol) GenerateSharedProtocol();

                if (_currentType == ModuleType.RoomComponent)
                {
                    if (_genServer) GenerateServerRoomComponent();
                    if (_genClient) GenerateClientRoomComponent();
                }
                else
                {
                    if (_genServer) GenerateServerGlobalModule();
                    if (_genClient) GenerateClientGlobalModule();
                }

                AssetDatabase.Refresh();
                NetLogger.LogInfo($"[Scaffold] ", $" 业务模块 {_moduleName} 生成完毕，输出路径: {_outputRootPath}");

                // 核心修复：更新弹窗指引，引导开发者使用自动装配工具链
                EditorUtility.DisplayDialog("成功",
                    $"业务模块 {_moduleName} 生成完毕！\n\n请点击顶部菜单：\n[StellarNet/Lite 强制重新生成协议与组件常量表]\n以完成模块的自动装配。",
                    "确定");
            }
            catch (Exception e)
            {
                // 允许的 Try-Catch：处理不可控的底层文件 I/O 异常
                NetLogger.LogError($"[Scaffold] ", $" 文件写入异常: {e.Message}");
                EditorUtility.DisplayDialog("致命错误", $"生成过程中发生文件读写异常:\n{e.Message}", "确定");
            }
        }

        private void GenerateSharedProtocol()
        {
            string path = $"{_outputRootPath}/Shared/Protocol/{_moduleName}Protocols.cs";
            string scope = _currentType == ModuleType.RoomComponent ? "NetScope.Room" : "NetScope.Global";
            string ns = $"{_baseNamespace}.Shared.Protocol";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("// ========================================================");
            sb.AppendLine($"// 业务模块: {_moduleName}");
            sb.AppendLine($"// 作者: {_authorName}");
            sb.AppendLine($"// 时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("// ========================================================");
            sb.AppendLine("using StellarNet.Lite.Shared.Core;");
            sb.AppendLine("");
            sb.AppendLine($"namespace {ns}");
            sb.AppendLine("{");
            sb.AppendLine($"    [NetMsg({_startProtocolId}, {scope}, NetDir.C2S)]");
            sb.AppendLine($"    public sealed class C2S_{_moduleName}Req");
            sb.AppendLine("    {");
            sb.AppendLine("    }");
            sb.AppendLine("");
            sb.AppendLine($"    [NetMsg({_startProtocolId + 1}, {scope}, NetDir.S2C)]");
            sb.AppendLine($"    public sealed class S2C_{_moduleName}Sync");
            sb.AppendLine("    {");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            WriteToFile(path, sb.ToString());
        }

        private void GenerateServerRoomComponent()
        {
            string path = $"{_outputRootPath}/Server/Components/Server{_moduleName}Component.cs";
            string ns = $"{_baseNamespace}.Server.Components";
            string protocolNs = $"{_baseNamespace}.Shared.Protocol";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine("using StellarNet.Lite.Shared.Core;");
            sb.AppendLine("using StellarNet.Lite.Server.Core;");
            sb.AppendLine($"using {protocolNs};");
            sb.AppendLine("");
            sb.AppendLine($"namespace {ns}");
            sb.AppendLine("{");
            sb.AppendLine($"    [RoomComponent({_componentId}, \"{_moduleName}\", \"{_displayName}\")]");
            sb.AppendLine($"    public sealed class Server{_moduleName}Component : RoomComponent");
            sb.AppendLine("    {");
            sb.AppendLine("        private readonly ServerApp _app;");
            sb.AppendLine("");
            sb.AppendLine($"        public Server{_moduleName}Component(ServerApp app)");
            sb.AppendLine("        {");
            sb.AppendLine("            _app = app;");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        public override void OnInit()");
            sb.AppendLine("        {");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        [NetHandler]");
            sb.AppendLine($"        public void OnC2S_{_moduleName}Req(Session session, C2S_{_moduleName}Req msg)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (session == null || msg == null) return;");
            sb.AppendLine("");
            sb.AppendLine($"            var syncMsg = new S2C_{_moduleName}Sync();");
            sb.AppendLine("            Room.BroadcastMessage(syncMsg);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            WriteToFile(path, sb.ToString());
        }

        private void GenerateClientRoomComponent()
        {
            string path = $"{_outputRootPath}/Client/Components/Client{_moduleName}Component.cs";
            string ns = $"{_baseNamespace}.Client.Components";
            string protocolNs = $"{_baseNamespace}.Shared.Protocol";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine("using StellarNet.Lite.Shared.Core;");
            sb.AppendLine("using StellarNet.Lite.Client.Core;");
            sb.AppendLine($"using {protocolNs};");
            sb.AppendLine("");
            sb.AppendLine($"namespace {ns}");
            sb.AppendLine("{");
            sb.AppendLine($"    [RoomComponent({_componentId}, \"{_moduleName}\", \"{_displayName}\")]");
            sb.AppendLine($"    public sealed class Client{_moduleName}Component : ClientRoomComponent");
            sb.AppendLine("    {");
            sb.AppendLine("        private readonly ClientApp _app;");
            sb.AppendLine("");
            sb.AppendLine($"        public Client{_moduleName}Component(ClientApp app)");
            sb.AppendLine("        {");
            sb.AppendLine("            _app = app;");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        public override void OnInit()");
            sb.AppendLine("        {");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        [NetHandler]");
            sb.AppendLine($"        public void OnS2C_{_moduleName}Sync(S2C_{_moduleName}Sync msg)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (msg == null) return;");
            sb.AppendLine("");
            sb.AppendLine($"            Room.EventSystem.Broadcast(msg);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            WriteToFile(path, sb.ToString());
        }

        private void GenerateServerGlobalModule()
        {
            string path = $"{_outputRootPath}/Server/Modules/Server{_moduleName}Module.cs";
            string ns = $"{_baseNamespace}.Server.Modules";
            string protocolNs = $"{_baseNamespace}.Shared.Protocol";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine("using StellarNet.Lite.Shared.Core;");
            sb.AppendLine("using StellarNet.Lite.Server.Core;");
            sb.AppendLine($"using {protocolNs};");
            sb.AppendLine("");
            sb.AppendLine($"namespace {ns}");
            sb.AppendLine("{");
            sb.AppendLine($"    [GlobalModule(\"{_moduleName}\", \"{_displayName}\")]");
            sb.AppendLine($"    public sealed class Server{_moduleName}Module");
            sb.AppendLine("    {");
            sb.AppendLine("        private readonly ServerApp _app;");
            sb.AppendLine("");
            sb.AppendLine($"        public Server{_moduleName}Module(ServerApp app)");
            sb.AppendLine("        {");
            sb.AppendLine("            _app = app;");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        [NetHandler]");
            sb.AppendLine($"        public void OnC2S_{_moduleName}Req(Session session, C2S_{_moduleName}Req msg)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (session == null || msg == null) return;");
            sb.AppendLine("");
            sb.AppendLine($"            var syncMsg = new S2C_{_moduleName}Sync();");
            sb.AppendLine("            _app.SendMessageToSession(session, syncMsg);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            WriteToFile(path, sb.ToString());
        }

        private void GenerateClientGlobalModule()
        {
            string path = $"{_outputRootPath}/Client/Modules/Client{_moduleName}Module.cs";
            string ns = $"{_baseNamespace}.Client.Modules";
            string protocolNs = $"{_baseNamespace}.Shared.Protocol";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine("using StellarNet.Lite.Shared.Core;");
            sb.AppendLine("using StellarNet.Lite.Client.Core;");
            sb.AppendLine("using StellarNet.Lite.Client.Core.Events;");
            sb.AppendLine($"using {protocolNs};");
            sb.AppendLine("");
            sb.AppendLine($"namespace {ns}");
            sb.AppendLine("{");
            sb.AppendLine($"    [GlobalModule(\"{_moduleName}\", \"{_displayName}\")]");
            sb.AppendLine($"    public sealed class Client{_moduleName}Module");
            sb.AppendLine("    {");
            sb.AppendLine("        private readonly ClientApp _app;");
            sb.AppendLine("");
            sb.AppendLine($"        public Client{_moduleName}Module(ClientApp app)");
            sb.AppendLine("        {");
            sb.AppendLine("            _app = app;");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        [NetHandler]");
            sb.AppendLine($"        public void OnS2C_{_moduleName}Sync(S2C_{_moduleName}Sync msg)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (msg == null) return;");
            sb.AppendLine("");
            sb.AppendLine($"            GlobalTypeEvent.Broadcast(msg);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            WriteToFile(path, sb.ToString());
        }

        private void WriteToFile(string path, string content)
        {
            string directory = Path.GetDirectoryName(path);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(path))
            {
                NetLogger.LogWarning($"[Scaffold]", $"  文件已存在，跳过生成以防止覆盖手写业务逻辑: {path}");
                return;
            }

            File.WriteAllText(path, content, Encoding.UTF8);
        }

        #endregion
    }
}
#endif