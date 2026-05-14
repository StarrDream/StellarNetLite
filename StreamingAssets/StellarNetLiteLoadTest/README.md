# StellarNetLiteLoadTest

`StellarNetLiteLoadTest` 是当前框架的独立压测工具，用于在不依赖 Unity 场景客户端的情况下验证 KCP/TCP 传输、默认登录/房间流程和移动同步主链。

## 当前能力

- 支持 `kcp` 和 `tcp` 两种正式可靠传输。
- 支持多房间、多客户端压测。
- 每个房间的第一个客户端负责建房，其余客户端加入同一房间。
- 自动完成房间框架需要的最小 `Ready` / `StartGame` 流程。
- 开局后模拟正常客户端：小范围移动、停顿、偶发动作和气泡聊天。
- 支持运行时房间控制命令：`addroom`、`removeroom`、`endroom`、`status`。
- `--duration 0` 表示持续运行，直到手动停止。

## 构建

```powershell
dotnet build -c Release
```

## 示例

```powershell
dotnet run -c Release -- --transport kcp --host 127.0.0.1 --port 7777 --rooms 5 --clients-per-room 20 --duration 0 --move-rate 8
```

## 传输层验收口径

KCP 和 TCP 是当前正式可靠传输；UDP 只用于教学、实验或对照测试，不纳入生产可靠性目标。

推荐的 100 CCU 验收分别跑 KCP 和 TCP：

```powershell
dotnet run -c Release -- --transport kcp --host 127.0.0.1 --port 7777 --rooms 5 --clients-per-room 20 --duration 600 --move-rate 8
dotnet run -c Release -- --transport tcp --host 127.0.0.1 --port 7777 --rooms 5 --clients-per-room 20 --duration 600 --move-rate 8
```

期望结果：

- 无 transport error。
- 无解包失败。
- 无异常 state timeout 爆发。
- 建房、进房、Ready、StartGame 主链稳定。
- 移动同步持续推进。
- TCP pending frame/bytes 和 queue abort 指标不持续增长。

## 参数

- `--transport kcp|tcp`
- `--host 127.0.0.1`
- `--port 7777`
- `--rooms 5`
- `--clients-per-room 20`
- `--connect-rate 10`
- `--duration 0`
- `--move-rate 8`
- `--room-name LoadTestRoom`
- `--account-prefix bot`
- `--client-version 0.0.1`
- `--log-interval 5`

## 说明

- `duration = 0` 表示一直运行，直到手动停止。
- `move-rate` 是机器人移动时的速度上限，机器人不会持续不停移动。
- 运行时命令可以直接输入到压测进程，也可以从 Unity Editor 的压测窗口发送。
- Unity Editor 入口位于 `StellarNetLite/Load Test`。
