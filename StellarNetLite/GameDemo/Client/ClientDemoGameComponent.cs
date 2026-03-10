using UnityEngine;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.GameDemo.Shared;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.GameDemo.Client
{
    // 核心新增：添加组件元数据特性，驱动常量表生成
    [RoomComponent(100, "DemoGame")]
    public sealed class ClientDemoGameComponent : ClientRoomComponent
    {
        public override void OnInit()
        {
            LiteLogger.LogInfo("[ClientDemoGame]", $"  客户端业务组件初始化完毕，开始监听服务端同步数据");
        }

        public override void OnDestroy()
        {
            LiteLogger.LogInfo("[ClientDemoGame]", $"  客户端业务组件销毁，清理相关状态");
        }

        [NetHandler]
        public void OnS2C_DemoSnapshot(S2C_DemoSnapshot msg)
        {
            if (msg == null || msg.Players == null)
            {
                LiteLogger.LogError("[ClientDemoGame]", $"  处理快照失败：接收到的消息体或玩家列表数据为空");
                return;
            }

            Room.EventBus.Fire(new DemoSnapshotEvent { Players = msg.Players });
        }

        [NetHandler]
        public void OnS2C_DemoPlayerJoined(S2C_DemoPlayerJoined msg)
        {
            if (msg == null || msg.Player == null)
            {
                LiteLogger.LogError("[ClientDemoGame] ", $" 处理玩家加入失败：消息体或玩家数据为空");
                return;
            }

            Room.EventBus.Fire(new DemoPlayerJoinedEvent { Player = msg.Player });
        }

        [NetHandler]
        public void OnS2C_DemoPlayerLeft(S2C_DemoPlayerLeft msg)
        {
            if (msg == null || string.IsNullOrEmpty(msg.SessionId))
            {
                LiteLogger.LogError("[ClientDemoGame]", $"  处理玩家离开失败：SessionId 为空");
                return;
            }

            Room.EventBus.Fire(new DemoPlayerLeftEvent { SessionId = msg.SessionId });
        }

        [NetHandler]
        public void OnS2C_DemoMoveSync(S2C_DemoMoveSync msg)
        {
            if (msg == null || string.IsNullOrEmpty(msg.SessionId))
            {
                LiteLogger.LogError("[ClientDemoGame]", $"  处理移动同步失败：消息体或 SessionId 为空");
                return;
            }

            Room.EventBus.Fire(new DemoMoveEvent
            {
                SessionId = msg.SessionId,
                TargetX = msg.TargetX,
                TargetY = msg.TargetY,
                TargetZ = msg.TargetZ
            });
        }

        [NetHandler]
        public void OnS2C_DemoHpSync(S2C_DemoHpSync msg)
        {
            if (msg == null || string.IsNullOrEmpty(msg.SessionId))
            {
                LiteLogger.LogError("[ClientDemoGame]", $"  处理血量同步失败：消息体或 SessionId 为空");
                return;
            }

            Room.EventBus.Fire(new DemoHpEvent
            {
                SessionId = msg.SessionId,
                Hp = msg.Hp
            });
        }

        [NetHandler]
        public void OnS2C_GameEnded(S2C_GameEnded msg)
        {
            if (msg == null)
            {
                LiteLogger.LogError("[ClientDemoGame] ", $" 处理游戏结束失败：消息体为空");
                return;
            }

            Room.EventBus.Fire(new DemoGameOverEvent { WinnerSessionId = msg.WinnerSessionId });
        }
    }
}