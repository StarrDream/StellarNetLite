#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Editor
{
    /// <summary>
    /// 协议与组件元数据扫描器。
    /// 职责：
    /// 1. 编译后极速扫描所有 [NetMsg]、[RoomComponent]、[GlobalModule]。
    /// 2. 自动生成 MsgIdConst.cs 与 ComponentIdConst.cs 常量表。
    /// 3. 自动生成 AutoRegistry.cs，实现模块与组件的 0 反射自动装配。
    /// </summary>
    [InitializeOnLoad]
    public static class LiteProtocolScanner
    {
        private const string ProtocolOutputPath = "Assets/StellarNetLite/Runtime/Shared/Protocol/MsgIdConst.cs";
        private const string ComponentOutputPath = "Assets/StellarNetLite/Runtime/Shared/Protocol/ComponentIdConst.cs";
        private const string RegistryOutputPath = "Assets/StellarNetLite/Runtime/Shared/Binders/AutoRegistry.cs";

        static LiteProtocolScanner()
        {
            RunScanAndGenerate();
        }

        [MenuItem("StellarNet/Lite 强制重新生成协议与组件常量表")]
        public static void ManualRun()
        {
            RunScanAndGenerate();
            NetLogger.LogInfo("[LiteProtocolScanner]", "手动触发扫描与常量表生成完毕。");
        }

        private static void RunScanAndGenerate()
        {
            bool protocolChanged = ScanAndGenerateProtocols();
            bool componentChanged = ScanAndGenerateComponentsAndRegistry();

            if (protocolChanged || componentChanged)
            {
                AssetDatabase.Refresh();
            }
        }

        private static bool ScanAndGenerateProtocols()
        {
            var types = TypeCache.GetTypesWithAttribute<NetMsgAttribute>();
            var protocolList = new List<(int Id, string Name)>();
            var idSet = new HashSet<int>();
            bool hasConflict = false;

            foreach (var type in types)
            {
                var attr = type.GetCustomAttribute<NetMsgAttribute>();
                if (attr != null)
                {
                    if (!idSet.Add(attr.Id))
                    {
                        NetLogger.LogError($"[StellarNet 致命错误]", $"协议 ID 冲突: ID {attr.Id} 在类 {type.Name} 中重复使用，请立即修改！");
                        hasConflict = true;
                    }
                    else
                    {
                        protocolList.Add((attr.Id, type.Name));
                    }
                }
            }

            if (hasConflict)
            {
                NetLogger.LogError("[LiteProtocolScanner]", $"存在协议 ID 冲突，已终止协议常量表生成。");
                return false;
            }

            return GenerateProtocolConstFile(protocolList);
        }

        private static bool ScanAndGenerateComponentsAndRegistry()
        {
            // 1. 扫描 RoomComponent
            var roomCompTypes = TypeCache.GetTypesWithAttribute<RoomComponentAttribute>();
            var compList = new List<(int Id, string Name, string DisplayName, Type ClassType)>();
            var idToName = new Dictionary<int, string>();
            bool hasConflict = false;

            foreach (var type in roomCompTypes)
            {
                var attr = type.GetCustomAttribute<RoomComponentAttribute>();
                if (attr != null)
                {
                    if (idToName.TryGetValue(attr.Id, out string existingName))
                    {
                        if (existingName != attr.Name)
                        {
                            NetLogger.LogError($"[StellarNet 致命错误]", $"组件 ID 冲突: ID {attr.Id} 被分配给了 {existingName} 和 {attr.Name}！");
                            hasConflict = true;
                        }
                        else
                        {
                            // 双端同 ID 同名，允许，加入列表用于生成装配代码
                            compList.Add((attr.Id, attr.Name, attr.DisplayName, type));
                        }
                    }
                    else
                    {
                        idToName[attr.Id] = attr.Name;
                        compList.Add((attr.Id, attr.Name, attr.DisplayName, type));
                    }
                }
            }

            if (hasConflict)
            {
                NetLogger.LogError("[LiteProtocolScanner]", $"存在组件 ID 冲突，已终止组件常量表生成。");
                return false;
            }

            // 2. 扫描 GlobalModule
            var globalModTypes = TypeCache.GetTypesWithAttribute<GlobalModuleAttribute>();
            var modList = new List<(string Name, string DisplayName, Type ClassType)>();

            foreach (var type in globalModTypes)
            {
                var attr = type.GetCustomAttribute<GlobalModuleAttribute>();
                if (attr != null)
                {
                    modList.Add((attr.Name, attr.DisplayName, type));
                }
            }

            bool constChanged = GenerateComponentConstFile(compList.GroupBy(c => c.Id).Select(g => g.First()).ToList());
            bool registryChanged = GenerateAutoRegistryFile(compList, modList);

            return constChanged || registryChanged;
        }

        private static bool GenerateProtocolConstFile(List<(int Id, string Name)> protocolList)
        {
            protocolList = protocolList.OrderBy(p => p.Id).ToList();
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("// ========================================================");
            sb.AppendLine("// 自动生成的协议 ID 常量表。");
            sb.AppendLine("// 请勿手动修改！请通过给类添加 [NetMsg] 特性来驱动此文件更新。");
            sb.AppendLine("// ========================================================");
            sb.AppendLine("namespace StellarNet.Lite.Shared.Protocol");
            sb.AppendLine("{");
            sb.AppendLine("    public static class MsgIdConst");
            sb.AppendLine("    {");

            foreach (var proto in protocolList)
            {
                sb.AppendLine($"        public const int {proto.Name} = {proto.Id};");
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            return WriteToFileIfChanged(ProtocolOutputPath, sb.ToString());
        }

        private static bool GenerateComponentConstFile(List<(int Id, string Name, string DisplayName, Type ClassType)> compList)
        {
            compList = compList.OrderBy(p => p.Id).ToList();
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("// ========================================================");
            sb.AppendLine("// 自动生成的组件 ID 常量表。");
            sb.AppendLine("// 请勿手动修改！请通过给类添加 [RoomComponent] 特性来驱动此文件更新。");
            sb.AppendLine("// ========================================================");
            sb.AppendLine("namespace StellarNet.Lite.Shared.Protocol");
            sb.AppendLine("{");
            sb.AppendLine("    public static class ComponentIdConst");
            sb.AppendLine("    {");

            foreach (var comp in compList)
            {
                sb.AppendLine($"        public const int {comp.Name} = {comp.Id};");
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            return WriteToFileIfChanged(ComponentOutputPath, sb.ToString());
        }

        private static bool GenerateAutoRegistryFile(
            List<(int Id, string Name, string DisplayName, Type ClassType)> compList,
            List<(string Name, string DisplayName, Type ClassType)> modList)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("// ========================================================");
            sb.AppendLine("// 自动生成的模块与组件装配器。");
            sb.AppendLine("// 请勿手动修改！由 LiteProtocolScanner 自动生成。");
            sb.AppendLine("// ========================================================");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using StellarNet.Lite.Shared.Core;");
            sb.AppendLine("using StellarNet.Lite.Server.Core;");
            sb.AppendLine("using StellarNet.Lite.Client.Core;");
            sb.AppendLine("using StellarNet.Lite.Shared.Protocol;");
            sb.AppendLine("");
            sb.AppendLine("namespace StellarNet.Lite.Shared.Binders");
            sb.AppendLine("{");
            sb.AppendLine("    public static class AutoRegistry");
            sb.AppendLine("    {");

            // 1. 生成元数据列表
            sb.AppendLine("        public static readonly List<RoomComponentMeta> RoomComponentMetaList = new List<RoomComponentMeta>");
            sb.AppendLine("        {");
            var uniqueComps = compList.GroupBy(c => c.Id).Select(g => g.First()).OrderBy(c => c.Id).ToList();
            foreach (var comp in uniqueComps)
            {
                sb.AppendLine($"            new RoomComponentMeta {{ Id = {comp.Id}, Name = \"{comp.Name}\", DisplayName = \"{comp.DisplayName}\" }},");
            }

            sb.AppendLine("        };");
            sb.AppendLine("");

            sb.AppendLine("        public static readonly List<GlobalModuleMeta> GlobalModuleMetaList = new List<GlobalModuleMeta>");
            sb.AppendLine("        {");
            var uniqueMods = modList.GroupBy(m => m.Name).Select(g => g.First()).OrderBy(m => m.Name).ToList();
            foreach (var mod in uniqueMods)
            {
                sb.AppendLine($"            new GlobalModuleMeta {{ Name = \"{mod.Name}\", DisplayName = \"{mod.DisplayName}\" }},");
            }

            sb.AppendLine("        };");
            sb.AppendLine("");

            // 2. 生成服务端装配方法
            sb.AppendLine("        public static void RegisterServer(ServerApp serverApp, Func<byte[], Type, object> deserializeFunc)");
            sb.AppendLine("        {");
            foreach (var mod in modList.Where(m => m.ClassType.Namespace != null && m.ClassType.Namespace.Contains("Server")))
            {
                sb.AppendLine($"            AutoBinder.BindServerModule(new {mod.ClassType.FullName}(serverApp), serverApp.GlobalDispatcher, deserializeFunc);");
            }

            foreach (var comp in compList.Where(c => c.ClassType.Namespace != null && c.ClassType.Namespace.Contains("Server")))
            {
                sb.AppendLine($"            ServerRoomFactory.Register({comp.Id}, () => new {comp.ClassType.FullName}(serverApp));");
            }

            sb.AppendLine("        }");
            sb.AppendLine("");

            // 3. 生成客户端装配方法
            sb.AppendLine("        public static void RegisterClient(ClientApp clientApp, Func<byte[], Type, object> deserializeFunc)");
            sb.AppendLine("        {");
            foreach (var mod in modList.Where(m => m.ClassType.Namespace != null && m.ClassType.Namespace.Contains("Client")))
            {
                sb.AppendLine($"            AutoBinder.BindClientModule(new {mod.ClassType.FullName}(clientApp), clientApp.GlobalDispatcher, deserializeFunc);");
            }

            foreach (var comp in compList.Where(c => c.ClassType.Namespace != null && c.ClassType.Namespace.Contains("Client")))
            {
                sb.AppendLine($"            ClientRoomFactory.Register({comp.Id}, () => new {comp.ClassType.FullName}(clientApp));");
            }

            sb.AppendLine("        }");

            sb.AppendLine("    }");
            sb.AppendLine("}");

            return WriteToFileIfChanged(RegistryOutputPath, sb.ToString());
        }

        private static bool WriteToFileIfChanged(string path, string newContent)
        {
            string oldContent = string.Empty;
            if (File.Exists(path))
            {
                oldContent = File.ReadAllText(path);
            }

            if (newContent != oldContent)
            {
                string directory = Path.GetDirectoryName(path);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(path, newContent, Encoding.UTF8);
                NetLogger.LogInfo($"[LiteProtocolScanner]", $"检测到变更，已自动更新装配文件: {path}");
                return true;
            }

            return false;
        }
    }
}
#endif