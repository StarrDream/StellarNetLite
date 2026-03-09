using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Shared.Core
{
    /// <summary>
    /// 房间内事件标识接口。
    /// </summary>
    public interface IRoomEvent
    {
    }

    /// <summary>
    /// 房间作用域事件总线 (实例级)。
    /// 核心规范 (Point 12 & 13)：
    /// 1. 边界规范：仅允许处理房间内的状态同步、战斗表现、结算等强房间上下文事件。严禁将大厅、登录等全局事件放入此总线。
    /// 2. 性能口径：本总线为“低 GC 实例级隔离总线”（内部使用 Dictionary 与 Delegate 维护，订阅/注销时有微量装箱分配），并非绝对的零 GC。
    /// 3. 物理隔离：通过与 RoomId 绑定，彻底解决回放房间与在线房间共存、多房间切换时的事件串线问题。
    /// </summary>
    public sealed class RoomEventBus
    {
        private readonly Dictionary<Type, Delegate> _eventHandlers = new Dictionary<Type, Delegate>();
        private readonly string _ownerRoomId;

        public RoomEventBus(string ownerRoomId)
        {
            _ownerRoomId = ownerRoomId;
        }

        public void Subscribe<T>(Action<T> handler) where T : struct, IRoomEvent
        {
            if (handler == null)
            {
                LiteLogger.LogError("RoomEventBus", "订阅失败: 传入的 handler 为空", _ownerRoomId);
                return;
            }

            Type eventType = typeof(T);
            if (_eventHandlers.TryGetValue(eventType, out Delegate existingDelegate))
            {
                _eventHandlers[eventType] = Delegate.Combine(existingDelegate, handler);
            }
            else
            {
                _eventHandlers[eventType] = handler;
            }
        }

        public void Unsubscribe<T>(Action<T> handler) where T : struct, IRoomEvent
        {
            if (handler == null) return;

            Type eventType = typeof(T);
            if (_eventHandlers.TryGetValue(eventType, out Delegate existingDelegate))
            {
                Delegate currentDelegate = Delegate.Remove(existingDelegate, handler);
                if (currentDelegate == null)
                {
                    _eventHandlers.Remove(eventType);
                }
                else
                {
                    _eventHandlers[eventType] = currentDelegate;
                }
            }
        }

        public void Fire<T>(T evt) where T : struct, IRoomEvent
        {
            Type eventType = typeof(T);
            if (_eventHandlers.TryGetValue(eventType, out Delegate existingDelegate))
            {
                var action = existingDelegate as Action<T>;
                action?.Invoke(evt);
            }
        }

        public void Clear()
        {
            _eventHandlers.Clear();
        }
    }
}