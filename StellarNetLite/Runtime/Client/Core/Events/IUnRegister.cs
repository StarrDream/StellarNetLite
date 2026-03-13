using System;
using System.Collections.Generic;
using UnityEngine;

namespace StellarNet.Lite.Client.Core.Events
{
    /// <summary>
    /// 事件注销接口
    /// 职责：提供统一的事件注销能力，支持与 GameObject 生命周期强绑定，防止内存泄漏。
    /// </summary>
    public interface IUnRegister
    {
        void UnRegister();

        IUnRegister UnRegisterWhenGameObjectDestroyed(GameObject gameObject);

        /// <summary>
        /// 将事件注销与 MonoBehaviour 的 OnDisable 生命周期绑定。
        /// 架构意图：专为 UI 对象池与频繁隐藏/显示的组件设计，防止隐藏期间后台持续响应网络事件。
        /// </summary>
        IUnRegister UnRegisterWhenMonoDisable(MonoBehaviour mono);
    }

    /// <summary>
    /// 注销接口的具体实现 (RoomNetEventSystem 默认使用此实现)
    /// </summary>
    public class CustomUnRegister : IUnRegister
    {
        private Action _onUnRegister;

        public CustomUnRegister(Action onUnRegister)
        {
            _onUnRegister = onUnRegister;
        }

        public void UnRegister()
        {
            _onUnRegister?.Invoke();
            _onUnRegister = null;
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

    /// <summary>
    /// 自动挂载的辅助组件，用于监听 OnDestroy 并触发批量注销
    /// </summary>
    [DisallowMultipleComponent]
    public class EventUnregisterTrigger : MonoBehaviour
    {
        private readonly HashSet<IUnRegister> _unRegisters = new HashSet<IUnRegister>();

        public void Add(IUnRegister unRegister)
        {
            if (unRegister == null) return;
            _unRegisters.Add(unRegister);
        }

        private void OnDestroy()
        {
            foreach (var unRegister in _unRegisters)
            {
                unRegister?.UnRegister();
            }

            _unRegisters.Clear();
        }
    }

    /// <summary>
    /// 自动挂载的辅助组件，用于监听 OnDisable 并触发批量注销
    /// </summary>
    [DisallowMultipleComponent]
    public class EventUnregisterDisableTrigger : MonoBehaviour
    {
        private readonly HashSet<IUnRegister> _unRegisters = new HashSet<IUnRegister>();

        public void Add(IUnRegister unRegister)
        {
            if (unRegister == null) return;
            _unRegisters.Add(unRegister);
        }

        private void OnDisable()
        {
            foreach (var unRegister in _unRegisters)
            {
                unRegister?.UnRegister();
            }

            // 触发后必须清空，以便对象在 OnEnable 重新注册时能够开启新一轮的生命周期追踪
            _unRegisters.Clear();
        }
    }
}