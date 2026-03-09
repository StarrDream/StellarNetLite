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

        // 核心修复 2：废弃 DeserializeInto，改为返回全新实例的 Deserialize
        object Deserialize(byte[] data, Type type);
    }

    public sealed class JsonNetSerializer : INetSerializer
    {
        public byte[] Serialize(object obj)
        {
            if (obj == null)
            {
                Debug.LogError("[JsonNetSerializer] 序列化失败: 传入对象为空");
                return new byte[0];
            }

            try
            {
                string json = JsonConvert.SerializeObject(obj);
                return System.Text.Encoding.UTF8.GetBytes(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[JsonNetSerializer] 序列化异常: {e.Message}");
                return new byte[0];
            }
        }

        public object Deserialize(byte[] data, Type type)
        {
            if (data == null || data.Length == 0)
            {
                Debug.LogError("[JsonNetSerializer] 反序列化失败: 字节数组为空");
                return null;
            }

            if (type == null)
            {
                Debug.LogError("[JsonNetSerializer] 反序列化失败: 目标类型为空");
                return null;
            }

            try
            {
                string json = System.Text.Encoding.UTF8.GetString(data);
                // 核心机制：每次都反序列化为全新的对象，保证状态绝对纯净
                return JsonConvert.DeserializeObject(json, type);
            }
            catch (Exception e)
            {
                Debug.LogError($"[JsonNetSerializer] 反序列化异常: 目标类型 {type.Name}, 错误: {e.Message}");
                return null;
            }
        }
    }

    public struct MirrorPacketMsg : NetworkMessage
    {
        public int MsgId;
        public byte Scope;
        public string RoomId;
        public byte[] Payload;

        public MirrorPacketMsg(Packet packet)
        {
            MsgId = packet.MsgId;
            Scope = (byte)packet.Scope;
            RoomId = packet.RoomId ?? string.Empty;
            Payload = packet.Payload;
        }

        public Packet ToPacket()
        {
            return new Packet(MsgId, (NetScope)Scope, RoomId, Payload);
        }
    }
}