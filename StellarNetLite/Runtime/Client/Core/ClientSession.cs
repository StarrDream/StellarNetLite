using StellarNet.Lite.Shared.Infrastructure;
using UnityEngine;

namespace StellarNet.Lite.Client.Core
{
    public sealed class ClientSession
    {
        public string SessionId { get; private set; }
        public string Uid { get; private set; }
        public string CurrentRoomId { get; private set; }

        public bool IsLoggedIn => !string.IsNullOrEmpty(SessionId);

        public void OnLoginSuccess(string sessionId, string uid)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                NetLogger.LogError("[ClientSession]",$"  登录成功回调失败: 下发的 SessionId 为空");
                return;
            }

            SessionId = sessionId;
            Uid = uid;
        }

        public void BindRoom(string roomId)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                NetLogger.LogError("[ClientSession] ",$" 绑定房间失败: 传入的 roomId 为空");
                return;
            }

            CurrentRoomId = roomId;
        }

        public void UnbindRoom()
        {
            CurrentRoomId = string.Empty;
        }

        public void Clear()
        {
            SessionId = string.Empty;
            Uid = string.Empty;
            CurrentRoomId = string.Empty;
        }
    }
}