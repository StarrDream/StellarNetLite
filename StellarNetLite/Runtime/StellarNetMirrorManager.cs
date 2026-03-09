using System;
using System.Reflection;
using UnityEngine;
using Mirror;
using StellarNet.Lite.Shared.Core;
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
            _netConfig = new NetConfig();

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
            ServerRoomFactory.Register(1, () => new ServerRoomSettingsComponent(SerializeFunc));
            ServerRoomFactory.Register(100, () => new ServerDemoGameComponent(SerializeFunc));
            ServerRoomFactory.ComponentBinder = (comp, dispatcher) =>
                AutoBinder.BindServerComponent(comp, dispatcher, DeserializeFunc);
        }

        protected virtual void OnRegisterClientComponents()
        {
            ClientRoomFactory.Register(1, () => new ClientRoomSettingsComponent());
            ClientRoomFactory.Register(100, () => new ClientDemoGameComponent());
            ClientRoomFactory.ComponentBinder = (comp, dispatcher) =>
                AutoBinder.BindClientComponent(comp, dispatcher, DeserializeFunc);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            ServerApp = new ServerApp(MirrorServerSend);

            var userModule = new ServerUserModule(ServerApp, MirrorServerSend, SerializeFunc);
            var roomModule = new ServerRoomModule(ServerApp, MirrorServerSend, SerializeFunc);
            var lobbyModule = new ServerLobbyModule(ServerApp, MirrorServerSend, SerializeFunc);
            // 核心新增：注册服务端录像模块
            var replayModule = new ServerReplayModule(ServerApp, MirrorServerSend, SerializeFunc);

            AutoBinder.BindServerModule(userModule, ServerApp.GlobalDispatcher, DeserializeFunc);
            AutoBinder.BindServerModule(roomModule, ServerApp.GlobalDispatcher, DeserializeFunc);
            AutoBinder.BindServerModule(lobbyModule, ServerApp.GlobalDispatcher, DeserializeFunc);
            AutoBinder.BindServerModule(replayModule, ServerApp.GlobalDispatcher, DeserializeFunc);

            NetworkServer.RegisterHandler<MirrorPacketMsg>(OnServerReceivePacket, false);
            Debug.Log("<color=green>[StellarNet Server] 服务端装配完毕，开始监听网络请求。</color>");
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            if (ServerApp != null)
            {
                var method = typeof(ServerApp).GetMethod("GetSessionByConnectionId", BindingFlags.NonPublic | BindingFlags.Instance);
                if (method != null)
                {
                    var session = method.Invoke(ServerApp, new object[] { conn.connectionId }) as Session;
                    if (session != null)
                    {
                        ServerApp.UnbindConnection(session);
                    }
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
            ClientApp = new ClientApp(MirrorClientSend);

            var userModule = new ClientUserModule(ClientApp, MirrorClientSend, SerializeFunc);
            var roomModule = new ClientRoomModule(ClientApp, MirrorClientSend, SerializeFunc);
            var lobbyModule = new ClientLobbyModule(ClientApp, MirrorClientSend, SerializeFunc);
            // 核心新增：注册客户端录像模块
            var replayModule = new ClientReplayModule(ClientApp, MirrorClientSend, SerializeFunc);

            AutoBinder.BindClientModule(userModule, ClientApp.GlobalDispatcher, DeserializeFunc);
            AutoBinder.BindClientModule(roomModule, ClientApp.GlobalDispatcher, DeserializeFunc);
            AutoBinder.BindClientModule(lobbyModule, ClientApp.GlobalDispatcher, DeserializeFunc);
            AutoBinder.BindClientModule(replayModule, ClientApp.GlobalDispatcher, DeserializeFunc);

            NetworkClient.RegisterHandler<MirrorPacketMsg>(OnClientReceivePacket, false);
            Debug.Log("<color=green>[StellarNet Client] 客户端装配完毕，准备就绪。</color>");
        }

        public override void OnClientDisconnect()
        {
            if (ClientApp != null)
            {
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