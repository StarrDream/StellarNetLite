# StellarNet Lite 快速接入指南 (手把手实战版)
> 面向第一次接入 StellarNet Lite 的开发者  
> 目标：**从 0 到 1，手把手带你开发全局模块、房间业务组件，并摆脱 Demo 制作属于你自己的正式游戏启动器。**

---

## 目录
- [1. 核心心智与开发原则 (必读)](#1-核心心智与开发原则-必读)
- [2. 实战一：开发一个全局模块 (大厅广播)](#2-实战一开发一个全局模块-大厅广播)
    - [步骤 1：定义共享协议 (Shared)](#步骤-1定义共享协议-shared)
    - [步骤 2：编写服务端模块 (Server)](#步骤-2编写服务端模块-server)
    - [步骤 3：编写客户端模块 (Client)](#步骤-3编写客户端模块-client)
    - [步骤 4：装配与注册 (Manager)](#步骤-4装配与注册-manager)
    - [步骤 5：表现层接入 (View)](#步骤-5表现层接入-view)
- [3. 实战二：开发一个房间业务组件 (房间表情与统计)](#3-实战二开发一个房间业务组件-房间表情与统计)
    - [步骤 1：定义协议与内部事件 (Shared)](#步骤-1定义协议与内部事件-shared)
    - [步骤 2：生成组件常量表 (Editor)](#步骤-2生成组件常量表-editor)
    - [步骤 3：编写服务端组件 (Server - 含重连原理)](#步骤-3编写服务端组件-server---含重连原理)
    - [步骤 4：编写客户端组件 (Client)](#步骤-4编写客户端组件-client)
    - [步骤 5：装配与建房挂载 (Manager)](#步骤-5装配与建房挂载-manager)
    - [步骤 6：表现层接入 (View)](#步骤-6表现层接入-view)
- [4. 实战三：摆脱 DemoUI，制作你自己的游戏启动器](#4-实战三摆脱-demoui制作你自己的游戏启动器)
    - [步骤 1：理解启动器的核心职责](#步骤-1理解启动器的核心职责)
    - [步骤 2：编写正式的 Launcher 脚本](#步骤-2编写正式的-launcher-脚本)
    - [步骤 3：场景挂载与运行测试](#步骤-3场景挂载与运行测试)
- [5. 回放与重连是怎么生效的？(原理解析)](#5-回放与重连是怎么生效的原理解析)
- [6. 避坑指南与终极排障手册](#6-避坑指南与终极排障手册)

---

## 1. 核心心智与开发原则 (必读)

在写下第一行代码前，请将以下三句话刻在脑子里：
1. **服务端才是真相**：客户端绝不能自己修改核心数据，只能“发请求 -> 等服务端广播 -> 刷新表现”。
2. **拒绝巨石类，拥抱组件化**：新增房间玩法，绝对不要去改 `Room.cs` 或 `ServerRoomSettingsComponent.cs`，而是新建一个独立的 `RoomComponent`。
3. **MSV 架构解耦**：View（MonoBehaviour）只负责播特效和点按钮，绝不能直接解析网络协议。网络包必须由 ClientComponent 接收，并转成纯值类型 Event 丢给 View。

---

## 2. 实战一：开发一个全局模块 (大厅广播)

**需求描述**：玩家在大厅点击按钮，发送一条全服广播，所有在线玩家的控制台都会打印这条消息。
**技术定性**：不依赖具体房间，属于 `Global` 作用域。

### 步骤 1：定义共享协议 (Shared)
在 `Assets/StellarNetLite/Runtime/Shared/Protocol/` 下新建 `GlobalBroadcastProtocols.cs`。

**为什么这么写？**
- `[NetMsg]` 特性让框架自动识别协议，免去手动维护 switch-case。
- `NetScope.Global` 明确这是全局消息，底层路由会自动将其派发给 `GlobalDispatcher`。
- 必须定义一个 `IGlobalEvent` 结构体，用于 Client 层和 View 层解耦。

```csharp
using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Shared.Protocol
{
    // 1. 客户端发给服务端的请求
    [NetMsg(800, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_GlobalBroadcastReq
    {
        public string Content;
    }

    // 2. 服务端广播给所有客户端的同步包
    [NetMsg(801, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_GlobalBroadcastSync
    {
        public string SenderSessionId;
        public string Content;
    }

    // 3. 客户端内部事件（用于解耦 View）
    public struct GlobalBroadcastEvent : IGlobalEvent
    {
        public string SenderSessionId;
        public string Content;
    }
}
```

### 步骤 2：编写服务端模块 (Server)
在 `Assets/StellarNetLite/Runtime/Server/Modules/` 下新建 `ServerBroadcastModule.cs`。

**为什么这么写？**
- `[NetHandler]` 标记处理函数，`AutoBinder` 会在启动时自动将它与协议绑定。
- 必须进行前置拦截（判空），防止脏数据引发服务端异常。
- 遍历 `_app.Sessions` 进行全局发送。

```csharp
using System;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Server.Modules
{
    public sealed class ServerBroadcastModule
    {
        private readonly ServerApp _app;

        public ServerBroadcastModule(ServerApp app)
        {
            _app = app;
        }

        [NetHandler]
        public void OnC2S_GlobalBroadcastReq(Session session, C2S_GlobalBroadcastReq msg)
        {
            // 1. 前置拦截防御
            if (session == null || msg == null || string.IsNullOrEmpty(msg.Content)) return;

            // 2. 构造下发协议
            var syncMsg = new S2C_GlobalBroadcastSync
            {
                SenderSessionId = session.SessionId,
                Content = msg.Content
            };

            // 3. 遍历所有在线会话进行发送
            foreach (var kvp in _app.Sessions)
            {
                var targetSession = kvp.Value;
                if (targetSession.IsOnline)
                {
                    _app.SendMessageToSession(targetSession, syncMsg);
                }
            }

            LiteLogger.LogInfo("ServerBroadcast", $"玩家 {session.SessionId} 触发了全服广播: {msg.Content}");
        }
    }
}
```

### 步骤 3：编写客户端模块 (Client)
在 `Assets/StellarNetLite/Runtime/Client/Modules/` 下新建 `ClientBroadcastModule.cs`。

**为什么这么写？**
- 客户端模块**绝不**直接操作 UI 组件。
- 收到协议后，立刻将其转化为 `GlobalBroadcastEvent`，通过 `GlobalEventBus` 派发出去。

```csharp
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Client.Core;

namespace StellarNet.Lite.Client.Modules
{
    public sealed class ClientBroadcastModule
    {
        private readonly ClientApp _app;

        public ClientBroadcastModule(ClientApp app)
        {
            _app = app;
        }

        [NetHandler]
        public void OnS2C_GlobalBroadcastSync(S2C_GlobalBroadcastSync msg)
        {
            if (msg == null) return;

            // 转化为纯值类型事件，派发给表现层
            GlobalEventBus<GlobalBroadcastEvent>.Fire(new GlobalBroadcastEvent
            {
                SenderSessionId = msg.SenderSessionId,
                Content = msg.Content
            });
        }
    }
}
```

### 步骤 4：装配与注册 (Manager)
打开 `StellarNetMirrorManager.cs`，在 `OnStartServer` 和 `OnStartClient` 中注册新模块。

```csharp
// 在 OnStartServer 方法中添加：
var broadcastModule = new ServerBroadcastModule(ServerApp);
AutoBinder.BindServerModule(broadcastModule, ServerApp.GlobalDispatcher, DeserializeFunc);

// 在 OnStartClient 方法中添加：
var clientBroadcastModule = new ClientBroadcastModule(ClientApp);
AutoBinder.BindClientModule(clientBroadcastModule, ClientApp.GlobalDispatcher, DeserializeFunc);
```

### 步骤 5：表现层接入 (View)
在任意 UI 脚本中监听事件并发送请求。

```csharp
// 1. 订阅与解绑
private void OnEnable()
{
    GlobalEventBus<GlobalBroadcastEvent>.OnEvent += HandleGlobalBroadcast;
}
private void OnDisable()
{
    GlobalEventBus<GlobalBroadcastEvent>.OnEvent -= HandleGlobalBroadcast;
}

// 2. 接收事件刷新表现
private void HandleGlobalBroadcast(GlobalBroadcastEvent evt)
{
    LiteLogger.Log($"<color=cyan>[全服广播] {evt.SenderSessionId}: {evt.Content}</color>");
}

// 3. 按钮点击发送请求
public void SendBroadcast()
{
    _manager.ClientApp.SendMessage(new C2S_GlobalBroadcastReq { Content = "大家好，我是新来的！" });
}
```

---

## 3. 实战二：开发一个房间业务组件 (房间表情与统计)

**需求描述**：玩家在房间内发表情。要求：
1. 只有同房间的人能看到。
2. **回放**中必须能看到发表情的历史。
3. **断线重连**后，玩家需要知道当前房间总共发了多少个表情（状态恢复）。
   **技术定性**：强依赖房间上下文，属于 `Room` 作用域，必须使用 `RoomComponent`。

### 步骤 1：定义协议与内部事件 (Shared)
新建 `Assets/StellarNetLite/Runtime/Shared/Protocol/RoomEmojiProtocols.cs`。

```csharp
using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Shared.Protocol
{
    // 1. 发送表情请求
    [NetMsg(900, NetScope.Room, NetDir.C2S)]
    public sealed class C2S_SendEmojiReq { public int EmojiId; }

    // 2. 广播表情表现 (用于即时表现与回放)
    [NetMsg(901, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_EmojiSync 
    { 
        public string SessionId; 
        public int EmojiId; 
    }

    // 3. 房间表情状态快照 (用于断线重连恢复)
    [NetMsg(902, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_EmojiSnapshot 
    { 
        public int TotalEmojiCount; 
    }

    // 4. 客户端内部事件 (注意是 IRoomEvent)
    public struct RoomEmojiEvent : IRoomEvent
    {
        public string SessionId;
        public int EmojiId;
    }
    public struct RoomEmojiSnapshotEvent : IRoomEvent
    {
        public int TotalEmojiCount;
    }
}
```

### 步骤 2：生成组件常量表 (Editor)
在写双端组件前，先分配一个组件 ID。
点击 Unity 顶部菜单：`StellarNet/Lite 强制重新生成协议与组件常量表`。

### 步骤 3：编写服务端组件 (Server - 含重连原理)
新建 `Assets/StellarNetLite/Runtime/Server/Components/ServerEmojiComponent.cs`。

**核心原理讲解**：
- `[RoomComponent(2, "RoomEmoji")]`：声明组件元数据，框架会自动生成 `ComponentIdConst.RoomEmoji = 2`。
- `Room.BroadcastMessage`：**只要调用这个方法，该消息就会自动被录入 Replay 时间轴！**
- `OnSendSnapshot`：**断线重连的核心！** 当玩家断线重连并装配好房间后，框架会自动回调这个方法，你必须在这里把当前组件的“状态”（如表情总数）定向发送给该玩家（定向发送默认不进录像，防止污染时间轴）。

```csharp
using System;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Server.Core;

namespace StellarNet.Lite.Server.Components
{
    [RoomComponent(2, "RoomEmoji")]
    public sealed class ServerEmojiComponent : RoomComponent
    {
        private int _totalEmojiCount = 0;

        public override void OnInit()
        {
            _totalEmojiCount = 0;
        }

        [NetHandler]
        public void OnC2S_SendEmojiReq(Session session, C2S_SendEmojiReq msg)
        {
            if (session == null || msg == null) return;

            // 1. 修改权威状态
            _totalEmojiCount++;

            // 2. 构造广播包
            var syncMsg = new S2C_EmojiSync
            {
                SessionId = session.SessionId,
                EmojiId = msg.EmojiId
            };

            // 3. 房间内广播 (此时会自动录入 Replay 回放文件)
            Room.BroadcastMessage(syncMsg);
        }

        // 【重连核心】玩家加入或断线重连时，下发当前状态快照
        public override void OnSendSnapshot(Session session)
        {
            if (session == null) return;

            var snapshot = new S2C_EmojiSnapshot
            {
                TotalEmojiCount = _totalEmojiCount
            };

            // 定向发送给该玩家 (不录入回放)
            Room.SendMessageTo(session, snapshot);
        }
    }
}
```

### 步骤 4：编写客户端组件 (Client)
新建 `Assets/StellarNetLite/Runtime/Client/Components/ClientEmojiComponent.cs`。

**为什么这么写？**
- 必须使用 `Room.EventBus` 派发事件，绝不能用 `GlobalEventBus`。这保证了回放房间和在线房间的事件物理隔离。

```csharp
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Client.Core;
using UnityEngine;

namespace StellarNet.Lite.Client.Components
{
    [RoomComponent(2, "RoomEmoji")]
    public sealed class ClientEmojiComponent : ClientRoomComponent
    {
        public int LocalTotalCount { get; private set; }

        public override void OnInit()
        {
            LocalTotalCount = 0;
        }

        [NetHandler]
        public void OnS2C_EmojiSync(S2C_EmojiSync msg)
        {
            if (msg == null) return;
            
            LocalTotalCount++; // 客户端轻状态累加
            Room.EventBus.Fire(new RoomEmojiEvent { SessionId = msg.SessionId, EmojiId = msg.EmojiId });
        }

        [NetHandler]
        public void OnS2C_EmojiSnapshot(S2C_EmojiSnapshot msg)
        {
            if (msg == null) return;

            LocalTotalCount = msg.TotalEmojiCount; // 重连时状态覆盖
            Room.EventBus.Fire(new RoomEmojiSnapshotEvent { TotalEmojiCount = msg.TotalEmojiCount });
            LiteLogger.Log($"[ClientEmoji] 收到重连快照，当前房间已发表情总数: {LocalTotalCount}");
        }
    }
}
```

### 步骤 5：装配与建房挂载 (Manager)
**1. 重新生成常量表**
点击菜单 `StellarNet/Lite 强制重新生成协议与组件常量表`。

**2. 注册工厂 (StellarNetMirrorManager.cs)**
```csharp
protected virtual void OnRegisterServerComponents()
{
    ServerRoomFactory.Register(ComponentIdConst.RoomSettings, () => new ServerRoomSettingsComponent(SerializeFunc));
    ServerRoomFactory.Register(ComponentIdConst.DemoGame, () => new ServerDemoGameComponent(SerializeFunc));
    // 新增注册
    ServerRoomFactory.Register(ComponentIdConst.RoomEmoji, () => new ServerEmojiComponent());
}

protected virtual void OnRegisterClientComponents()
{
    ClientRoomFactory.Register(ComponentIdConst.RoomSettings, () => new ClientRoomSettingsComponent());
    ClientRoomFactory.Register(ComponentIdConst.DemoGame, () => new ClientDemoGameComponent());
    // 新增注册
    ClientRoomFactory.Register(ComponentIdConst.RoomEmoji, () => new ClientEmojiComponent());
}
```

### 步骤 6：表现层接入 (View)
在 `DemoGameView.cs` 中监听房间事件。

**避坑警告**：房间事件必须在绑定了具体房间时才订阅，离开房间必须解绑！框架已在 `DemoGameView.Update` 中提供了动态绑定机制。

```csharp
// 1. 在 BindEvents() 中添加：
_boundRoom.EventBus.Subscribe<RoomEmojiEvent>(HandleEmoji);
_boundRoom.EventBus.Subscribe<RoomEmojiSnapshotEvent>(HandleEmojiSnapshot);

// 2. 在 UnbindEvents() 中添加：
_boundRoom.EventBus.Unsubscribe<RoomEmojiEvent>(HandleEmoji);
_boundRoom.EventBus.Unsubscribe<RoomEmojiSnapshotEvent>(HandleEmojiSnapshot);

// 3. 实现表现逻辑
private void HandleEmoji(RoomEmojiEvent evt)
{
    LiteLogger.Log($"<color=yellow>[房间表现] 玩家 {evt.SessionId} 头顶冒出了表情 {evt.EmojiId}！</color>");
}

private void HandleEmojiSnapshot(RoomEmojiSnapshotEvent evt)
{
    LiteLogger.Log($"<color=green>[房间表现] UI刷新：本局累计表情数 {evt.TotalEmojiCount}</color>");
}

// 4. 输入发送 (在 ProcessInput 中添加)
if (Input.GetKeyDown(KeyCode.E))
{
    _manager.ClientApp.SendMessage(new C2S_SendEmojiReq { EmojiId = 1 });
}
```

---

## 4. 实战三：摆脱 DemoUI，制作你自己的游戏启动器

很多开发者在看完 Demo 后会有疑问：“我正式项目里总不能用 `OnGUI` 写的 `StellarNetDemoUI` 吧？我该怎么自己接管登录和建房流程？”

### 步骤 1：理解启动器的核心职责
`StellarNetDemoUI` 只是一个调试壳子，真正的网络引擎是 `StellarNetMirrorManager`。
一个正式的游戏启动器只需要做三件事：
1. **物理连接建立**：调用 `_manager.StartClient()` 或 `_manager.StartHost()`。
2. **逻辑鉴权建立**：发送 `C2S_Login` 协议。
3. **状态轮询与跳转**：检测 `_manager.ClientApp.Session.IsLoggedIn`，成功后关闭登录 UI，打开大厅 UI。

### 步骤 2：编写正式的 Launcher 脚本
新建一个脚本 `MyGameLauncher.cs`，使用 Unity 原生的 UGUI 按钮来驱动网络。

**为什么这么写？**
- 必须等待 `NetworkClient.active` 为 true 且 `ClientApp` 初始化完毕后，才能发送登录协议。
- 登录协议中必须携带 `Application.version`，因为服务端的 `ServerUserModule` 会校验版本号，低于 `NetConfig` 配置的版本会被直接踢下线。

```csharp
using UnityEngine;
using UnityEngine.UI;
using Mirror;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Shared.Infrastructure;

public class MyGameLauncher : MonoBehaviour
{
    [Header("UI 引用")]
    public GameObject LoginPanel;
    public GameObject LobbyPanel;
    public InputField AccountInput;
    public Button BtnStartHost;
    public Button BtnStartClient;
    public Button BtnLogin;
    public Button BtnCreateRoom;

    private StellarNetMirrorManager _manager;

    private void Start()
    {
        // 1. 获取核心网络管理器
        _manager = NetworkManager.singleton as StellarNetMirrorManager;
        if (_manager == null)
        {
            LiteLogger.LogError("[Launcher] 场景中缺失 StellarNetMirrorManager！");
            return;
        }

        // 2. 绑定物理连接按钮
        BtnStartHost.onClick.AddListener(() => _manager.StartHost());
        BtnStartClient.onClick.AddListener(() => _manager.StartClient());

        // 3. 绑定业务逻辑按钮
        BtnLogin.onClick.AddListener(OnLoginClicked);
        BtnCreateRoom.onClick.AddListener(OnCreateRoomClicked);

        // 初始化 UI 状态
        LoginPanel.SetActive(true);
        LobbyPanel.SetActive(false);
    }

    private void Update()
    {
        // 4. 状态机轮询：一旦登录成功，自动切换到大厅 UI
        if (_manager != null && _manager.ClientApp != null)
        {
            if (_manager.ClientApp.Session.IsLoggedIn && LoginPanel.activeSelf)
            {
                LiteLogger.Log("[Launcher] 登录成功，切换至大厅界面");
                LoginPanel.SetActive(false);
                LobbyPanel.SetActive(true);
            }
        }
    }

    private void OnLoginClicked()
    {
        // 防御性拦截：确保物理连接已建立
        if (!NetworkClient.active || _manager.ClientApp == null)
        {
            LiteLogger.LogWarning("[Launcher] 请先点击 Start Client 或 Start Host 建立物理连接！");
            return;
        }

        string account = AccountInput.text;
        if (string.IsNullOrEmpty(account)) account = "Player_" + Random.Range(1000, 9999);

        // 发送登录请求 (核心：必须附带 ClientVersion 供服务端校验)
        var loginReq = new C2S_Login 
        { 
            AccountId = account, 
            ClientVersion = Application.version 
        };
        _manager.ClientApp.SendMessage(loginReq);
    }

    private void OnCreateRoomClicked()
    {
        if (_manager.ClientApp == null) return;

        // 发送建房请求，使用自动生成的强类型常量装配组件
        var createReq = new C2S_CreateRoom 
        { 
            RoomName = "我的专属房间", 
            ComponentIds = new int[] 
            { 
                ComponentIdConst.RoomSettings, 
                ComponentIdConst.DemoGame,
                ComponentIdConst.RoomEmoji // 挂载我们刚才写的表情组件
            } 
        };
        _manager.ClientApp.SendMessage(createReq);
    }
}
```

### 步骤 3：场景挂载与运行测试
1. 在场景中禁用或删除原有的 `StellarNetDemoUI`。
2. 创建一个 Canvas，拼好对应的 InputField 和 Button，并挂载 `MyGameLauncher`。
3. 运行游戏：
    - 点击 `Start Host`。
    - 点击 `Login`。
    - 观察 UI 是否成功切换到大厅面板。
    - 点击 `Create Room`，观察控制台是否打印建房成功日志。

至此，你已经完全掌握了如何用自己的 UI 架构接管 StellarNet Lite 的底层核心！

---

## 5. 回放与重连是怎么生效的？(原理解析)

通过上面的实战，你其实已经完美接入了回放和重连。原理如下：

### 回放原理
1. 只要你在 Server 端调用了 `Room.BroadcastMessage(syncMsg)`，底层会自动把这个 Packet 塞进 `ReplayFrame` 列表。
2. 游戏结束时，录像文件落地。
3. 客户端下载录像并进入 `ReplayRoom` 状态。
4. `ClientReplayPlayer` 会按 Tick 顺序，把历史包丢给 `ClientEmojiComponent`。
5. 你的 View 层会像在线一样收到 `RoomEmojiEvent`，完美重演！

### 重连原理
1. 玩家断网，Session 保留。
2. 玩家重新登录，服务端识别到旧 Session，下发重连授权。
3. 客户端先在本地 `ClientRoomFactory` 把 `RoomEmoji` 组件装配好。
4. 客户端发 `Ready`，服务端触发所有组件的 `OnSendSnapshot`。
5. `ServerEmojiComponent` 把 `_totalEmojiCount` 定向发给客户端。
6. 客户端收到快照，UI 瞬间恢复到断线前的状态！

---

## 6. 避坑指南与终极排障手册

如果你接完功能发现跑不通，请按以下顺序排查：

### 坑 1：发了请求没反应？
- **检查 1**：协议类上有没有加 `[NetMsg]`？方向是不是 `C2S`？
- **检查 2**：Server 端的 Handler 方法有没有加 `[NetHandler]`？参数是不是 `(Session, Msg)`？
- **检查 3**：看 Unity Console，底层 `LiteLogger` 会明确告诉你是不是被 Seq 防重放拦截了，或者是不是没找到 Handler。

### 坑 2：建房成功了，但进不去房间？
- **原因**：你写了 `RoomEmoji` 组件，但**忘记在 Manager 里注册工厂**了。
- **排查**：看客户端日志，一定会有一句红错：“`[ClientRoomFactory] 装配致命失败: 本地未注册 ComponentId X`”。

### 坑 3：回放里看不到刚才的表现？
- **原因**：你可能在服务端用了 `Room.SendMessageTo` (单播) 而不是 `BroadcastMessage` (广播)。单播默认是不进录像的！
- **修正**：影响全局表现的事件必须走广播。

### 坑 4：切换房间后，UI 疯狂报错或收到旧房间消息？
- **原因**：你的 View 直接监听了全局静态事件，或者在 `OnDisable` 时忘记 `Unsubscribe`。
- **修正**：严格使用 `Room.EventBus`，并在房间销毁或 View 销毁时注销事件。

### 坑 5：手拼 Packet 导致各种诡异 Bug
- **原因**：业务层手写了 `new Packet(...)`，导致 Seq 为 0，或者 RoomId 拼错。
- **修正**：**永远、绝对**使用 `ClientApp.SendMessage<T>()` 和 `Room.BroadcastMessage<T>()`，让框架帮你处理底层封套。