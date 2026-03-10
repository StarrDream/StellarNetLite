using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
using UnityEngine;

namespace StellarNet.Lite.Client.Core
{
    /// <summary>
    /// 客户端回放播放器 (沙盒时间轴控制器)
    /// 职责：接管回放房间的时间轴，支持自动播放、倍速、暂停以及基于状态重建的任意 Tick 跳转。
    /// </summary>
    public sealed class ClientReplayPlayer
    {
        private readonly ClientApp _app;
        private ReplayFile _currentFile;

        public int CurrentTick { get; private set; }
        public float PlaybackSpeed { get; set; } = 1f;
        public bool IsPaused { get; set; } = false;

        private int _frameIndex;
        private bool _isPlaying;
        private float _tickAccumulator;
        private const float TickInterval = 1f / 60f; // 严格对齐服务端的 60 TickRate

        public ClientReplayPlayer(ClientApp app)
        {
            _app = app;
        }

        public void StartReplay(ReplayFile file)
        {
            if (file == null || file.Frames == null)
            {
                LiteLogger.LogError("[ClientReplayPlayer]",$"  启动失败: 回放文件为空");
                return;
            }

            if (_app.State != ClientAppState.Idle)
            {
                LiteLogger.LogError($"[ClientReplayPlayer] ",$" 启动阻断: 当前状态为 {_app.State}，必须在 Idle 状态下才能进入回放");
                return;
            }

            _currentFile = file;
            _isPlaying = true;
            IsPaused = false;
            PlaybackSpeed = 1f;
            _tickAccumulator = 0f;

            RestartSandbox();
            LiteLogger.LogInfo(
                $"[ClientReplayPlayer] ",$" 回放启动: 房间 {file.RoomId}, 总帧数 {file.Frames.Count}, 总 Tick {GetTotalTicks()}");
        }

        public void StopReplay()
        {
            if (!_isPlaying) return;
            _isPlaying = false;
            _currentFile = null;
            _app.LeaveRoom();
            LiteLogger.LogInfo("[ClientReplayPlayer]",$"  回放结束，已清理沙盒");
        }

        public void Update(float deltaTime)
        {
            if (!_isPlaying || IsPaused || _currentFile == null) return;

            _tickAccumulator += deltaTime * PlaybackSpeed;

            // 追帧逻辑：根据倍速与 deltaTime 消耗累加器
            while (_tickAccumulator >= TickInterval)
            {
                _tickAccumulator -= TickInterval;
                ProcessNextTick();

                if (CurrentTick > GetTotalTicks())
                {
                    IsPaused = true;
                    LiteLogger.LogInfo("[ClientReplayPlayer]",$"  回放播放完毕，已自动暂停");
                    break;
                }
            }
        }

        public void Seek(int targetTick)
        {
            if (!_isPlaying || _currentFile == null) return;

            targetTick = Mathf.Clamp(targetTick, 0, GetTotalTicks());

            // 核心架构：由于是事件同步，时间轴倒退必须通过“销毁重建 + 极速快进”来实现状态的绝对纯净
            if (targetTick < CurrentTick)
            {
                RestartSandbox();
            }

            // 极速快进到目标 Tick (纯逻辑派发，不渲染)
            while (CurrentTick < targetTick)
            {
                ProcessNextTick();
            }
        }

        public int GetTotalTicks()
        {
            if (_currentFile == null || _currentFile.Frames == null || _currentFile.Frames.Count == 0) return 0;
            return _currentFile.Frames[_currentFile.Frames.Count - 1].Tick;
        }

        private void RestartSandbox()
        {
            if (_app.State == ClientAppState.ReplayRoom)
            {
                _app.LeaveRoom();
            }

            _app.EnterReplayRoom(_currentFile.RoomId);
            bool buildSuccess = ClientRoomFactory.BuildComponents(_app.CurrentRoom, _currentFile.ComponentIds);

            if (!buildSuccess)
            {
                LiteLogger.LogError($"[ClientReplayPlayer]",$"  回放房间 {_currentFile.RoomId} 本地装配失败，强制终止回放");
                StopReplay();
                return;
            }

            CurrentTick = 0;
            _frameIndex = 0;
            _tickAccumulator = 0f;
        }

        private void ProcessNextTick()
        {
            if (_currentFile == null || _app.CurrentRoom == null) return;

            while (_frameIndex < _currentFile.Frames.Count)
            {
                var frame = _currentFile.Frames[_frameIndex];
                if (frame.Tick > CurrentTick)
                {
                    break;
                }

                var packet = new Packet(0, frame.MsgId, NetScope.Room, frame.RoomId, frame.Payload);
                _app.CurrentRoom.Dispatcher.Dispatch(packet);
                _frameIndex++;
            }

            CurrentTick++;
        }
    }
}