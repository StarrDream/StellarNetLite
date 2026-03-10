using System;
using UnityEngine;
using Newtonsoft.Json;
using Mirror;
using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Shared.Infrastructure
{
    public interface INetSerializer
    {
        byte[] Serialize(object obj);
        object Deserialize(byte[] data, Type type);
    }

    public sealed class JsonNetSerializer : INetSerializer
    {
        public byte[] Serialize(object obj)
        {
            if (obj == null)
            {
                LiteLogger.LogError("[JsonNetSerializer] ",$" 序列化失败: 传入对象为空");
                return new byte[0];
            }

            try
            {
                string json = JsonConvert.SerializeObject(obj);
                return System.Text.Encoding.UTF8.GetBytes(json);
            }
            catch (Exception e)
            {
                LiteLogger.LogError($"[JsonNetSerializer]",$"  序列化异常: {e.Message}");
                return new byte[0];
            }
        }

        public object Deserialize(byte[] data, Type type)
        {
            if (data == null || data.Length == 0)
            {
                LiteLogger.LogError("[JsonNetSerializer] ",$" 反序列化失败: 字节数组为空");
                return null;
            }

            if (type == null)
            {
                LiteLogger.LogError("[JsonNetSerializer] ",$" 反序列化失败: 目标类型为空");
                return null;
            }

            try
            {
                string json = System.Text.Encoding.UTF8.GetString(data);
                return JsonConvert.DeserializeObject(json, type);
            }
            catch (Exception e)
            {
                LiteLogger.LogError($"[JsonNetSerializer] ",$" 反序列化异常: 目标类型 {type.Name}, 错误: {e.Message}");
                return null;
            }
        }
    }

    public struct MirrorPacketMsg : NetworkMessage
    {
        // 核心新增：同步传输 Seq
        public uint Seq;
        public int MsgId;
        public byte Scope;
        public string RoomId;
        public byte[] Payload;

        public MirrorPacketMsg(Packet packet)
        {
            Seq = packet.Seq;
            MsgId = packet.MsgId;
            Scope = (byte)packet.Scope;
            RoomId = packet.RoomId ?? string.Empty;
            Payload = packet.Payload;
        }

        public Packet ToPacket()
        {
            return new Packet(Seq, MsgId, (NetScope)Scope, RoomId, Payload);
        }
    }
}