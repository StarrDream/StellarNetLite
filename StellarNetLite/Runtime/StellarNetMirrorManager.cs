using System;
using UnityEngine;
using Mirror;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.Binders;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Server.Modules;
using StellarNet.Lite.Server.Components;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Modules;
using StellarNet.Lite.Client.Components;
using StellarNet.Lite.GameDemo.Server;
using StellarNet.Lite.GameDemo.Client;

namespace StellarNet.Lite.Shared.Infrastructure
{
    public class StellarNetMirrorManager : NetworkManager
    {
        public ServerApp ServerApp { get; private set; }
        public ClientApp ClientApp { get; private set; }

        public Func<object, byte[]> SerializeFunc { get; private set; }
        public Func<byte[], Type, object> DeserializeFunc { get; private set; }

        private NetConfig _netConfig;
        private static bool _factoriesRegistered = false;

        public override void Awake()
        {
            base.Awake();

            var serializer = new JsonNetSerializer();
            SerializeFunc = serializer.Serialize;
            DeserializeFunc = serializer.Deserialize;

            _netConfig = NetConfigLoader.LoadServerConfigSync(ConfigRootPath.PersistentDataPath);
            this.maxConnections = _netConfig.MaxConnections;

            NetMessageMapper.Initialize();

            if (!_factoriesRegistered)
            {
                OnRegisterServerComponents();
                OnRegisterClientComponents();
                _factoriesRegistered = true;
            }
        }

        private void FixedUpdate()
        {
            if (NetworkServer.active && ServerApp != null)
            {
                ServerApp.Tick(_netConfig);
            }
        }

        protected virtual void OnRegisterServerComponents()
        {
            // 核心修复：彻底消灭魔法数字，使用自动生成的强类型常量
            ServerRoomFactory.Register(ComponentIdConst.RoomSettings, () => new ServerRoomSettingsComponent(SerializeFunc));
            ServerRoomFactory.Register(ComponentIdConst.DemoGame, () => new ServerDemoGameComponent(SerializeFunc));

            ServerRoomFactory.ComponentBinder = (comp, dispatcher) =>
                AutoBinder.BindServerComponent(comp, dispatcher, DeserializeFunc);
        }

        protected virtual void OnRegisterClientComponents()
        {
            // 核心修复：彻底消灭魔法数字，使用自动生成的强类型常量
            ClientRoomFactory.Register(ComponentIdConst.RoomSettings, () => new ClientRoomSettingsComponent());
            ClientRoomFactory.Register(ComponentIdConst.DemoGame, () => new ClientDemoGameComponent());

            ClientRoomFactory.ComponentBinder = (comp, dispatcher) =>
                AutoBinder.BindClientComponent(comp, dispatcher, DeserializeFunc);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            NetworkServer.tickRate = _netConfig.TickRate;
            ServerApp = new ServerApp(MirrorServerSend, SerializeFunc);

            var userModule = new ServerUserModule(ServerApp, MirrorServerSend, SerializeFunc, _netConfig);
            var roomModule = new ServerRoomModule(ServerApp, MirrorServerSend, SerializeFunc);
            var lobbyModule = new ServerLobbyModule(ServerApp, MirrorServerSend, SerializeFunc);
            var replayModule = new ServerReplayModule(ServerApp, MirrorServerSend, SerializeFunc);

            AutoBinder.BindServerModule(userModule, ServerApp.GlobalDispatcher, DeserializeFunc);
            AutoBinder.BindServerModule(roomModule, ServerApp.GlobalDispatcher, DeserializeFunc);
            AutoBinder.BindServerModule(lobbyModule, ServerApp.GlobalDispatcher, DeserializeFunc);
            AutoBinder.BindServerModule(replayModule, ServerApp.GlobalDispatcher, DeserializeFunc);

            NetworkServer.RegisterHandler<MirrorPacketMsg>(OnServerReceivePacket, false);

            LiteLogger.LogInfo("StellarNetManager", $"服务端装配完毕，开始监听网络请求。TickRate: {NetworkServer.tickRate}, MaxConn: {this.maxConnections}");
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            if (ServerApp != null)
            {
                var session = ServerApp.TryGetSessionByConnectionId(conn.connectionId);
                if (session != null)
                {
                    LiteLogger.LogInfo("StellarNetManager", $"物理连接断开，触发会话离线", "-", session.SessionId, $"ConnId:{conn.connectionId}");
                    ServerApp.UnbindConnection(session);
                }
            }

            base.OnServerDisconnect(conn);
        }

        private void MirrorServerSend(int connId, Packet packet)
        {
            if (NetworkServer.connections.TryGetValue(connId, out var conn))
            {
                conn.Send(new MirrorPacketMsg(packet));
            }
        }

        private void OnServerReceivePacket(NetworkConnectionToClient conn, MirrorPacketMsg msg)
        {
            ServerApp.OnReceivePacket(conn.connectionId, msg.ToPacket());
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            ClientApp = new ClientApp(MirrorClientSend, SerializeFunc);

            var userModule = new ClientUserModule(ClientApp, MirrorClientSend, SerializeFunc);
            var roomModule = new ClientRoomModule(ClientApp, MirrorClientSend, SerializeFunc);
            var lobbyModule = new ClientLobbyModule(ClientApp, MirrorClientSend, SerializeFunc);
            var replayModule = new ClientReplayModule(ClientApp, MirrorClientSend, SerializeFunc);

            AutoBinder.BindClientModule(userModule, ClientApp.GlobalDispatcher, DeserializeFunc);
            AutoBinder.BindClientModule(roomModule, ClientApp.GlobalDispatcher, DeserializeFunc);
            AutoBinder.BindClientModule(lobbyModule, ClientApp.GlobalDispatcher, DeserializeFunc);
            AutoBinder.BindClientModule(replayModule, ClientApp.GlobalDispatcher, DeserializeFunc);

            NetworkClient.RegisterHandler<MirrorPacketMsg>(OnClientReceivePacket, false);

            LiteLogger.LogInfo("StellarNetManager", "客户端装配完毕，准备就绪。");
        }

        public override void OnClientDisconnect()
        {
            if (ClientApp != null)
            {
                LiteLogger.LogInfo("StellarNetManager", "客户端物理断开，清理本地房间与会话状态");
                ClientApp.LeaveRoom();
                ClientApp.Session.Clear();
            }

            base.OnClientDisconnect();
        }

        private void MirrorClientSend(Packet packet)
        {
            if (NetworkClient.ready)
            {
                NetworkClient.Send(new MirrorPacketMsg(packet));
            }
        }

        private void OnClientReceivePacket(MirrorPacketMsg msg)
        {
            ClientApp.OnReceivePacket(msg.ToPacket());
        }
    }
}