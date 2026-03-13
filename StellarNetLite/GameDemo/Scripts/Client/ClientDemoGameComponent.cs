using UnityEngine;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.GameDemo.Shared;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.GameDemo.Client
{
    [RoomComponent(100, "DemoGame", "测试对战玩法")]
    public sealed class ClientDemoGameComponent : ClientRoomComponent
    {
        private readonly ClientApp _app;

        public ClientDemoGameComponent(ClientApp app)
        {
            _app = app;
        }

        public override void OnInit()
        {
            NetLogger.LogInfo("[ClientDemoGame]", $"客户端业务组件初始化完毕，开始监听服务端同步数据");
        }

        public override void OnDestroy()
        {
            NetLogger.LogInfo("[ClientDemoGame]", $"客户端业务组件销毁，清理相关状态");
        }

        [NetHandler]
        public void OnS2C_DemoSnapshot(S2C_DemoSnapshot msg)
        {
            if (msg == null || msg.Players == null) return;
            Room.NetEventSystem.Broadcast(msg);
        }

        [NetHandler]
        public void OnS2C_DemoPlayerJoined(S2C_DemoPlayerJoined msg)
        {
            if (msg == null || msg.Player == null) return;
            Room.NetEventSystem.Broadcast(msg);
        }

        [NetHandler]
        public void OnS2C_DemoPlayerLeft(S2C_DemoPlayerLeft msg)
        {
            if (msg == null || string.IsNullOrEmpty(msg.SessionId)) return;
            Room.NetEventSystem.Broadcast(msg);
        }

        [NetHandler]
        public void OnS2C_DemoMoveSync(S2C_DemoMoveSync msg)
        {
            if (msg == null || string.IsNullOrEmpty(msg.SessionId)) return;
            Room.NetEventSystem.Broadcast(msg);
        }

        [NetHandler]
        public void OnS2C_DemoHpSync(S2C_DemoHpSync msg)
        {
            if (msg == null || string.IsNullOrEmpty(msg.SessionId)) return;
            Room.NetEventSystem.Broadcast(msg);
        }

        [NetHandler]
        public void OnS2C_GameEnded(S2C_GameEnded msg)
        {
            if (msg == null) return;
            Room.NetEventSystem.Broadcast(msg);
        }
    }
}