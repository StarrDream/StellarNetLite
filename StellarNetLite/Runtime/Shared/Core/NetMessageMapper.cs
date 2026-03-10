using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using StellarNet.Lite.Shared.Infrastructure;
using UnityEngine;

namespace StellarNet.Lite.Shared.Core
{
    /// <summary>
    /// 网络消息元数据映射器。
    /// 职责：在程序启动时扫描并缓存所有携带 [NetMsg] 特性的类型。
    /// </summary>
    public static class NetMessageMapper
    {
        private static readonly Dictionary<Type, NetMsgAttribute> _typeToMetaCache =
            new Dictionary<Type, NetMsgAttribute>();

        private static bool _isInitialized = false;

        public static void Initialize()
        {
            if (_isInitialized) return;

            _typeToMetaCache.Clear();
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var assembly in assemblies)
            {
                if (assembly.FullName.StartsWith("System") || assembly.FullName.StartsWith("UnityEngine") ||
                    assembly.FullName.StartsWith("UnityEditor") || assembly.FullName.StartsWith("mscorlib"))
                {
                    continue;
                }

                try
                {
                    Type[] types = assembly.GetTypes();
                    foreach (var type in types)
                    {
                        var attr = type.GetCustomAttribute<NetMsgAttribute>();
                        if (attr != null)
                        {
                            if (_typeToMetaCache.ContainsKey(type))
                            {
                                LiteLogger.LogError($"[NetMessageMapper] ",$" 致命错误: 发现重复的协议类型 {type.Name}，请检查代码！");
                                continue;
                            }

                            _typeToMetaCache[type] = attr;
                        }
                    }
                }
                catch (ReflectionTypeLoadException ex)
                {
                    // 核心修复 (Point 3)：暴露 LoaderExceptions，防止第三方 DLL 冲突导致的静默失败
                    LiteLogger.LogError(
                        $"[NetMessageMapper]",$"  扫描程序集 {assembly.FullName} 时发生 ReflectionTypeLoadException，协议扫描可能不完整！");
                    foreach (var loaderEx in ex.LoaderExceptions)
                    {
                        if (loaderEx != null)
                        {
                            LiteLogger.LogError($"[NetMessageMapper]",$"  LoaderException 明细: {loaderEx.Message}");
                        }
                    }
                }
                catch (Exception e)
                {
                    LiteLogger.LogError($"[NetMessageMapper]",$"  扫描程序集 {assembly.FullName} 时发生未知异常: {e.Message}");
                }
            }

            _isInitialized = true;
            GenerateIntegrityReport();
        }

        // 核心修复 (Point 4)：启动期完整性校验报告
        private static void GenerateIntegrityReport()
        {
            int c2sCount = _typeToMetaCache.Values.Count(m => m.Dir == NetDir.C2S);
            int s2cCount = _typeToMetaCache.Values.Count(m => m.Dir == NetDir.S2C);
            int globalCount = _typeToMetaCache.Values.Count(m => m.Scope == NetScope.Global);
            int roomCount = _typeToMetaCache.Values.Count(m => m.Scope == NetScope.Room);

            LiteLogger.LogInfo(
                $"<color=cyan>[StellarNet 协议加载报告] ",$" 总协议数: {_typeToMetaCache.Count} | C2S: {c2sCount} | S2C: {s2cCount} | Global: {globalCount} | Room: {roomCount}</color>");

            // 最小核心协议校验 (100: Login, 200: CreateRoom, 202: JoinRoom)
            bool hasLogin = _typeToMetaCache.Values.Any(m => m.Id == 100);
            bool hasCreateRoom = _typeToMetaCache.Values.Any(m => m.Id == 200);
            bool hasJoinRoom = _typeToMetaCache.Values.Any(m => m.Id == 202);

            if (!hasLogin || !hasCreateRoom || !hasJoinRoom)
            {
                LiteLogger.LogError(
                    "[NetMessageMapper] 致命阻断: 核心调度协议",$"  (Login/CreateRoom/JoinRoom) 缺失！请检查 MsgIdConst 或协议定义文件是否被意外移除。");
            }
        }

        public static bool TryGetMeta(Type msgType, out NetMsgAttribute meta)
        {
            if (!_isInitialized)
            {
                LiteLogger.LogError("[NetMessageMapper]",$"  尚未初始化，请先调用 Initialize()！");
                meta = null;
                return false;
            }

            if (msgType == null)
            {
                LiteLogger.LogError("[NetMessageMapper] ",$" 查询失败: 传入的消息类型为空。");
                meta = null;
                return false;
            }

            if (!_typeToMetaCache.TryGetValue(msgType, out meta))
            {
                LiteLogger.LogError($"[NetMessageMapper]",$"  查询失败: 类型 {msgType.Name} 缺失 [NetMsg] 特性，无法进行强类型发包。");
                return false;
            }

            return true;
        }
    }
}