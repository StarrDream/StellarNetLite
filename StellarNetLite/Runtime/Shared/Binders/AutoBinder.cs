using System;
using System.Reflection;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Client.Core;
using UnityEngine;

namespace StellarNet.Lite.Shared.Binders
{
    public static class AutoBinder
    {
        private static readonly MethodInfo _createServerActionMethod;
        private static readonly MethodInfo _createClientActionMethod;

        static AutoBinder()
        {
            _createServerActionMethod = typeof(AutoBinder).GetMethod(nameof(CreateServerAction), BindingFlags.NonPublic | BindingFlags.Static);
            _createClientActionMethod = typeof(AutoBinder).GetMethod(nameof(CreateClientAction), BindingFlags.NonPublic | BindingFlags.Static);
        }

        // 核心修复 3：委托签名改为 Func<byte[], Type, object>
        public static void BindServerModule(object moduleInstance, GlobalDispatcher dispatcher, Func<byte[], Type, object> deserializeFunc)
        {
            if (moduleInstance == null || dispatcher == null || deserializeFunc == null)
            {
                Debug.LogError("[AutoBinder] 服务端全局模块装配失败: 参数存在空值");
                return;
            }

            Type moduleType = moduleInstance.GetType();
            MethodInfo[] methods = moduleType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            foreach (var method in methods)
            {
                var handlerAttr = method.GetCustomAttribute<NetHandlerAttribute>();
                if (handlerAttr == null) continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 2 || parameters[0].ParameterType != typeof(Session))
                {
                    Debug.LogError($"[AutoBinder] 装配跳过: 方法 {moduleType.Name}.{method.Name} 签名非法。服务端全局 Handler 必须为 (Session, TMsg)");
                    continue;
                }

                Type msgType = parameters[1].ParameterType;
                var msgAttr = msgType.GetCustomAttribute<NetMsgAttribute>();
                if (msgAttr == null)
                {
                    Debug.LogError($"[AutoBinder] 装配跳过: 消息类型 {msgType.Name} 缺失 [NetMsg] 特性");
                    continue;
                }

                if (msgAttr.Scope != NetScope.Global || msgAttr.Dir != NetDir.C2S)
                {
                    Debug.LogError($"[AutoBinder] 装配跳过: 消息类型 {msgType.Name} 的 Scope 或 Dir 与服务端全局模块不匹配");
                    continue;
                }

                MethodInfo genericHelper = _createServerActionMethod.MakeGenericMethod(msgType);
                var wrapper = (Action<Session, Packet>)genericHelper.Invoke(null, new object[] { moduleInstance, method, deserializeFunc });
                dispatcher.Register(msgAttr.Id, wrapper);
            }
        }

        public static void BindServerComponent(RoomComponent componentInstance, RoomDispatcher dispatcher, Func<byte[], Type, object> deserializeFunc)
        {
            if (componentInstance == null || dispatcher == null || deserializeFunc == null)
            {
                Debug.LogError("[AutoBinder] 服务端房间组件装配失败: 参数存在空值");
                return;
            }

            Type compType = componentInstance.GetType();
            MethodInfo[] methods = compType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            foreach (var method in methods)
            {
                var handlerAttr = method.GetCustomAttribute<NetHandlerAttribute>();
                if (handlerAttr == null) continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 2 || parameters[0].ParameterType != typeof(Session))
                {
                    Debug.LogError($"[AutoBinder] 装配跳过: 方法 {compType.Name}.{method.Name} 签名非法。服务端房间 Handler 必须为 (Session, TMsg)");
                    continue;
                }

                Type msgType = parameters[1].ParameterType;
                var msgAttr = msgType.GetCustomAttribute<NetMsgAttribute>();
                if (msgAttr == null)
                {
                    Debug.LogError($"[AutoBinder] 装配跳过: 消息类型 {msgType.Name} 缺失 [NetMsg] 特性");
                    continue;
                }

                if (msgAttr.Scope != NetScope.Room || msgAttr.Dir != NetDir.C2S)
                {
                    Debug.LogError($"[AutoBinder] 装配跳过: 消息类型 {msgType.Name} 的 Scope 或 Dir 与服务端房间组件不匹配");
                    continue;
                }

                MethodInfo genericHelper = _createServerActionMethod.MakeGenericMethod(msgType);
                var wrapper = (Action<Session, Packet>)genericHelper.Invoke(null, new object[] { componentInstance, method, deserializeFunc });
                dispatcher.Register(msgAttr.Id, wrapper);
            }
        }

        public static void BindClientModule(object moduleInstance, ClientGlobalDispatcher dispatcher, Func<byte[], Type, object> deserializeFunc)
        {
            if (moduleInstance == null || dispatcher == null || deserializeFunc == null)
            {
                Debug.LogError("[AutoBinder] 客户端全局模块装配失败: 参数存在空值");
                return;
            }

            Type moduleType = moduleInstance.GetType();
            MethodInfo[] methods = moduleType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            foreach (var method in methods)
            {
                var handlerAttr = method.GetCustomAttribute<NetHandlerAttribute>();
                if (handlerAttr == null) continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 1)
                {
                    Debug.LogError($"[AutoBinder] 装配跳过: 方法 {moduleType.Name}.{method.Name} 签名非法。客户端全局 Handler 必须为 (TMsg)");
                    continue;
                }

                Type msgType = parameters[0].ParameterType;
                var msgAttr = msgType.GetCustomAttribute<NetMsgAttribute>();
                if (msgAttr == null)
                {
                    Debug.LogError($"[AutoBinder] 装配跳过: 消息类型 {msgType.Name} 缺失 [NetMsg] 特性");
                    continue;
                }

                if (msgAttr.Scope != NetScope.Global || msgAttr.Dir != NetDir.S2C)
                {
                    Debug.LogError($"[AutoBinder] 装配跳过: 消息类型 {msgType.Name} 的 Scope 或 Dir 与客户端全局模块不匹配");
                    continue;
                }

                MethodInfo genericHelper = _createClientActionMethod.MakeGenericMethod(msgType);
                var wrapper = (Action<Packet>)genericHelper.Invoke(null, new object[] { moduleInstance, method, deserializeFunc });
                dispatcher.Register(msgAttr.Id, wrapper);
            }
        }

        public static void BindClientComponent(ClientRoomComponent componentInstance, ClientRoomDispatcher dispatcher, Func<byte[], Type, object> deserializeFunc)
        {
            if (componentInstance == null || dispatcher == null || deserializeFunc == null)
            {
                Debug.LogError("[AutoBinder] 客户端房间组件装配失败: 参数存在空值");
                return;
            }

            Type compType = componentInstance.GetType();
            MethodInfo[] methods = compType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            foreach (var method in methods)
            {
                var handlerAttr = method.GetCustomAttribute<NetHandlerAttribute>();
                if (handlerAttr == null) continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 1)
                {
                    Debug.LogError($"[AutoBinder] 装配跳过: 方法 {compType.Name}.{method.Name} 签名非法。客户端房间 Handler 必须为 (TMsg)");
                    continue;
                }

                Type msgType = parameters[0].ParameterType;
                var msgAttr = msgType.GetCustomAttribute<NetMsgAttribute>();
                if (msgAttr == null)
                {
                    Debug.LogError($"[AutoBinder] 装配跳过: 消息类型 {msgType.Name} 缺失 [NetMsg] 特性");
                    continue;
                }

                if (msgAttr.Scope != NetScope.Room || msgAttr.Dir != NetDir.S2C)
                {
                    Debug.LogError($"[AutoBinder] 装配跳过: 消息类型 {msgType.Name} 的 Scope 或 Dir 与客户端房间组件不匹配");
                    continue;
                }

                MethodInfo genericHelper = _createClientActionMethod.MakeGenericMethod(msgType);
                var wrapper = (Action<Packet>)genericHelper.Invoke(null, new object[] { componentInstance, method, deserializeFunc });
                dispatcher.Register(msgAttr.Id, wrapper);
            }
        }

        private static Action<Session, Packet> CreateServerAction<TMsg>(object instance, MethodInfo method, Func<byte[], Type, object> deserializeFunc)
            where TMsg : class
        {
            var del = (Action<Session, TMsg>)Delegate.CreateDelegate(typeof(Action<Session, TMsg>), instance, method);
            return (session, packet) =>
            {
                // 核心修复：直接反序列化为新对象，不再从对象池 Rent
                object deserializedObj = deserializeFunc(packet.Payload, typeof(TMsg));
                if (deserializedObj is TMsg msg)
                {
                    del(session, msg);
                }
                else
                {
                    Debug.LogError($"[AutoBinder] 运行时路由失败: MsgId {packet.MsgId}, 数据反序列化为 {typeof(TMsg).Name} 失败");
                }
            };
        }

        private static Action<Packet> CreateClientAction<TMsg>(object instance, MethodInfo method, Func<byte[], Type, object> deserializeFunc)
            where TMsg : class
        {
            var del = (Action<TMsg>)Delegate.CreateDelegate(typeof(Action<TMsg>), instance, method);
            return (packet) =>
            {
                object deserializedObj = deserializeFunc(packet.Payload, typeof(TMsg));
                if (deserializedObj is TMsg msg)
                {
                    del(msg);
                }
                else
                {
                    Debug.LogError($"[AutoBinder] 运行时路由失败: MsgId {packet.MsgId}, 数据反序列化为 {typeof(TMsg).Name} 失败");
                }
            };
        }
    }
}