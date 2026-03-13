using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.GameDemo.Shared;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.GameDemo.Server
{
    [RoomComponent(100, "DemoGame", "测试对战玩法")]
    public sealed class ServerDemoGameComponent : RoomComponent
    {
        private readonly ServerApp _app;
        private readonly Dictionary<string, DemoPlayerInfo> _players = new Dictionary<string, DemoPlayerInfo>();
        private readonly List<string> _pendingMembers = new List<string>();
        private bool _isGameOver = false;

        public ServerDemoGameComponent(ServerApp app)
        {
            _app = app;
        }

        public override void OnInit()
        {
            _players.Clear();
            _pendingMembers.Clear();
            _isGameOver = false;
        }

        public override void OnMemberJoined(Session session)
        {
            if (session == null) return;

            if (Room.State == RoomState.Waiting)
            {
                if (!_pendingMembers.Contains(session.SessionId))
                {
                    _pendingMembers.Add(session.SessionId);
                }
            }

            OnSendSnapshot(session);
        }

        public override void OnMemberLeft(Session session)
        {
            if (session == null) return;

            _pendingMembers.Remove(session.SessionId);

            if (_players.Remove(session.SessionId))
            {
                var msg = new S2C_DemoPlayerLeft { SessionId = session.SessionId };
                Room.BroadcastMessage(msg);
                CheckWinCondition();
            }
        }

        public override void OnGameStart()
        {
            _players.Clear();
            _isGameOver = false;

            foreach (string sessionId in _pendingMembers)
            {
                _players[sessionId] = new DemoPlayerInfo
                {
                    SessionId = sessionId,
                    PosX = UnityEngine.Random.Range(-5f, 5f),
                    PosY = 1f,
                    PosZ = UnityEngine.Random.Range(-5f, 5f),
                    Hp = 10
                };
            }

            var snapshot = new List<DemoPlayerInfo>(_players.Values);
            var msg = new S2C_DemoSnapshot { Players = snapshot.ToArray() };
            Room.BroadcastMessage(msg);

            NetLogger.LogInfo("ServerDemoGame", $"游戏正式开始，已为 {_players.Count} 名玩家生成实体", Room.RoomId);
        }

        public override void OnSendSnapshot(Session session)
        {
            if (session == null) return;

            var snapshot = new List<DemoPlayerInfo>(_players.Values);
            var msg = new S2C_DemoSnapshot { Players = snapshot.ToArray() };
            Room.SendMessageTo(session, msg);
        }

        [NetHandler]
        public void OnC2S_DemoMoveReq(Session session, C2S_DemoMoveReq msg)
        {
            if (session == null || msg == null) return;

            if (_isGameOver || Room.State != RoomState.Playing) return;

            if (!_players.TryGetValue(session.SessionId, out var player)) return;
            if (player.Hp <= 0) return;

            player.PosX = msg.TargetX;
            player.PosY = msg.TargetY;
            player.PosZ = msg.TargetZ;

            var syncMsg = new S2C_DemoMoveSync
            {
                SessionId = session.SessionId,
                TargetX = msg.TargetX,
                TargetY = msg.TargetY,
                TargetZ = msg.TargetZ
            };
            Room.BroadcastMessage(syncMsg);
        }

        [NetHandler]
        public void OnC2S_DemoAttackReq(Session session, C2S_DemoAttackReq msg)
        {
            if (session == null || msg == null) return;

            if (_isGameOver || Room.State != RoomState.Playing) return;

            if (string.IsNullOrEmpty(msg.TargetSessionId)) return;

            if (!_players.TryGetValue(session.SessionId, out var attacker)) return;
            if (attacker.Hp <= 0) return;

            if (!_players.TryGetValue(msg.TargetSessionId, out var target)) return;
            if (target.Hp <= 0) return;

            target.Hp -= 1;

            var hpMsg = new S2C_DemoHpSync
            {
                SessionId = target.SessionId,
                Hp = target.Hp
            };
            Room.BroadcastMessage(hpMsg);

            if (target.Hp <= 0)
            {
                CheckWinCondition();
            }
        }

        private void CheckWinCondition()
        {
            if (_isGameOver || Room.State != RoomState.Playing) return;

            int aliveCount = 0;
            string lastAliveSessionId = string.Empty;

            foreach (var kvp in _players)
            {
                if (kvp.Value.Hp > 0)
                {
                    aliveCount++;
                    lastAliveSessionId = kvp.Key;
                }
            }

            if (aliveCount <= 1 && _players.Count > 1)
            {
                _isGameOver = true;
                var overMsg = new S2C_GameEnded { WinnerSessionId = lastAliveSessionId };
                Room.BroadcastMessage(overMsg);
                NetLogger.LogInfo("ServerDemoGame", $"游戏结束，胜利者: {lastAliveSessionId}。触发房间结算。", Room.RoomId);
                Room.EndGame();
            }
        }
    }
}