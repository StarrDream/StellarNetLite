using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;
using UnityEngine;

namespace StellarNet.Lite.Client.Core
{
    public sealed class ClientReplayPlayer
    {
        private readonly ClientApp _app;
        private ReplayFile _currentFile;
        private int _currentTick;
        private int _frameIndex;
        private bool _isPlaying;

        public ClientReplayPlayer(ClientApp app)
        {
            _app = app;
        }

        public void StartReplay(ReplayFile file)
        {
            if (file == null || file.Frames == null)
            {
                Debug.LogError("[ClientReplayPlayer] 启动失败: 回放文件为空");
                return;
            }

            if (_app.State != ClientAppState.Idle)
            {
                Debug.LogError($"[ClientReplayPlayer] 启动阻断: 当前状态为 {_app.State}，必须在 Idle 状态下才能进入回放");
                return;
            }

            _currentFile = file;
            _currentTick = 0;
            _frameIndex = 0;
            _isPlaying = true;

            _app.EnterReplayRoom(file.RoomId);

            bool buildSuccess = ClientRoomFactory.BuildComponents(_app.CurrentRoom, file.ComponentIds);
            if (!buildSuccess)
            {
                // 核心防御：回放文件中的组件在当前客户端版本不存在时，强行播放会导致严重报错，必须阻断
                Debug.LogError($"[ClientReplayPlayer] 回放房间 {file.RoomId} 本地装配失败，存在缺失组件，已强制终止回放");
                StopReplay();
                return;
            }

            Debug.Log($"[ClientReplayPlayer] 回放启动: 房间 {file.RoomId}, 总帧数 {file.Frames.Count}");
        }

        public void StopReplay()
        {
            if (!_isPlaying) return;

            _isPlaying = false;
            _currentFile = null;
            _app.LeaveRoom();
            Debug.Log("[ClientReplayPlayer] 回放结束，已清理沙盒");
        }

        public void Tick()
        {
            if (!_isPlaying || _currentFile == null || _app.CurrentRoom == null) return;

            while (_frameIndex < _currentFile.Frames.Count)
            {
                var frame = _currentFile.Frames[_frameIndex];
                if (frame.Tick > _currentTick)
                {
                    break;
                }

                var packet = new Packet(frame.MsgId, NetScope.Room, frame.RoomId, frame.Payload);
                _app.CurrentRoom.Dispatcher.Dispatch(packet);

                _frameIndex++;
            }

            _currentTick++;

            if (_frameIndex >= _currentFile.Frames.Count)
            {
                Debug.Log("[ClientReplayPlayer] 回放播放完毕");
                StopReplay();
            }
        }
    }
}