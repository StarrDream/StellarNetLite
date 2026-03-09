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

        public override void OnInit()
        {
            Members.Clear();
        }

        [NetHandler]
        public void OnS2C_RoomSnapshot(S2C_RoomSnapshot msg)
        {
            if (msg == null)
            {
                Debug.LogError("[ClientRoomSettings] 处理快照失败: msg 为空");
                return;
            }

            if (msg.Members == null)
            {
                Debug.LogError("[ClientRoomSettings] 处理快照失败: Members 数组为空");
                return;
            }

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
            if (msg == null)
            {
                Debug.LogError("[ClientRoomSettings] 处理成员加入失败: msg 为空");
                return;
            }

            if (string.IsNullOrEmpty(msg.SessionId))
            {
                Debug.LogError("[ClientRoomSettings] 处理成员加入失败: SessionId 为空");
                return;
            }

            if (!Members.ContainsKey(msg.SessionId))
            {
                Members[msg.SessionId] = new MemberInfo { SessionId = msg.SessionId, IsReady = false, IsOwner = false };
                Debug.Log($"[ClientRoomSettings] 成员加入: {msg.SessionId}");
            }
        }

        [NetHandler]
        public void OnS2C_MemberLeft(S2C_MemberLeft msg)
        {
            if (msg == null)
            {
                Debug.LogError("[ClientRoomSettings] 处理成员离开失败: msg 为空");
                return;
            }

            if (string.IsNullOrEmpty(msg.SessionId))
            {
                Debug.LogError("[ClientRoomSettings] 处理成员离开失败: SessionId 为空");
                return;
            }

            if (Members.Remove(msg.SessionId))
            {
                Debug.Log($"[ClientRoomSettings] 成员离开: {msg.SessionId}");
            }
        }

        [NetHandler]
        public void OnS2C_MemberReadyChanged(S2C_MemberReadyChanged msg)
        {
            if (msg == null)
            {
                Debug.LogError("[ClientRoomSettings] 处理准备状态失败: msg 为空");
                return;
            }

            if (string.IsNullOrEmpty(msg.SessionId))
            {
                Debug.LogError("[ClientRoomSettings] 处理准备状态失败: SessionId 为空");
                return;
            }

            if (Members.TryGetValue(msg.SessionId, out var member))
            {
                member.IsReady = msg.IsReady;
                Debug.Log($"[ClientRoomSettings] 成员准备状态变更: {msg.SessionId} -> {msg.IsReady}");
            }
        }
    }
}