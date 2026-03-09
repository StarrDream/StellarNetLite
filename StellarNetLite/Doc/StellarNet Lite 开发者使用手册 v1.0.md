# StellarNet Lite 开发者使用手册 v1.0
> 面向 5 人左右中小型商业项目的轻量级 Unity 房间式网络框架  
> 架构目标：服务端绝对权威、协议事件驱动、房间组件化、回放沙盒化、客户端表现解耦

---

# 目录

- [1. 框架定位与核心原则](#1-框架定位与核心原则)
- [2. 总体架构总览](#2-总体架构总览)
- [3. 目录结构与分层职责](#3-目录结构与分层职责)
- [4. 核心通信模型](#4-核心通信模型)
- [5. 房间生命周期与握手流程](#5-房间生命周期与握手流程)
- [6. Room Component 组件化开发规范](#6-room-component-组件化开发规范)
- [7. Global Module 全局模块开发规范](#7-global-module-全局模块开发规范)
- [8. 客户端分层心智：Service、轻状态、View](#8-客户端分层心智service轻状态view)
- [9. 回放系统心智与限制](#9-回放系统心智与限制)
- [10. 底层 Seq 防重放机制](#10-底层-seq-防重放机制)
- [11. 协议设计规范](#11-协议设计规范)
- [12. 事件总线使用规范](#12-事件总线使用规范)
- [13. 配置系统与编辑器工具](#13-配置系统与编辑器工具)
- [14. 录像系统与文件滚动清理](#14-录像系统与文件滚动清理)
- [15. 保姆级实战：扩展一个房间表情功能](#15-保姆级实战扩展一个房间表情功能)
- [16. 常见问题排查清单](#16-常见问题排查清单)
- [17. 生产级编码规范](#17-生产级编码规范)
- [18. 当前实现中的已知边界与注意事项](#18-当前实现中的已知边界与注意事项)
- [19. 推荐开发工作流](#19-推荐开发工作流)

---

# 1. 框架定位与核心原则

StellarNet Lite 不是一个追求“全自动同步、开箱即飞”的黑盒网络方案。  
它的目标非常明确：

1. **保证服务端权威**
2. **保证多人房间状态流向可追踪**
3. **让团队成员可以横向扩展功能而不互相踩文件**
4. **让回放、重连、断线恢复这些高风险场景保持可控**
5. **在 100~200 CCU 量级项目中，用简单但清晰的协议流转保持系统稳定**

这套框架最核心的设计哲学只有一句话：

> **所有核心业务状态，只能由服务端修改；客户端只负责发请求、接权威同步、做表现。**

---

## 1.1 不信任客户端

客户端永远只能做两类事情：

- 采集输入
- 播放结果

客户端不能权威决定以下内容：

- 血量
- 房间状态
- 是否加入成功
- 是否准备成功
- 是否开始游戏
- 是否攻击命中
- 是否可重连
- 回放数据内容

客户端可以维护的，只能是以下类型的本地状态：

- UI 当前展示状态
- 服务端同步结果的本地镜像
- 插值、动画、过渡表现
- 回放时间轴与快进进度
- 当前客户端自己处于 Idle / OnlineRoom / ReplayRoom 哪种模式

也就是说：

> **客户端可以有状态，但不能有权威。**

---

## 1.2 一切状态变化必须通过显式协议事件驱动

本框架明确拒绝以下黑盒同步方式：

- SyncVar
- 自动字段镜像
- 不可追踪的状态复制器
- 模糊的“谁改了值但不知道从哪来的”同步方式

所有业务变化必须通过明确协议完成：

- 客户端请求：`C2S_xxx`
- 服务端结果：`S2C_xxx`
- 客户端内部解耦事件：`xxxEvent`

标准流转永远是：

1. 客户端发请求
2. 服务端校验
3. 服务端修改权威状态
4. 服务端广播同步
5. 客户端接收同步
6. 客户端转换为内部事件
7. View 层监听事件并刷新表现

---

## 1.3 组件化房间，不做巨石类

房间逻辑复杂后，最大的敌人不是“写不出来”，而是：

- 文件越来越大
- 逻辑越来越耦合
- 合并代码越来越痛苦
- 任何一个需求都要改同一个房间主类
- 最终所有人都在互相冲突

因此本框架采用 **Room Component** 模式：

- 房间本体 `Room` 只负责容器、成员管理、分发器、生命周期
- 具体玩法拆分到多个 `RoomComponent`
- 建房时通过 `ComponentIds` 动态装配

例如：

- `1` = 基础房间设置组件
- `100` = Demo 战斗组件
- `2` = 表情组件
- `3` = 房间聊天组件
- `4` = 投票踢人组件

这样做的核心收益是：

> **新增一个玩法，只需要新增一个 Component，不需要改别人的 Component。**

---

# 2. 总体架构总览

整个框架可以从三个维度理解：

- **Shared / Server / Client 物理分层**
- **Global / Room 作用域分层**
- **协议 / 状态 / 表现 分层**

---

## 2.1 物理分层

### Shared
放双端共享定义：

- 协议类
- 传输结构
- 事件总线
- 配置结构
- 自动绑定器
- 序列化器接口
- 公共 Attribute

### Server
放服务端权威逻辑：

- `ServerApp`
- `Room`
- `Session`
- `RoomComponent`
- `Global Module`
- `RoomDispatcher`
- 录像落盘
- GC 与熔断
- 重连恢复
- 权限与合法性校验

### Client
放客户端逻辑与表现桥接：

- `ClientApp`
- `ClientRoom`
- `ClientRoomComponent`
- `ClientGlobalDispatcher`
- `ClientRoomDispatcher`
- `ClientReplayPlayer`
- 各种 Client Module
- View 监听事件并刷新场景

---

## 2.2 作用域分层

### Global
不属于任何房间上下文的逻辑都走 Global，例如：

- 登录
- 大厅房间列表
- 建房
- 加房
- 离房
- 录像列表
- 下载录像
- 邮件
- 公会
- 世界聊天

### Room
只在某个房间上下文里有效的逻辑都走 Room，例如：

- 房间成员快照
- 准备状态
- 开始游戏
- 表情广播
- 战斗移动
- 攻击
- 房间内聊天
- 局内技能释放

原则是：

> **只要消息必须绑定某个 RoomId 才有意义，就应该设计为 Room Scope。**

---

## 2.3 客户端逻辑分层

客户端不是“纯 View”，但也不是权威逻辑端。  
当前实现更准确的心智是：

- **ClientApp**：客户端状态机与消息路由总入口
- **ClientRoomComponent / ClientModule**：协议接入层 + 轻量状态缓存 + 事件转发
- **LiteEventBus**：模块间解耦
- **View(MonoBehaviour)**：纯表现与输入采集

因此你可以把它理解成：

> **客户端是“网络协议接入层 + 轻状态缓存层 + 表现层”的组合体。**

---

# 3. 目录结构与分层职责

以下是推荐理解方式，不是硬性文件数量限制，但职责边界必须守住。

---

## 3.1 Shared 目录职责

### `Shared/Core`
放最核心、最基础、任何层都可能依赖的定义，例如：

- `NetMsgAttribute`
- `NetHandlerAttribute`
- `Packet`
- `ReplayFrame`
- `ReplayFile`
- `LiteEventBus<T>`

### `Shared/Protocol`
放所有双端协议与共享数据结构，例如：

- 登录协议
- 房间调度协议
- 房间快照协议
- 回放协议
- 游戏流程协议

### `Shared/Infrastructure`
放基础设施，例如：

- `NetConfig`
- `NetConfigLoader`
- `JsonNetSerializer`
- `MirrorPacketMsg`

### `Shared/Binders`
放自动装配与反射绑定逻辑，例如：

- `AutoBinder`

---

## 3.2 Server 目录职责

### `Server/Core`
放服务端核心模型与容器：

- `ServerApp`
- `Room`
- `Session`
- `GlobalDispatcher`
- `RoomDispatcher`
- `ServerRoomFactory`
- `RoomComponent`

### `Server/Modules`
放 Global 作用域的权威业务处理模块：

- `ServerUserModule`
- `ServerRoomModule`
- `ServerLobbyModule`
- `ServerReplayModule`

### `Server/Components`
放 Room 作用域的房间组件：

- `ServerRoomSettingsComponent`
- `ServerEmojiComponent`
- `ServerBattleComponent`

### `Server/Infrastructure`
放服务端基础设施：

- 录像落盘
- 滚动清理
- 文件路径管理
- 低频后台服务

---

## 3.3 Client 目录职责

### `Client/Core`
放客户端状态机和路由：

- `ClientApp`
- `ClientRoom`
- `ClientSession`
- `ClientRoomFactory`
- `ClientReplayPlayer`
- `ClientDispatchers`

### `Client/Modules`
放 Global 作用域协议接入：

- `ClientUserModule`
- `ClientRoomModule`
- `ClientLobbyModule`
- `ClientReplayModule`

### `Client/Components`
放 Room 作用域协议接入：

- `ClientRoomSettingsComponent`
- `ClientEmojiComponent`
- `ClientBattleComponent`

---

## 3.4 GameDemo 目录职责

`GameDemo` 是示例业务，不是框架底层。  
它用于告诉开发者这套系统应该怎么扩。

例如：

- `GameDemo/Shared/GameDemoProtocols.cs`
- `GameDemo/Server/ServerDemoGameComponent.cs`
- `GameDemo/Client/ClientDemoGameComponent.cs`
- `GameDemo/Client/DemoGameView.cs`

这里面的代码代表“推荐接法”，但不代表所有 Demo 写法都能直接当生产模板。  
尤其 `StellarNetDemoUI` 包含了大量调试性反射代码，只适合作为演示台，不应直接复制到正式项目主业务。

---

# 4. 核心通信模型

---

## 4.1 Packet 是底层统一传输封套

所有消息最终都会被包装成 `Packet`：

- `Seq`
- `MsgId`
- `Scope`
- `RoomId`
- `Payload`

其中：

- `Seq`：客户端发包序列号，服务端用来做防重放
- `MsgId`：协议 ID
- `Scope`：Global / Room
- `RoomId`：房间上下文
- `Payload`：序列化后的实际协议体

原则：

> **协议类只关注业务字段；Packet 负责传输上下文。**

---

## 4.2 Mirror 只是承载通道，不是业务同步层

当前接入是基于 Mirror，但 Mirror 在这套架构里只负责：

- 建连
- 底层消息收发
- `NetworkServer` / `NetworkClient` 生命周期

Mirror 不负责：

- 业务状态同步
- 权威状态管理
- 房间逻辑复制
- 自动变量同步

也就是说：

> **Mirror 在这里是运输车，不是裁判。**

---

## 4.3 AutoBinder 是协议到处理函数的自动桥接层

`AutoBinder` 会扫描带 `[NetHandler]` 的方法，并根据参数类型上的 `[NetMsg]` 进行自动注册。

例如：

服务端房间组件必须是：

`public void OnXXX(Session session, TMsg msg)`

客户端房间组件必须是：

`public void OnXXX(TMsg msg)`

如果签名不对，自动绑定会直接跳过并报错。

这样做的好处是：

- 不必手写大量 `switch(msgId)`
- 模块新增协议时更自然
- 协议定义和处理函数能通过类型直接关联

但要注意：

> **AutoBinder 依赖反射，适合装配期，不适合热路径业务反射调用。**

---

# 5. 房间生命周期与握手流程

房间不是客户端收到了“建房成功”就算完成。  
当前实现为了避免客户端未装配完成时就收到房间消息，采用了**二段式握手**。

---

## 5.1 创建房间完整流程

### 第一步：客户端发起建房请求
客户端发：

- `C2S_CreateRoom`

其中包含：

- `RoomName`
- `ComponentIds`

### 第二步：服务端创建房间容器并装配组件
`ServerRoomModule.OnC2S_CreateRoom` 会：

1. 校验玩家当前不在房间中
2. 生成 `RoomId`
3. `CreateRoom`
4. 根据 `ComponentIds` 调用 `ServerRoomFactory.BuildComponents`
5. 将组件列表记录到 `Room.ComponentIds`

### 第三步：服务端只返回“可进入许可”，不立刻加房
服务端发送：

- `S2C_CreateRoomResult`

这里并不会直接把玩家放进房间，而是先给客户端一个“你可以进入这个房间”的授权结果。

服务端同时会：

- `session.AuthorizeRoom(roomId)`

### 第四步：客户端本地先装配房间组件
客户端收到建房成功后：

1. `EnterOnlineRoom(roomId)`
2. `ClientRoomFactory.BuildComponents`

只有本地组件全部装配成功，才会继续。

### 第五步：客户端发送房间装配完成握手
客户端发送：

- `C2S_RoomSetupReady`

### 第六步：服务端确认授权并正式加入房间
服务端收到握手后会验证：

- `msg.RoomId` 不为空
- `session.AuthorizedRoomId == msg.RoomId`
- 当前尚未在其他房间中
- 目标房间存在

通过后才执行：

- `room.AddMember(session)`
- `session.ClearAuthorizedRoom()`

这一步才是真正意义上的“正式进房”。

---

## 5.2 加入房间完整流程

加入房间和建房完全同理：

1. 客户端发 `C2S_JoinRoom`
2. 服务端检查目标房间是否存在
3. 服务端返回 `S2C_JoinRoomResult`
4. 客户端先本地装配房间组件
5. 客户端发送 `C2S_RoomSetupReady`
6. 服务端正式将该 Session 加入房间

这个设计的目的非常明确：

> **先本地装配，再进房收包，避免“组件没装完快照先到了”的时序错乱。**

---

## 5.3 离开房间流程

### 正常离房
客户端发：

- `C2S_LeaveRoom`

服务端：

1. 从房间移除成员
2. 如果房间没人了，可直接自动销毁
3. 返回 `S2C_LeaveRoomResult`

客户端收到后：

- `_app.LeaveRoom()`

### 异常断线
如果是物理断网，可能不会先发 Leave。  
此时服务端通过：

- `OnServerDisconnect`
- `UnbindConnection`
- Session 离线时间记录
- 房间内成员离线通知

来维持状态一致性。

---

# 6. Room Component 组件化开发规范

---

## 6.1 什么时候用 Room Component

只要一个功能满足以下条件，就应该做成 Room Component：

- 逻辑只在某个房间中有效
- 需要监听房间域的协议
- 需要房间成员上下文
- 需要随着房间创建/销毁而创建/销毁
- 需要参与房间快照、开始、结束、重连恢复

典型例子：

- 房间设置
- 战斗
- 表情
- 房间内聊天
- 投票
- Ready 校验
- 玩法状态机

---

## 6.2 服务端 RoomComponent 的职责

服务端组件负责：

- 处理房间域 `C2S`
- 校验合法性
- 修改房间内权威状态
- 广播 `S2C`
- 响应成员加入/离开/离线/上线
- 在重连时给单个玩家补快照
- 在 `OnGameStart/OnGameEnd` 做玩法生命周期控制

它不负责：

- UI
- GameObject
- Transform
- Scene 物体引用
- 客户端特效

---

## 6.3 客户端 ClientRoomComponent 的职责

客户端房间组件负责：

- 接收 `S2C`
- 做基础防御校验
- 将协议转成内部事件
- 在必要时维护轻量本地缓存

它不负责：

- 服务端权威业务判定
- 直接依赖复杂 UI 树
- 直接操控场景中所有对象
- 跨房间全局状态

---

## 6.4 RoomComponent 生命周期

### 服务端
- `OnInit`
- `OnMemberJoined`
- `OnMemberLeft`
- `OnMemberOffline`
- `OnMemberOnline`
- `OnSendSnapshot`
- `OnGameStart`
- `OnGameEnd`
- `OnDestroy`

### 客户端
- `OnInit`
- `OnDestroy`

这里要特别理解：

> 客户端的房间组件不是行为实体，而是“房间消息接入节点”。

因此它生命周期比服务端更轻。

---

# 7. Global Module 全局模块开发规范

如果一个业务不属于任何房间上下文，就不要做 RoomComponent，而要做 **Global Module**。

---

## 7.1 适合做 Global Module 的业务

- 登录
- 重连确认
- 大厅列表
- 邮件
- 世界聊天
- 公会
- 排行榜
- 商城
- 录像大厅
- CDN 资源版本检查

---

## 7.2 Global Module 的特点

- 协议 `Scope` 必须是 `NetScope.Global`
- 不依赖房间成员列表
- 不挂到某个房间容器上
- 在 `ServerApp` / `ClientApp` 初始化时绑定
- 通过 `AutoBinder.BindServerModule / BindClientModule` 自动接入

---

# 8. 客户端分层心智：Service、轻状态、View

这个章节非常重要，因为很多新同学最容易把客户端写烂。

---

## 8.1 当前客户端不是完整 MVC，也不是纯 MVVM

当前实现最准确的理解方式：

### ClientApp
负责：

- 当前客户端状态机
- 全局包 / 房间包路由
- 在线房间与回放房间切换
- 发包入口

### ClientModule / ClientRoomComponent
负责：

- 收协议
- 做基础校验
- 维护轻量缓存
- 转内部事件

### View
负责：

- 监听事件
- 查询必要轻状态
- 驱动 UI
- 驱动动画
- 驱动插值
- 采集输入并发请求

因此：

> **View 不直接理解服务端协议，只理解事件和少量本地状态。**

---

## 8.2 为什么客户端组件允许保留轻量缓存

例如 `ClientRoomSettingsComponent` 里有：

- `Members`
- `IsGameStarted`

这不是违背服务端权威，而是因为客户端 View 需要一个可查询的当前镜像。  
重点不在于“有没有本地缓存”，而在于：

- 这些数据不是客户端自己算出来的
- 它们来自服务端同步
- 它们只用于 UI 展示和输入 gating
- 它们不能反向作为权威依据

---

## 8.3 View 层可以做什么

可以做：

- 监听 `LiteEventBus`
- 调整 UI 面板
- 播放特效
- 做插值移动
- 做文本提示
- 基于当前客户端状态判断是否允许用户点按钮
- 调用 `ClientApp.SendGlobal/SendRoom`

不能做：

- 直接篡改客户端房间核心状态作为真相
- 绕过 Service 自己推演多人结算结果
- 在回放状态下继续发真实网络包
- 未校验状态就发房间包

---

# 9. 回放系统心智与限制

回放是这套框架里最容易被误解的部分。  
必须明确：

> **ReplayRoom 不是在线房间的一个标志位，而是客户端本地沙盒房间。**

---

## 9.1 ReplayRoom 的本质

回放模式下：

- 客户端不依赖真实服务端推送房间消息
- 客户端会创建一个本地 `ClientRoom`
- 使用录像文件中的 `ComponentIds` 装配房间组件
- 按 Tick 顺序把录制下来的 Room 包重新喂给本地 Dispatcher

也就是说：

> 回放是在客户端本地重新“重演一次房间协议历史”。

---

## 9.2 为什么回放必须单独做状态机

如果回放和在线房间共用一个状态，会产生灾难性问题：

- 正在看录像时，真实服务端又推来房间消息
- View 分不清哪些是录像帧，哪些是真实房间包
- 玩家还能继续点击按钮发战斗请求
- 录像 seek 倒退后状态无法正确重建

因此框架现在做了专门阻断：

### `ClientApp.OnReceivePacket`
回放模式下拦截真实网络 Room 包

### `ClientApp.SendRoom`
回放模式下禁止发送房间包

### `ClientRoomModule / ClientUserModule`
回放模式下忽略在线房间相关结果

### `DemoGameView`
只有 `OnlineRoom` 状态才处理输入

---

## 9.3 Seek 为什么要“销毁重建 + 极速快进”

因为你当前房间同步模型是**事件驱动**，不是完整帧状态覆盖。  
事件驱动回放如果要倒退，不能简单把当前状态逆推回去。  
正确做法只能是：

1. 销毁当前回放沙盒
2. 重新装配房间组件
3. 从 Tick 0 开始重新派发历史帧
4. 快进到目标 Tick

这就是 `ClientReplayPlayer.Seek` 当前实现的根本原因。

这个思路虽然更笨，但最大的优点是：

> **状态绝对纯净，不会因为倒退残留旧状态。**

---

# 10. 底层 Seq 防重放机制

这是当前版本比很多“玩具网络层”更工程化的地方。

---

## 10.1 为什么要有 Seq

多人游戏里最常见的问题之一是：

- 玩家狂点按钮，连续发同一个请求
- 网络层重试导致旧包重复到达
- 某些连接切换或乱序导致旧请求再次进入业务层
- 同一个建房 / 加房 / 开始游戏请求被执行多次

如果业务层每个协议都自己做去重，代码会迅速膨胀。  
因此现在引入底层统一方案：

- 客户端对每个发出的包写入递增 `Seq`
- 服务端按 Session 记录 `LastReceivedSeq`
- 收到 `seq <= LastReceivedSeq` 的包，直接丢弃

---

## 10.2 当前实现细节

### 客户端
`ClientApp.SendGlobal/SendRoom` 会自动：

- `_sendSeq++`
- `packet.Seq = _sendSeq`

### 服务端
`ServerApp.OnReceivePacket` 会先做：

- `session.TryConsumeSeq(packet.Seq)`

如果消费失败，说明是重复包或旧包，直接拒绝进入业务层。

---

## 10.3 Seq 能解决什么，不能解决什么

### 能解决
- 同一 Session 的重复点击
- 重放包
- 明显旧包
- 一般性的协议重入污染

### 不能解决
- 复杂业务幂等
- 跨账号幂等
- 支付回调幂等
- 奖励补发幂等
- 分布式唯一事务

所以正确理解是：

> **Seq 是会话级网络防重放机制，不是全业务万能幂等方案。**

---

# 11. 协议设计规范

---

## 11.1 命名规范

### C2S
客户端发给服务端的请求：

- `C2S_Login`
- `C2S_CreateRoom`
- `C2S_SetReady`
- `C2S_DemoMoveReq`

### S2C
服务端发给客户端的结果或广播：

- `S2C_LoginResult`
- `S2C_RoomSnapshot`
- `S2C_GameStarted`
- `S2C_DemoMoveSync`

### Event
客户端内部事件：

- `RoomListEvent`
- `ReplayDownloadedEvent`
- `DemoHpEvent`

---

## 11.2 协议 ID 规划

建议按业务域分段：

- `100~199` 用户与登录
- `200~299` 房间调度
- `300~399` 房间设置
- `400~499` 大厅聊天
- `500~599` 游戏流程
- `600~699` 回放系统
- `1000+` 具体玩法扩展

不要把一个业务域打散到多个零碎区间，否则排查非常痛苦。

---

## 11.3 Scope 与 Dir 必须精确

### Global + C2S
客户端发大厅请求、登录请求

### Global + S2C
服务端返回大厅数据、登录结果

### Room + C2S
客户端房间内行为请求

### Room + S2C
服务端房间广播、快照、局内同步

Scope 或方向写错时，`AutoBinder` 会直接跳过绑定。  
这也是为什么“客户端发了消息但服务端没反应”时，第一件事就要查协议 Attribute。

---

## 11.4 协议类只放数据，不放逻辑

协议类只应该包含字段，不应该放：

- 校验逻辑
- 行为函数
- Unity API
- 引用场景对象

原因很简单：

> 协议是运输数据的载体，不是业务对象。

---

# 12. 事件总线使用规范

---

## 12.1 LiteEventBus 的定位

`LiteEventBus<T>` 是客户端内部解耦机制。  
它不是网络层，不是存档层，不是权威状态层。

它只负责：

- 让 `ClientRoomComponent` 把收到的同步转给 View
- 让模块之间不直接互相持有引用

---

## 12.2 为什么事件要求实现 `IRoomEvent`

这是一个语义约束，告诉团队：

- 这个事件是给房间/客户端逻辑用的
- 这个事件应该尽量轻量
- 推荐使用 `struct`

---

## 12.3 “零 GC”如何正确理解

`LiteEventBus<T>` 的泛型静态通道可以避免：

- 字典查找
- 装箱拆箱
- 事件名字符串路由

这在架构上是轻量的。  
但如果事件字段里有：

- 数组
- 字符串
- 引用对象

那么整体链路依然可能产生托管分配。

因此准确说法应该是：

> `LiteEventBus<T>` 是一种低 GC 的泛型静态事件总线，而不是保证所有业务事件绝对零 GC。

---

## 12.4 使用规范

### 订阅
应在 `OnEnable` 中订阅

### 取消订阅
必须在 `OnDisable` 中取消

### 不允许
- 常驻对象忘记取消订阅
- 场景切换后重复订阅
- 一个事件同时承担多个不相关业务域

---

# 13. 配置系统与编辑器工具

---

## 13.1 NetConfig 作用

`NetConfig` 用来承载全局网络与服务端治理参数，例如：

- IP
- Port
- MaxConnections
- TickRate
- MaxRoomLifetimeHours
- MaxReplayFiles
- OfflineTimeoutLobbyMinutes
- OfflineTimeoutRoomMinutes
- EmptyRoomTimeoutMinutes

这些参数不应散落硬编码在几十个脚本里，必须统一配置化。

---

## 13.2 配置读取位置

支持两类根目录：

- `StreamingAssets`
- `PersistentDataPath`

编辑器窗口可切换目标根目录进行保存。

---

## 13.3 为什么要有 EmptyRoomTimeoutMinutes

空房间如果不清理，会产生：

- 房间字典膨胀
- 无主残留房间
- 回收不及时
- 长期堆积导致管理成本上升

所以这个配置属于服务端熔断与 GC 防线的一部分。

---

## 13.4 LiteProtocolScanner 的作用

编辑器编译后自动扫描所有带 `[NetMsg]` 的协议类型，检查 ID 是否重复。

这个工具非常关键，因为多人协作时最常见的问题之一就是：

- 两个人分别新加了协议
- 都用了相同 ID
- 代码能编译，但运行路由错乱

扫描器能在编译期尽早把问题炸出来。

---

# 14. 录像系统与文件滚动清理

---

## 14.1 录像记录的是什么

当前房间录像记录的不是“Transform 轨迹”，而是：

> **房间广播出去的协议帧。**

当房间处于录制状态时，`Room.Broadcast` 会把：

- `CurrentTick`
- `MsgId`
- `Payload`
- `RoomId`

记录为 `ReplayFrame`

最终组成：

- `ReplayFile`

这意味着回放本质上是**协议重放**。

---

## 14.2 录像什么时候开始和结束

### 开始
`Room.StartGame()` 时：

- `State = Playing`
- `StartRecord()`

### 结束
`Room.EndGame()` 时：

- `State = Finished`
- `StopRecordAndSave()`
- `ServerReplayStorage.SaveReplay(...)`

---

## 14.3 为什么要滚动清理

录像文件是持续增长的。  
如果服务端长期运行不做上限，会直接把磁盘打满。

因此 `ServerReplayStorage` 在每次保存后都会执行：

- 读取目录下所有录像
- 按创建时间排序
- 保留最新 `MaxReplayFiles`
- 删除更旧文件

这属于典型的生产环境防暴毙措施。

---

# 15. 保姆级实战：扩展一个房间表情功能

下面用“房间内发表情”这个需求，从协议到 UI 全链路演示一次。

---

## 15.1 需求描述

策划需求：

- 玩家在房间内点击表情按钮
- 服务端校验表情 ID 合法
- 服务端向房间全员广播
- 所有客户端在对应玩家头顶显示表情气泡
- 回放中应能看到该表情广播
- 回放中禁止发送表情请求

---

## 15.2 第一步：设计协议

位置建议：

`Assets/StellarNetLite/Runtime/Shared/Protocol/RoomEmojiProtocols.cs`

设计三类结构：

### 客户端请求
- `C2S_SendEmojiReq`

### 服务端广播
- `S2C_EmojiBroadcast`

### 客户端内部事件
- `RoomEmojiEvent`

原则：

- 请求里只放必要字段，例如 `EmojiId`
- 广播里带上 `SenderSessionId`
- 事件用 `struct`

---

## 15.3 第二步：写服务端 RoomComponent

位置建议：

`Assets/StellarNetLite/Runtime/Server/Components/ServerEmojiComponent.cs`

职责：

1. 接收 `C2S_SendEmojiReq`
2. 校验 `session != null && msg != null`
3. 校验 `EmojiId` 合法范围
4. 构造 `S2C_EmojiBroadcast`
5. `Room.Broadcast(...)`

这里要注意几个原则：

### 原则 A：客户端永远不能直接指定 SenderSessionId
发送者身份必须来自服务端 `session.SessionId`

### 原则 B：非法表情必须直接拦截
例如只允许 `1~10`

### 原则 C：房间广播必须使用 `Room.RoomId`
不能允许客户端伪造广播目标房间

---

## 15.4 第三步：写客户端 RoomComponent

位置建议：

`Assets/StellarNetLite/Runtime/Client/Components/ClientEmojiComponent.cs`

职责：

1. 接收 `S2C_EmojiBroadcast`
2. 判空
3. 转成 `RoomEmojiEvent`
4. 通过 `LiteEventBus<RoomEmojiEvent>.Fire(...)` 派发给 View

这一层不要直接去找 UI，不要保存场景节点，不要实例化预制体。  
它只是 Service 层，不是表现层。

---

## 15.5 第四步：注册组件工厂

在 `StellarNetMirrorManager` 中：

### 服务端注册
在 `OnRegisterServerComponents()` 注册新的 `ComponentId`

### 客户端注册
在 `OnRegisterClientComponents()` 注册相同 `ComponentId`

如果忘记注册，会出现：

- 服务端建房装配失败
- 或客户端进入房间装配失败

---

## 15.6 第五步：建房时带上 ComponentId

如果表情组件 ID 设为 `2`，那么建房时：

- 基础房间：`[1]`
- 对战房间：`[1, 100]`
- 带表情的对战房间：`[1, 2, 100]`

原则：

> **房间拥有什么功能，不靠写死逻辑，而靠 ComponentIds 声明式装配。**

---

## 15.7 第六步：View 层监听事件并表现

View 层脚本职责：

- 订阅 `RoomEmojiEvent`
- 根据 `SenderSessionId` 找到对应玩家表现实体
- 播放表情气泡 UI
- 提供按钮点击入口

### 关键防御点
发送请求前必须检查：

- `_manager != null`
- `_manager.ClientApp != null`
- 当前 `State == OnlineRoom`

如果是 `ReplayRoom`，必须直接拦截。

---

## 15.8 第七步：验证全链路

至少验证以下场景：

1. 单人房内发自己表情
2. 双人房互相可见
3. 非法表情 ID 被服务端拦截
4. 回放时能看到旧表情广播
5. 回放时点击表情按钮不会发真实网络包
6. 未注册组件时客户端建房失败并阻断进入
7. 断线重连后仍能继续接收后续表情广播

---

# 16. 常见问题排查清单

---

## 16.1 客户端发了消息，服务端没进 Handler

按顺序查：

1. 协议类是否有 `[NetMsg]`
2. `Scope` 是否正确
3. `Dir` 是否正确
4. `MsgId` 是否冲突
5. 方法是否标记 `[NetHandler]`
6. 方法签名是否正确
7. 对应模块/组件是否已绑定
8. 房间组件是否已注册到 Factory
9. 建房时是否传入对应 `ComponentId`
10. 客户端是否真的处于 `OnlineRoom`

---

## 16.2 客户端收到了协议，但 View 没反应

查：

1. ClientComponent 是否成功收到并转发事件
2. View 是否在 `OnEnable` 订阅
3. View 是否在别的时机被禁用
4. 是否在 `OnDisable` 后未重新启用
5. 事件是否被错误写成类对象或空数据
6. 表现层是否按 `SenderSessionId` 找到了目标实体

---

## 16.3 录像播放时报空指针或 UI 乱套

查：

1. 所有输入逻辑是否都拦截了 `ReplayRoom`
2. View 是否把 `ReplayRoom` 当作允许纯表现更新的白名单状态
3. 是否有真实网络包混入回放沙盒
4. 录像的 `ComponentIds` 是否和本地注册一致
5. seek 时是否正确走了沙盒重建

---

## 16.4 建房成功但立刻又退回大厅

这是典型的本地装配失败后被强制回退。查：

1. 客户端是否注册了所有 `ComponentId`
2. `ClientRoomFactory.BuildComponents` 是否返回 false
3. `AutoBinder` 是否因为协议 Scope / Dir 错误跳过绑定
4. 组件构造器是否返回了 null
5. 某个组件 `OnInit` 是否触发了非法状态

---

## 16.5 重连时拿不到快照

查完整链路：

1. 登录结果是否带 `HasReconnectRoom`
2. 玩家是否点击了“接受重连”
3. 服务端是否返回 `S2C_ReconnectResult`
4. 客户端是否先本地装配房间组件
5. 客户端是否发送 `C2S_ReconnectReady`
6. 服务端是否调用 `room.TriggerReconnectSnapshot(session)`

---

## 16.6 房间列表里出现已结束僵尸房

当前服务端大厅列表已经过滤 `RoomState.Finished`。  
如果仍出现，排查：

1. 是否使用了旧协议或旧服务端版本
2. 列表接口是否走了正确模块
3. 房间状态是否真的变为 `Finished`
4. 是否有自定义大厅逻辑没加过滤

---

## 16.7 高频场景 GC 飙升

重点查：

- `Tick`
- `[NetHandler]`
- 大量广播逻辑
- View 插值与对象创建

尤其禁止：

- 高频 LINQ
- 每帧 new List
- 每次同步都重复 `FindObjectOfType`
- 临时字符串拼接过多
- 大量无意义日志

---

# 17. 生产级编码规范

---

## 17.1 Early Return 前置拦截

所有网络入口函数第一件事是判空、判非法状态。

错误示范：

深层嵌套、先做逻辑后判空。

正确思路：

- 先拦截
- 先 return
- 再进入主流程

---

## 17.2 常规业务逻辑禁止 try-catch

### 禁止使用场景
- 房间业务逻辑
- Handler 正常分支
- 状态机切换
- UI 点击业务

### 允许使用场景
- 文件读写
- JSON 反序列化
- 第三方 SDK
- 平台 API
- 不可控底层 I/O

原则不是“永远不许 catch”，而是：

> **不要用 try-catch 掩盖本该暴露出来的业务错误。**

---

## 17.3 精准错误日志

日志至少要带：

- 类名
- 触发对象
- 关键变量状态
- 当前上下文

例如：

- 当前 `RoomId`
- 当前 `SessionId`
- 当前 `State`
- 当前 `MsgId`
- 当前 `ComponentId`

不要只打一个模糊的 `"Error"`。

---

## 17.4 注释写“为什么”，不是写“做了什么”

差注释：

- 获取组件
- 发送消息
- 播放动画

好注释：

- 先在本地装配完成后再发握手，目的是防止服务端快照早于组件注册到达，导致房间进入半初始化状态

---

## 17.5 高频路径不要使用 LINQ

### 严禁区域
- Tick
- 高并发 Handler
- 房间内频繁广播处理
- 高频匹配逻辑

### 可酌情使用区域
- 编辑器工具
- 低频管理逻辑
- 文件滚动清理
- 后台查询
- 一次性初始化脚本

---

# 18. 当前实现中的已知边界与注意事项

这一章不是架构理想，而是当前代码真实现状，必须让接入同学知道。

---

## 18.1 当前发包入口存在两套路径

推荐统一走：

- `ClientApp.SendGlobal`
- `ClientApp.SendRoom`

因为它们会自动维护：

- `Seq`
- `Scope`
- `RoomId`

但当前部分模块内部仍直接调用 `_networkSender(packet)`，例如握手流程里。  
这意味着这些内部包不会自动附带统一的发包治理逻辑。

建议开发者遵守原则：

> 新业务发包优先走 `ClientApp` 统一入口，不要自行绕过。

---

## 18.2 AutoBinder 大量依赖反射，属于装配期成本

这是当前框架刻意接受的设计。  
原因是装配期牺牲一点反射成本，换取业务模块接入的统一性。

但你要清楚：

- 反射适合初始化与工具层
- 不适合每帧做复杂反射查找
- `DemoUI` 的反射读取私有字段只适合作为调试台写法

生产业务如果要展示服务端统计数据，建议补正式只读接口，而不是继续扩散反射读私有字段。

---

## 18.3 Factory 装配失败后必须立刻销毁实例

当前 Factory 是边创建边注册。  
如果中途某个 `ComponentId` 缺失，可能已经装好了前面的若干组件。

因此只要 `BuildComponents` 返回 false，外层必须立即：

- `LeaveRoom()` 或
- `DestroyRoom()`

绝不能继续运行。

---

## 18.4 LiteEventBus 的静态事件在 Domain 生命周期下要注意清理

虽然大多数 View 都会在 `OnDisable` 取消订阅，但如果你写了：

- 常驻单例
- 热重载工具
- Editor Runtime 桥接对象

就要关注静态事件残留问题。  
必要时在全局重置点调用 `LiteEventBus<T>.Clear()`。

---

## 18.5 Replay 是基于协议重放，不是全状态快照回放

因此：

- seek 倒退成本较高
- 某些纯本地表现不会天然被回放
- 如果某个效果没有走协议，而只是客户端本地临时播放，录像里不会有

所以你新增玩法时要想清楚：

> **哪些表现必须可回放，就必须有对应的权威协议事件。**

---

# 19. 推荐开发工作流

最后给一份新人最该照着做的工作流。

---

## 19.1 新增房间玩法的标准步骤

1. 明确这个需求是 Global 还是 Room
2. 定义 Shared 协议
3. 定义客户端内部事件
4. 写服务端 RoomComponent
5. 写客户端 ClientRoomComponent
6. 注册服务端工厂
7. 注册客户端工厂
8. 在建房配置中加入 `ComponentId`
9. 写 View 订阅事件
10. 做在线房间测试
11. 做回放测试
12. 做断线重连测试
13. 做非法输入测试
14. 做多人并发测试
15. 最后补文档

---

## 19.2 新增大厅功能的标准步骤

1. 定义 Global 协议
2. 写 `ServerXXXModule`
3. 写 `ClientXXXModule`
4. 在 `StellarNetMirrorManager` 启动时完成绑定
5. View 通过事件总线或轻状态获取数据
6. 验证在线、断线、重复点击、异常输入场景

---

## 19.3 提交代码前自检清单

### 协议层
- [ ] MsgId 是否唯一
- [ ] Scope 是否正确
- [ ] Dir 是否正确

### 服务端
- [ ] 第一行是否做前置拦截
- [ ] 是否存在越权漏洞
- [ ] 是否错误信任客户端字段
- [ ] 是否有非法状态漏拦截
- [ ] 高频路径是否误用了 LINQ

### 客户端
- [ ] 是否只做轻状态缓存
- [ ] 是否通过事件驱动 View
- [ ] 回放状态下是否禁止发包
- [ ] 是否在不在线房间时错误发了 Room 包

### View
- [ ] OnEnable 订阅
- [ ] OnDisable 取消订阅
- [ ] 输入前是否校验状态机
- [ ] 是否误把本地状态当权威状态

### 其他
- [ ] 是否注册到 Factory / Module Binder
- [ ] 是否补充了组件 ID 配置
- [ ] 是否做了重连验证
- [ ] 是否做了回放验证

---

# 结语

StellarNet Lite 的核心优势不在“功能很多”，而在于：

- 数据流转清晰
- 房间上下文明确
- 权威边界明确
- 扩展点清晰
- 回放和重连有明确状态机隔离
- 团队协作时不容易把代码写成一团

请始终记住三句话：

1. **服务端才是真相，客户端只播结果。**
2. **新增玩法优先横向扩展 Component，不要纵向堆进巨石类。**
3. **回放是本地沙盒，不是在线房间的附属状态。**

只要守住这三条，这套框架就能长期保持可维护、可扩展、可排障。

