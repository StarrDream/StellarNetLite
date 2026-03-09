using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Shared.Protocol
{
    [NetMsg(600, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_GetReplayList
    {
    }

    [NetMsg(601, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_ReplayList
    {
        public string[] ReplayIds;
    }

    [NetMsg(602, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_DownloadReplay
    {
        public string ReplayId;
    }

    [NetMsg(603, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_DownloadReplayResult
    {
        public bool Success;
        public string ReplayId;
        public string ReplayFileData;
        public string Reason;
    }
}