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
    /// 1. 编译后极速扫描所有 [NetMsg] 与 [RoomComponent]，校验 ID 是否冲突。
    /// 2. 自动生成 MsgIdConst.cs 与 ComponentIdConst.cs 常量表，彻底消灭手动维护魔数带来的错位风险。
    /// </summary>
    [InitializeOnLoad]
    public static class LiteProtocolScanner
    {
        private const string ProtocolOutputPath = "Assets/StellarNetLite/Runtime/Shared/Protocol/MsgIdConst.cs";
        private const string ComponentOutputPath = "Assets/StellarNetLite/Runtime/Shared/Protocol/ComponentIdConst.cs";

        static LiteProtocolScanner()
        {
            RunScanAndGenerate();
        }

        [MenuItem("StellarNet/Lite 强制重新生成协议与组件常量表")]
        public static void ManualRun()
        {
            RunScanAndGenerate();
            LiteLogger.LogInfo("[LiteProtocolScanner]", " 手动触发扫描与常量表生成完毕。");
        }

        private static void RunScanAndGenerate()
        {
            bool protocolChanged = ScanAndGenerateProtocols();
            bool componentChanged = ScanAndGenerateComponents();

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
                        LiteLogger.LogError($"[StellarNet 致命错误]", $" 协议 ID 冲突: ID {attr.Id} 在类 {type.Name} 中重复使用，请立即修改！");
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
                LiteLogger.LogError("[LiteProtocolScanner] ", $"存在协议 ID 冲突，已终止协议常量表生成。");
                return false;
            }

            return GenerateProtocolConstFile(protocolList);
        }

        private static bool ScanAndGenerateComponents()
        {
            var types = TypeCache.GetTypesWithAttribute<RoomComponentAttribute>();
            var compList = new List<(int Id, string Name)>();
            var idToName = new Dictionary<int, string>();
            bool hasConflict = false;

            foreach (var type in types)
            {
                var attr = type.GetCustomAttribute<RoomComponentAttribute>();
                if (attr != null)
                {
                    if (idToName.TryGetValue(attr.Id, out string existingName))
                    {
                        // 允许双端使用同一个 ID，但名称必须严格一致
                        if (existingName != attr.Name)
                        {
                            LiteLogger.LogError($"[StellarNet 致命错误] ", $"组件 ID 冲突: ID {{attr.Id}} 被分配给了 {{existingName}} 和 {{attr.Name}}！");
                            hasConflict = true;
                        }
                    }
                    else
                    {
                        idToName[attr.Id] = attr.Name;
                        compList.Add((attr.Id, attr.Name));
                    }
                }
            }

            if (hasConflict)
            {
                LiteLogger.LogError("[LiteProtocolScanner] ", $"存在组件 ID 冲突，已终止组件常量表生成。");
                return false;
            }

            return GenerateComponentConstFile(compList);
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

        private static bool GenerateComponentConstFile(List<(int Id, string Name)> compList)
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
                LiteLogger.LogInfo($"[LiteProtocolScanner] ", $"检测到变更，已自动更新常量表: {path}");
                return true;
            }

            return false;
        }
    }
}
#endif