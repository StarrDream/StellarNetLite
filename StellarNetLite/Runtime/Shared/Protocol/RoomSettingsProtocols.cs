using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Shared.Protocol
{
    public sealed class MemberInfo
    {
        public string SessionId;
        public bool IsReady;
        public bool IsOwner;
    }

    [NetMsg(300, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_RoomSnapshot
    {
        public MemberInfo[] Members;
    }

    [NetMsg(301, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_MemberJoined
    {
        public string SessionId;
    }

    [NetMsg(302, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_MemberLeft
    {
        public string SessionId;
    }

    [NetMsg(303, NetScope.Room, NetDir.C2S)]
    public sealed class C2S_SetReady
    {
        public bool IsReady;
    }

    [NetMsg(304, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_MemberReadyChanged
    {
        public string SessionId;
        public bool IsReady;
    }
}