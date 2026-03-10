using System.Diagnostics;
using Debug = UnityEngine.Debug;

namespace StellarNet.Lite.Shared.Infrastructure
{
    /// <summary>
    /// 生产级结构化日志记录器。
    /// 职责：强制统一日志的上下文输出格式，确保线上排障时能够通过正则快速过滤出特定房间或会话的完整生命周期。
    /// 核心修复 (Point 8)：告别模糊的“失败了”日志，建立可审计的日志规范。
    /// </summary>
    public static class LiteLogger
    {
        [Conditional("ENABLE_LOG")]
        public static void LogInfo(string module, string message, string roomId = "-", string sessionId = "-",
            string extraContext = "")
        {
            Debug.Log(FormatMessage("INFO", module, message, roomId, sessionId, extraContext));
        }

        [Conditional("ENABLE_LOG")]
        public static void LogWarning(string module, string message, string roomId = "-", string sessionId = "-",
            string extraContext = "")
        {
            Debug.LogWarning(FormatMessage("WARN", module, message, roomId, sessionId, extraContext));
        }

        [Conditional("ENABLE_LOG")]
        public static void LogError(string module, string message, string roomId = "-", string sessionId = "-",
            string extraContext = "")
        {
            Debug.LogError(FormatMessage("ERROR", module, message, roomId, sessionId, extraContext));
        }

        private static string FormatMessage(string level, string module, string message, string roomId,
            string sessionId, string extraContext)
        {
            string roomStr = string.IsNullOrEmpty(roomId) ? "-" : roomId;
            string sessionStr = string.IsNullOrEmpty(sessionId) ? "-" : sessionId;
            string contextStr = string.IsNullOrEmpty(extraContext) ? "" : $" | Context: {extraContext}";

            // 格式范例: [ERROR][ServerRoomModule][Room:1234][Session:ABCD] 玩家越权操作 | Context: State=Playing
            return $"[{level}][{module}][Room:{roomStr}][Session:{sessionStr}] {message}{contextStr}";
        }
    }
}