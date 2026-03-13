using UnityEngine;
using Mirror;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Demo
{
    /// <summary>
    /// 独立的物理隔离管理面板。
    /// 职责：仅在服务端激活时显示，提供对 ServerApp 的上帝视角监控与强制干预能力。
    /// </summary>
    public class ServerAdminPanel : MonoBehaviour
    {
        private StellarNetMirrorManager _manager;
        private Vector2 _serverScroll;

        private void Start()
        {
            _manager = NetworkManager.singleton as StellarNetMirrorManager;
        }

        private void OnGUI()
        {
            if (_manager == null || !NetworkServer.active || _manager.ServerApp == null) return;

            // 如果客户端也激活了（Host 模式），面板向右偏移，避免重叠
            int xOffset = NetworkClient.active ? 440 : 20;

            GUILayout.BeginArea(new Rect(xOffset, 20, 500, Screen.height - 40), GUI.skin.box);
            _serverScroll = GUILayout.BeginScrollView(_serverScroll);
            DrawServerPanel();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawServerPanel()
        {
            GUILayout.Label("<b><size=16>服务端上帝视角 (Server Admin)</size></b>",
                new GUIStyle(GUI.skin.label) { richText = true, alignment = TextAnchor.MiddleCenter });
            GUILayout.Space(10);

            var sessions = _manager.ServerApp.Sessions;
            var rooms = _manager.ServerApp.Rooms;

            GUILayout.Label($"<b>在线/离线会话总数: {sessions.Count}</b>", new GUIStyle(GUI.skin.label) { richText = true });
            foreach (var kvp in sessions)
            {
                var session = kvp.Value;
                GUILayout.BeginVertical("box");
                string status = session.IsOnline ? "<color=green>在线</color>" : "<color=gray>物理离线</color>";
                GUILayout.Label($"UID: {session.Uid} | 状态: {status}", new GUIStyle(GUI.skin.label) { richText = true });
                GUILayout.Label($"SessionId: {session.SessionId}");
                GUILayout.Label($"所在房间: {(string.IsNullOrEmpty(session.CurrentRoomId) ? "无" : session.CurrentRoomId)}");

                if (session.IsOnline)
                {
                    GUI.color = Color.red;
                    if (GUILayout.Button("强制踢下线 (KickOut)"))
                    {
                        var msg = new Shared.Protocol.S2C_KickOut { Reason = "被管理员强制踢出" };
                        _manager.ServerApp.SendMessageToSession(session, msg);
                        _manager.ServerApp.UnbindConnection(session);
                        NetLogger.LogWarning("ServerAdmin", "管理员触发强制踢出", "-", session.SessionId);
                    }

                    GUI.color = Color.white;
                }

                GUILayout.EndVertical();
            }

            GUILayout.Space(20);
            GUILayout.Label($"<b>活跃房间总数: {rooms.Count}</b>", new GUIStyle(GUI.skin.label) { richText = true });
            foreach (var kvp in rooms)
            {
                var room = kvp.Value;
                GUILayout.BeginVertical("box");

                // 核心更新：展示 Config 领域模型中的数据
                string privateStr = room.Config.IsPrivate ? "<color=red>[私密]</color>" : "";
                GUILayout.Label(
                    $"RoomName: {room.Config.RoomName} {privateStr}\nRoomId: {room.RoomId} | 人数: {room.MemberCount}/{room.Config.MaxMembers} | 状态: {room.State}",
                    new GUIStyle(GUI.skin.label) { richText = true });

                GUILayout.Label($"录制状态: {(room.IsRecording ? "<color=red>录制中...</color>" : "未录制")}",
                    new GUIStyle(GUI.skin.label) { richText = true });

                GUILayout.BeginHorizontal();
                if (room.State == RoomState.Playing)
                {
                    GUI.enabled = false;
                    GUILayout.Button("对局录制中...");
                    GUI.enabled = true;
                }
                else if (room.State == RoomState.Finished)
                {
                    GUI.enabled = false;
                    GUILayout.Button("已结算，等待清理");
                    GUI.enabled = true;
                }

                GUI.color = Color.red;
                if (GUILayout.Button("强制销毁房间"))
                {
                    _manager.ServerApp.DestroyRoom(room.RoomId);
                    NetLogger.LogWarning("ServerAdmin", "管理员触发强制销毁房间", room.RoomId);
                }

                GUI.color = Color.white;
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }
        }
    }
}