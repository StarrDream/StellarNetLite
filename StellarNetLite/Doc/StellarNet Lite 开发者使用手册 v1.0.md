# StellarNet Lite
2026年3月9日22:59:30修订
> 面向中小型 Unity 商业项目的轻量级房间式网络框架  
> 核心目标：**服务端绝对权威、协议事件驱动、房间组件化、回放沙盒化、客户端表现解耦**

---

## 目录

- [框架定位](#框架定位)
- [核心特性](#核心特性)
- [适用边界](#适用边界)
- [总体架构](#总体架构)
- [目录结构](#目录结构)
- [快速启动](#快速启动)
- [核心开发流程](#核心开发流程)
- [通信与状态流转](#通信与状态流转)
- [房间与回放机制](#房间与回放机制)
- [当前版本开发建议](#当前版本开发建议)
- [文档入口](#文档入口)

---

## 框架定位

StellarNet Lite 不是一个追求“黑盒自动同步”的网络方案。  
它的设计目标非常明确：

1. **保证服务端权威**
2. **保证协议流转清晰可控**
3. **保证房间作用域隔离**
4. **保证回放、重连、断线恢复这些高风险场景可控**
5. **保证业务功能可以横向扩展，不把代码堆进巨石类**

这套框架的核心哲学只有一句话：

> **客户端只发请求和播放结果，服务端才是真相。**

---

## 核心特性

### 1. 强类型协议发送

业务层不直接手拼 `Packet`，统一通过强类型发送入口完成发包。

- 客户端：`ClientApp.SendMessage<T>()`
- 服务端：`ServerApp.SendMessageToSession<T>()`
- 房间内广播：`Room.BroadcastMessage<T>()`
- 房间内单播：`Room.SendMessageTo<T>()`

这套机制会自动处理：

- `MsgId`
- `Scope`
- `RoomId`
- `Seq`
- 协议方向校验
- 房间上下文校验

---

### 2. NetMessageMapper 元数据驱动

框架启动时扫描所有带 `[NetMsg]` 的协议类型，建立：

- 类型 -> 协议元数据
- 协议 ID
- 作用域
- 方向

这意味着业务层只需要关心“发送什么对象”，不需要关心“这个对象应该用哪个魔数协议号”。

---

### 3. Shared / Server / Client 物理分层

框架严格分为三层：

- **Shared**：共享协议、基础结构、事件、序列化抽象、工具
- **Server**：服务端权威逻辑、房间容器、会话、录制、GC、重连
- **Client**：客户端状态机、协议接入、轻状态缓存、回放控制、表现桥接

---

### 4. Global / Room 作用域分离

框架内部把所有网络消息严格分成两种作用域：

#### Global
不依赖房间上下文的逻辑，例如：

- 登录
- 建房
- 加房
- 离房
- 大厅房间列表
- 录像列表
- 下载录像

#### Room
必须依赖某个房间上下文的逻辑，例如：

- 房间成员快照
- Ready 状态
- 开始游戏
- 局内移动
- 房间聊天
- 表情广播
- 战斗同步

---

### 5. 房间组件化装配

房间不是靠一个巨大的 `RoomLogic.cs` 处理所有事情，而是采用 **Room Component** 横向扩展模式。

例如：

- `1`：房间基础设置组件
- `100`：Demo 战斗组件
- `2`：房间表情组件
- `3`：房间聊天组件

建房时通过 `ComponentIds` 指定房间装配内容。  
这样新增功能时只需要新增组件，不需要改已有巨石类。

---

### 6. 两阶段装配与失败回滚

服务端和客户端房间组件装配都采用统一原则：

1. 先全量校验
2. 再统一挂载与绑定
3. 最后统一初始化
4. 任一阶段失败，整体回滚

这保证不会产生“半残房间实例”。

---

### 7. 房间级事件总线隔离

客户端内部采用两类事件总线：

- `GlobalEventBus<T>`：大厅、登录、录像列表等全局事件
- `RoomEventBus`：房间快照、战斗同步、结算等房间内事件

这样可以彻底避免：

- 回放房间和在线房间串线
- 多房间切换残留监听
- 全局静态事件污染局内状态

---

### 8. Replay 沙盒回放

回放不是在线房间的一个标志位，而是一个**客户端本地沙盒房间**。

回放时：

- 不接收真实在线房间广播
- 不发送真实网络包
- 使用录像文件里的 `ComponentIds` 本地装配房间组件
- 按 Tick 顺序重放历史房间消息
- Seek 倒退时采用“销毁重建 + 极速快进”恢复状态纯净

---

### 9. Seq 防重放机制

客户端每次发包自动递增 `Seq`，服务端按 Session 记录 `LastReceivedSeq`。  
如果收到旧包或重复包，直接拦截，不进入业务层。

这套机制用于防止：

- 重复点击
- 网络重试导致的旧包重入
- 一般性的重复请求污染

---

### 10. 结构化日志

框架提供统一日志入口 `LiteLogger`，统一输出：

- 模块名
- RoomId
- SessionId
- 额外上下文

便于多人联机、回放、断线、GC、装配失败等场景快速排查。

---

## 适用边界

这套框架适合：

- 轻中量级多人合作游戏
- 房间制 PVE
- 多人副本
- 生存 / 建造 / 休闲联机
- 100~200 连接规模的中小型商业项目
- 需要回放、断线重连、房间组件化管理的项目

这套框架不适合：

- 高竞技强对抗 PVP
- 强预测回滚
- 严苛帧级命中公平判定
- 电竞级低容错对抗产品

一句话概括：

> **它适合可维护、可扩展、可快速落地的合作型房间项目，不适合高竞技对抗底座。**

---

## 总体架构

### 服务端主链路

客户端请求 -> `MirrorPacketMsg` -> `ServerApp.OnReceivePacket()` ->  
按 `Scope` 路由到 `GlobalDispatcher` 或 `RoomDispatcher` ->  
对应 `Module / RoomComponent` 处理 ->  
修改权威状态 ->  
服务端发送 `S2C` -> 客户端接收同步

---

### 客户端主链路

收到服务端协议 -> `ClientApp.OnReceivePacket()` ->  
按 `Scope` 路由到 `ClientGlobalDispatcher` 或 `ClientRoomDispatcher` ->  
对应 `ClientModule / ClientRoomComponent` 处理 ->  
转成 `GlobalEventBus<T>` 或 `Room.EventBus` 内部事件 ->  
View 层监听事件并刷新表现

---

### 核心分层心智

#### Server
- 绝对权威
- 校验合法性
- 修改状态
- 广播同步
- 录制回放
- 管理 Session 与 Room 生命周期

#### Client
- 协议接入
- 轻状态缓存
- View 事件桥接
- 本地回放沙盒
- 输入采集与请求发送

#### View
- 监听事件
- 查询轻状态
- 驱动 UI / 特效 / 插值
- 根据状态决定是否允许输入

---

## 目录结构

推荐按下面的目录心智理解项目：

```
Assets/StellarNetLite
├── Runtime
│   ├── Shared
│   │   ├── Core
│   │   ├── Protocol
│   │   ├── Infrastructure
│   │   └── Binders
│   ├── Server
│   │   ├── Core
│   │   ├── Modules
│   │   ├── Components
│   │   └── Infrastructure
│   ├── Client
│   │   ├── Core
│   │   ├── Modules
│   │   └── Components
│   └── StellarNetMirrorManager.cs
├── Editor
│   ├── LiteProtocolScanner.cs
│   ├── NetConfigEditorWindow.cs
│   └── StellarNetScaffoldWindow.cs
└── GameDemo
    ├── Shared
    ├── Server
    ├── Client
    ├── StellarNetDemoUI.cs
    └── ServerAdminPanel.cs
```

---

## 快速启动

### 1. 导入依赖

当前框架依赖：

- Unity
- Mirror
- Newtonsoft.Json

请先确保工程内已经正确导入以上依赖。

---

### 2. 场景中放置 `StellarNetMirrorManager`

在启动场景中挂载：

- `StellarNetMirrorManager`

它负责：

- 初始化序列化器
- 初始化 `NetMessageMapper`
- 注册客户端 / 服务端组件工厂
- 驱动 `ServerApp` / `ClientApp`
- 接入 Mirror 生命周期

---

### 3. 配置网络参数

打开编辑器菜单：

- `StellarNet/Lite 网络配置 (NetConfig)`

配置：

- `Ip`
- `Port`
- `MaxConnections`
- `TickRate`
- `MaxRoomLifetimeHours`
- `MaxReplayFiles`
- `OfflineTimeoutLobbyMinutes`
- `OfflineTimeoutRoomMinutes`
- `EmptyRoomTimeoutMinutes`
- `MinClientVersion`

保存到：

- `StreamingAssets/NetConfig/netconfig.json`
- 或 `PersistentDataPath/NetConfig/netconfig.json`

---

### 4. 运行 Demo

项目内已提供 Demo 控制台与服务端管理面板：

- `StellarNetDemoUI`
- `ServerAdminPanel`

支持快速验证：

- 登录
- 建房
- 加房
- 准备
- 开始游戏
- 战斗同步
- 断线重连
- 录像列表
- 下载并播放回放

---

### 5. 重新生成协议常量表

若新增了 `[NetMsg]` 协议，执行：

- `StellarNet/Lite 强制重新生成协议常量表`

编辑器会自动扫描协议 ID，并更新：

- `MsgIdConst.cs`

注意：当前主发送链路已经是强类型发送器，`MsgIdConst.cs` 主要用于辅助审查和调试，不建议业务层继续依赖手写魔数发包。

---

## 核心开发流程

### 新增一个房间玩法功能

标准步骤如下：

1. 定义 Shared 协议
2. 设计客户端内部房间事件
3. 编写服务端 `RoomComponent`
4. 编写客户端 `ClientRoomComponent`
5. 注册服务端组件工厂
6. 注册客户端组件工厂
7. 在建房时把 `ComponentId` 加入 `ComponentIds`
8. View 监听事件并做表现
9. 验证在线房间
10. 验证断线重连
11. 验证回放
12. 验证非法输入拦截

---

### 新增一个大厅功能

标准步骤如下：

1. 定义 Global 协议
2. 写 `ServerXXXModule`
3. 写 `ClientXXXModule`
4. 在 `StellarNetMirrorManager` 启动时绑定
5. 用 `GlobalEventBus<T>` 或轻状态把数据交给 UI
6. 验证断线、重复点击、异常输入场景

---

### 使用脚手架生成业务模板

打开菜单：

- `StellarNet/Lite 业务脚手架 (Scaffold)`

可一键生成：

- Shared 协议
- Server 组件 / 模块
- Client 组件 / 模块

当前脚手架已统一到新口径：

- 默认使用强类型发送器
- 房间模块默认走 `Room.EventBus`
- 全局模块默认走 `GlobalEventBus<T>`
- 不再鼓励手拼 `Packet`

---

## 通信与状态流转

### 标准请求-同步流程

标准业务链路必须遵守：

1. 客户端发 `C2S`
2. 服务端校验
3. 服务端修改权威状态
4. 服务端发 `S2C`
5. 客户端接收同步
6. 客户端转为内部事件
7. View 刷新表现

---

### 为什么拒绝自动同步

框架明确不依赖：

- SyncVar
- 自动字段镜像
- 黑盒状态复制器

原因不是“不能用”，而是这些方案在多人房间、重连、回放、局部恢复、协议排障场景下很容易失去可追踪性。

框架坚持：

> **所有状态变化必须通过显式协议事件流转。**

---

## 房间与回放机制

### 房间加入为什么分两段握手

建房 / 加房成功后，服务端不会立刻把玩家正式加进房间，而是先：

1. 返回房间信息和 `ComponentIds`
2. 客户端本地装配房间组件
3. 客户端发送 `C2S_RoomSetupReady`
4. 服务端再正式 `room.AddMember(session)`

这样做的目的，是防止：

- 客户端组件还没绑完
- 服务端房间快照已经下发
- 导致进入半初始化状态

---

### 回放为什么必须单独状态机隔离

回放房间和在线房间必须物理隔离，否则会出现：

- 回放时收到真实在线房间包
- 在线包覆盖回放状态
- View 分不清当前数据来源
- 玩家在回放里还能继续发在线请求

所以框架内部把客户端状态明确切成：

- `Idle`
- `OnlineRoom`
- `ReplayRoom`

并在底层阻断：

- 回放中接收真实房间包
- 回放中发送在线房间请求

---

### 回放为什么要“销毁重建 + 快进”

因为当前同步模型是**事件流重演**，不是完整状态帧覆盖。  
Seek 回退时，不能安全逆推当前状态，所以正确做法只能是：

1. 销毁当前回放沙盒
2. 重建本地回放房间
3. 从 Tick 0 重新按历史帧重放
4. 快进到目标 Tick

这套方案虽然更朴素，但可以保证状态绝对纯净。

---

## 当前版本开发建议

### 1. 新业务统一走强类型发送器

不要再手写：

- `new Packet(...)`
- 手动写 `MsgId`
- 手动拼 `RoomId`

统一使用：

- `ClientApp.SendMessage<T>()`
- `ServerApp.SendMessageToSession<T>()`
- `Room.BroadcastMessage<T>()`
- `Room.SendMessageTo<T>()`

---

### 2. 优先横向扩展 RoomComponent

新增房间功能时，优先新增独立组件，例如：

- `ServerEmojiComponent`
- `ClientEmojiComponent`

不要把功能继续堆进：

- `ServerRoomSettingsComponent`
- 某个现有大组件
- 某个测试 UI 脚本

---

### 3. View 不直接理解协议

View 推荐只依赖：

- `GlobalEventBus<T>`
- `RoomEventBus`
- 轻状态缓存

尽量不要让 View 直接处理：

- `S2C_XXX`
- 协议解析
- 权威状态判断

---

### 4. 回放友好设计优先

如果某个玩法表现必须进回放，就必须通过权威协议广播表达。  
纯本地临时表现默认不会出现在录像里。

---

### 5. 所有高风险入口先做前置拦截

例如：

- `msg == null`
- `session == null`
- `RoomId` 不匹配
- `State` 非法
- `ReplayRoom` 状态误发包
- 非房主越权操作

统一原则：

> **先拦截，先报错，先 return，绝不带病继续。**

---

## 文档入口

如果你是第一次接项目，建议阅读顺序如下：

1. `README.md`
2. `Docs/开发者使用手册.md`

如果你要开始接正式业务，请重点阅读：

- 协议设计规范
- Room Component 开发规范
- 回放系统规范
- 重连恢复规范
- 编码规范与排障清单

---

## 一句话总结

StellarNet Lite 的核心价值不在“功能很多”，而在于：

- **数据流转清晰**
- **房间上下文明确**
- **服务端权威边界明确**
- **回放和重连有清晰状态隔离**
- **业务扩展点稳定，不容易写成巨石类**

请始终记住三句话：

1. **服务端才是真相，客户端只播结果。**
2. **新增玩法优先横向扩展组件，不要纵向堆进巨石类。**
3. **回放是本地沙盒，不是在线房间的附属状态。**
