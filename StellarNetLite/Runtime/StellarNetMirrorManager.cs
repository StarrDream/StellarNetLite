using System;
using UnityEngine;
using Mirror;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.Binders;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Client.Core;

namespace StellarNet.Lite.Shared.Infrastructure
{
    public class StellarNetMirrorManager : NetworkManager
    {
        #region 全局配置与基础设施

        public Func<object, byte[]> SerializeFunc { get; private set; }
        public Func<byte[], Type, object> DeserializeFunc { get; private set; }
        private NetConfig _netConfig;

        public override void Awake()
        {
            base.Awake();

            var serializer = new JsonNetSerializer();
            SerializeFunc = serializer.Serialize;
            DeserializeFunc = serializer.Deserialize;

            _netConfig = NetConfigLoader.LoadServerConfigSync(ConfigRootPath.PersistentDataPath);
            this.maxConnections = _netConfig.MaxConnections;

            NetMessageMapper.Initialize();

            // 核心改造：绑定组件装配器，实际的工厂注册已移交至 AutoRegistry
            ServerRoomFactory.ComponentBinder = (comp, dispatcher) =>
                AutoBinder.BindServerComponent(comp, dispatcher, DeserializeFunc);

            ClientRoomFactory.ComponentBinder = (comp, dispatcher) =>
                AutoBinder.BindClientComponent(comp, dispatcher, DeserializeFunc);
        }

        private void FixedUpdate()
        {
            if (NetworkServer.active && ServerApp != null)
            {
                ServerApp.Tick();
            }
        }

        #endregion

        #region 服务端专属 (状态、事件与逻辑)

        public ServerApp ServerApp { get; private set; }

        public static event Action OnServerStartedEvent;
        public static event Action OnServerStoppedEvent;
        public static event Action<int> OnServerClientConnectedEvent;
        public static event Action<int> OnServerClientDisconnectedEvent;

        public override void OnStartServer()
        {
            base.OnStartServer();
            NetworkServer.tickRate = _netConfig.TickRate;

            // 1. 初始化容器
            ServerApp = new ServerApp(MirrorServerSend, SerializeFunc, _netConfig);

            // 2. 核心改造：一键自动装配所有服务端模块与房间组件
            AutoRegistry.RegisterServer(ServerApp, DeserializeFunc);

            NetworkServer.RegisterHandler<MirrorPacketMsg>(OnServerReceivePacket, false);

            NetLogger.LogInfo("StellarNetManager", $"服务端装配完毕，开始监听网络请求。TickRate: {NetworkServer.tickRate}, MaxConn: {this.maxConnections}");
            OnServerStartedEvent?.Invoke();
        }

        public override void OnStopServer()
        {
            OnServerStoppedEvent?.Invoke();
            NetLogger.LogInfo("StellarNetManager", "服务端物理节点已停止运行");
            base.OnStopServer();
        }

        public override void OnServerConnect(NetworkConnectionToClient conn)
        {
            base.OnServerConnect(conn);
            NetLogger.LogInfo("StellarNetManager", $"物理连接建立", "-", "-", $"ConnId:{conn.connectionId}");
            OnServerClientConnectedEvent?.Invoke(conn.connectionId);
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            if (ServerApp != null)
            {
                var session = ServerApp.TryGetSessionByConnectionId(conn.connectionId);
                if (session != null)
                {
                    NetLogger.LogInfo("StellarNetManager", $"物理连接断开，触发会话离线", "-", session.SessionId, $"ConnId:{conn.connectionId}");
                    ServerApp.UnbindConnection(session);
                }
            }

            OnServerClientDisconnectedEvent?.Invoke(conn.connectionId);
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

        #endregion

        #region 客户端专属 (状态、事件与逻辑)

        public ClientApp ClientApp { get; private set; }

        public static event Action OnClientStartedEvent;
        public static event Action OnClientStoppedEvent;
        public static event Action OnClientConnectedEvent;
        public static event Action OnClientDisconnectedEvent;

        public override void OnStartClient()
        {
            base.OnStartClient();

            // 1. 初始化容器
            ClientApp = new ClientApp(MirrorClientSend, SerializeFunc);

            // 2. 核心改造：一键自动装配所有客户端模块与房间组件
            AutoRegistry.RegisterClient(ClientApp, DeserializeFunc);

            NetworkClient.RegisterHandler<MirrorPacketMsg>(OnClientReceivePacket, false);

            NetLogger.LogInfo("StellarNetManager", "客户端装配完毕，准备就绪。");
            OnClientStartedEvent?.Invoke();
        }

        public override void OnStopClient()
        {
            OnClientStoppedEvent?.Invoke();
            NetLogger.LogInfo("StellarNetManager", "客户端物理节点已停止运行");
            base.OnStopClient();
        }

        public override void OnClientConnect()
        {
            base.OnClientConnect();
            NetLogger.LogInfo("StellarNetManager", "成功连接到服务端");
            OnClientConnectedEvent?.Invoke();
        }

        public override void OnClientDisconnect()
        {
            if (ClientApp != null)
            {
                NetLogger.LogInfo("StellarNetManager", "与服务端的物理连接断开，清理本地房间与会话状态");
                ClientApp.LeaveRoom();
                ClientApp.Session.Clear();
            }

            OnClientDisconnectedEvent?.Invoke();
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

        #endregion
    }
}