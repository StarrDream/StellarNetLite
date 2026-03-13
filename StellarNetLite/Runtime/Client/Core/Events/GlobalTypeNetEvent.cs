using System;
using System.Collections.Generic;
using UnityEngine;

namespace StellarNet.Lite.Client.Core.Events
{
    /// <summary>
    /// 客户端全局强类型事件系统 (无侵入版)
    /// 职责：用于大厅、登录等脱离具体房间上下文的全局事件派发。
    /// 架构说明：移除了泛型接口约束，允许直接将 Shared 层的协议对象 (如 S2C_RoomListResponse) 作为事件抛出，实现 0GC 与极速路由。
    /// </summary>
    public static class GlobalTypeNetEvent
    {
        public static IUnRegister Register<T>(Action<T> onEvent)
        {
            if (onEvent == null) return new CustomUnRegister(null);

            EventBox<T>.Subscribers += onEvent;
            return EventBox<T>.AllocateToken(onEvent);
        }

        /// <summary>
        /// 显式注销指定的事件监听器。
        /// 架构意图：提供非 Token 依赖的对称注销方式，适用于基于状态机切换而非生命周期的精准控制。
        /// </summary>
        public static void UnRegister<T>(Action<T> onEvent)
        {
            if (onEvent == null) return;
            EventBox<T>.Subscribers -= onEvent;
        }

        public static void Broadcast<T>(T e)
        {
            EventBox<T>.Subscribers?.Invoke(e);
        }

        public static void Broadcast<T>() where T : new()
        {
            EventBox<T>.Subscribers?.Invoke(new T());
        }

        public static void UnRegisterAll<T>()
        {
            EventBox<T>.Subscribers = null;
            EventBox<T>.ClearPool();
        }

        private static class EventBox<T>
        {
            public static Action<T> Subscribers;
            private static readonly Stack<EventToken> _pool = new Stack<EventToken>();

            public static EventToken AllocateToken(Action<T> callback)
            {
                EventToken token = _pool.Count > 0 ? _pool.Pop() : new EventToken();
                token.Handler = callback;
                token.IsRecycled = false;
                return token;
            }

            public static void RecycleToken(EventToken token)
            {
                if (token == null || token.IsRecycled) return;
                token.Handler = null;
                token.IsRecycled = true;
                _pool.Push(token);
            }

            public static void ClearPool()
            {
                _pool.Clear();
            }

            public class EventToken : IUnRegister
            {
                public Action<T> Handler;
                public bool IsRecycled;

                public void UnRegister()
                {
                    if (IsRecycled) return;

                    if (Handler != null)
                    {
                        Subscribers -= Handler;
                    }

                    RecycleToken(this);
                }

                public IUnRegister UnRegisterWhenGameObjectDestroyed(GameObject gameObject)
                {
                    if (gameObject == null)
                    {
                        UnRegister();
                        return this;
                    }

                    if (!gameObject.TryGetComponent<EventUnregisterTrigger>(out var trigger))
                    {
                        trigger = gameObject.AddComponent<EventUnregisterTrigger>();
                        trigger.hideFlags = HideFlags.HideInInspector;
                    }

                    trigger.Add(this);
                    return this;
                }

                public IUnRegister UnRegisterWhenMonoDisable(MonoBehaviour mono)
                {
                    if (mono == null)
                    {
                        UnRegister();
                        return this;
                    }

                    if (!mono.TryGetComponent<EventUnregisterDisableTrigger>(out var trigger))
                    {
                        trigger = mono.gameObject.AddComponent<EventUnregisterDisableTrigger>();
                        trigger.hideFlags = HideFlags.HideInInspector;
                    }

                    trigger.Add(this);
                    return this;
                }
            }
        }
    }
}