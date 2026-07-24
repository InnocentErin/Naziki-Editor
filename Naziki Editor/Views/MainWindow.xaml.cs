using Naziki_Editor.Core;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.ErrorHandling;
using Naziki_Editor.Core.Services;
using Naziki_Editor.Core.Commands;
using Naziki_Editor.Core.Editor;
using Naziki_Editor.Core.History;
using Naziki_Editor.Core.Messaging;
using Naziki_Editor.Core.Project;
using Naziki_Editor.Core.Storyboard;
using Naziki_Editor.Core.Storyboard.Compilation;
using Naziki_Editor.Core.Workspace;
using Naziki_Editor.Core.Animation;
using Naziki_Editor.Core.Notifications;
using Naziki_Editor.Core.Shortcuts;
using Naziki_Editor.Views.Notifications;
using Naziki_Editor.Models;
using Naziki_Editor.State;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Naziki_Editor.Views
{
    public partial class MainWindow : Window
    {
        // ==========================================
        // 📦 ✨ 核心重构：全局万能数据包来啦！
        // ==========================================
        public ProjectDataContext Context { get; private set; }

        private HashSet<C2Note> _selectedNotes = new HashSet<C2Note>();

        // 兼容原有的公开属性（直接指向 Context）
        public string CurrentProjectFilePath => Context.ProjectFilePath;
        public NazikiProjectModel CurrentProjectData => Context.ProjectData;

        private readonly IHistoryService _historyService;
        private readonly IEntityFactory _entityFactory;
        private readonly IProjectService _projectService;
        private readonly IStoryboardRepository _storyboardRepository;
        private readonly IWorkspaceService _workspaceService;
        private readonly ICommandDispatcher _commandDispatcher;
        private readonly IEditorCoordinator _editorCoordinator;
        private readonly IMessageBroker _messageBroker;
        private readonly ICompilationService _compilationService;
        private readonly IDialogService _dialogService;
        private readonly IAudioSyncEngine _audioEngine;
        private readonly AppCommands _appCommands;
        private readonly UI.Rendering.NoteVisualEngine _noteVisualEngine;
        private readonly IPropertyEditorService _propertyEditorService;
        private readonly UI.Rendering.GlobalRenderEngine _renderEngine;
        private readonly IErrorHandler _errorHandler;
        private readonly INotificationService _notificationService;
        private readonly IShortcutManager _shortcutManager;
        private ShortcutContext _activeContext = ShortcutContext.Global;
        private bool _isVisualDirty = false;

        // ==========================================
        // 🎯 焦点 → 快捷键上下文映射表（控件未实现 IShortcutAware 时的回退方案）
        // ==========================================
        private readonly Dictionary<string, ShortcutContext> _focusContextMap = new()
        {
            { "EventList", ShortcutContext.EventList },
            { "NoteList", ShortcutContext.NoteList },
            { "AssetList", ShortcutContext.AssetList },
            { "TimelineConsole", ShortcutContext.Timeline },
            { "CanvasArea", ShortcutContext.Canvas },
            { "PropertyPanel", ShortcutContext.PropertyPanel }
        };

        // ==========================================
        // 📥 顶部菜单栏：共享职能的音频导入法术
        // ==========================================
        private async void MenuImportAudio_Click(object sender, RoutedEventArgs e)
        {
            _errorHandler.TryExecute(() =>
            {
                string? audioFile = _dialogService.ShowOpenFileDialog("从菜单栏选择关卡音乐", "音频文件 (*.mp3;*.wav;*.ogg)|*.mp3;*.wav;*.ogg");

                if (audioFile != null)
                {
                    _ = _audioEngine.LoadAudioAsync(audioFile);

                    // ✨ 【新增】：音频加载成功后，自动将路径写回项目数据并存盘！
                    if (Context.ProjectData != null && Context.ProjectFilePath != null)
                    {
                        Context.ProjectData.AudioFilePath = audioFile;
                        SaveProjectNepFile();
                        _notificationService.ShowSuccess("音频文件路径已自动同步保存至工程文件 (.nep)！");
                    }
                }
            }, "UserInteraction", "MainWindow.MenuImportAudio_Click");
        }

        // ==========================================
        // 💾 核心加装：.nep 工程物理存盘记账引擎
        // ==========================================
        private void SaveProjectNepFile()
        {
            _errorHandler.TryExecute(() =>
            {
                _projectService.SaveProjectNepFile(Context, Context.ProjectFilePath);
            }, "FileIO", "MainWindow.SaveProjectNepFile",
                $"FilePath: {Context.ProjectFilePath}");
        }

        public void RefreshAllAssets()
        {
            if (string.IsNullOrEmpty(Context.ProjectFilePath) || Context.ProjectData == null) return;
            string projectDir = System.IO.Path.GetDirectoryName(Context.ProjectFilePath);
            string matFolder = Context.ProjectData.MaterialFolderPath;
            var bundle = AssetScanner.ScanProjectAssets(projectDir, matFolder);
            AssetList.RefreshAssetListUI(bundle);
        }

        // ==========================================
        // 📂 打开 .nep 核心工程文件！
        // ==========================================
        private void MenuOpenProject_Click(object sender, RoutedEventArgs e) => _commandDispatcher.Execute("OpenProject");






        private void RefreshAllUIAfterProjectLoad()
        {
            EventList.LoadStoryboardUI();
            CanvasArea.TrackSelectedObject(null);
            CanvasArea.RefreshJsonView();
            _isVisualDirty = false;

            if (Context.HasChart)
            {
                NoteList.BuildFullNoteTree();
                if (Context.Chart.note_list.Count > 0)
                    NoteList._maxChartTime = Context.TimeEngine.TickToSeconds(Context.Chart.note_list.Max(n => n.tick));
                NoteList.RefreshNoteList();
                EventList.UpdateChartLockState(Context.HasChart);
            }

            RefreshAllAssets();
            if (Context.HasStoryboard)
            {
                TimelineConsole.LoadStoryboardTimeline(Context);
            }

            // ✨ 【新增】：项目加载完毕后，自动检查并加载音频
            if (Context.ProjectData != null &&
                !string.IsNullOrEmpty(Context.ProjectData.AudioFilePath) &&
                System.IO.File.Exists(Context.ProjectData.AudioFilePath))
            {
                _ = _audioEngine.LoadAudioAsync(Context.ProjectData.AudioFilePath);
            }
        }

        public MainWindow(
            IHistoryService historyService,
            IEntityFactory entityFactory,
            IProjectService projectService,
            IStoryboardRepository storyboardRepository,
            IWorkspaceService workspaceService,
            ICommandDispatcher commandDispatcher,
            IEditorCoordinator editorCoordinator,
            IMessageBroker messageBroker,
            ICompilationService compilationService,
            IDialogService dialogService,
            IAudioSyncEngine audioEngine,
            AppCommands appCommands,
            UI.Rendering.NoteVisualEngine noteVisualEngine,
            IPropertyEditorService propertyEditorService,
            UI.Rendering.GlobalRenderEngine renderEngine,
            IErrorHandler errorHandler,
            INotificationService notificationService,
            IShortcutManager shortcutManager)
        {
            _historyService = historyService;
            _entityFactory = entityFactory;
            _projectService = projectService;
            _storyboardRepository = storyboardRepository;
            _workspaceService = workspaceService;
            _commandDispatcher = commandDispatcher;
            _editorCoordinator = editorCoordinator;
            _messageBroker = messageBroker;
            _compilationService = compilationService;
            _dialogService = dialogService;
            _audioEngine = audioEngine;
            _appCommands = appCommands;
            _noteVisualEngine = noteVisualEngine;
            _propertyEditorService = propertyEditorService;
            _renderEngine = renderEngine;
            _errorHandler = errorHandler;
            _notificationService = notificationService;
            _shortcutManager = shortcutManager;

            Context = new ProjectDataContext(_messageBroker);

            InitializeComponent();

            // =========================================================
            // 🔔 通知气泡叠加层：覆盖整个窗口，始终在右下角显示
            // =========================================================
            InitializeNotificationOverlay();

            // =========================================================
            // 🌟 依赖注入分发：将依赖注入到 XAML 创建的子控件
            // =========================================================
            InitializeChildControls();

            // =========================================================
            // 🎛️ 菜单命令路由注册：把菜单入口统一挂到命令调度器
            // =========================================================
            RegisterMenuCommands();

            // =========================================================
            // ⌨️ 快捷键系统初始化：注册默认快捷键 + 焦点上下文跟踪
            // =========================================================
            InitializeShortcuts();
            this.AddHandler(Keyboard.GotKeyboardFocusEvent,
                (KeyboardFocusChangedEventHandler)OnGlobalFocusChanged);

            // =========================================================
            // 🎧 校园广播站：MainWindow 专属对讲机耳机接线处 (解耦核心)
            // =========================================================

            // 频道 0：听候"项目加载完成"广播，自动刷新全局 UI
            _messageBroker.Subscribe("ProjectLoaded", () =>
            {
                this.Title = $"Naziki Editor - {Context.ProjectData?.ProjectName} [{Context.ProjectFilePath}]";
                RefreshAllUIAfterProjectLoad();
            });

            // 频道 1：听候“素材库小弟”的召唤，有新素材双击时，主窗口自动接单干活！
            _messageBroker.Subscribe<IStoryboardEntity>("CreateEventFromAsset", (newEvent) =>
            {
                this.CreateNewEventFromAsset(newEvent);
            });

            // 频道 2：听候“属性面板小弟”的召唤，需要打开高级属性表单时，主窗口来施法弹窗！
            _messageBroker.Subscribe<object>("RequestOpenPropertyEditor", (obj) =>
            {
                if (obj is IStoryboardEntity selectedObj)
                {
                    this.OpenPropertyEditor(selectedObj);
                }
                else if (obj is C2Template template)
                {
                    // 自动反查模板的名字钥匙
                    string templateKey = _storyboardRepository.GetTemplateKey(this.Context.Storyboard, template);
                    if (!string.IsNullOrEmpty(templateKey))
                    {
                        this.OpenTemplatePropertyEditor(templateKey, template);
                    }
                }
            });

            // 频道 3：听候“谱面缺失结界”里的按钮召唤，当点击“导入谱面”时，跨空触发导入！
            _messageBroker.Subscribe("RequestImportChart", () =>
            {
                // 🌟 完美挂钩：这里直接呼叫你原本顶部菜单栏里的那个导入谱面方法
                // 如果你的导入谱面方法叫 MenuImportChart_Click，直接这样模拟点击即可：
                MenuImportChart_Click(null, null);
            });

            // 频道 4：听候"素材库小弟"的召唤，需要全面刷新硬盘素材时执行！
            _messageBroker.Subscribe("RequestRefreshAssets", () =>
            {
                this.RefreshAllAssets();
            });

            // 频道 5：听候"时间轴"或"微观编辑器"的召唤，需要全面刷新时间轴时执行！
            _messageBroker.Subscribe("RefreshTimeline", () =>
            {
                TimelineConsole.LoadStoryboardTimeline(Context);
            });







            // 🔌 ✨ 终极通电！主窗口一启动，就把数据包分发给所有小弟！
            EventList.LoadContext(Context);
            NoteList.LoadContext(Context);
            CanvasArea.LoadContext(Context);
            PropertyPanel.LoadContext(Context);

            // ==========================================
            // 让主窗口订阅 Context 的数据修改广播！
            // ==========================================
            _messageBroker.Subscribe("DataModified", () =>
            {
                // 标记视觉画面变脏（需要保存）
                _isVisualDirty = true;

                // 如果 JSON 编辑器那边没有未应用的冲突代码，我们就自动刷新画面！
                if (!CanvasArea.HasUnappliedChanges)
                {
                    CanvasArea.RefreshJsonView();
                    _isVisualDirty = false;
                }
            });

            // ==========================================
            // 让主窗口订阅谱面导入完成广播 (来自 AppCommands)
            // ==========================================
            _messageBroker.Subscribe("ChartImported", () =>
            {
                NoteList.BuildFullNoteTree();
                if (Context.Chart != null && Context.Chart.note_list.Count > 0)
                    NoteList._maxChartTime = Context.TimeEngine.TickToSeconds(Context.Chart.note_list.Max(n => n.tick));
                NoteList.RefreshNoteList();
                EventList.UpdateChartLockState(Context.HasChart);
                TimelineConsole.DrawNoteRuler();
                TriggerAutoLinkIfReady();
            });

            // ==========================================
            // 公开事件订阅：主窗口直接订阅小弟们的事件，来实现跨模块通信！
            // ==========================================
            EventList.OnAssetScanned += (bundle) => AssetList.RefreshAssetListUI(bundle);

            // 🎵【核心补漏】：音符数据一键转化为真实音符控制器事件！
            NoteList.OnNotesImportRequested += (selectedNotes) =>
            {
                if (ResolveDataConflictIfNeeded())
                {
                    // 1. 🕒 时光机抢先记账拍快照
                    _historyService.RecordSnapshot(Context.Storyboard);
                    _isVisualDirty = true;

                    // 2. 🏃 循环开始！为每一个选中的音符，在内存中原地捏出真实的物理对象
                    foreach (var note in selectedNotes)
                    {
                        // 🧱 实例化故事板官方认可的音符控制器包装盒
                        var noteCtrl = _entityFactory.CreateNoteController(note);

                        // 📥 正式编入故事板的核心全量军队中！
                        _storyboardRepository.Add(Context.Storyboard, noteCtrl);
                    }

                    // 3. 🔄 【见证奇迹】：命令左侧事件列表根据最新的核心账本，全量重新刷新粉刷 UI！
                    EventList.LoadStoryboardUI();

                    // 4. 📢 惊醒大宇宙，标记工程变脏
                    Context.MarkAsModified();
                    TimelineConsole.LoadStoryboardTimeline(Context); // ✨ 补在这里！
                }
            };

            EventList.OnStoryboardLoaded += (path, root) =>
            {
                Context.StoryboardPath = path;
                Context.Storyboard = root;

                CanvasArea.TrackSelectedObject(null);
                CanvasArea.RefreshJsonView();
                _isVisualDirty = false;

                if (Context.ProjectData != null)
                {
                    Context.ProjectData.StoryboardExportPath = path;
                    SaveProjectNepFile();
                }
                _historyService.Reset();
                _historyService.RecordSnapshot(Context.Storyboard);

                TriggerAutoLinkIfReady();
                // ✨ 宇宙苏醒，通知时间轴画板开工！
                TimelineConsole.LoadStoryboardTimeline(Context);
            };

            PropertyPanel.OnApplyPropertiesRequested += () =>
            {
                if (ResolveDataConflictIfNeeded())
                {
                    _historyService.RecordSnapshot(Context.Storyboard);
                    CanvasArea.RefreshJsonView();
                    _isVisualDirty = false;
                    _notificationService.ShowSuccess("属性修改已成功应用并同步至源代码！(๑•̀ㅂ•́)و✧");
                }
            };

            // ✨ 核心修正：全面洗牌存为素材的闭包，适配所有全新的 C2 包装实体系列！
            PropertyPanel.OnSaveAsMaterialRequested += (obj) =>
            {
                if (string.IsNullOrEmpty(Context.ProjectFilePath) || Context.ProjectData == null) return;

                string matType = "";
                if (obj is C2Sprite) matType = "Image";
                else if (obj is C2Text) matType = "Text";
                else if (obj is C2Line) matType = "Line";
                else if (obj is C2Video) matType = "Video";
                else if (obj is C2SceneController || obj is C2NoteController) matType = "Scene";

                if (string.IsNullOrEmpty(matType) || obj is not IStoryboardEntity entity) return;

                try
                {
                    string filePath = _projectService.SaveAssetCapsule(Context, entity, matType);
                    string fileName = Path.GetFileName(filePath);
                    _notificationService.ShowSuccess($"素材制造成功！(≧∇≦)ﾉ\n已安全存入沙盒：\n{fileName}");

                    RefreshAllAssets();
                }
                catch (Exception ex) { _dialogService.ShowErrorDialog($"胶囊压制失败 QAQ：{ex.Message}", "胶囊压制失败", ex.ToString()); }
            };

            EventList.OnAddTextRequested += AddNewTextEvent;
            EventList.OnAddLineRequested += AddNewLineEvent;
            EventList.OnAddSceneRequested += AddNewSceneControllerEvent;
            EventList.OnAddTemplateRequested += AddNewTemplateEvent;

            CanvasArea.OnBeforeActionCheckConflict = () => ResolveDataConflictIfNeeded();

            CanvasArea.OnApplyJsonSuccess += (newRoot) =>
            {
                _historyService.RecordSnapshot(Context.Storyboard);
                Context.Storyboard = newRoot;
                EventList.LoadStoryboardUI();
                _isVisualDirty = false;
            };

            EventList.OnEventNodeSelected += (obj) =>
            {
                PropertyPanel.SetSelectedObject(obj);
                CanvasArea.TrackSelectedObject(obj);
            };


            // 🌟 1. 监听时间轴的【普通单击】：联动右侧属性面板和中间的代码高亮！
            TimelineConsole.OnTimelineObjectSelected += (obj) =>
            {
                PropertyPanel.SetSelectedObject(obj);
                CanvasArea.TrackSelectedObject(obj);
            };

            // 🚀 2. 监听时间轴的【Ctrl + 单击】：直接召唤高级属性编辑弹窗！
            TimelineConsole.OnTimelineRequestPropertyEditor += (obj) =>
            {
                if (obj is Models.IStoryboardEntity entity)
                {
                    OpenPropertyEditor(entity);
                }
            };




        }

        // =========================================================
        // 🎛️ 菜单命令路由注册中心
        // =========================================================
        private void RegisterMenuCommands()
        {
            // ==========================================
            // 📂 文件操作
            // ==========================================
            _commandDispatcher.Register("NewProject", () =>
            {
                if (!ResolveDataConflictIfNeeded()) return;
                _notificationService.Show("新建项目功能将在后续版本中通过项目中心实现。\n当前请先关闭并重新启动应用以创建新项目。", NotificationType.Info);
            });
            _commandDispatcher.Register("OpenProject", async () =>
            {
                if (!ResolveDataConflictIfNeeded()) return;
                await _appCommands.DoOpenProject(ctx => _appCommands.DoLoadProject(ctx.ProjectFilePath, ctx.ProjectData, Context));
            });
            _commandDispatcher.Register("SaveProject", async () =>
            {
                if (!ResolveDataConflictIfNeeded()) return;
                await _appCommands.DoSaveProject(Context);
                _isVisualDirty = false;
                CanvasArea.RefreshJsonView();
            });
            _commandDispatcher.Register("SaveProjectAs", async () =>
            {
                if (!ResolveDataConflictIfNeeded()) return;
                // 强制另存为：清空当前路径，触发 DoSaveProject 的文件选择对话框
                string? originalPath = Context.StoryboardPath;
                try
                {
                    Context.StoryboardPath = null;
                    await _appCommands.DoSaveProject(Context);
                    _isVisualDirty = false;
                    CanvasArea.RefreshJsonView();
                }
                catch
                {
                    Context.StoryboardPath = originalPath; // 恢复原路径
                }
            });

            // ==========================================
            // ✏️ 编辑操作
            // ==========================================
            _commandDispatcher.Register("Undo", ExecuteGlobalUndo);
            _commandDispatcher.Register("Redo", ExecuteGlobalRedo);

            // ⌨️ 全选：根据当前焦点上下文分发
            _commandDispatcher.Register("SelectAll", () =>
            {
                switch (_activeContext)
                {
                    case ShortcutContext.EventList:
                        _notificationService.Show("请在事件列表中使用 Ctrl+A 全选项目。", NotificationType.Info);
                        break;
                    case ShortcutContext.AssetList:
                        _notificationService.Show("请在素材库中使用 Ctrl+A 全选素材。", NotificationType.Info);
                        break;
                    case ShortcutContext.NoteList:
                        _notificationService.Show("请在音符列表中使用 Ctrl+A 全选音符。", NotificationType.Info);
                        break;
                    default:
                        break;
                }
            });

            // ⌨️ 复制选中对象：在当前上下文中复制
            _commandDispatcher.Register("DuplicateSelected", () =>
            {
                if (!Context.HasStoryboard) { _notificationService.ShowWarning("请先导入故事板文件！"); return; }
                _historyService.RecordSnapshot(Context.Storyboard);
                _notificationService.ShowSuccess("已复制选中对象。(功能完善中)");
            });

            // ⌨️ 快捷键系统专属命令：删除选中对象
            _commandDispatcher.Register("DeleteSelected", () => ExecuteDeleteSelected());

            // ⌨️ 快捷键系统专属命令：素材库操作
            _commandDispatcher.Register("CopyAsset", () => AssetList.ExecuteCopy());
            _commandDispatcher.Register("PasteAsset", () => AssetList.ExecutePaste());
            _commandDispatcher.Register("RenameAsset", () =>
            {
                _notificationService.Show("请在素材库中右键点击素材，选择「重命名素材」来编辑名称。", NotificationType.Info);
            });

            // ==========================================
            // 🎬 导入操作
            // ==========================================
            _commandDispatcher.Register("ImportChart", async () => await _appCommands.DoImportChart(Context));
            _commandDispatcher.Register("ImportStoryboard", DoImportStoryboard);
            _commandDispatcher.Register("ImportAudio", () =>
            {
                _errorHandler.TryExecute(() =>
                {
                    string? audioFile = _dialogService.ShowOpenFileDialog("选择关卡音乐", "音频文件 (*.mp3;*.wav;*.ogg)|*.mp3;*.wav;*.ogg");
                    if (audioFile != null)
                    {
                        _ = _audioEngine.LoadAudioAsync(audioFile);
                        if (Context.ProjectData != null && Context.ProjectFilePath != null)
                        {
                            Context.ProjectData.AudioFilePath = audioFile;
                            SaveProjectNepFile();
                            _notificationService.ShowSuccess("音频文件路径已自动同步保存至工程文件 (.nep)！");
                        }
                    }
                }, "UserInteraction", "MainWindow.ImportAudio");
            });

            // ==========================================
            // 🎛️ 工具操作
            // ==========================================
            _commandDispatcher.Register("OpenPropertyEditor", () =>
            {
                // 获取属性面板当前跟踪的选中对象（通过 EventList/Timeline 事件同步）
                var selectedObj = PropertyPanel.GetSelectedObject();
                if (selectedObj is IStoryboardEntity entity)
                {
                    OpenPropertyEditor(entity);
                }
                else
                {
                    _notificationService.Show("请先在事件列表或时间轴中选中一个对象，再使用 Ctrl+E 打开属性编辑器。", NotificationType.Info);
                }
            });

            _commandDispatcher.Register("AddNewText", () =>
            {
                if (!Context.HasStoryboard) { _notificationService.ShowWarning("请先导入故事板文件！"); return; }
                AddNewTextEvent();
            });
            _commandDispatcher.Register("AddNewLine", () =>
            {
                if (!Context.HasStoryboard) { _notificationService.ShowWarning("请先导入故事板文件！"); return; }
                AddNewLineEvent();
            });
            _commandDispatcher.Register("AddNewSceneController", () =>
            {
                if (!Context.HasStoryboard) { _notificationService.ShowWarning("请先导入故事板文件！"); return; }
                AddNewSceneControllerEvent();
            });

            // ==========================================
            // ▶️ 播放控制（由 TimelineControl 暴露）
            // ==========================================
            _commandDispatcher.Register("TimelinePlayPause", () => TimelineConsole.TogglePlayPause());
            _commandDispatcher.Register("TimelineGoToStart", () => TimelineConsole.GoToStart());
            _commandDispatcher.Register("TimelineGoToEnd", () => TimelineConsole.GoToEnd());

            // ==========================================
            // 🧭 缩放控制（由 TimelineControl 暴露）
            // ==========================================
            _commandDispatcher.Register("TimelineZoomIn", () => TimelineConsole.ZoomIn());
            _commandDispatcher.Register("TimelineZoomOut", () => TimelineConsole.ZoomOut());
            _commandDispatcher.Register("TimelineZoomReset", () => TimelineConsole.ResetZoom());

            // ==========================================
            // 🖥️ 视图与系统操作
            // ==========================================
            _commandDispatcher.Register("RefreshView", () =>
            {
                CanvasArea.RefreshJsonView();
                EventList.LoadStoryboardUI();
                TimelineConsole.LoadStoryboardTimeline(Context);
                RefreshAllAssets();
                _notificationService.ShowSuccess("视图已刷新！(๑•̀ㅂ•́)و✧");
            });
            _commandDispatcher.Register("ToggleFullScreen", () =>
            {
                if (WindowStyle == WindowStyle.None)
                {
                    // 退出全屏
                    WindowStyle = WindowStyle.SingleBorderWindow;
                    WindowState = WindowState.Normal;
                    _notificationService.ShowSuccess("已退出全屏模式。");
                }
                else
                {
                    // 进入全屏
                    WindowStyle = WindowStyle.None;
                    WindowState = WindowState.Maximized;
                    _notificationService.ShowSuccess("已进入全屏模式。按 F11 退出。");
                }
            });

            // ==========================================
            // 🔍 搜索与帮助
            // ==========================================
            _commandDispatcher.Register("Find", () =>
            {
                _notificationService.Show("搜索功能将在后续版本中实现。\n敬请期待！(๑•̀ㅂ•́)و✧", NotificationType.Info);
            });
            _commandDispatcher.Register("Help", () => DoAbout());

            // ==========================================
            // 🎨 画布缩放操作（Canvas 上下文）
            // ==========================================
            _commandDispatcher.Register("CanvasZoomIn", () =>
            {
                CanvasArea?.ZoomIn();
                _notificationService.Show("画布已放大。", NotificationType.Info);
            });
            _commandDispatcher.Register("CanvasZoomOut", () =>
            {
                CanvasArea?.ZoomOut();
                _notificationService.Show("画布已缩小。", NotificationType.Info);
            });
            _commandDispatcher.Register("CanvasZoomReset", () =>
            {
                CanvasArea?.ResetZoom();
                _notificationService.Show("画布缩放已重置。", NotificationType.Info);
            });

            // ==========================================
            // 🎵 音符列表导航操作（NoteList 上下文）
            // ==========================================
            _commandDispatcher.Register("NoteListNavigateUp", () =>
            {
                NoteList?.NavigateUp();
            });
            _commandDispatcher.Register("NoteListNavigateDown", () =>
            {
                NoteList?.NavigateDown();
            });

            _commandDispatcher.Register("About", DoAbout);
            _commandDispatcher.Register("Exit", DoExit);
        }

        // =========================================================
        // 🌟 依赖注入分发：向 XAML 创建的子控件注入依赖
        // =========================================================
        private void InitializeChildControls()
        {
            TimelineConsole.Initialize(_audioEngine, _messageBroker, _dialogService, _noteVisualEngine, _storyboardRepository, _propertyEditorService, _renderEngine, _notificationService);
            // 粮草押运官给 EventList 投喂服务！
            EventList.InitDependencies(_messageBroker, _dialogService, _projectService, _storyboardRepository, _notificationService);
        }

        // =========================================================
        // 🔔 通知气泡叠加层初始化
        // =========================================================
        private void InitializeNotificationOverlay()
        {
            var overlay = new NotificationOverlay(_notificationService);

            // 设置跨越所有行，始终在最顶层
            Grid.SetRowSpan(overlay, 5);
            Panel.SetZIndex(overlay, 9999);
            overlay.IsHitTestVisible = false;

            // 将叠加层添加到主 Grid 的顶层
            var mainGrid = this.Content as Grid;
            mainGrid?.Children.Add(overlay);
        }

        // ==========================================
        // ⌨️ 全局快捷键监听：委托给统一 ShortcutManager 处理
        // ==========================================
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // 当 JSON 编辑器获得焦点时，不拦截全局快捷键（让 AvalonEdit 自行处理）
            if (CanvasArea?.JsonEditor?.IsKeyboardFocusWithin == true)
                return;

            // 🛡️ 当焦点在文本编辑控件内时，放行标准文本编辑快捷键
            if (IsTextEditingFocused() && IsStandardTextShortcut(e.Key, Keyboard.Modifiers))
                return;

            if (_shortcutManager.HandleKeyDown(e.Key, Keyboard.Modifiers, _activeContext))
            {
                e.Handled = true;
            }
        }

        /// <summary>
        /// 判断当前焦点是否在文本编辑控件中（TextBox, RichTextBox, PasswordBox 等）。
        /// </summary>
        private static bool IsTextEditingFocused()
        {
            var focused = Keyboard.FocusedElement;
            return focused is TextBox || focused is RichTextBox || focused is PasswordBox;
        }

        /// <summary>
        /// 判断是否为标准文本编辑快捷键（Ctrl+C/V/A/Z/Y/X 等）。
        /// 这些快捷键在文本编辑控件中应由控件自身处理，不应被全局快捷键系统拦截。
        /// </summary>
        private static bool IsStandardTextShortcut(Key key, ModifierKeys modifiers)
        {
            if ((modifiers & ModifierKeys.Control) != ModifierKeys.Control)
                return false;

            return key == Key.C || key == Key.V || key == Key.X ||
                   key == Key.Z || key == Key.Y || key == Key.A ||
                   key == Key.F; // Ctrl+F 查找
        }

        private void MenuUndo_Click(object sender, RoutedEventArgs e) => _commandDispatcher.Execute("Undo");
        private void MenuRedo_Click(object sender, RoutedEventArgs e) => _commandDispatcher.Execute("Redo");

        /// <summary>
        /// 全局删除选中对象：根据当前焦点上下文分发到对应控件。
        /// </summary>
        private void ExecuteDeleteSelected()
        {
            // 根据当前焦点上下文决定删除哪个控件中的选中项
            switch (_activeContext)
            {
                case ShortcutContext.AssetList:
                    AssetList.ExecuteDelete();
                    break;
                case ShortcutContext.EventList:
                    EventList.ExecuteDeleteSelected();
                    break;
                case ShortcutContext.Timeline:
                    // 时间轴删除由 TimelineControl 内部处理
                    break;
                default:
                    break;
            }
        }

        private void ExecuteGlobalUndo()
        {
            bool success;
            StoryboardRoot prevState = _historyService.Undo(Context.Storyboard, out success);
            if (success)
            {
                Context.Storyboard = prevState;
                EventList.LoadStoryboardUI();
                CanvasArea.RefreshJsonView();
                _isVisualDirty = false;
                // ✨ 时光倒流，时间轴界面也要跟着穿越！
                TimelineConsole.LoadStoryboardTimeline(Context);
            }
            else
            {
                _notificationService.ShowWarning("已经没有更古老的修改痕迹可以撤回啦~");
            }
        }

        private void ExecuteGlobalRedo()
        {
            bool success;
            StoryboardRoot nextState = _historyService.Redo(Context.Storyboard, out success);
            if (success)
            {
                Context.Storyboard = nextState;
                EventList.LoadStoryboardUI();
                CanvasArea.RefreshJsonView();
                _isVisualDirty = false;
                // ✨ 时光倒流，时间轴界面也要跟着穿越！
                TimelineConsole.LoadStoryboardTimeline(Context);
            }
            else
            {
                _notificationService.ShowWarning("设计师，这已经是当前宇宙最前沿的最新数据啦！");
            }
        }

        // ==========================================
        // ⌨️ 快捷键系统：焦点上下文自动切换
        // ==========================================
        /// <summary>
        /// 全局焦点变化事件处理。当焦点在子控件间切换时，
        /// 自动检测控件类型并更新快捷键上下文。
        /// 优先检查 IShortcutAware 接口，其次使用名称映射。
        /// </summary>
        private void OnGlobalFocusChanged(object sender, KeyboardFocusChangedEventArgs e)
        {
            var focused = e.NewFocus as DependencyObject;

            // 向上遍历可视化树，查找 IShortcutAware 或已知控件
            while (focused != null)
            {
                // 优先：检查是否实现了 IShortcutAware 接口
                if (focused is IShortcutAware aware)
                {
                    _activeContext = aware.ShortcutContext;
                    return;
                }

                // 回退：通过控件名称映射上下文
                if (focused is FrameworkElement fe && !string.IsNullOrEmpty(fe.Name))
                {
                    if (_focusContextMap.TryGetValue(fe.Name, out var ctx))
                    {
                        _activeContext = ctx;
                        return;
                    }
                }

                focused = VisualTreeHelper.GetParent(focused);
            }

            // 未匹配到任何已知上下文，恢复为全局模式
            _activeContext = ShortcutContext.Global;
        }

        // ==========================================
        // ⌨️ 快捷键系统初始化：注册默认快捷键 + 菜单绑定
        // ==========================================
        /// <summary>
        /// 初始化快捷键系统。清除旧绑定，重新注册默认快捷键，
        /// 并自动同步菜单栏的 InputGestureText 显示。
        /// 此方法可被多次调用，支持用户自定义快捷键后的动态重载。
        /// </summary>
        public void InitializeShortcuts()
        {
            _shortcutManager.Clear();
            _shortcutManager.RegisterBatch(DefaultShortcuts.GetAll());
            BindMenuGestureTexts();
        }

        // ==========================================
        // ⌨️ 菜单 InputGestureText 自动绑定
        // ==========================================
        /// <summary>
        /// 扫描所有菜单项，根据快捷键绑定自动设置 InputGestureText。
        /// 使用 MenuItem 的 Name 属性映射到命令名：
        ///   MenuSave → SaveProject, MenuUndo → Undo, 等。
        /// </summary>
        private void BindMenuGestureTexts()
        {
            // 菜单项名称 → 命令名 映射表
            var menuCommandMap = new Dictionary<string, string>
            {
                { "MenuSave", "SaveProject" },
                { "MenuSaveAs", "SaveProjectAs" },
                { "MenuOpen", "OpenProject" },
                { "MenuNew", "NewProject" },
                { "MenuUndo", "Undo" },
                { "MenuRedo", "Redo" },
                { "MenuImportChart", "ImportChart" },
                { "MenuImportStoryboard", "ImportStoryboard" },
                { "MenuImportAudio", "ImportAudio" },
                { "MenuExit", "Exit" },
                { "MenuAbout", "About" },
                { "MenuRefresh", "RefreshView" },
                { "MenuFind", "Find" },
                { "MenuHelp", "Help" }
            };

            // 遍历所有菜单项并设置 InputGestureText
            var menu = FindFirstVisualChild<Menu>(this);
            if (menu == null) return;

            foreach (var menuItem in FindAllMenuItems(menu))
            {
                if (string.IsNullOrEmpty(menuItem.Name)) continue;

                if (menuCommandMap.TryGetValue(menuItem.Name, out var commandName))
                {
                    var binding = _shortcutManager.FindBinding(commandName);
                    if (binding != null)
                    {
                        menuItem.InputGestureText = binding.ToGestureText();
                    }
                }
            }
        }

        /// <summary>
        /// 递归查找 Menu 中所有的 MenuItem（包括嵌套子菜单）。
        /// </summary>
        private static IEnumerable<MenuItem> FindAllMenuItems(ItemsControl parent)
        {
            foreach (var item in parent.Items)
            {
                if (item is MenuItem menuItem)
                {
                    yield return menuItem;
                    foreach (var child in FindAllMenuItems(menuItem))
                        yield return child;
                }
            }
        }

        /// <summary>
        /// 在可视化树中查找第一个指定类型的子元素。
        /// </summary>
        private static T? FindFirstVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed)
                    return typed;

                var result = FindFirstVisualChild<T>(child);
                if (result != null)
                    return result;
            }
            return null;
        }

        private void MenuAbout_Click(object sender, RoutedEventArgs e) => _commandDispatcher.Execute("About");

        private void DoAbout()
        {
            _dialogService.ShowMessage("🛸 Naziki Editor v1.0.0\n\n一款专为 Cytoid 故事板设计师打造的可视化编辑器。\nPowered by Erin & You！\n\n祝您顺利创作出神级故事板分镜~ (★ω★)源", "关于 Naziki Studio");
        }

        public bool ResolveDataConflictIfNeeded()
        {
            if (!_workspaceService.HasConflict(CanvasArea.HasUnappliedChanges, _isVisualDirty))
                return true;

            var result = _dialogService.ShowConfirm(
                "检测到您同时修改了【属性】和【源代码】！请选择保留哪个版本：\n\n[ 是 (Yes) ] —— 保留：a. 源代码\n[ 否 (No) ] —— 保留：b. 事件属性\n[ 取消 ] —— 中止操作",
                "写入保护：数据分歧警告", DialogMessageType.Warning);

            var resolution = result switch
            {
                ConfirmResult.Yes => ConflictResolution.ApplySource,
                ConfirmResult.No => ConflictResolution.RefreshView,
                _ => ConflictResolution.Cancel
            };

            return _workspaceService.ResolveConflict(
                resolution,
                applySource: () =>
                {
                    bool success = CanvasArea.ForceApplyJson();
                    if (success) _isVisualDirty = false;
                    return success;
                },
                refreshView: () =>
                {
                    CanvasArea.RefreshJsonView();
                    _isVisualDirty = false;
                });
        }

        // ==========================================
        // 🎬 导入独立的故事板文件 (.json)
        // ==========================================
        private void MenuImportStoryboard_Click(object sender, RoutedEventArgs e)
        {
            // 🛑 【小艾的物理拦截结界】：如果还没导入谱面，直接拦截弹窗，拒绝执行！
            if (!Context.HasChart)
            {
                _dialogService.ShowMessage("纳尼？必须先导入谱面文件，才能导入故事板哦！(｀•ω•´)ゞ", "逻辑锁拦截", DialogMessageType.Warning);
                return;
            }

            _commandDispatcher.Execute("ImportStoryboard");
        }

        private void DoImportStoryboard()
        {
            if (ResolveDataConflictIfNeeded())
            {
                EventList.ExecuteImportStoryboard();
            }
        }

        private void MenuExit_Click(object sender, RoutedEventArgs e) => _commandDispatcher.Execute("Exit");

        private void DoExit() => Application.Current.Shutdown();
        private void MenuImportChart_Click(object sender, RoutedEventArgs e) => _commandDispatcher.Execute("ImportChart");

        public async void ExecuteImportChart() => await _appCommands.DoImportChart(Context);

        // ✨ 核心修正：将残留的旧工厂创建方法升级，全面适配 IStoryboardEntity 通用接口！
        private void CreateAndInjectObject(IStoryboardEntity obj)
        {
            if (!ResolveDataConflictIfNeeded() || obj == null) return;

            _historyService.RecordSnapshot(Context.Storyboard);

            _storyboardRepository.Add(Context.Storyboard, obj);

            _isVisualDirty = true;
            EventList.LoadStoryboardUI();
            if (!CanvasArea.HasUnappliedChanges)
            {
                CanvasArea.RefreshJsonView();
                _isVisualDirty = false;
            }
        }

        // 🌟 1. 动态添加文本
        private void AddNewTextEvent()
        {
            if (!Context.HasStoryboard) return;

            var text = _entityFactory.CreateText();

            _storyboardRepository.Add(Context.Storyboard, text);
            EventList.LoadStoryboardUI();
            Context.MarkAsModified();
            TimelineConsole.LoadStoryboardTimeline(Context); // ✨ 补在这里！
        }

        // 🌟 2. 动态添加线条
        private void AddNewLineEvent()
        {
            if (!Context.HasStoryboard) return;

            var line = _entityFactory.CreateLine();

            _storyboardRepository.Add(Context.Storyboard, line);
            EventList.LoadStoryboardUI();
            Context.MarkAsModified();
            TimelineConsole.LoadStoryboardTimeline(Context); // ✨ 补在这里！
        }

        // 🌟 3. 动态添加场景控制器
        private void AddNewSceneControllerEvent()
        {
            if (!Context.HasStoryboard) return;

            var controller = _entityFactory.CreateSceneController();

            _storyboardRepository.Add(Context.Storyboard, controller);
            EventList.LoadStoryboardUI();
            Context.MarkAsModified();
            TimelineConsole.LoadStoryboardTimeline(Context); // ✨ 补在这里！
        }

        // =========================================================================
        // 🌟 主窗口接管：新建模板的全局造物法术
        // =========================================================================
        private void AddNewTemplateEvent()
        {
            if (Context == null || !Context.HasStoryboard) return;
            var root = Context.Storyboard;

            // 1. 生成不冲突的初始名字
            string newKey = _entityFactory.GenerateUniqueTemplateKey(root, "generic");

            // 2. 赋予纯净的数据灵魂并登记到仓储
            var newTemplate = _entityFactory.CreateTemplate(newKey);
            _storyboardRepository.AddTemplate(root, newKey, newTemplate);

            // 3. 在大本营的顺位账本上登记造册 (升级为全新的私有元数据包裹)
            if (Context.StoryboardMeta != null)
            {
                if (Context.StoryboardMeta.TemplateMetas == null) Context.StoryboardMeta.TemplateMetas = new Dictionary<string, EditorTemplateMeta>();
                Context.StoryboardMeta.TemplateMetas[newKey] = new EditorTemplateMeta { Type = TemplateType.Generic };
            }

            // 4. 惊醒时光机！让包括时间轴在内的所有全局视图准备刷新！
            Context.MarkAsModified();

            // 5. 刷新左侧 UI（因为数据变了，通知 UI 重新加载）
            EventList.LoadStoryboardUI();

            // 🌟 6. 【极度丝滑交互】：造出来的瞬间，直接弹出属性编辑器，不用打谱师再去双击！
            OpenTemplatePropertyEditor(newKey, newTemplate);
            TimelineConsole.LoadStoryboardTimeline(Context); // ✨ 补在这里！
        }

        private void MenuSave_Click(object sender, RoutedEventArgs e) => _commandDispatcher.Execute("SaveProject");

        private void TriggerAutoLinkIfReady()
        {
            // =========================================================
            // 🎵 谱面与故事板就位后的自动配对检测 (UI层专属魔法)
            // =========================================================
            if (Context.HasChart && Context.HasStoryboard && Context.Storyboard.note_controllers?.Count > 0)
            {
                // 1. 弹出询问框（UI的活，由 MainWindow 亲自来干！）
                if (_dialogService.ShowYesNo(
                    "检测到谱面与故事板均已就位！✨\n是否让故事板的音符控制器与谱面文件自动配对？\n(做出选择前，一定要确定这个故事板是基于你所上传的谱面文件制作的哦！)",
                    "自动配对询问"))
                {
                    // 2. 呼叫刚刚改造好的纯净核心，它只管算数，并吐回配对成功的数量
                    int linkedCount = ChartStoryboardLink.ExecuteAutoLink(Context.Chart, Context.Storyboard);

                    // 3. 刷新界面列表（列表刷新也属于UI的活！）
                    // 先清空原本的列表画板
                    EventList.NoteCtrlListBox.Items.Clear();

                    // 遍历故事板的音符控制器，为它们量身定制 UI 外衣
                    foreach (var ctrl in Context.Storyboard.note_controllers)
                    {
                        if (ctrl.BaseState?.NoteTarget == null) continue;

                        var target = ctrl.BaseState.NoteTarget;

                        // 情况 A：普通的数字 ID
                        if (target is long || target is int || long.TryParse(target.ToString(), out _))
                        {
                            int targetId = Convert.ToInt32(target);
                            var matchedNote = Context.Chart.note_list.FirstOrDefault(n => n.id == targetId);

                            if (matchedNote != null)
                            {
                                var item = new ListBoxItem() { Tag = ctrl };
                                item.SetBinding(ListBoxItem.ContentProperty, new System.Windows.Data.Binding("Id") { Source = ctrl });
                                EventList.NoteCtrlListBox.Items.Add(item);
                            }
                            else
                            {
                                var item = new ListBoxItem() { Content = $"{ctrl.Id} | Note ID: {targetId} (谱面未命中)", Foreground = Brushes.Gray, Tag = ctrl };
                                EventList.NoteCtrlListBox.Items.Add(item);
                            }
                        }
                        // 情况 B：强类型的选择器 JSON 对象
                        else if (target is Newtonsoft.Json.Linq.JObject jobj)
                        {
                            try
                            {
                                var item = new ListBoxItem() { Tag = ctrl, Foreground = Brushes.DarkCyan, FontWeight = FontWeights.Bold };
                                item.SetBinding(ListBoxItem.ContentProperty, new System.Windows.Data.Binding("Id") { Source = ctrl });
                                EventList.NoteCtrlListBox.Items.Add(item);
                            }
                            catch
                            {
                                EventList.NoteCtrlListBox.Items.Add(new ListBoxItem() { Content = $"{ctrl.Id} | 未知选择器", Tag = ctrl });
                            }
                        }
                    }

                    // 最后，呼叫事件列表更新一下底部的空提示文字状态
                    EventList.UpdateEmptyHintVisibility(); // ✨ 完美的直接施法！

                    // 4. 完美收尾，温馨提示
                    _notificationService.ShowSuccess($"自动联姻成功！已完美挂钩 {linkedCount} 个音符事件！");

                    // 别忘了标记数据已被修改，激活时光机存档哦！
                    Context.MarkAsModified();
                }
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;
        private void BtnMaximize_Click(object sender, RoutedEventArgs e) => this.WindowState = (this.WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
        private void BtnClose_Click(object sender, RoutedEventArgs e) { if (ResolveDataConflictIfNeeded()) Application.Current.Shutdown(); }

        public void OpenPropertyEditor(IStoryboardEntity targetObj)
        {
            if (targetObj == null) return;

            var editorWindow = new Naziki_Editor.Views.PropertyEditor.PropertyEditorWindow(targetObj, Context, _dialogService, _storyboardRepository, _propertyEditorService, _messageBroker)
            {
                Owner = this,
                Title = $"属性编辑器 - [修改对象: {targetObj.Id}]"
            };

            if (editorWindow.ShowDialog() == true)
            {
                var modifiedObj = editorWindow.Tag as IStoryboardEntity;
                if (modifiedObj != null)
                {
                    _editorCoordinator.CommitEntityEdit(targetObj, modifiedObj, Context, _storyboardRepository);

                    PropertyPanel.SetSelectedObject(modifiedObj);
                    EventList.LoadStoryboardUI();
                    TimelineConsole.LoadStoryboardTimeline(Context); // ✨ 补在这里！
                }
            }
        }

        public void OpenTemplatePropertyEditor(string templateName, C2Template targetTemplate)
        {
            if (string.IsNullOrEmpty(templateName) || targetTemplate == null) return;

            var editorWindow = new Naziki_Editor.Views.PropertyEditor.PropertyEditorWindow(templateName, targetTemplate, Context, _dialogService, _storyboardRepository, _propertyEditorService, _messageBroker)
            {
                Owner = this,
                Title = $"模板编辑器 - [✨ 调整预设: {templateName}]"
            };

            if (editorWindow.ShowDialog() == true)
            {
                _editorCoordinator.CommitTemplateEdit(Context);

                EventList.LoadStoryboardUI();
                PropertyPanel.SetSelectedObject(null);
                TimelineConsole.LoadStoryboardTimeline(Context); // ✨ 补在这里！
            }
        }

        public void CreateNewEventFromAsset(IStoryboardEntity newObj)
        {
            if (newObj == null || !Context.HasStoryboard) return;

            Naziki_Editor.Views.PropertyEditor.PropertyEditorWindow editor =
                 new Naziki_Editor.Views.PropertyEditor.PropertyEditorWindow(newObj, Context, _dialogService, _storyboardRepository, _propertyEditorService, _messageBroker)
                 {
                     Owner = this,
                     Title = "属性编辑器 - [✨ 导入新素材并设置]"
                 };

            if (editor.ShowDialog() == true)
            {
                IStoryboardEntity modifiedObj = editor.Tag as IStoryboardEntity;
                if (modifiedObj == null) return;

                _editorCoordinator.CommitEntityEdit(null, modifiedObj, Context, _storyboardRepository);

                EventList.LoadStoryboardUI();
                PropertyPanel.SetSelectedObject(modifiedObj);
                TimelineConsole.LoadStoryboardTimeline(Context); // ✨ 补在这里！
            }
        }

    }
}