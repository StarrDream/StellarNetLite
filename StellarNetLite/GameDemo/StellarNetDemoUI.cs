using System;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Components;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Demo
{
    /// <summary>
    /// 核心重构 (Point 15)：物理边界剥离。
    /// 本脚本现在仅作为纯粹的客户端消费方，不再承担服务端上帝视角的管理职责。
    /// </summary>
    public class StellarNetDemoUI : MonoBehaviour
    {
        private StellarNetMirrorManager _manager;
        private string _inputAccountId = "Player_1001";
        private string _inputRoomName = "我的对战房间";
        private Vector2 _clientScroll;
        private ClientReplayPlayer _replayPlayer;
        private RoomBriefInfo[] _roomList = new RoomBriefInfo[0];
        private string[] _replayList = new string[0];
        private string _downloadingReplayId = string.Empty;

        private void Start()
        {
            _manager = NetworkManager.singleton as StellarNetMirrorManager;
        }

        private void Update()
        {
            _replayPlayer?.Update(Time.deltaTime);
        }

        private void OnEnable()
        {
            GlobalEventBus<RoomListEvent>.OnEvent += HandleRoomList;
            GlobalEventBus<ReplayListEvent>.OnEvent += HandleReplayList;
            GlobalEventBus<ReplayDownloadedEvent>.OnEvent += HandleReplayDownloaded;
        }

        private void OnDisable()
        {
            GlobalEventBus<RoomListEvent>.OnEvent -= HandleRoomList;
            GlobalEventBus<ReplayListEvent>.OnEvent -= HandleReplayList;
            GlobalEventBus<ReplayDownloadedEvent>.OnEvent -= HandleReplayDownloaded;
        }

        private void HandleRoomList(RoomListEvent evt)
        {
            _roomList = evt.Rooms;
        }

        private void HandleReplayList(ReplayListEvent evt)
        {
            _replayList = evt.ReplayIds;
        }

        private void HandleReplayDownloaded(ReplayDownloadedEvent evt)
        {
            _downloadingReplayId = string.Empty;
            if (evt.Success && evt.File != null)
            {
                _replayPlayer = new ClientReplayPlayer(_manager.ClientApp);
                _replayPlayer.StartReplay(evt.File);
            }
            else
            {
                LiteLogger.LogError("DemoUI", $"录像下载或解析失败: {evt.Reason}");
            }
        }

        private void OnGUI()
        {
            if (_manager == null) return;

            if (!NetworkServer.active && !NetworkClient.active)
            {
                DrawModeSelection();
                return;
            }

            if (NetworkClient.active && _manager.ClientApp != null)
            {
                GUILayout.BeginArea(new Rect(20, 20, 400, Screen.height - 40), GUI.skin.box);
                _clientScroll = GUILayout.BeginScrollView(_clientScroll);
                DrawClientPanel();
                GUILayout.EndScrollView();
                GUILayout.EndArea();
            }
        }

        private void DrawModeSelection()
        {
            GUILayout.BeginArea(new Rect(Screen.width / 2 - 150, Screen.height / 2 - 100, 300, 250), GUI.skin.box);
            GUILayout.Label("<b><size=16>StellarNet 综合测试台</size></b>",
                new GUIStyle(GUI.skin.label) { richText = true, alignment = TextAnchor.MiddleCenter });
            GUILayout.Space(20);
            if (GUILayout.Button("Host 模式 (Server + Client 同进程)", GUILayout.Height(40))) _manager.StartHost();
            GUILayout.Space(10);
            if (GUILayout.Button("Server Only (独立服务端)", GUILayout.Height(40))) _manager.StartServer();
            GUILayout.Space(10);
            if (GUILayout.Button("Client Only (独立客户端)", GUILayout.Height(40))) _manager.StartClient();
            GUILayout.EndArea();
        }

        private void DrawClientPanel()
        {
            var app = _manager.ClientApp;

            GUILayout.Label("<b><size=16>客户端控制台 (Client View)</size></b>",
                new GUIStyle(GUI.skin.label) { richText = true, alignment = TextAnchor.MiddleCenter });
            GUILayout.Space(10);

            if (app.State == ClientAppState.ReplayRoom)
            {
                DrawClientReplayPanel();
                return;
            }

            if (!app.Session.IsLoggedIn)
            {
                GUILayout.Label("当前状态: 未登录");
                GUILayout.Space(10);
                GUILayout.BeginHorizontal();
                GUILayout.Label("账号 ID:", GUILayout.Width(60));
                _inputAccountId = GUILayout.TextField(_inputAccountId);
                GUILayout.EndHorizontal();
                GUILayout.Space(10);
                if (GUILayout.Button("发起登录 (Login)", GUILayout.Height(40)))
                {
                    // 核心修复 (Point 18)：发送登录请求时附带客户端版本号
                    app.SendMessage(new C2S_Login { AccountId = _inputAccountId, ClientVersion = Application.version });
                }
            }
            else if (app.State == ClientAppState.Idle)
            {
                GUILayout.Label($"当前状态: 大厅闲置\nSessionId: {app.Session.SessionId}\nUID: {app.Session.Uid}");
                GUILayout.Space(10);

                GUI.color = Color.yellow;
                GUILayout.Label("--- 断线重连测试区 ---");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("接受重连", GUILayout.Height(30)))
                {
                    app.SendMessage(new C2S_ConfirmReconnect { Accept = true });
                }

                if (GUILayout.Button("拒绝重连", GUILayout.Height(30)))
                {
                    app.SendMessage(new C2S_ConfirmReconnect { Accept = false });
                }

                GUILayout.EndHorizontal();
                GUI.color = Color.white;

                GUILayout.Space(10);
                GUILayout.Label("--- 房间创建 ---");
                GUILayout.BeginHorizontal();
                GUILayout.Label("房间名称:", GUILayout.Width(60));
                _inputRoomName = GUILayout.TextField(_inputRoomName);
                GUILayout.EndHorizontal();
                GUILayout.Space(5);
                if (GUILayout.Button("创建基础房间 (仅 Settings: ID 1)", GUILayout.Height(30)))
                {
                    app.SendMessage(new C2S_CreateRoom { RoomName = _inputRoomName, ComponentIds = new int[] { 1 } });
                }

                GUILayout.Space(5);
                GUI.color = Color.green;
                if (GUILayout.Button("创建对战房间 (Settings + GameDemo: ID 1, 100)", GUILayout.Height(40)))
                {
                    app.SendMessage(new C2S_CreateRoom
                        { RoomName = _inputRoomName, ComponentIds = new int[] { 1, 100 } });
                }

                GUI.color = Color.white;

                GUILayout.Space(15);
                GUILayout.Label("--- 房间大厅 ---");
                if (GUILayout.Button("刷新房间列表", GUILayout.Height(30)))
                {
                    app.SendMessage(new C2S_GetRoomList());
                }

                GUILayout.Space(5);

                if (_roomList != null && _roomList.Length > 0)
                {
                    foreach (var room in _roomList)
                    {
                        GUILayout.BeginHorizontal("box");
                        string stateStr = room.State == 0 ? "<color=green>等待中</color>" : "<color=yellow>游戏中</color>";
                        GUILayout.Label(
                            $"<b>{room.RoomName}</b>\nID: {room.RoomId} | 人数: {room.MemberCount} | 状态: {stateStr}",
                            new GUIStyle(GUI.skin.label) { richText = true });

                        if (room.State == 0)
                        {
                            if (GUILayout.Button("加入", GUILayout.Width(60), GUILayout.Height(35)))
                            {
                                app.SendMessage(new C2S_JoinRoom { RoomId = room.RoomId });
                            }
                        }
                        else
                        {
                            GUI.enabled = false;
                            GUILayout.Button("加入", GUILayout.Width(60), GUILayout.Height(35));
                            GUI.enabled = true;
                        }

                        GUILayout.EndHorizontal();
                    }
                }
                else
                {
                    GUILayout.Label("当前没有活跃的房间。");
                }

                GUILayout.Space(15);
                GUILayout.Label("--- 录像大厅 ---");
                GUI.color = Color.cyan;
                if (GUILayout.Button("刷新云端录像列表", GUILayout.Height(30)))
                {
                    app.SendMessage(new C2S_GetReplayList());
                }

                GUI.color = Color.white;
                GUILayout.Space(5);

                if (_replayList != null && _replayList.Length > 0)
                {
                    foreach (string replayId in _replayList)
                    {
                        GUILayout.BeginHorizontal("box");
                        GUILayout.Label($"录像 ID:\n{replayId}");

                        bool isDownloading = _downloadingReplayId == replayId;
                        GUI.enabled = !isDownloading && string.IsNullOrEmpty(_downloadingReplayId);

                        if (GUILayout.Button(isDownloading ? "下载中..." : "下载并播放", GUILayout.Width(90),
                                GUILayout.Height(35)))
                        {
                            _downloadingReplayId = replayId;
                            app.SendMessage(new C2S_DownloadReplay { ReplayId = replayId });
                        }

                        GUI.enabled = true;
                        GUILayout.EndHorizontal();
                    }
                }
                else
                {
                    GUILayout.Label("云端暂无录像。");
                }
            }
            else if (app.State == ClientAppState.OnlineRoom)
            {
                GUILayout.Label($"当前状态: 房间内\nRoomId: {app.CurrentRoom.RoomId}");
                GUILayout.Space(10);

                ClientRoomSettingsComponent settingsComp = app.CurrentRoom.GetComponent<ClientRoomSettingsComponent>();
                if (settingsComp != null)
                {
                    if (!settingsComp.IsGameStarted)
                    {
                        bool isMeOwner = false;
                        bool allOthersReady = true;

                        GUILayout.Label("<b>房间等待中 - 成员列表:</b>", new GUIStyle(GUI.skin.label) { richText = true });
                        foreach (var kvp in settingsComp.Members)
                        {
                            var member = kvp.Value;
                            bool isMe = member.SessionId == app.Session.SessionId;
                            if (isMe && member.IsOwner) isMeOwner = true;
                            if (!isMe && !member.IsReady) allOthersReady = false;

                            string meStr = isMe ? " (我)" : "";
                            string ownerStr = member.IsOwner ? "<color=yellow>[房主]</color>" : "";
                            string readyStr = member.IsReady ? "<color=green>已准备</color>" : "<color=red>未准备</color>";

                            GUILayout.Label($"- {member.SessionId.Substring(0, 8)}...{meStr} {ownerStr} 状态: {readyStr}",
                                new GUIStyle(GUI.skin.label) { richText = true });
                        }

                        GUILayout.Space(10);
                        if (GUILayout.Button("切换准备状态 (Toggle Ready)", GUILayout.Height(40)))
                        {
                            if (settingsComp.Members.TryGetValue(app.Session.SessionId, out var myInfo))
                            {
                                app.SendMessage(new C2S_SetReady { IsReady = !myInfo.IsReady });
                            }
                        }

                        if (isMeOwner)
                        {
                            GUILayout.Space(10);
                            GUI.enabled = allOthersReady;
                            GUI.color = Color.green;
                            if (GUILayout.Button(allOthersReady ? "开始游戏 (Start Game)" : "等待全员准备...",
                                    GUILayout.Height(40)))
                            {
                                app.SendMessage(new C2S_StartGame());
                            }

                            GUI.color = Color.white;
                            GUI.enabled = true;
                        }
                    }
                    else
                    {
                        GUILayout.Label("<color=green><b>游戏进行中...</b></color>",
                            new GUIStyle(GUI.skin.label) { richText = true });

                        if (settingsComp.Members.TryGetValue(app.Session.SessionId, out var myInfo) && myInfo.IsOwner)
                        {
                            GUILayout.Space(10);
                            GUI.color = Color.red;
                            if (GUILayout.Button("强制结束游戏 (Force End Game)", GUILayout.Height(40)))
                            {
                                app.SendMessage(new C2S_EndGame());
                            }

                            GUI.color = Color.white;
                        }
                    }
                }

                GUILayout.Space(10);
                if (GUILayout.Button("正常离开房间 (Leave Room)", GUILayout.Height(40)))
                {
                    app.SendMessage(new C2S_LeaveRoom());
                }
            }

            GUILayout.Space(30);
            GUI.color = Color.red;
            if (GUILayout.Button("模拟物理断网 (断开 Mirror Client 但不发 Leave)", GUILayout.Height(40)))
            {
                _manager.StopClient();
            }

            GUI.color = Color.white;
        }

        private void DrawClientReplayPanel()
        {
            GUILayout.Label("当前状态: <color=cyan>回放沙盒模式 (ReplayRoom)</color>",
                new GUIStyle(GUI.skin.label) { richText = true });
            GUILayout.Space(10);

            if (_replayPlayer == null) return;

            int totalTicks = _replayPlayer.GetTotalTicks();
            int currentTick = _replayPlayer.CurrentTick;

            GUILayout.BeginVertical("box");
            GUILayout.Label($"<b>播放进度: {currentTick} / {totalTicks}</b>",
                new GUIStyle(GUI.skin.label) { richText = true });

            float newProgress = GUILayout.HorizontalSlider(currentTick, 0, totalTicks);
            if (Mathf.Abs(newProgress - currentTick) > 1f)
            {
                _replayPlayer.Seek((int)newProgress);
            }

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_replayPlayer.IsPaused ? "播放 (Play)" : "暂停 (Pause)", GUILayout.Height(30)))
            {
                _replayPlayer.IsPaused = !_replayPlayer.IsPaused;
            }

            if (GUILayout.Button("重新播放 (Restart)", GUILayout.Height(30)))
            {
                _replayPlayer.Seek(0);
                _replayPlayer.IsPaused = false;
            }

            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            GUILayout.Label($"播放倍速: {_replayPlayer.PlaybackSpeed}x");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("0.5x")) _replayPlayer.PlaybackSpeed = 0.5f;
            if (GUILayout.Button("1.0x")) _replayPlayer.PlaybackSpeed = 1.0f;
            if (GUILayout.Button("2.0x")) _replayPlayer.PlaybackSpeed = 2.0f;
            if (GUILayout.Button("4.0x")) _replayPlayer.PlaybackSpeed = 4.0f;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.Space(20);
            GUI.color = Color.red;
            if (GUILayout.Button("退出回放 (Stop Replay)", GUILayout.Height(40)))
            {
                _replayPlayer.StopReplay();
                _replayPlayer = null;
            }

            GUI.color = Color.white;
        }
    }
}