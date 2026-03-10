using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq.Expressions;

namespace StellarNet.Lite.Shared.Core
{
    public enum NetScope : byte
    {
        Global = 0,
        Room = 1
    }

    public enum NetDir : byte
    {
        C2S = 0,
        S2C = 1
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class NetMsgAttribute : Attribute
    {
        public int Id { get; }
        public NetScope Scope { get; }
        public NetDir Dir { get; }

        public NetMsgAttribute(int id, NetScope scope, NetDir dir)
        {
            Id = id;
            Scope = scope;
            Dir = dir;
        }
    }

    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class NetHandlerAttribute : Attribute
    {
        public NetHandlerAttribute()
        {
        }
    }

    // 核心新增：房间业务组件元数据特性，用于驱动常量表自动生成
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class RoomComponentAttribute : Attribute
    {
        public int Id { get; }
        public string Name { get; }

        public RoomComponentAttribute(int id, string name)
        {
            Id = id;
            Name = name;
        }
    }
}