using System;
using UnityEngine;

namespace StellarNet.Lite.Server.Core
{
    public sealed class Session
    {
        public string SessionId { get; }
        public string Uid { get; }
        public int ConnectionId { get; private set; }
        public string CurrentRoomId { get; private set; }
        public string AuthorizedRoomId { get; private set; }
        public bool IsOnline => ConnectionId >= 0;
        public DateTime LastOfflineTime { get; private set; }

        // 架构说明：记录该会话最后一次成功处理的网络包序列号，用于底层防重放
        public uint LastReceivedSeq { get; private set; }

        public Session(string sessionId, string uid, int connectionId)
        {
            SessionId = sessionId;
            Uid = uid;
            ConnectionId = connectionId;
            CurrentRoomId = string.Empty;
            AuthorizedRoomId = string.Empty;
            LastOfflineTime = DateTime.UtcNow;
            LastReceivedSeq = 0;
        }

        public void UpdateConnection(int newConnectionId)
        {
            ConnectionId = newConnectionId;
        }

        public void BindRoom(string roomId)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                Debug.LogError($"[Session] 绑定房间失败: 传入的 roomId 为空，SessionId: {SessionId}");
                return;
            }

            CurrentRoomId = roomId;
        }

        public void UnbindRoom()
        {
            CurrentRoomId = string.Empty;
        }

        public void AuthorizeRoom(string roomId)
        {
            AuthorizedRoomId = roomId;
        }

        public void ClearAuthorizedRoom()
        {
            AuthorizedRoomId = string.Empty;
        }

        public void MarkOffline()
        {
            ConnectionId = -1;
            LastOfflineTime = DateTime.UtcNow;
        }

        // 架构说明：尝试消费传入的序列号。若序列号小于等于已记录的最大值，说明是重放包或乱序包，拒绝消费。
        public bool TryConsumeSeq(uint seq)
        {
            if (seq <= LastReceivedSeq)
            {
                return false;
            }

            LastReceivedSeq = seq;
            return true;
        }
    }
}