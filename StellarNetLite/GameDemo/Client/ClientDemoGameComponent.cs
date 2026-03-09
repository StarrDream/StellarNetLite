using UnityEngine;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.GameDemo.Shared;

namespace StellarNet.Lite.GameDemo.Client
{
    /// <summary>
    /// 客户端胶囊对战业务组件 (Service层)。
    /// 职责：接收服务端的权威状态同步，进行基础的防空校验后，转化为纯值类型事件派发给表现层。
    /// </summary>
    public sealed class ClientDemoGameComponent : ClientRoomComponent
    {
        public override void OnInit()
        {
            Debug.Log("[ClientDemoGame] 客户端业务组件初始化完毕，开始监听服务端同步数据");
        }

        public override void OnDestroy()
        {
            Debug.Log("[ClientDemoGame] 客户端业务组件销毁，清理相关状态");
        }

        [NetHandler]
        public void OnS2C_DemoSnapshot(S2C_DemoSnapshot msg)
        {
            if (msg == null)
            {
                Debug.LogError("[ClientDemoGame] 处理快照失败：接收到的消息体为空");
                return;
            }

            if (msg.Players == null)
            {
                Debug.LogError("[ClientDemoGame] 处理快照失败：玩家列表数据为空");
                return;
            }

            LiteEventBus<DemoSnapshotEvent>.Fire(new DemoSnapshotEvent { Players = msg.Players });
        }

        [NetHandler]
        public void OnS2C_DemoPlayerJoined(S2C_DemoPlayerJoined msg)
        {
            if (msg == null || msg.Player == null)
            {
                Debug.LogError("[ClientDemoGame] 处理玩家加入失败：消息体或玩家数据为空");
                return;
            }

            LiteEventBus<DemoPlayerJoinedEvent>.Fire(new DemoPlayerJoinedEvent { Player = msg.Player });
        }

        [NetHandler]
        public void OnS2C_DemoPlayerLeft(S2C_DemoPlayerLeft msg)
        {
            if (msg == null || string.IsNullOrEmpty(msg.SessionId))
            {
                Debug.LogError("[ClientDemoGame] 处理玩家离开失败：SessionId 为空");
                return;
            }

            LiteEventBus<DemoPlayerLeftEvent>.Fire(new DemoPlayerLeftEvent { SessionId = msg.SessionId });
        }

        [NetHandler]
        public void OnS2C_DemoMoveSync(S2C_DemoMoveSync msg)
        {
            if (msg == null || string.IsNullOrEmpty(msg.SessionId))
            {
                Debug.LogError("[ClientDemoGame] 处理移动同步失败：消息体或 SessionId 为空");
                return;
            }

            LiteEventBus<DemoMoveEvent>.Fire(new DemoMoveEvent
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
                Debug.LogError("[ClientDemoGame] 处理血量同步失败：消息体或 SessionId 为空");
                return;
            }

            LiteEventBus<DemoHpEvent>.Fire(new DemoHpEvent
            {
                SessionId = msg.SessionId,
                Hp = msg.Hp
            });
        }

        // 核心修改：废弃 1008 协议，改为监听框架标准的 503 游戏结束协议
        [NetHandler]
        public void OnS2C_GameEnded(S2C_GameEnded msg)
        {
            if (msg == null)
            {
                Debug.LogError("[ClientDemoGame] 处理游戏结束失败：消息体为空");
                return;
            }

            LiteEventBus<DemoGameOverEvent>.Fire(new DemoGameOverEvent { WinnerSessionId = msg.WinnerSessionId });
        }
    }
}