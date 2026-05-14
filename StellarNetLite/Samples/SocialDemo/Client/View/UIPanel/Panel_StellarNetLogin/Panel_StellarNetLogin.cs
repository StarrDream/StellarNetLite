using StellarNet.UI;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 登录与重连确认面板。
/// </summary>
public class Panel_StellarNetLogin : UIPanelBase
{
    /// <summary>
    /// 账号输入框。
    /// </summary>
    [SerializeField] private TMP_InputField accountIpt;

    /// <summary>
    /// 登录按钮。
    /// </summary>
    [SerializeField] private Button loginBtn;

    /// <summary>
    /// 客户端连接状态文本。
    /// </summary>
    [SerializeField] private TMP_Text loginStatusTxt;

    /// <summary>
    /// 重新启动客户端按钮。
    /// </summary>
    [SerializeField] private Button restartClientBtn;

    /// <summary>
    /// 重连确认按钮组根节点。
    /// </summary>
    [SerializeField] private Transform reconnectGroupTrans;

    /// <summary>
    /// 确认重连按钮。
    /// </summary>
    [SerializeField] private Button reconnectBtn;

    /// <summary>
    /// 放弃重连按钮。
    /// </summary>
    [SerializeField] private Button reconnectCancelBtn;

    /// <summary>
    /// 是否正在等待登录或重连结果。
    /// </summary>
    private bool _isRequesting;
    private bool _isConnectingServer;

    /// <summary>
    /// 最近一次请求开始时间。
    /// </summary>
    private float _requestStartTime;
    private float _connectAttemptStartTime;

    /// <summary>
    /// 登录或重连的超时时间。
    /// </summary>
    private const float RequestTimeoutSeconds = 5f;
    private const float ConnectAttemptTimeoutSeconds = 4f;

    /// <summary>
    /// 初始化登录面板事件和网络状态监听。
    /// </summary>
    public override void OnInit()
    {
        base.OnInit();
        if (NetClient.App == null)
        {
            Debug.LogError($"[Panel_StellarNetLogin] 初始化失败: NetClient.App 为空, Object:{name}");
            return;
        }

        loginBtn.onClick.AddListener(OnLoginBtnClick);
        restartClientBtn.onClick.AddListener(OnRestartClientBtnClick);
        reconnectBtn.onClick.AddListener(OnReconnectBtnClick);
        reconnectCancelBtn.onClick.AddListener(OnReconnectCancelBtnClick);

        StellarNetAppManager.OnClientConnectedEvent += OnClientConnected;
        StellarNetAppManager.OnClientDisconnectedEvent += OnClientDisconnected;
        GlobalTypeNetEvent.Register<S2C_LoginResult>(OnS2C_LoginResult).UnRegisterWhenGameObjectDestroyed(gameObject);
    }

    /// <summary>
    /// 打开面板时刷新当前连接状态和按钮交互。
    /// </summary>
    public override void OnOpen(object uiData = null)
    {
        base.OnOpen(uiData);
        _isRequesting = false;
        _isConnectingServer = false;

        if (reconnectGroupTrans != null) reconnectGroupTrans.gameObject.SetActive(false);
        if (loginBtn != null) loginBtn.interactable = true;
        if (reconnectBtn != null) reconnectBtn.interactable = true;
        if (reconnectCancelBtn != null) reconnectCancelBtn.interactable = true;

        if (GameLauncher.Instance.IsClientConnectedServer)
        {
            restartClientBtn.gameObject.SetActive(false);
            loginStatusTxt.text = "<color=green>服务端已连接</color>";
        }
        else
        {
            restartClientBtn.gameObject.SetActive(true);
            restartClientBtn.interactable = true;
            loginStatusTxt.text = "<color=red>服务端已断开</color>";
        }
    }

    /// <summary>
    /// 销毁面板时解除外部事件和按钮绑定。
    /// </summary>
    private void OnDestroy()
    {
        StellarNetAppManager.OnClientConnectedEvent -= OnClientConnected;
        StellarNetAppManager.OnClientDisconnectedEvent -= OnClientDisconnected;

        loginBtn?.onClick.RemoveAllListeners();
        reconnectBtn?.onClick.RemoveAllListeners();
        reconnectCancelBtn?.onClick.RemoveAllListeners();
        restartClientBtn?.onClick.RemoveAllListeners();
    }

    /// <summary>
    /// 检查登录或重连请求是否超时。
    /// </summary>
    private void Update()
    {
        if (_isConnectingServer && !GameLauncher.Instance.IsClientConnectedServer)
        {
            if (Time.realtimeSinceStartup - _connectAttemptStartTime > ConnectAttemptTimeoutSeconds)
            {
                _isConnectingServer = false;
                if (restartClientBtn != null)
                {
                    restartClientBtn.gameObject.SetActive(true);
                    restartClientBtn.interactable = true;
                }

                if (loginStatusTxt != null)
                {
                    loginStatusTxt.text = "<color=red>Server offline, retry available</color>";
                }

                NetLogger.LogWarning("Panel_StellarNetLogin", "manual server reconnect timed out, UI restored");
            }
        }

        if (_isRequesting)
        {
            if (Time.realtimeSinceStartup - _requestStartTime > RequestTimeoutSeconds)
            {
                _isRequesting = false;
                if (loginBtn != null) loginBtn.interactable = true;
                if (reconnectBtn != null) reconnectBtn.interactable = true;
                if (reconnectCancelBtn != null) reconnectCancelBtn.interactable = true;

                NetLogger.LogWarning("Panel_StellarNetLogin", "登录或重连请求超时，已恢复 UI 交互");
                GlobalTypeNetEvent.Broadcast(new Local_SystemPrompt { Message = "请求超时，请重试" });
            }
        }
    }

    /// <summary>
    /// 客户端连接成功后刷新状态文案。
    /// </summary>
    private void OnClientConnected()
    {
        _isConnectingServer = false;
        restartClientBtn.gameObject.SetActive(false);
        restartClientBtn.interactable = false;
        loginStatusTxt.text = "<color=green>服务端已连接</color>";
    }

    /// <summary>
    /// 客户端断开后恢复手动重连入口。
    /// </summary>
    private void OnClientDisconnected()
    {
        _isConnectingServer = false;
        restartClientBtn.gameObject.SetActive(true);
        restartClientBtn.interactable = true;
        loginStatusTxt.text = "<color=red>服务端已断开</color>";

        _isRequesting = false;
        if (loginBtn != null) loginBtn.interactable = true;
    }

    /// <summary>
    /// 发起登录请求。
    /// </summary>
    private void OnLoginBtnClick()
    {
        if (NetClient.App == null || NetClient.Session == null) return;

        string accountId = accountIpt != null ? accountIpt.text : string.Empty;
        if (string.IsNullOrWhiteSpace(accountId))
        {
            GlobalTypeNetEvent.Broadcast(new Local_SystemPrompt { Message = "请输入账号" });
            return;
        }

        string safeAccountId = accountId.Trim();
        NetClient.Session.SetAccountId(safeAccountId);

        _isRequesting = true;
        _requestStartTime = Time.realtimeSinceStartup;
        if (loginBtn != null) loginBtn.interactable = false;

        var msg = new C2S_Login
        {
            AccountId = safeAccountId,
            ClientVersion = Application.version
        };
        NetClient.Send(msg);
    }

    /// <summary>
    /// 在客户端断线后尝试重启客户端连接。
    /// </summary>
    private void OnRestartClientBtnClick()
    {
        if (GameLauncher.Instance.IsClientConnectedServer) return;
        if (GameLauncher.AppManager == null)
        {
            NetLogger.LogWarning("Panel_StellarNetLogin", "manual server reconnect skipped, AppManager is null");
            return;
        }

        _isConnectingServer = true;
        _connectAttemptStartTime = Time.realtimeSinceStartup;
        restartClientBtn.interactable = false;
        if (loginStatusTxt != null)
        {
            loginStatusTxt.text = "<color=yellow>Connecting server...</color>";
        }

        GameLauncher.AppManager.StopClient();
        GameLauncher.Instance.StartClient();

        NetLogger.LogInfo("Panel_StellarNetLogin", "尝试重新连接服务端... ");
    }

    /// <summary>
    /// 向服务端确认接受重连。
    /// </summary>
    private void OnReconnectBtnClick()
    {
        _isRequesting = true;
        _requestStartTime = Time.realtimeSinceStartup;
        if (reconnectBtn != null) reconnectBtn.interactable = false;
        if (reconnectCancelBtn != null) reconnectCancelBtn.interactable = false;

        NetClient.Send(new C2S_ConfirmReconnect { Accept = true });
    }

    /// <summary>
    /// 向服务端确认放弃重连。
    /// </summary>
    private void OnReconnectCancelBtnClick()
    {
        _isRequesting = true;
        _requestStartTime = Time.realtimeSinceStartup;
        if (reconnectBtn != null) reconnectBtn.interactable = false;
        if (reconnectCancelBtn != null) reconnectCancelBtn.interactable = false;

        NetClient.Send(new C2S_ConfirmReconnect { Accept = false });
    }

    /// <summary>
    /// 处理登录结果，并在需要时展示重连确认区。
    /// </summary>
    private void OnS2C_LoginResult(S2C_LoginResult msg)
    {
        _isRequesting = false;
        if (msg == null)
        {
            if (loginBtn != null) loginBtn.interactable = true;
            return;
        }

        if (!msg.Success)
        {
            if (loginBtn != null) loginBtn.interactable = true;
            GlobalTypeNetEvent.Broadcast(new Local_SystemPrompt { Message = $"登录失败: {msg.Reason}" });
            return;
        }

        if (msg.HasReconnectRoom && reconnectGroupTrans != null)
        {
            reconnectGroupTrans.gameObject.SetActive(true);
        }
    }
}
