# StellarNet Lite

**StellarNet Lite** 是一套面向 Unity 房间制联机项目的工程级 C# 网络框架。

当前版本的核心方向是：

- `Runtime` 只保留底层运行时骨架
- `Extensions` 负责登录、房间生命周期、实体同步、录像回放、弱网监控等功能实现
- 协议、模块、组件通过 **静态生成** 的绑定代码接入，不依赖运行时反射扫描

框架核心设计遵循：

- **服务端权威 + 客户端表现层解耦**
- **显式协议通信**
- **组件化房间业务**
- **低 GC、可审计、可维护**

---

## 核心能力

- **登录与重连主链**：默认流程由 `Extensions/DefaultGameFlow` 提供
- **房间生命周期管理**：建房、进房、挂起、恢复、开始、结束
- **房间级录像回放**：支持录制、下载、播放、Seek 与快照恢复
- **实体同步与动画同步**：支持对象生成/销毁、位置同步、动画同步与重连快照
- **可插拔传输层**：内置 KCP、TCP、UDP 三种 Provider，其中 KCP/TCP 是正式可靠传输，UDP 主要用于教学和实验对照
- **房间多线程调度**：服务端支持房间 worker 并发推进
- **编辑器工具链**：协议/组件生成、预制体扫描、配置编辑、压测工具

---

## 当前结构

```text
StellarNetLite/
├── Runtime/          # 纯运行时核心：App / Session / Room / Transport / Dispatcher
├── Extensions/       # 官方扩展：DefaultGameFlow / Replay / ObjectSync / NetworkMonitoring
├── Editor/           # 编辑器工具：扫描生成、配置窗口、监控与压测窗口
├── Samples/          # 示例业务：SocialDemo
├── Doc/              # 中文工程文档
└── StellarNetLiteGenerated/  # 生成代码占位与 Unity Editor 生成结果
```

需要注意：

- `StellarNetLiteGenerated` 默认会保留**可编译占位文件**
- 真正完整的注册绑定内容，需要在 Unity Editor 中重新生成

---

## Runtime 与 Extensions 边界

当前版本已经把 `Runtime` 进一步纯化：

- `Runtime` 不再直接写死默认登录、弱网监控、录像落盘、强制离房通知等扩展逻辑
- 这些能力通过运行时桥与服务接口接入：
  - `IRuntimeFeatureBridge`
  - `IRoomRecordingService`
  - `IRoomMembershipNotifier`

这意味着：

- 普通业务开发者继续按原方式写协议、全局模块、房间组件
- 框架维护者可以在不侵入 `Runtime` 的前提下扩展默认功能

---

## 配置文件

当前配置已经拆分，不再把 Runtime 与扩展配置混写在一个文件里。

默认使用同一个编辑器窗口统一编辑：

- 菜单：`StellarNetLite/网络配置`
- 对应窗口：`Editor/NetConfigEditorWindow.cs`

保存后会生成三份配置：

- `NetConfig/netconfig.json`
- `NetConfig/replay_config.json`
- `NetConfig/objectsync_config.json`

含义分别是：

- `netconfig.json`：Runtime 核心配置
- `replay_config.json`：Replay 扩展配置
- `objectsync_config.json`：ObjectSync 扩展配置

同时仍保留对旧版单文件配置的兼容回退逻辑。

---

## 代码生成

修改以下内容后，需要重新生成自动绑定代码：

- 协议类 `[NetMsg]`
- 房间组件 `[RoomComponent]`
- 全局模块 `[ServerModule] / [ClientModule]`
- 消息处理函数 `[NetHandler]`

菜单入口：

- `StellarNetLite/强制重新生成协议与组件常量表`

生成结果包括：

- `MsgIdConst.cs`
- `ComponentIdConst.cs`
- `AutoMessageMetaRegistry.cs`
- `AutoUnauthenticatedProtocolRegistry.cs`
- `AutoRegistry.cs`
- `AutoRuntimeFeatureRegistry.cs`
- `NetPrefabConsts.cs`

---

## 快速开始

1. 打开 Unity 工程并等待首次导入完成
2. 执行 `StellarNetLite/强制重新生成协议与组件常量表`
3. 如有网络预制体变更，再执行 `StellarNetLite/生成网络预制体常量表`
4. 打开 `StellarNetLite/网络配置`，保存一次配置
5. 打开 `Samples/SocialDemo` 场景运行 Demo

常用场景：

- `Samples/SocialDemo/Client/Scenes/KCP/KCPHostScene.unity`

---

## SocialDemo 说明

`SocialDemo` 当前主要用于演示：

- 默认登录/房间主链
- 房间内实体同步与动画同步
- 房间录像回放
- 本地真实移动结果上报、服务端轻校验、即时状态转发与远端预测

其中角色移动当前采用：

- 本地客户端先做真实移动与碰撞
- 服务端做轻量合法性校验
- 服务端立刻广播 `S2C_SocialStateSync`
- 远端客户端立即刷新状态并继续做前向预测
- 周期 `ObjectSync` 仍负责兜底校正与回放记录

---

## 文档阅读顺序

推荐从这里开始：

1. `Doc/00. StellarNet Lite 文档总览与学习路径.md`
2. `Doc/01. StellarNet Lite 首次运行与 SocialDemo 跑通指南.md`
3. `Doc/02. StellarNet Lite 核心概念与全流程指南.md`
4. `Doc/05. StellarNet Lite 代码生成与编辑器工具链指南.md`
5. `Doc/07. StellarNet Lite 实体同步与动画同步深度解析.md`
6. `Doc/09. StellarNet Lite 回放重连与框架自定义指南.md`

---

## 适用场景

更适合：

- 社交/派对类房间游戏
- 中轻度合作/竞技项目
- 教学、训练、模拟类多人场景
- 中小型 Unity 联机项目

不建议直接用于：

- MMORPG / 无缝大世界
- 强对抗 FPS / 需要服务端物理回滚的项目

---

## 开发约定

- 运行时业务逻辑避免反射
- 拒绝 `SyncVar` 一类黑盒同步
- 尽量使用 Early Return，避免深层嵌套
- UI 与 Runtime 解耦，通过事件总线或 Router 协作
- 业务协议、全局模块、房间组件改动后及时重新生成绑定代码

---

## License

本项目遵循 **MIT License**。

详见根目录：

- [LICENSE.text](LICENSE.text)
