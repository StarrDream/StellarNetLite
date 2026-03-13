using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Shared.Protocol
{
    public sealed class MemberInfo
    {
        public string SessionId;

        // 扩展表现层数据
        public string Uid;
        public string DisplayName;
        // 后续还可以加 public int AvatarId; 等等

        public bool IsReady;
        public bool IsOwner;
    }

    [NetMsg(300, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_RoomSnapshot
    {
        public string RoomName;
        public int MaxMembers;
        public bool IsPrivate;
        public MemberInfo[] Members;
    }

    [NetMsg(301, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_MemberJoined
    {
        // 加入时，下发全量信息，供客户端直接渲染 UI
        public MemberInfo Member;
    }

    [NetMsg(302, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_MemberLeft
    {
        // 离开时，只发 SessionId，客户端通过本地缓存查名字，节省带宽
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