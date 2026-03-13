using System.Collections.Generic;
using UnityEngine;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.GameDemo.Shared;
using StellarNet.Lite.Client.Core.Events;

namespace StellarNet.Lite.GameDemo.Client
{
    public class DemoGameView : MonoBehaviour
    {
        private StellarNetMirrorManager _manager;
        private Camera _mainCamera;
        private Plane _groundPlane = new Plane(Vector3.up, Vector3.zero);

        private class CapsuleViewData
        {
            public GameObject RootGo;
            public TextMesh HpText;
            public Vector3 TargetPosition;
            public int CurrentHp;
        }

        private readonly Dictionary<string, CapsuleViewData> _views = new Dictionary<string, CapsuleViewData>();
        private string _winnerSessionId = string.Empty;
        private float _autoLeaveTimer = -1f;

        private ClientRoom _boundRoom;

        // 核心重构：使用令牌列表管理房间级事件的注销，确保沙盒切换时绝对不残留
        private readonly List<IUnRegister> _roomEventTokens = new List<IUnRegister>();

        private void Start()
        {
            _manager = FindObjectOfType<StellarNetMirrorManager>();
            _mainCamera = Camera.main;
        }

        private void Update()
        {
            if (_manager == null || _manager.ClientApp == null) return;

            var app = _manager.ClientApp;
            var state = app.State;

            if ((state == ClientAppState.OnlineRoom || state == ClientAppState.ReplayRoom) && app.CurrentRoom != null)
            {
                if (_boundRoom != app.CurrentRoom)
                {
                    UnbindEvents();
                    _boundRoom = app.CurrentRoom;
                    BindEvents();
                }
            }
            else
            {
                if (_boundRoom != null)
                {
                    UnbindEvents();
                    _boundRoom = null;
                    DestroyAllCapsules();
                    _autoLeaveTimer = -1f;
                }

                return;
            }

            if (_autoLeaveTimer > 0 && state == ClientAppState.OnlineRoom)
            {
                _autoLeaveTimer -= Time.deltaTime;
                if (_autoLeaveTimer <= 0)
                {
                    _autoLeaveTimer = -1f;
                    NetLogger.LogInfo("[DemoGameView] ", $" 倒计时结束，自动发送离开房间请求");
                    app.SendMessage(new C2S_LeaveRoom());
                }
            }

            if (state == ClientAppState.OnlineRoom)
            {
                ProcessInput();
            }

            InterpolateMovement();
        }

        private void BindEvents()
        {
            if (_boundRoom == null) return;

            // 核心重构：直接监听协议，并收集注销令牌
            _roomEventTokens.Add(_boundRoom.NetEventSystem.Register<S2C_DemoSnapshot>(HandleSnapshot));
            _roomEventTokens.Add(_boundRoom.NetEventSystem.Register<S2C_DemoPlayerJoined>(HandlePlayerJoined));
            _roomEventTokens.Add(_boundRoom.NetEventSystem.Register<S2C_DemoPlayerLeft>(HandlePlayerLeft));
            _roomEventTokens.Add(_boundRoom.NetEventSystem.Register<S2C_DemoMoveSync>(HandleMoveSync));
            _roomEventTokens.Add(_boundRoom.NetEventSystem.Register<S2C_DemoHpSync>(HandleHpSync));
            _roomEventTokens.Add(_boundRoom.NetEventSystem.Register<S2C_GameEnded>(HandleGameOver));
        }

        private void UnbindEvents()
        {
            // 核心重构：遍历令牌安全注销
            foreach (var token in _roomEventTokens)
            {
                token?.UnRegister();
            }

            _roomEventTokens.Clear();
        }

        private void OnGUI()
        {
            if (!string.IsNullOrEmpty(_winnerSessionId))
            {
                GUI.color = Color.yellow;
                GUI.skin.label.fontSize = 30;
                string tips = _manager.ClientApp.State == ClientAppState.ReplayRoom
                    ? "回放结束"
                    : "即将自动离开房间...";

                GUI.Label(new Rect(Screen.width / 2 - 200, Screen.height / 2 - 50, 400, 100),
                    $"游戏结束!\n胜利者: {_winnerSessionId}\n{tips}");
                GUI.skin.label.fontSize = 0;
                GUI.color = Color.white;
            }
        }

        private void ProcessInput()
        {
            if (!string.IsNullOrEmpty(_winnerSessionId)) return;

            string mySessionId = _manager.ClientApp.Session.SessionId;
            if (!_views.TryGetValue(mySessionId, out var myView) || myView.CurrentHp <= 0) return;

            if (Input.GetMouseButtonDown(1))
            {
                Ray ray = _mainCamera.ScreenPointToRay(Input.mousePosition);
                if (_groundPlane.Raycast(ray, out float distance))
                {
                    Vector3 hitPoint = ray.GetPoint(distance);
                    SendMoveRequest(hitPoint);
                }
            }

            if (Input.GetMouseButtonDown(0))
            {
                Ray ray = _mainCamera.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out RaycastHit hit))
                {
                    string targetSessionId = hit.collider.gameObject.name;
                    if (targetSessionId != mySessionId && _views.ContainsKey(targetSessionId))
                    {
                        SendAttackRequest(targetSessionId);
                    }
                }
            }
        }

        private void SendMoveRequest(Vector3 targetPos)
        {
            var msg = new C2S_DemoMoveReq { TargetX = targetPos.x, TargetY = targetPos.y, TargetZ = targetPos.z };
            _manager.ClientApp.SendMessage(msg);
        }

        private void SendAttackRequest(string targetSessionId)
        {
            var msg = new C2S_DemoAttackReq { TargetSessionId = targetSessionId };
            _manager.ClientApp.SendMessage(msg);
        }

        private void InterpolateMovement()
        {
            float deltaTime = Time.deltaTime;
            foreach (var kvp in _views)
            {
                var view = kvp.Value;
                if (view.RootGo != null && view.CurrentHp > 0)
                {
                    view.RootGo.transform.position = Vector3.Lerp(view.RootGo.transform.position, view.TargetPosition,
                        deltaTime * 10f);
                }
            }
        }

        private void CreateOrUpdateCapsule(DemoPlayerInfo info)
        {
            if (info == null) return;

            if (!_views.TryGetValue(info.SessionId, out var view))
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.name = info.SessionId;

                GameObject textGo = new GameObject("HpText");
                textGo.transform.SetParent(go.transform);
                textGo.transform.localPosition = new Vector3(0, 1.5f, 0);

                var tm = textGo.AddComponent<TextMesh>();
                tm.anchor = TextAnchor.MiddleCenter;
                tm.characterSize = 0.1f;
                tm.fontSize = 60;
                tm.color = Color.red;

                var renderer = go.GetComponent<Renderer>();
                if (_manager.ClientApp.Session.SessionId == info.SessionId)
                {
                    renderer.material.color = Color.green;
                }

                view = new CapsuleViewData
                {
                    RootGo = go,
                    HpText = tm
                };
                _views[info.SessionId] = view;
            }

            view.TargetPosition = new Vector3(info.PosX, info.PosY, info.PosZ);
            view.RootGo.transform.position = view.TargetPosition;
            UpdateHpDisplay(view, info.Hp);
        }

        private void UpdateHpDisplay(CapsuleViewData view, int hp)
        {
            view.CurrentHp = hp;
            if (hp <= 0)
            {
                view.HpText.text = "DEAD";
                view.HpText.color = Color.gray;
                view.RootGo.transform.rotation = Quaternion.Euler(90, 0, 0);
                view.RootGo.transform.position = new Vector3(view.TargetPosition.x, 0.5f, view.TargetPosition.z);
            }
            else
            {
                view.HpText.text = $"HP: {hp}";
            }
        }

        private void DestroyAllCapsules()
        {
            foreach (var kvp in _views)
            {
                if (kvp.Value.RootGo != null)
                {
                    Destroy(kvp.Value.RootGo);
                }
            }

            _views.Clear();
            _winnerSessionId = string.Empty;
        }

        // 核心重构：参数直接改为协议类型 S2C_XXX
        private void HandleSnapshot(S2C_DemoSnapshot evt)
        {
            DestroyAllCapsules();
            if (evt.Players == null) return;
            for (int i = 0; i < evt.Players.Length; i++) CreateOrUpdateCapsule(evt.Players[i]);
        }

        private void HandlePlayerJoined(S2C_DemoPlayerJoined evt) => CreateOrUpdateCapsule(evt.Player);

        private void HandlePlayerLeft(S2C_DemoPlayerLeft evt)
        {
            if (_views.TryGetValue(evt.SessionId, out var view))
            {
                if (view.RootGo != null) Destroy(view.RootGo);
                _views.Remove(evt.SessionId);
            }
        }

        private void HandleMoveSync(S2C_DemoMoveSync evt)
        {
            if (_views.TryGetValue(evt.SessionId, out var view))
                view.TargetPosition = new Vector3(evt.TargetX, evt.TargetY, evt.TargetZ);
        }

        private void HandleHpSync(S2C_DemoHpSync evt)
        {
            if (_views.TryGetValue(evt.SessionId, out var view))
                UpdateHpDisplay(view, evt.Hp);
        }

        private void HandleGameOver(S2C_GameEnded evt)
        {
            _winnerSessionId = evt.WinnerSessionId;
            _autoLeaveTimer = 3f;
        }
    }
}