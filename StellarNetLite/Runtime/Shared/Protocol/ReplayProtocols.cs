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

    // 核心新增：客户端表现层使用的纯值类型事件
    public struct ReplayListEvent : IRoomEvent
    {
        public string[] ReplayIds;
    }

    public struct ReplayDownloadedEvent : IRoomEvent
    {
        public bool Success;
        public ReplayFile File;
        public string Reason;
    }
}