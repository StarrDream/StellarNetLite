using System;
using System.Collections.Generic;
using UnityEngine;

namespace StellarNet.Lite.Client.Core.Events
{
    /// <summary>
    /// 客户端房间实例级事件系统
    /// 职责：与 ClientRoom 生命周期强绑定，确保在线房间与回放沙盒的事件绝对物理隔离。
    /// 架构说明：支持无侵入式协议直抛，并完美接入 IUnRegister 生命周期管理生态。
    /// </summary>
    public sealed class RoomNetEventSystem
    {
        private readonly Dictionary<Type, Delegate> _delegates = new Dictionary<Type, Delegate>();
        private readonly string _roomId;

        public RoomNetEventSystem(string roomId)
        {
            _roomId = roomId;
        }

        public IUnRegister Register<T>(Action<T> onEvent)
        {
            if (onEvent == null) return new CustomUnRegister(null);

            Type eventType = typeof(T);
            if (_delegates.TryGetValue(eventType, out Delegate existingDelegate))
            {
                _delegates[eventType] = Delegate.Combine(existingDelegate, onEvent);
            }
            else
            {
                _delegates[eventType] = onEvent;
            }

            return new CustomUnRegister(() => UnRegister(onEvent));
        }

        public void UnRegister<T>(Action<T> onEvent)
        {
            if (onEvent == null) return;

            Type eventType = typeof(T);
            if (_delegates.TryGetValue(eventType, out Delegate existingDelegate))
            {
                Delegate currentDelegate = Delegate.Remove(existingDelegate, onEvent);
                if (currentDelegate == null)
                {
                    _delegates.Remove(eventType);
                }
                else
                {
                    _delegates[eventType] = currentDelegate;
                }
            }
        }

        public void Broadcast<T>(T e)
        {
            Type eventType = typeof(T);
            if (_delegates.TryGetValue(eventType, out Delegate existingDelegate))
            {
                var action = existingDelegate as Action<T>;
                action?.Invoke(e);
            }
        }

        public void Clear()
        {
            _delegates.Clear();
        }
    }
}