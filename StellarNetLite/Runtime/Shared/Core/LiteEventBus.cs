using System;

namespace StellarNet.Lite.Shared.Core
{
    /// <summary>
    /// 房间内事件标识接口。
    /// 强制约束事件必须为 struct，配合泛型静态类实现零 GC。
    /// </summary>
    public interface IRoomEvent
    {
    }

    /// <summary>
    /// 零 GC 值类型事件总线。
    /// 职责：解决 ClientRoomComponent 之间的孤岛问题，实现无引用的横向解耦。
    /// 架构说明：利用泛型静态类的特性，每种事件类型拥有独立的委托通道，彻底消灭字典寻址与装箱拆箱。
    /// </summary>
    public static class LiteEventBus<T> where T : struct, IRoomEvent
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