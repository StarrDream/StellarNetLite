// ========================================================
// 自动生成的模块与组件装配器。
// 请勿手动修改！由 LiteProtocolScanner 自动生成。
// ========================================================
using System;
using System.Collections.Generic;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Shared.Protocol;

namespace StellarNet.Lite.Shared.Binders
{
    public static class AutoRegistry
    {
        public static readonly List<RoomComponentMeta> RoomComponentMetaList = new List<RoomComponentMeta>
        {
            new RoomComponentMeta { Id = 1, Name = "RoomSettings", DisplayName = "基础房间设置" },
            new RoomComponentMeta { Id = 100, Name = "DemoGame", DisplayName = "测试对战玩法" },
            new RoomComponentMeta { Id = 101, Name = "WorkRunComponent", DisplayName = "工人运动组件" },
        };

        public static readonly List<GlobalModuleMeta> GlobalModuleMetaList = new List<GlobalModuleMeta>
        {
            new GlobalModuleMeta { Name = "ClientLobbyModule", DisplayName = "客户端大厅模块" },
            new GlobalModuleMeta { Name = "ClientReplayModule", DisplayName = "客户端回放模块" },
            new GlobalModuleMeta { Name = "ClientRoomModule", DisplayName = "客户端房间生命周期模块" },
            new GlobalModuleMeta { Name = "ClientUserModule", DisplayName = "客户端用户模块" },
            new GlobalModuleMeta { Name = "ServerLobbyModule", DisplayName = "大厅信息模块" },
            new GlobalModuleMeta { Name = "ServerReplayModule", DisplayName = "录像下载与分发模块" },
            new GlobalModuleMeta { Name = "ServerRoomModule", DisplayName = "房间生命周期模块" },
            new GlobalModuleMeta { Name = "ServerUserModule", DisplayName = "用户鉴权与登录模块" },
        };

        public static void RegisterServer(ServerApp serverApp, Func<byte[], Type, object> deserializeFunc)
        {
            AutoBinder.BindServerModule(new StellarNet.Lite.Server.Modules.ServerUserModule(serverApp), serverApp.GlobalDispatcher, deserializeFunc);
            AutoBinder.BindServerModule(new StellarNet.Lite.Server.Modules.ServerRoomModule(serverApp), serverApp.GlobalDispatcher, deserializeFunc);
            AutoBinder.BindServerModule(new StellarNet.Lite.Server.Modules.ServerLobbyModule(serverApp), serverApp.GlobalDispatcher, deserializeFunc);
            AutoBinder.BindServerModule(new StellarNet.Lite.Server.Modules.ServerReplayModule(serverApp), serverApp.GlobalDispatcher, deserializeFunc);
            ServerRoomFactory.Register(1, () => new StellarNet.Lite.Server.Components.ServerRoomSettingsComponent(serverApp));
            ServerRoomFactory.Register(101, () => new Game.Server.Components.ServerWorkRunComponent(serverApp));
            ServerRoomFactory.Register(100, () => new StellarNet.Lite.GameDemo.Server.ServerDemoGameComponent(serverApp));
        }

        public static void RegisterClient(ClientApp clientApp, Func<byte[], Type, object> deserializeFunc)
        {
            AutoBinder.BindClientModule(new StellarNet.Lite.Client.Modules.ClientUserModule(clientApp), clientApp.GlobalDispatcher, deserializeFunc);
            AutoBinder.BindClientModule(new StellarNet.Lite.Client.Modules.ClientLobbyModule(clientApp), clientApp.GlobalDispatcher, deserializeFunc);
            AutoBinder.BindClientModule(new StellarNet.Lite.Client.Modules.ClientRoomModule(clientApp), clientApp.GlobalDispatcher, deserializeFunc);
            AutoBinder.BindClientModule(new StellarNet.Lite.Client.Modules.ClientReplayModule(clientApp), clientApp.GlobalDispatcher, deserializeFunc);
            ClientRoomFactory.Register(101, () => new Game.Client.Components.ClientWorkRunComponent(clientApp));
            ClientRoomFactory.Register(1, () => new StellarNet.Lite.Client.Components.ClientRoomSettingsComponent(clientApp));
            ClientRoomFactory.Register(100, () => new StellarNet.Lite.GameDemo.Client.ClientDemoGameComponent(clientApp));
        }
    }
}
