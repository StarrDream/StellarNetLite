using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Shared.Protocol
{
    /// <summary>
    /// 房间简要信息数据结构
    /// </summary>
    public class RoomBriefInfo
    {
        public string RoomId;
        public string RoomName;
        public int MemberCount;
        public int State; // 0: Waiting, 1: Playing, 2: Finished
    }

    /// <summary>
    /// 客户端请求获取大厅房间列表
    /// 核心修复：将 ID 改为 210，避免与房间调度协议 205 冲突
    /// </summary>
    [NetMsg(210, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_GetRoomList
    {
    }

    /// <summary>
    /// 服务端下发大厅房间列表
    /// 核心修复：将 ID 改为 211，避免与房间调度协议 206 冲突
    /// </summary>
    [NetMsg(211, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_RoomListResponse
    {
        public RoomBriefInfo[] Rooms;
    }

    /// <summary>
    /// 客户端事件总线使用的纯值类型事件
    /// </summary>
    public struct RoomListEvent : IRoomEvent
    {
        public RoomBriefInfo[] Rooms;
    }
}