## 📋 时间轴模块化拆分 - 全面梳理完成

我已完成对整个项目时间轴系统的全面梳理，完整规划文档已输出至：

> **[时间轴模块化拆分规划文档.md](file:///d:/Naziki_Editor/Naziki Editor/Naziki Editor/tutorial/时间轴模块化拆分规划文档.md)**

---

### 🔍 梳理成果概要

#### 识别到的文件（共 30+ 个源文件）

| 层级 | 文件数 | 关键文件 |
|------|--------|---------|
| **Core层** | 12个 | `ChartTimeEngine`, `StoryboardTimeConverter`, `TimelineCoordEngine`, `TimelineAnchorEngine`, `TimelineLayoutEngine`, `MacroTimelineService`, `MicroTimelineService` 等 |
| **Views层** | 8个 (.xaml+.cs) | `TimelineControl` (1090行), `TimelineClipControl` (556行), `ClipDetailedEditor` (616行), `ClipPropertyTrackRow` (583行) |
| **UI层** | 3个 | `TimelineDataEngine`, `TimelineViewModel`, `MacroTimelineModels` |
| **Models层** | 1个 | `TimelineClipModels` |
| **Themes层** | 3个 | `TimelineStyles`, `TimelineColors` (Dark/Light) |
| **Tests层** | 2个 | `TimelineServiceTests`, `TimelineSharedEngineTests` |

#### 识别到的核心问题

| 严重程度 | 问题 |
|---------|------|
| 🔴 | `TimelineControl.xaml.cs` 承担 **9大职责**，超1000行 |
| 🔴 | `TimelineClipControl` 直接 `new` 已废弃的 `TimelineInteractionService` |
| 🟡 | `StoryboardTimeConverter` 单文件650行，混合5种职责 |
| 🟡 | `ClipDetailedEditor` 混合微观编辑器+模板管理+缩放+音符渲染 |

#### 目标模块划分

```
📦 Shared（共用基础）   → Core/Timeline/Shared/     (10个文件，纯算法引擎)
📦 MainTimeline（宏观）   → Core/Timeline/MainTimeline/ + Views/MainTimeline/ (11个文件)
📦 MicroTimeline（微观）  → Core/Timeline/MicroTimeline/ + Views/MicroTimeline/ (9个文件)
📦 TimelineClip（方块）   → Core/Timeline/TimelineClip/ + Views/TimelineClip/ (10个文件)
```

#### 五阶段实施路线

| 阶段 | 内容 | 预估工时 |
|------|------|---------|
| Phase 1 | Shared 模块拆分（StoryboardTimeConverter分5文件） | 2-3h |
| Phase 2 | TimelineClip 模块独立化（剥离拖拽/伸缩/主题/菜单） | 3-4h |
| Phase 3 | MainTimeline 重构（剥离5个子控件） | 4-5h |
| Phase 4 | MicroTimeline 重构（剥离3个子模块） | 3-4h |
| Phase 5 | 清理废弃代码 + 测试重组 | 2-3h |

**总计预估**: 14-19小时，分5个阶段可独立交付验证。

---

是否需要我立即开始执行 Phase 1（Shared 模块拆分），还是您希望先审阅规划文档后再决定？