using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Shared.Protocol
{
    [NetMsg(200, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_CreateRoom
    {
        public string RoomName;
        public int[] ComponentIds;
        public string RequestToken;
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

    // 核心新增：显式就绪握手协议。彻底解耦“下发建房/加房结果”与“加入房间广播树”的时序强绑定。
    [NetMsg(206, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_RoomSetupReady
    {
        public string RoomId;
    }
}