#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Editor
{
    /// <summary>
    /// 协议 ID 防重扫描器 (极简版)。
    /// 职责：在每次代码编译完成后，利用 TypeCache 极速扫描所有协议，防止团队协作时出现 ID 冲突。
    /// </summary>
    [InitializeOnLoad]
    public static class LiteProtocolScanner
    {
        static LiteProtocolScanner()
        {
            var ids = new HashSet<int>();
            var types = TypeCache.GetTypesWithAttribute<NetMsgAttribute>();
            
            foreach (var type in types)
            {
                var attr = type.GetCustomAttribute<NetMsgAttribute>();
                if (attr != null)
                {
                    if (!ids.Add(attr.Id))
                    {
                        Debug.LogError($"[StellarNet 致命错误] 协议 ID 冲突: ID {attr.Id} 在类 {type.Name} 中重复使用，请立即修改！");
                    }
                }
            }
        }
    }
}
#endif