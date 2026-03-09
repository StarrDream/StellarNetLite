using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Shared.Protocol
{
    [NetMsg(400, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_SendLobbyChat
    {
        public string Content;
    }

    [NetMsg(401, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_LobbyChatMsg
    {
        public string SenderSessionId;
        public string Content;
        public long SendUnixTime;
    }
}