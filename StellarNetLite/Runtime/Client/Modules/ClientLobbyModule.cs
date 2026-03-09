using System;
using UnityEngine;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Client.Core;

namespace StellarNet.Lite.Client.Modules
{
    /// <summary>
    /// 客户端大厅模块。
    /// 职责：接收服务端下发的大厅数据（如房间列表），并转化为纯值类型事件驱动 UI 刷新。
    /// </summary>
    public sealed class ClientLobbyModule
    {
        private readonly ClientApp _app;
        private readonly Action<Packet> _networkSender;
        private readonly Func<object, byte[]> _serializeFunc;

        public ClientLobbyModule(ClientApp app, Action<Packet> networkSender, Func<object, byte[]> serializeFunc)
        {
            _app = app;
            _networkSender = networkSender;
            _serializeFunc = serializeFunc;
        }

        [NetHandler]
        public void OnS2C_RoomListResponse(S2C_RoomListResponse msg)
        {
            if (msg == null) return;
            // 将网络协议转化为表现层事件派发
            LiteEventBus<RoomListEvent>.Fire(new RoomListEvent { Rooms = msg.Rooms ?? new RoomBriefInfo[0] });
        }
    }
}