using System;

namespace StellarNet.Lite.Shared.Core
{
    /// <summary>
    /// 全局事件标识接口。
    /// 强制约束事件必须为 struct，配合泛型静态类实现零 GC。
    /// </summary>
    public interface IGlobalEvent
    {
    }

    /// <summary>
    /// 零 GC 值类型全局事件总线。
    /// 核心规范 (Point 12)：
    /// 边界规范：仅允许处理登录、大厅房间列表、录像列表等脱离具体房间上下文的全局系统事件。
    /// 严禁将战斗同步、房间快照等房间内高频状态放入全局总线，否则将导致严重的状态串线与内存泄漏。
    /// </summary>
    public static class GlobalEventBus<T> where T : struct, IGlobalEvent
    {
        public static Action<T> OnEvent;

        public static void Fire(T evt)
        {
            OnEvent?.Invoke(evt);
        }

        public static void Clear()
        {
            OnEvent = null;
        }
    }
}