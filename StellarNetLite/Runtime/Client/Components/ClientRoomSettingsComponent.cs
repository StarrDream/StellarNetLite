using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Client.Core;
using UnityEngine;

namespace StellarNet.Lite.Client.Components
{
    public sealed class ClientRoomSettingsComponent : ClientRoomComponent
    {
        public readonly Dictionary<string, MemberInfo> Members = new Dictionary<string, MemberInfo>();

        public bool IsGameStarted { get; private set; }

        public override void OnInit()
        {
            Members.Clear();
            IsGameStarted = false;
        }

        [NetHandler]
        public void OnS2C_RoomSnapshot(S2C_RoomSnapshot msg)
        {
            if (msg == null || msg.Members == null) return;

            Members.Clear();
            foreach (var m in msg.Members)
            {
                if (m != null)
                {
                    Members[m.SessionId] = m;
                }
            }

            Debug.Log($"[ClientRoomSettings] 收到房间快照, 当前人数: {Members.Count}");
        }

        [NetHandler]
        public void OnS2C_MemberJoined(S2C_MemberJoined msg)
        {
            if (msg == null || string.IsNullOrEmpty(msg.SessionId)) return;

            if (!Members.ContainsKey(msg.SessionId))
            {
                Members[msg.SessionId] = new MemberInfo { SessionId = msg.SessionId, IsReady = false, IsOwner = false };
                Debug.Log($"[ClientRoomSettings] 成员加入: {msg.SessionId}");
            }
        }

        [NetHandler]
        public void OnS2C_MemberLeft(S2C_MemberLeft msg)
        {
            if (msg == null || string.IsNullOrEmpty(msg.SessionId)) return;

            if (Members.Remove(msg.SessionId))
            {
                Debug.Log($"[ClientRoomSettings] 成员离开: {msg.SessionId}");
            }
        }

        [NetHandler]
        public void OnS2C_MemberReadyChanged(S2C_MemberReadyChanged msg)
        {
            if (msg == null || string.IsNullOrEmpty(msg.SessionId)) return;

            if (Members.TryGetValue(msg.SessionId, out var member))
            {
                member.IsReady = msg.IsReady;
                Debug.Log($"[ClientRoomSettings] 成员准备状态变更: {msg.SessionId} -> {msg.IsReady}");
            }
        }

        [NetHandler]
        public void OnS2C_GameStarted(S2C_GameStarted msg)
        {
            IsGameStarted = true;
            Debug.Log($"[ClientRoomSettings] 收到游戏开始事件, 时间戳: {msg?.StartUnixTime}");
        }

        // 核心新增：监听标准协议 503，重置房间状态机，形成闭环
        [NetHandler]
        public void OnS2C_GameEnded(S2C_GameEnded msg)
        {
            IsGameStarted = false;

            // 游戏结束后，自动取消所有人的准备状态
            foreach (var kvp in Members)
            {
                kvp.Value.IsReady = false;
            }

            Debug.Log($"[ClientRoomSettings] 收到游戏结束事件, 胜者: {msg?.WinnerSessionId}。房间状态已重置为等待中。");
        }
    }
}