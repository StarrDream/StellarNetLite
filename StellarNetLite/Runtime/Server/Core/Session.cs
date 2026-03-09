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

        // 核心修复 1：新增房间授权通行证。防止恶意客户端越权发送握手协议强行加房
        public string AuthorizedRoomId { get; private set; }

        public bool IsOnline => ConnectionId >= 0;
        public DateTime LastOfflineTime { get; private set; }

        public Session(string sessionId, string uid, int connectionId)
        {
            SessionId = sessionId;
            Uid = uid;
            ConnectionId = connectionId;
            CurrentRoomId = string.Empty;
            AuthorizedRoomId = string.Empty;
            LastOfflineTime = DateTime.UtcNow;
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

        // 颁发通行证（在建房/加房校验通过后调用）
        public void AuthorizeRoom(string roomId)
        {
            AuthorizedRoomId = roomId;
        }

        // 核销通行证（在完成最终握手后调用）
        public void ClearAuthorizedRoom()
        {
            AuthorizedRoomId = string.Empty;
        }

        public void MarkOffline()
        {
            ConnectionId = -1;
            LastOfflineTime = DateTime.UtcNow;
        }
    }
}