using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.GameDemo.Shared
{
    #region ================= 共享数据结构 =================

    public sealed class DemoPlayerInfo
    {
        public string SessionId;
        public float PosX;
        public float PosY;
        public float PosZ;
        public int Hp;
    }

    #endregion

    #region ================= 客户端 -> 服务端 (C2S) =================

    [NetMsg(1001, NetScope.Room, NetDir.C2S)]
    public sealed class C2S_DemoMoveReq
    {
        public float TargetX;
        public float TargetY;
        public float TargetZ;
    }

    [NetMsg(1002, NetScope.Room, NetDir.C2S)]
    public sealed class C2S_DemoAttackReq
    {
        public string TargetSessionId;
    }

    #endregion

    #region ================= 服务端 -> 客户端 (S2C) =================

    [NetMsg(1003, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_DemoSnapshot
    {
        public DemoPlayerInfo[] Players;
    }

    [NetMsg(1004, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_DemoPlayerJoined
    {
        public DemoPlayerInfo Player;
    }

    [NetMsg(1005, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_DemoPlayerLeft
    {
        public string SessionId;
    }

    [NetMsg(1006, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_DemoMoveSync
    {
        public string SessionId;
        public float TargetX;
        public float TargetY;
        public float TargetZ;
    }

    [NetMsg(1007, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_DemoHpSync
    {
        public string SessionId;
        public int Hp;
    }

    // 核心修复：彻底移除已废弃的 1008 (S2C_DemoGameOver) 协议，避免代码残留

    #endregion

    #region ================= 客户端内部事件 (LiteEventBus) =================

    public struct DemoSnapshotEvent : IRoomEvent
    {
        public DemoPlayerInfo[] Players;
    }

    public struct DemoPlayerJoinedEvent : IRoomEvent
    {
        public DemoPlayerInfo Player;
    }

    public struct DemoPlayerLeftEvent : IRoomEvent
    {
        public string SessionId;
    }

    public struct DemoMoveEvent : IRoomEvent
    {
        public string SessionId;
        public float TargetX;
        public float TargetY;
        public float TargetZ;
    }

    public struct DemoHpEvent : IRoomEvent
    {
        public string SessionId;
        public int Hp;
    }

    // 注意：内部事件总线使用的 Event 必须保留，因为 ClientDemoGameComponent 监听到基础 503 协议后，仍需要将其转化为该事件通知表现层
    public struct DemoGameOverEvent : IRoomEvent
    {
        public string WinnerSessionId;
    }

    #endregion
}