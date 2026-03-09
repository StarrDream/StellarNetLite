using System.Collections.Generic;

namespace StellarNet.Lite.Shared.Core
{
    /// <summary>
    /// 极简底层传输封套 (高性能值类型版)。
    /// 核心优化：改为 struct，在栈上分配，彻底消灭高频发包时的堆内存 GC。
    /// </summary>
    public struct Packet
    {
        public int MsgId;
        public NetScope Scope;
        public string RoomId;
        public byte[] Payload;

        public Packet(int msgId, NetScope scope, string roomId, byte[] payload)
        {
            MsgId = msgId;
            Scope = scope;
            RoomId = roomId ?? string.Empty;
            Payload = payload;
        }
    }

    /// <summary>
    /// 独立回放帧结构 (高性能值类型版)。
    /// 核心优化：改为 struct，防止长时间录制导致服务端 List 产生海量堆内存对象。
    /// </summary>
    public struct ReplayFrame
    {
        public int Tick;
        public int MsgId;
        public byte[] Payload;
        public string RoomId;

        public ReplayFrame(int tick, int msgId, byte[] payload, string roomId)
        {
            Tick = tick;
            MsgId = msgId;
            Payload = payload;
            RoomId = roomId ?? string.Empty;
        }
    }

    /// <summary>
    /// 完整的回放文件结构。
    /// </summary>
    public sealed class ReplayFile
    {
        public string ReplayId;
        public string RoomId;
        public int[] ComponentIds;
        public List<ReplayFrame> Frames = new List<ReplayFrame>();
    }
}