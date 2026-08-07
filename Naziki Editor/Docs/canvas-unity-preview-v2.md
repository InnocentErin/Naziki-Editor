# Canvas–Unity Preview v2 设计

## 边界与不变量

- 编辑器预览协议固定为 `naziki.editor-preview.v2`；生产协议 `cytoid.game-core.v2` 不变。
- 一次 Unity 进程启动对应一组不可变的 `connectionId + generation + sessionId + nonce`。
- 所有消息使用小驼峰 envelope：`protocol`、`connectionId`、`generation`、`sessionId`、`type`、`requestId`、`editorVersion`、`basePreviewVersion`、`targetPreviewVersion`、`payload`。
- 帧格式是 4 字节 little-endian 长度加 UTF-8 JSON，最大 64 MiB。协议、连接、代次、会话或请求身份不匹配的消息只记录诊断，不改变状态。
- 连接健康与内容有效性独立。`preview.load.failed`、素材解码错误和校验错误均不得转换为“连接已断开”。
- `host.ready` 只完成握手，不加载内容。项目协调器显式提交且每次只提交一个 `preview.open`。
- Transport 主动停止是预期生命周期事件，不发布断连；只有 EOF、管道 I/O、损坏帧或超大帧才是物理连接故障。
- 编辑器消息订阅者、UI 订阅者和错误上报订阅者彼此隔离；其中任一订阅者抛错都不能关闭管道或终止后续消息分发。

## 状态与握手

连接阶段依次为：进程启动、图形窗口、管道连接、握手、健康、恢复或终态。内容阶段依次为：空、校验、VFS 物化、加载、就绪/带警告就绪或失败。

握手严格为：

1. Unity 发送 `host.hello`，携带 nonce、`hostRevision = 5` 和能力表。
2. 编辑器验证身份、revision 和能力后发送 `host.accept`。
3. Unity 再次验证 nonce 后发送 `host.ready`。此时只应用视口/性能设置。

旧 revision 或 v1 runtime 直接进入 `PREVIEW_RUNTIME_OUTDATED`，提示重新构建，不降级兼容。

编辑器使用单读者协调队列串行处理协议消息、Transport 状态、进程退出、心跳和超时结果。每个启动代次创建不可变连接上下文，包含 connectionId、host generation、transport generation、sessionId、nonce 和独立取消令牌。任何后台结果回到队列后必须再次匹配该上下文；旧代次事件只记为 stale，不得修改当前连接。`host.accept` 必须实际发送完成后才继续等待 `host.ready`。

## 期限与恢复

| 项目 | 期限 |
|---|---:|
| 进程启动 | 30 秒 |
| 图形窗口 | 15 秒 |
| 管道连接 | 15 秒 |
| 握手 | 5 秒 |
| 首个加载进度 | 5 秒 |
| 当前请求进度静默 | 30 秒 |
| 单次加载绝对上限 | 120 秒 |
| 普通命令 ACK | 5 秒 |
| 心跳 | 每 5 秒；连续 2 次失败断连 |

遥测消息不会刷新心跳或加载期限。加载进度只接受当前连接、代次、会话、requestId 和 target version，阶段只可前进或幂等重复。自动恢复、手动重试和设置触发的重启合并到同一重启任务，不允许同时清理或启动两个连接；每个新连接恢复内容时使用一次 `preview.open`。

故障恢复会取消旧请求、停止管道、请求 Unity 退出并终止超时残留进程，以新 generation 重启并从 LastKnownGood 恢复时间和播放状态。每次事故只自动恢复一次；再次失败等待工具栏“重试”。连续健康 30 秒后事故计数归零。

## 故障分类与诊断

| 诊断码 | 条件 | 连接含义 |
|---|---|---|
| `PREVIEW_CONNECTION_LOST` | 已确认 EOF 或管道 I/O 失败 | 物理连接已断开 |
| `PREVIEW_FRAME_INVALID` | 帧长度非法、超过 64 MiB 或 JSON 损坏 | 协议连接故障，保留底层异常 |
| `PREVIEW_MESSAGE_HANDLER_FAILED` | 编辑器处理协议消息或订阅事件时抛错 | 管道仍连接；不得伪装为断连 |
| `PREVIEW_HANDSHAKE_SEND_FAILED` | `host.accept` 写入失败 | 握手失败，保留发送异常 |
| `PREVIEW_HOST_READY_TIMEOUT` | 管道正常但 5 秒内无匹配 `host.ready` | 握手超时，不写成断连 |
| `PREVIEW_RUNTIME_UNRESPONSIVE` | 连续两次匹配心跳失败 | Runtime 无响应，触发一次恢复 |
| `PREVIEW_CLEANUP_FAILED` | 停止管道或清理 Unity 进程失败 | 阻止残留连接静默污染下一代次 |

最近 200 条协议元数据保存在内存环形追踪中，并异步写入 `%LOCALAPPDATA%\NazikiEditor\Logs\preview-YYYYMMDD.log`。记录方向、时间、阶段、消息类型、requestId、generation 和版本；不记录 nonce、完整 payload 或用户素材内容。诊断保留首次根因及异常链，自动恢复的后续状态不能覆盖它；只有内容重新达到 Ready/ReadyWithWarnings，或发生明确的正常关闭/项目切换时才清除。

## Canvas 行为

- `LoadingOverlay` 是 `UnityHostContainer` 的子元素，只覆盖原生画面区。
- 无 LastKnownGood 且处于启动或首次加载阶段才显示遮罩。
- Ready、ReadyWithWarnings、校验失败、超时、断连、runtime 缺失及其他终态立即移除遮罩。
- 热更新期间保留旧画面且不显示遮罩；候选 Storyboard 完成解析、初始化和目标时刻求值后整棵切换。
- 终态失败隐藏原生 HWND，以非阻塞占位状态显示错误；工具栏、诊断和重试始终可交互。

## 数据流

1. `StoryboardPreviewService` 从工程生成无损编辑源快照，并解析唯一 `chart_difficulty`。
2. 官方兼容投影过滤非官方字段、映射旧 `w/h`，不修改编辑源。
3. `PreviewVfsMaterializer` 只物化所选难度，保留该难度的 difficulty、music override、storyboard 和 level 元数据。
4. Unity 使用官方 `LevelMeta`、`ChartModel`、Storyboard 模型及 StateParser 读取 VFS。
5. Unity 返回 started/progress 和匹配 request/version 的 Ready、ReadyWithWarnings 或 LoadFailed。
