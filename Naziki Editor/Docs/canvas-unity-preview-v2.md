# Canvas–Unity Preview v2 设计

## 边界与不变量

- 编辑器预览协议固定为 `naziki.editor-preview.v2`；生产协议 `cytoid.game-core.v2` 不变。
- 一次 Unity 进程启动对应一组不可变的 `connectionId + generation + sessionId + nonce`。
- 所有消息使用小驼峰 envelope：`protocol`、`connectionId`、`generation`、`sessionId`、`type`、`requestId`、`editorVersion`、`basePreviewVersion`、`targetPreviewVersion`、`payload`。
- 帧格式是 4 字节 little-endian 长度加 UTF-8 JSON，最大 64 MiB。协议、连接、代次、会话或请求身份不匹配的消息只记录诊断，不改变状态。
- 连接健康与内容有效性独立。`preview.load.failed`、素材解码错误和校验错误均不得转换为“连接已断开”。
- `host.ready` 只完成握手，不加载内容。项目协调器显式提交且每次只提交一个 `preview.open`。

## 状态与握手

连接阶段依次为：进程启动、图形窗口、管道连接、握手、健康、恢复或终态。内容阶段依次为：空、校验、VFS 物化、加载、就绪/带警告就绪或失败。

握手严格为：

1. Unity 发送 `host.hello`，携带 nonce、`hostRevision = 5` 和能力表。
2. 编辑器验证身份、revision 和能力后发送 `host.accept`。
3. Unity 再次验证 nonce 后发送 `host.ready`。此时只应用视口/性能设置。

旧 revision 或 v1 runtime 直接进入 `PREVIEW_RUNTIME_OUTDATED`，提示重新构建，不降级兼容。

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

遥测消息不会刷新心跳或加载期限。加载进度只接受当前连接、代次、会话、requestId 和 target version，阶段只可前进或幂等重复。

故障恢复会取消旧请求、停止管道、请求 Unity 退出并终止超时残留进程，以新 generation 重启并从 LastKnownGood 恢复时间和播放状态。每次事故只自动恢复一次；再次失败等待工具栏“重试”。连续健康 30 秒后事故计数归零。

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

