using StellarNet.Lite.Game.Client.Infrastructure;
using StellarNet.Lite.Runtime;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.UI;
using StellarNet.View;
using UnityEngine;

/// <summary>
/// 网络模式。
/// </summary>
public enum ENetMode
{
    None,
    Client,
    Server,
    Host
}

/// <summary>
/// Demo 启动入口。
/// </summary>
public class GameLauncher : MonoSingleton<GameLauncher>
{
    [SerializeField]
    private StellarNetAppManager appManager;

    /// <summary>
    /// 当前启动器持有的应用管理器。
    /// </summary>
    public static StellarNetAppManager AppManager => Instance != null ? Instance.appManager : null;

    /// <summary>
    /// 当前启动的网络模式。
    /// </summary>
    public ENetMode netMode = ENetMode.None;

    /// <summary>
    /// 当前客户端是否已连接到服务端。
    /// </summary>
    public bool IsClientConnectedServer { get; private set; }

    /// <summary>
    /// 初始化启动器。
    /// </summary>
    protected override void Awake()
    {
        base.Awake();
        if (netMode != ENetMode.Server)
        {
            UIKit.Instance.Init();
        }

        if (appManager == null)
        {
            NetLogger.LogError("GameLauncher", "初始化失败: appManager 未绑定");
        }
    }

    /// <summary>
    /// 启动网络模式。
    /// </summary>
    private void Start()
    {
        StellarNetAppManager.OnClientStartedEvent += OnClientStarted;
        StellarNetAppManager.OnClientConnectedEvent += OnClientConnected;
        StellarNetAppManager.OnClientDisconnectedEvent += OnClientDisconnected;

        NetLogger.LogInfo("GameLauncher", $"启动流程开始: Mode:{netMode}");
        LauncherNetAsync(netMode);
    }

    /// <summary>
    /// 销毁启动器。
    /// </summary>
    protected override void OnDestroy()
    {
        base.OnDestroy();
        StellarNetAppManager.OnClientStartedEvent -= OnClientStarted;
        StellarNetAppManager.OnClientConnectedEvent -= OnClientConnected;
        StellarNetAppManager.OnClientDisconnectedEvent -= OnClientDisconnected;
    }

    /// <summary>
    /// 处理客户端连接成功事件。
    /// </summary>
    private void OnClientStarted()
    {
        if (netMode == ENetMode.Server) return;
        IsClientConnectedServer = false;
        OpenClientPanels();
        NetLogger.LogInfo("GameLauncher", "client core ready, login UI opened while waiting for server connection");
    }

    private void OnClientConnected()
    {
        // 先更新连接状态，再刷新 UI。
        IsClientConnectedServer = true;

        OpenClientPanels();

        NetLogger.LogInfo("GameLauncher", "连接完成");
    }

    /// <summary>
    /// 处理客户端断开连接事件。
    /// </summary>
    private void OpenClientPanels()
    {
        GlobalUIRouter.Instance.Init();
        RoomViewManager.Instance.Init();
        UIKit.OpenPanel<Panel_GlobalNetMonitor>();
        UIKit.OpenPanel<Panel_StellarNetLogin>();
    }

    private void OnClientDisconnected()
    {
        // 先更新连接状态，再通知断线处理。
        IsClientConnectedServer = false;

        if (GlobalUIRouter.Instance != null)
        {
            GlobalUIRouter.Instance.HandlePhysicalDisconnect();
        }

        NetLogger.LogWarning("GameLauncher", "连接断开");
    }

    /// <summary>
    /// 按指定模式启动网络。
    /// </summary>
    private async void LauncherNetAsync(ENetMode eNetMode)
    {
        if (eNetMode == ENetMode.None)
        {
            NetLogger.LogWarning("GameLauncher", "启动跳过: 未选择网络模式");
            return;
        }

        if (appManager == null)
        {
            NetLogger.LogError("GameLauncher", "启动失败: appManager 为空");
            return;
        }

        ConfigRootPath activeRoot = NetConfigLoader.LoadRuntimeRootSync();
        NetLogger.LogInfo("GameLauncher", $"加载配置: Root:{activeRoot}");
        NetConfig config = await NetConfigLoader.LoadRuntimeConfigAsync();
        if (config == null)
        {
            NetLogger.LogError("GameLauncher", "启动失败: 配置加载为空");
            return;
        }

        appManager.ApplyConfig(config);
        NetLogger.LogInfo("GameLauncher", $"配置应用完成: Ip:{config.Ip}, Port:{config.Port}, TickRate:{config.TickRate}");

        switch (eNetMode)
        {
            case ENetMode.Client:
                StartClient();
                break;
            case ENetMode.Server:
                StartServer();
                break;
            case ENetMode.Host:
                StartHost();
                break;
        }
    }

    /// <summary>
    /// 启动客户端模式。
    /// </summary>
    [ContextMenu("启动客户端")]
    public void StartClient()
    {
        if (appManager == null || appManager.Transport == null)
        {
            NetLogger.LogError("GameLauncher", "启动客户端失败: Transport 未就绪");
            return;
        }

        appManager.StartClient();
        NetLogger.LogInfo("GameLauncher", "启动网络模式: Client");
    }

    /// <summary>
    /// 启动服务端模式。
    /// </summary>
    private void StartServer()
    {
        if (appManager == null || appManager.Transport == null)
        {
            NetLogger.LogError("GameLauncher", "启动服务端失败: Transport 未就绪");
            return;
        }

        appManager.StartServer();
        NetLogger.LogInfo("GameLauncher", "启动网络模式: Server");
    }

    /// <summary>
    /// 启动主机模式。
    /// </summary>
    private void StartHost()
    {
        if (appManager == null || appManager.Transport == null)
        {
            NetLogger.LogError("GameLauncher", "启动主机失败: Transport 未就绪");
            return;
        }

        appManager.StartHost();
        NetLogger.LogInfo("GameLauncher", "启动网络模式: Host");
    }
}
