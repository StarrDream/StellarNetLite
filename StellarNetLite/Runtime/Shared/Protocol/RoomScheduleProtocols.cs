using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Shared.Protocol
{
    [NetMsg(200, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_CreateRoom
    {
        public string RoomName;

        public int[] ComponentIds;
        // 架构说明：底层网络已实现基于 Seq 的防重放机制，业务层不再需要手动传递 Token 保证幂等性。
    }

    [NetMsg(201, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_CreateRoomResult
    {
        public bool Success;
        public string RoomId;
        public int[] ComponentIds;
        public string Reason;
    }

    [NetMsg(202, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_JoinRoom
    {
        public string RoomId;
    }

    [NetMsg(203, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_JoinRoomResult
    {
        public bool Success;
        public string RoomId;
        public int[] ComponentIds;
        public string Reason;
    }

    [NetMsg(204, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_LeaveRoom
    {
    }

    [NetMsg(205, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_LeaveRoomResult
    {
        public bool Success;
    }

    [NetMsg(206, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_RoomSetupReady
    {
        public string RoomId;
    }
}