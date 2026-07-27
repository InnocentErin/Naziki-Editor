# Naziki Editor 项目内容索引

> 最后更新：2026-07-10
> 更新批次：修复指南功能修复 + 精细化重构优化

---

## 一、最近变更记录 (2026-07-10)

### 功能错误修复（依据 `tutorial/错误修复和整合.md`）

| 修改文件 | 变更内容 | 影响范围 |
| :--- | :--- | :--- |
| `Core/Commands/AppCommands.cs` | 重写 `DoLoadProject` 加载顺序：强制 Chart→Storyboard→通知UI，无谱面时拦截故事板加载并弹窗报错 | 核心加载总线 |
| `Views/MainWindow.xaml.cs` | `RefreshAllUIAfterProjectLoad` 末尾新增音频自动加载逻辑；`MenuImportAudio_Click` 新增音频路径自动回写 `.nep` 并存盘 | 用户交互 |
| `Views/EventListControl.xaml.cs` | `ExecuteImportStoryboard` 加固前置条件，无 `Context.HasChart` 时强行拦截并弹窗 | 安全拦截 |
| `Core/Timeline/ChartTimeEngine.cs` | `ParseCytoidTimeExpression` 当 `allNotes` 为空时返回 `float.MaxValue`，防止空引用崩溃 | 底层防御 |

### 精细化重构优化

| 修改文件 | 变更内容 |
| :--- | :--- |
| `Core/Timeline/ChartTimeEngine.cs` | 清理 `TickToSeconds` 与 `ParseCytoidTimeExpression` 方法间多余空行 |
| `Views/TimelineControl.xaml.cs` | 移除重复注释行；移除未使用字段 `_detailedEditor`、`_isDetailedEditMode`、`_editingClipModel` |
| `Views/MainWindow.xaml.cs` | 移除重复注释行；移除未使用字段 `_maxChartTime` |
| `Views/TimelineClip/TimelineClipControl.xaml.cs` | 移除未使用字段 `_selectedNode` |
| `Views/PropertyPanelControl.xaml.cs` | 移除未使用事件 `OnDataModified` |

---

## 二、全项目详细索引表（按文件夹结构分级）

### 1. `Core/Abstractions` (核心接口定义层)

| 文件名 | 核心接口/功能 | 主要方法说明 |
| :--- | :--- | :--- |
| `IAudioSyncEngine.cs` | 音频同步引擎接口 | `LoadAudioAsync()`, `Play()`, `Pause()`, `Seek()`, `GetCurrentSmoothTime()` |
| `ICommandDispatcher.cs` | 命令调度器接口 | `Register()`, `CanExecute()`, `Execute()` |
| `ICompilationNotifier.cs` | 编译通知接口 | `NotifyInfo()`, `NotifyWarning()` |
| `ICompilationService.cs` | 故事板编译服务接口 | `CompileStoryboard()`, `CompileForExport()`, `SyncTemplateMetadata()` |
| `IDialogService.cs` | 对话框服务接口 | `ShowMessage()`, `ShowConfirm()`, `ShowOpenFileDialog()` 等 |
| `IEditorCoordinator.cs` | 编辑器协调器接口 | `CommitEntityEdit()`, `CommitTemplateEdit()` |
| `IEntityFactory.cs` | 实体工厂接口 | `CreateSpriteFromAsset()`, `CreateText()`, `CreateLine()`, `CreateNoteController()` |
| `IEntityIdService.cs` | 实体ID服务接口 | `GenerateUniqueId()`, `IsIdExists()`, `IsIdConflict()` |
| `IHistoryService.cs` | 历史记录服务接口 | `RecordSnapshot()`, `Undo<T>()`, `Redo<T>()` |
| `ILayoutWarningNotifier.cs` | 布局警告通知接口 | `WarnTooManyOverlappingObjects()` |
| `IMessageBroker.cs` | 消息总线接口 | `Subscribe<T>()`, `Publish<T>()` |
| `INoteSelectorService.cs` | 音符选择器服务接口 | `ParseSelector()`, `SelectNotes()`, `GetMatchedTimeRange()` |
| `IProjectService.cs` | 项目服务接口 | `LoadProjectAsync()`, `SaveProjectAsync()`, `ImportStoryboard()`, `SaveAssetCapsule()` |
| `IPropertyEditorService.cs` | 属性编辑器服务接口 | `TryGetValue()`, `TrySetValue()`, `GetCategory()`, `GetConstraint()` |
| `IStoryboardParser.cs` | 故事板解析器接口 | `StandardizeStoryboardIds()`, `SyncControlBoardIdMaps()` |
| `IStoryboardRepository.cs` | 故事板仓储接口 | `Add()`, `Remove()`, `Replace()`, `AddTemplate()`, `MoveEntityToIndex()` |
| `ITemplateManager.cs` | 模板管理器接口 | `CheckForCircularDependency()`, `RenameTemplateGlobally()`, `GetAllowedPropertiesForType()` |
| `ITimeEngine.cs` | 时间引擎接口 | `TickToSeconds()`, `ParseCytoidTimeExpression()` |
| `ITimelineInteractionService.cs` | 时间轴交互服务接口 | `DecodeKeyframes()`, `ScaleKeyframes()`, `WriteBackVisualTime()` |
| `ITrackBlueprintManager.cs` | 轨道蓝图管理器接口 | `GetBlueprintsForType()` |
| `IWorkspaceService.cs` | 工作区服务接口 | `HasConflict()`, `ResolveConflict()` |

### 2. `Core/Audio` (音频引擎实现)

| 文件名 | 核心类/功能 | 主要方法/说明 |
| :--- | :--- | :--- |
| `AudioSyncEngine.cs` | 实际音频引擎实现 | 基于 `NAudio` 和 `Vorbis`，提供 `LoadAudioAsync()`, `Play()`, `Pause()`, `Seek()` 等。 |
| `AudioSyncEngineAdapter.cs` | 接口适配器 | 包装 `AudioSyncEngine` 为 `IAudioSyncEngine` 供 DI 注入。 |

### 3. `Core/Chart` (谱面逻辑)

| 文件名 | 核心类/功能 | 主要方法/说明 |
| :--- | :--- | :--- |
| `ChartLogic.cs` | 谱面数学逻辑库 | `IsChainVisible()`, `FindChildren()`, `GetNoteTypeString()` |
| `NoteSelectorService.cs` | 音符选择器服务实现 | 解析 `NoteSelectorModel`，根据 JSON 条件过滤音符列表。 |

### 4. `Core/Commands` (命令实体层)

| 文件名 | 核心类/功能 | 主要方法/说明 |
| :--- | :--- | :--- |
| `AppCommands.cs` | 全局应用命令集合 | `DoLoadProject()`（强制 Chart→Storyboard 加载顺序）、`DoSaveProject()`、`DoImportChart()`、`DoOpenProject()`。连接UI与Core服务的枢纽。 |
| `CommandDispatcher.cs` | 命令调度器实现 | 内部依赖字典，提供 `Register`, `Execute` 功能。 |

### 5. `Core/Common` (通用服务)

| 文件名 | 核心类/功能 | 主要方法/说明 |
| :--- | :--- | :--- |
| `EntityIdService.cs` | ID服务实现 | `GenerateUniqueId()` 基于名称和 GUID 生成不冲突ID。 |
| `PropertyEditorService.cs` | 属性编辑器服务实现 | 封装 `FastReflectionHelper` 并调用 `PropertyClassifier` 进行分类。 |

### 6. `Core/Compilation` (编译与控制器优化)

| 文件名 | 核心类/功能 | 主要方法/说明 |
| :--- | :--- | :--- |
| `ControllerOptimizer.cs` | 场景控制器优化引擎 | `DetectScatteredProperties()`, `OptimizeControllers()` 将碎片化属性合并到统一控制器内。 |
| `StoryboardCompiler.cs` | 故事板展平编译器 | `FlattenStoryboard()` 递归展开模板、计算 `RelativeTime/AddTime`，拆分控制器。 |
| `TemplateClassifier.cs` | 模板类型分类器 | `AnalyzeTemplate()` 根据属性残留智能推断模板的 8 大门派分类。 |

### 7. `Core/Editor` (编辑器协调逻辑)

| 文件名 | 核心类/功能 | 主要方法/说明 |
| :--- | :--- | :--- |
| `EditorCoordinator.cs` | 编辑器协调器实现 | `CommitEntityEdit()` 负责编辑结束后完成持久化与脏标记。 |

### 8. `Core/Helpers` (辅助工具类)

| 文件名 | 核心类/功能 | 主要方法/说明 |
| :--- | :--- | :--- |
| `EventNameResolver.cs` | 实体名称解析器 | `GetDisplayName()` 根据对象类型（Sprite/Text等）返回友好名。 |
| `FastReflectionHelper.cs` | 高性能反射引擎 | `TryGetValue()`, `TrySetValue()` 缓存反射委托以提升性能。 |
| `PropertyClassifier.cs` | 属性分类器 | `GetCategory()` 将属性名映射为 `Spatial/Appearance/UIControl/Effects`。 |
| `PropertyConstraintManager.cs` | 属性约束大管家 | `GetConstraint()` 返回 `UIType`、最大值最小值、默认值等 UI 生成所需约束。 |

### 9. `Core/History` (历史记录与撤销重做)

| 文件名 | 核心类/功能 | 主要方法/说明 |
| :--- | :--- | :--- |
| `HistoryService.cs` | 撤销/重做引擎 | 基于 `List<string>` 的 JSON 快照，支持 `MaxCapacity` 自动剔除旧记录。 |

### 10. `Core/Messaging` (消息总线)

| 文件名 | 核心类/功能 | 主要方法/说明 |
| :--- | :--- | :--- |
| `MessageBroker.cs` | 主题级广播总线 | `Subscribe<T>()`, `Publish<T>()` 使用 `Dictionary<string, List<Delegate>>` 实现解耦通信。 |

### 11. `Core/Project` (项目管理服务)

| 文件名 | 核心类/功能 | 主要方法/说明 |
| :--- | :--- | :--- |
| `ProjectService.cs` | 物理文件读写服务 | 读写 `.nep`、`.json`、`.nem` 文件，处理 `ProjectDataContext` 上下文。包含 `SilentImportChart()`, `LoadProjectStoryboard()`, `ImportStoryboard()` 等。 |

### 12. `Core/Serialization/Converters` (序列化转换器)

| 文件名 | 核心类/功能 | 主要方法/说明 |
| :--- | :--- | :--- |
| `StoryboardEntityConverter.cs` | 实体转换器 | 自定义 JSON 序列化规则，将 `BaseState` 和 `Keyframes` 输出为官方要求的 `states` 数组。 |
| `UnitFloatConverter.cs` | 单位浮点转换器 | 序列化 `UnitFloat`（如 `"0.5notex"`、`"100stagex"`）。 |

### 13. `Core/Services` (业务服务)

| 文件名 | 核心类/功能 | 主要方法/说明 |
| :--- | :--- | :--- |
| `AssetMetaManager.cs` | 素材映射账本管家 | `LoadMetaMap()`, `SetExternalAssetDisplayName()`, `RenameNemAsset()` 管理素材的"显示名"。 |
| `AssetScanner.cs` | 素材扫描器 | `ScanProjectAssets()` 扫描目录图片、视频、`.nem` 胶囊并生成 `AssetBundle`。 |

### 14. `Core/Storyboard` (故事板实体层)

| 文件名 | 核心类/功能 | 主要方法/说明 |
| :--- | :--- | :--- |
| `Compilation/CompilationService.cs` | 编译服务实现 | 包装 `StoryboardCompiler` 和 `ControllerOptimizer`，提供上层调用入口。 |
| `Compilation/DialogCompilationNotifier.cs` | 对话框编译通知器 | 将编译警告通过 `IDialogService` 弹出。 |
| `ChartStoryboardLink.cs` | 故事板/谱面自动配对器 | `ExecuteAutoLink()` 纯数学计算配对数量。 |
| `EntityFactory.cs` | 实体工厂实现 | 集中创建 `C2Sprite`/`C2Text`/`C2Line` 等实体。 |
| `StoryboardParser.cs` | 故事板解析器实现 | `StandardizeStoryboardIds()`, `SyncControlBoardIdMaps()` 保证控制板ID持久化留痕。 |
| `StoryboardRepository.cs` | 仓库实现 | `Add()`, `Remove()`, `Replace()`, `AddTemplate()` 操作 `StoryboardRoot` 内部列表。 |
| `StoryboardValidator.cs` | 故事板数据校验器 | `ValidateStateConflicts()` 检查同一时间点是否有属性冲突。 |
| `TemplateManager.cs` | 模板管理器实现 | `CheckForCircularDependency()`, `GetAllowedPropertiesForType()` 等。 |
| `TrackBlueprintManager.cs` | 蓝图管理器实现 | 提供场景控制器的 `TrackBlueprint` 列表（UI属性集合）。 |

### 15. `Core/Timeline` (时间轴核心算法)

| 文件名 | 核心类/功能 | 主要方法/说明 |
| :--- | :--- | :--- |
| `ChartTimeEngine.cs` | 谱面时间引擎实现 | `TickToSeconds()` 计算 BPM 数组，`ParseCytoidTimeExpression()` 翻译锚点（含空引用防御）。 |
| `StoryboardTimeConverter.cs` | 时空终极转换引擎 | 提供 `DecodeTimelineKeyframes()`、`WriteBackVisualTime()`、`ScaleInternalKeyframes()` 复杂帧换算。 |
| `TimelineAnchorEngine.cs` | 音符锚点计算引擎 | `CalculateNearestAnchorExpression()` 找到离当前秒数最近的音符并生成锚点字符串。 |
| `TimelineCoordEngine.cs` | 时空坐标换算器 | `TimeToX()`, `XToTime()`, `CalculateVirtualEndPosition()` |
| `TimelineInteractionService.cs` | 时间轴交互服务实现 | 包装 `StoryboardTimeConverter`，提供时间轴操作抽象。 |
| `TimelineLayoutEngine.cs` | 智能排版引擎 | `AutoAssignOrderForVisualEntities()` 为重叠实体自动分配 `Layer/Order` 防止遮挡。 |

### 16. `Core/Workspace` (工作区)

| 文件名 | 核心类/功能 | 主要方法/说明 |
| :--- | :--- | :--- |
| `WorkspaceService.cs` | 属性/源码冲突仲裁 | `HasConflict()`, `ResolveConflict()` 解决属性面板与JSON源码的不一致问题。 |

### 17. `Models` (纯数据模型)

| 文件名 | 核心类/功能 | 主要方法/说明 |
| :--- | :--- | :--- |
| `ChartModels.cs` | 谱面模型 | `C2Chart`, `C2Note`, `C2Event` 等强类型数据实体。 |
| `EasingFunction.cs` | 缓动枚举 | 内置了 Cytoid 官方 33 种缓动枚举。 |
| `EditorModels.cs` | 编辑器专属元数据 | `EditorTrackMeta`, `EditorTemplateMeta` 不污染官方 JSON。 |
| `NazikiProjectModel.cs` | 工程模型 | `NazikiProjectModel` 用来序列化 `.nep` 文件（含 `AudioFilePath` 音频路径）。 |
| `NemDocument.cs` | 素材胶囊模型 | `NemDocument` 用来序列化 `.nem` 胶囊文件。 |
| `StoryboardMeta.cs` | 故事板元数据 | `StoryboardMeta` 包含模板和轨道的额外 UI 属性。 |
| `StoryboardModels.cs` | 故事板模型 | 定义 `IStoryboardEntity`, `StoryboardRoot`, `SpriteState`, `ControllerState` 等全量 C2 实体。 |
| `TemplateType.cs` | 模板门派枚举 | 8 大门派 `Generic/StageObject/Text` 等。 |
| `MacroClipModels.cs` | 时间轴模型 | `MacroClipModel` 用于 UI 层展示，包含 `StartTime`, `EndTime`, `AssociatedObject`。 |

### 18. `ProjectManagement` (项目入口)

| 文件名 | 核心类/功能 | 主要方法/说明 |
| :--- | :--- | :--- |
| `ProjectHubWindow.xaml.cs` | 项目中心窗口 | 启动时的项目选择/创建界面。 |

### 19. `State` (全局状态)

| 文件名 | 核心类/功能 | 主要方法/说明 |
| :--- | :--- | :--- |
| `ProjectDataContext.cs` | 全局数据上下文 | 保存所有 `Chart`, `StoryboardRoot`, `StoryboardMeta`, `TimeEngine`，并提供 `MarkAsModified()` 广播。 |

### 20. `UI/Rendering` (渲染引擎)

| 文件名 | 核心类/功能 | 主要方法/说明 |
| :--- | :--- | :--- |
| `GlobalRenderEngine.cs` | 全局硬件渲染引擎 | 订阅 `CompositionTarget.Rendering`，提供 `OnRenderTick` 驱动帧刷新。已改为 DI 管理（Singleton），移除静态单例。 |
| `NoteVisualEngine.cs` | 音符时空手绘工厂 | `RenderNoteRuler()` 根据 `C2Note` 列表和 `ChartTimeEngine` 在 Canvas 上绘制音符图标、连线、Hold 条。已改为 DI 管理。 |

### 21. `UI/Services` (UI 服务)

| 文件名 | 核心类/功能 | 主要方法/说明 |
| :--- | :--- | :--- |
| `TimelineDataEngine.cs` | 时间轴数据生成器 | `BuildMacroTimeline()` 生成主时间轴轨道组，`BuildDetailedTimeline()` 生成微观百叶窗轨道。 |

### 22. `UI/ViewModels` (视图模型)

| 文件名 | 核心类/功能 | 主要方法/说明 |
| :--- | :--- | :--- |
| `AssetItemViewModel.cs` | 素材项 VM | 包含 `DisplayName`, `IsEditing` 状态，支持双向绑定和重命名。 |
| `PropertyEditorViewModel.cs` | 属性编辑器 VM | 构建 `DynamicProperties` 集合，驱动 `PropertyRowViewModel`。 |
| `TimelineViewModel.cs` | 主时间轴 VM | 管理 `TrackGroups`, `PixelsPerSecond`, `CurrentPlayheadSeconds`，通过 `TimelineDataEngine` 生成数据。 |
| `TimelineViewModels.cs` | 时间轴轨道模型 | `TimelineTrackGroupModel`, `TimelineTrackModel`, `MacroClipModel` 纯数据结构。 |

### 23. `Views` (WPF 主视图层)

| 文件名 | 核心类/功能 | 主要方法/说明 |
| :--- | :--- | :--- |
| `AssetListControl.xaml.cs` | 素材列表 UI | `RefreshAssetListUI()`, 鼠标双击触发 `CreateEventFromAsset` 广播。 |
| `CanvasControl.xaml.cs` | 预览/JSON 源码页 | `RefreshJsonView()`, `TrackSelectedObject()`, `ForceApplyJson()` 提供了 AvalonEdit 编辑器冲突保护。 |
| `EventListControl.xaml.cs` | 事件列表 UI | `LoadStoryboardUI()`, `ExecuteImportStoryboard()`（含谱面拦截），双击触发 `RequestOpenPropertyEditor`。已通过 DI 注入 `IProjectService`。 |
| `MainWindow.xaml.cs` | 主窗口 | 核心控制器，依赖注入分发，订阅 `MessageBroker` 频道，协调各子控件的跨模块联动。含音频自动加载与路径存盘功能。 |
| `NoteListControl.xaml.cs` | 音符列表 UI | `BuildFullNoteTree()`, `RefreshNoteList()` 利用纯数学雷达过滤并展示音符。 |
| `PropertyPanelControl.xaml.cs` | 侧边属性速览 UI | `SetSelectedObject()`, `BuildSpriteForm()` 等根据不同类型展示摘要。点击"编辑"按钮触发 `RequestOpenPropertyEditor`。 |
| `TimelineControl.xaml.cs` | 主时间轴控制器 | `LoadStoryboardTimeline()`, `DrawNoteRuler()`, `BtnAutoLayout_Click()`, `OnTimelineMouseWheel()` 缩放联动。已通过 DI 注入 `IStoryboardRepository`、`IPropertyEditorService`、`GlobalRenderEngine`。 |

### 24. `Views/PropertyEditor` (高级属性编辑器子控件)

| 文件名 | 核心类/功能 | 主要方法/说明 |
| :--- | :--- | :--- |
| `PropertyEditorControls.cs` | 定制化 UI 组件 | `BoundedSliderControl`(滑块), `SingleColorPickerControl`(调色盘), `EasingPickerControl`(缓动矩阵), `NoteSelectorBuilderControl`(音符雷达) 等高级自定义控件。 |
| `PropertyEditorWindow.xaml.cs` | 主编辑器窗口 | 加载 `MainObject`，管理控制板 `ControlBoards` 列表，处理 `OnFrameSelected` 和模板编辑模式。 |
| `PropertyEditor_ControlBoardTabs.xaml.cs` | 控制板多标签页 | `Init()`, `RefreshTabs()` 管理主体与控制板之间的切换。 |
| `PropertyEditor_FrameDetails.xaml.cs` | 右侧详情页 | 解析 `ObjectState` 并动态生成 `BuildDynamicPanel()`，支持属性动态增删。已通过 DI 注入 `IPropertyEditorService`。 |
| `PropertyEditor_FrameList.xaml.cs` | 左侧关键帧列表 | `LoadData()`, `BtnAddFrame_Click()` 添加并克隆关键帧。 |
| `PropertyEditor_Identity.xaml.cs` | 对象身份信息 | 验证 `Id` 冲突，提供 `ParentId` 和 `TargetId` 绑定菜单。 |

### 25. `Views/Services` (WPF 服务)

| 文件名 | 核心类/功能 | 主要方法/说明 |
| :--- | :--- | :--- |
| `EditorResourceManager.cs` | 资源大管家 | `GetNoteIcon()` 缓存和加载音符图标资源。已改为 DI 管理。 |
| `WpfDialogService.cs` | WPF 对话框实现 | 实现 `IDialogService`，包装 `MessageBox`, `OpenFileDialog`, `SaveFileDialog`。 |

### 26. `Views/TimelineClip` (微观时间轴与具体方块)

| 文件名 | 核心类/功能 | 主要方法/说明 |
| :--- | :--- | :--- |
| `ClipDetailedEditor.xaml.cs` | 微观百叶窗编辑器 | `LoadClipData()` 加载属性轨道，处理 `MouseWheel` 缩放和 `PanCaptureLayer` 平移。 |
| `ClipPropertyTrackRow.xaml.cs` | 单属性关键帧行 | `Init()` 渲染 `Thumb` 小菱形，支持 `DragDelta` 拖拽修改，右键复制粘贴状态。 |
| `TimelineClipControl.xaml.cs` | 单个事件方块 | `Init()` 配置 `StartTime` 和 `EndTime` 物理像素位置，支持 `Resize` 拉伸和 `MacroDragStage` 换轨移动。已通过参数注入 `NoteVisualEngine`。 |

### 27. `Themes` & `App` (主题与入口)

| 文件名 | 核心类/功能 | 主要方法/说明 |
| :--- | :--- | :--- |
| `DarkTheme.xaml` / `LightTheme.xaml` | 暗/亮主题资源 | 定义全局 UI 颜色、背景、边框颜色资源。 |
| `Styles.xaml` | 全局控件样式 | 重写 `Button`, `ComboBox`, `Menu`, `ScrollBar` 等默认样式。 |
| `TimelineStyles.xaml` | 时间轴专用样式 | 时间轴控件的拖拽缩略图、轨道等样式。 |
| `App.xaml.cs` | 应用程序入口 | 启动 `ProjectHubWindow`，处理 `--watch` 哨兵进程，拦截未捕获异常。 |
| `AppServices.cs` | 依赖注入容器 | 使用 `Microsoft.Extensions.DependencyInjection` 构建 Ioc 容器，注册所有服务（含 `GlobalRenderEngine`、`NoteVisualEngine`、`EditorResourceManager` 等）。 |

### 28. `Resources` (静态资源)

| 路径 | 说明 |
| :--- | :--- |
| `Resources/NoteSkins/Cytus2_Default/Notes/` | 音符皮肤图片资源（Click, Drag, Hold, Flick 等） |

### 29. `tutorial` (教程与参考文档)

| 文件名 | 说明 |
| :--- | :--- |
| `错误修复和整合.md` | 本次功能错误修复指南（加载顺序修正与音频闭环） |
| `Cytoid_StoryboardModel.cs` | Cytoid 故事板模型参考代码 |
| `Cytus2谱面格式详解.md` | Cytus II 谱面格式说明 |
| `Storyboard说明.md` | 故事板功能说明 |
| `Easings.cs` | 缓动函数参考代码 |
| `FontManager.cs` | 字体管理器参考代码 |
| `easing.txt` | 缓动函数列表 |

---

## 三、架构要点

### 依赖注入体系
- 所有服务通过 `AppServices.cs` 注册到 `Microsoft.Extensions.DependencyInjection` 容器
- View 层通过构造函数注入获取 Core 层服务，杜绝 `AppServices.GetService` 静态定位器
- 已移除所有静态单例模式（`GlobalRenderEngine.Instance`、`AudioSyncEngine.Instance`、`NoteVisualEngine`、`EditorResourceManager`）

### 加载顺序约束
- `AppCommands.DoLoadProject` 强制执行：**先谱面(Chart) → 后故事板(Storyboard)**
- 无谱面时，故事板加载被拦截并弹窗报错
- `EventListControl.ExecuteImportStoryboard` 同样加固谱面前置检查

### 消息通信
- 跨模块通信通过 `IMessageBroker` 的 `Subscribe`/`Publish` 机制实现
- 主要频道：`ProjectLoaded`、`ChartImported`、`DataModified`、`RefreshTimeline`、`CreateEventFromAsset`、`RequestOpenPropertyEditor`、`RequestImportChart`、`RequestRefreshAssets`

### 关键数据流
```
.nep 文件 → AppCommands.DoLoadProject → SilentImportChart (谱面)
                                       → LoadProjectStoryboard (故事板，依赖谱面)
                                       → Publish("ProjectLoaded") → UI 刷新
音频     → MainWindow.MenuImportAudio_Click → AudioEngine.LoadAudioAsync
                                            → 自动回写路径到 .nep
```