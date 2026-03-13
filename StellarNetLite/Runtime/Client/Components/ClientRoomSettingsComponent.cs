using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Client.Components
{
    [RoomComponent(1, "RoomSettings", "基础房间设置")]
    public sealed class ClientRoomSettingsComponent : ClientRoomComponent
    {
        private readonly ClientApp _app;
        public readonly Dictionary<string, MemberInfo> Members = new Dictionary<string, MemberInfo>();

        public bool IsGameStarted { get; private set; }
        public string RoomName { get; private set; } = string.Empty;
        public int MaxMembers { get; private set; } = 0;
        public bool IsPrivate { get; private set; } = false;

        public ClientRoomSettingsComponent(ClientApp app)
        {
            _app = app;
        }

        public override void OnInit()
        {
            Members.Clear();
            IsGameStarted = false;
            RoomName = string.Empty;
            MaxMembers = 0;
            IsPrivate = false;
        }

        [NetHandler]
        public void OnS2C_RoomSnapshot(S2C_RoomSnapshot msg)
        {
            if (msg == null || msg.Members == null) return;

            RoomName = msg.RoomName;
            MaxMembers = msg.MaxMembers;
            IsPrivate = msg.IsPrivate;

            Members.Clear();
            foreach (var m in msg.Members)
            {
                if (m != null)
                {
                    Members[m.SessionId] = m;
                }
            }

            NetLogger.LogInfo($"[ClientRoomSettings]", $"收到房间快照, 房间名: {RoomName}, 当前人数: {Members.Count}/{MaxMembers}");
            Room.NetEventSystem.Broadcast(msg);
        }

        [NetHandler]
        public void OnS2C_MemberJoined(S2C_MemberJoined msg)
        {
            // 适配新协议：直接将服务端下发的全量 MemberInfo 存入字典
            if (msg == null || msg.Member == null || string.IsNullOrEmpty(msg.Member.SessionId)) return;

            Members[msg.Member.SessionId] = msg.Member;
            NetLogger.LogInfo($"[ClientRoomSettings]", $"成员加入: {msg.Member.DisplayName} (UID: {msg.Member.Uid})");

            Room.NetEventSystem.Broadcast(msg);
        }

        [NetHandler]
        public void OnS2C_MemberLeft(S2C_MemberLeft msg)
        {
            if (msg == null || string.IsNullOrEmpty(msg.SessionId)) return;

            // 离开前，可以通过 SessionId 查出他的名字打印日志
            if (Members.TryGetValue(msg.SessionId, out var leftMember))
            {
                NetLogger.LogInfo($"[ClientRoomSettings]", $"成员离开: {leftMember.DisplayName} (UID: {leftMember.Uid})");
                Members.Remove(msg.SessionId);
            }

            Room.NetEventSystem.Broadcast(msg);
        }

        [NetHandler]
        public void OnS2C_MemberReadyChanged(S2C_MemberReadyChanged msg)
        {
            if (msg == null || string.IsNullOrEmpty(msg.SessionId)) return;

            if (Members.TryGetValue(msg.SessionId, out var member))
            {
                member.IsReady = msg.IsReady;
                NetLogger.LogInfo($"[ClientRoomSettings]", $"成员准备状态变更: {member.DisplayName} -> {msg.IsReady}");
            }

            Room.NetEventSystem.Broadcast(msg);
        }

        [NetHandler]
        public void OnS2C_GameStarted(S2C_GameStarted msg)
        {
            if (msg == null) return;
            IsGameStarted = true;
            Room.NetEventSystem.Broadcast(msg);
        }

        [NetHandler]
        public void OnS2C_GameEnded(S2C_GameEnded msg)
        {
            if (msg == null) return;
            IsGameStarted = false;
            foreach (var kvp in Members) kvp.Value.IsReady = false;
            Room.NetEventSystem.Broadcast(msg);
        }
    }
}