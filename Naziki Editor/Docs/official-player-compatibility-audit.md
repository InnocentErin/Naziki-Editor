# 官方播放器兼容审计

权威参考为仓库根目录 `.original_player`，审计和测试只读该目录；可构建的编辑器 Preview fork 位于 `External/original_player`。

## 已发现并收敛的差异

| 契约 | 变化位置 | 影响 | 处理结果 |
|---|---|---|---|
| `ChartModel.time_base` | 编辑器 `ChartModels.cs` 原为 `int` | 小数 time base 在进入官方解析器前被改写/拒绝 | 改为 `double`，时间引擎同步使用 double；wire 不再要求整数 |
| Video `speed/loop` | Preview fork 的 Video model/parser/renderer | 官方不支持变速与循环，随机定位结果与原游戏不同 | 从 Preview model/parser 移除，视频按 1x、非循环定位 |
| 旧 `w/h` | 编辑器故事板源与属性面板 | 官方只读取 `width/height` | 源数据保留；runtime wire 映射为官方字段，冲突时官方字段优先并警告；面板改编辑 Width/Height |
| `pivot_x/pivot_y` | 编辑器 StageObject 扩展 | 官方 StateParser 不消费 | 停止新建，Preview/正式 runtime wire 过滤并警告，编辑源不删除 |
| Text `line_spacing/font_style` | 编辑器 Text 扩展 | 官方 Text parser 不消费 | 停止新建并从 runtime wire 过滤；保留旧源值并警告 |
| Video `preserve_aspect` | 编辑器 Video 扩展 | 官方 Video parser 不消费 | Video 模板停止新建并从 runtime wire 过滤；Sprite 的官方 preserve_aspect 保持支持 |
| StageObject `dx/dy` | 编辑器此前缺失 | 官方支持的单位偏移无法编辑或热更新 | 补入模型、模板白名单和属性编辑链 |
| Video `color` | 编辑器此前缺失 | 官方支持的视频着色无法表达 | 补入模型、属性面板、模板和 runtime wire |
| Text size / event tick 浮点 | 编辑器预检 | 过早取整会改变源数据 | 原值输出；警告官方 Newtonsoft 转 Int32 使用 midpoint-to-even；Preview 复用同一解析结果 |
| 非 PNG/JPG 与平台视频 | 编辑器导入校验 | 编辑器侧解码限制错误阻止官方可尝试的资源 | 仅兼容警告，不转换、不阻止；Unity 必需资源失败归内容失败，非致命视频 Prepare 失败归带警告就绪 |
| 多难度选择 | Preview VFS 原 hard-first | 工程所选谱面可能被另一难度替换 | `.nep` v3 可选记录 `chart_difficulty`；按规范化绝对路径唯一匹配 easy/hard/extreme，零/多匹配阻止 Preview |
| trigger 随机跳转 | Preview fork 的 `ApplyPreviewTriggers` | 只看目标时刻最终状态，未按官方音符清除顺序和 uses 重放 | 删除近似方法；从最后确认 wire 重建 Storyboard，按 note end time/id 顺序复用 NoteClear/Combo/Score 与 uses 条件 |
| 增量热更新 | Preview fork 的逐对象替换 | 失败时可能留下半更新 renderer | 所有候选先完整 Parse/Initialize，再原子替换；失败保留 LastKnownGood |

## 明确允许的 Preview 差异

- Windows 独立进程、HWND 嵌入、named pipe host bridge、外部时钟和编辑器性能设置。
- Autoplay、无输入/无结算副作用、随机访问 note window 重建。
- `naziki.editor-preview.v2` 仅用于编辑器；不得修改生产 `cytoid.game-core.v2`。
- Unity 6000.0.80f1 是 Preview 构建工具链；`.original_player` 的版本只作为解析语义基准。

## 自动保护

- `OfficialPreviewCompatibilityTests` 覆盖非官方字段过滤、w/h 冲突、dx/dy、Video color、浮点中点取偶、单难度路径匹配和 v2 小驼峰 envelope。
- 现有 VFS 集成测试验证 level 元数据保留、单难度重写和不可变版本。
- 最终校验必须确认 `.original_player` 无文件变化。

