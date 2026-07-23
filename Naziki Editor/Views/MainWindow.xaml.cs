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
        private bool _isVisualDirty = false;

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
                        _dialogService.ShowMessage("音频文件路径已自动同步保存至工程文件 (.nep)！", "路径同步成功");
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
            IErrorHandler errorHandler)
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

            Context = new ProjectDataContext(_messageBroker);

            InitializeComponent();

            // =========================================================
            // 🌟 依赖注入分发：将依赖注入到 XAML 创建的子控件
            // =========================================================
            InitializeChildControls();

            // =========================================================
            // 🎛️ 菜单命令路由注册：把菜单入口统一挂到命令调度器
            // =========================================================
            RegisterMenuCommands();

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
                    _dialogService.ShowMessage("属性修改已成功应用并同步至源代码！(๑•̀ㅂ•́)و✧", "应用成功");
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
                    _dialogService.ShowMessage($"素材制造成功！(≧∇≦)ﾉ\n已安全存入沙盒：\n{fileName}", "纯净资产封装完成");

                    RefreshAllAssets();
                }
                catch (Exception ex) { _dialogService.ShowMessage($"胶囊压制失败 QAQ：{ex.Message}"); }
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
            _commandDispatcher.Register("Undo", ExecuteGlobalUndo);
            _commandDispatcher.Register("Redo", ExecuteGlobalRedo);
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
            _commandDispatcher.Register("ImportChart", async () => await _appCommands.DoImportChart(Context));
            _commandDispatcher.Register("ImportStoryboard", DoImportStoryboard);
            _commandDispatcher.Register("About", DoAbout);
            _commandDispatcher.Register("Exit", DoExit);
        }

        // =========================================================
        // 🌟 依赖注入分发：向 XAML 创建的子控件注入依赖
        // =========================================================
        private void InitializeChildControls()
        {
            TimelineConsole.Initialize(_audioEngine, _messageBroker, _dialogService, _noteVisualEngine, _storyboardRepository, _propertyEditorService, _renderEngine);
            // 粮草押运官给 EventList 投喂服务！
            EventList.InitDependencies(_messageBroker, _dialogService, _projectService, _storyboardRepository);
        }

        // ==========================================
        // 🚨 全局快捷键监听：Ctrl+Z / Ctrl+Y 的撤销重做逻辑
        // ==========================================
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (e.Key == Key.Z)
                {
                    if (CanvasArea != null && CanvasArea.JsonEditor != null && CanvasArea.JsonEditor.IsKeyboardFocusWithin)
                        return;

                    e.Handled = true;
                    ExecuteGlobalUndo();
                }
                else if (e.Key == Key.Y)
                {
                    if (CanvasArea != null && CanvasArea.JsonEditor != null && CanvasArea.JsonEditor.IsKeyboardFocusWithin)
                        return;

                    e.Handled = true;
                    ExecuteGlobalRedo();
                }
            }
        }

        private void MenuUndo_Click(object sender, RoutedEventArgs e) => _commandDispatcher.Execute("Undo");
        private void MenuRedo_Click(object sender, RoutedEventArgs e) => _commandDispatcher.Execute("Redo");

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
                _dialogService.ShowMessage("已经没有更古老的修改痕迹可以撤回啦~", "时空尽头");
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
                _dialogService.ShowMessage("设计师，这已经是当前宇宙最前沿的最新数据啦！", "时空尽头");
            }
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
                    _dialogService.ShowMessage($"自动联姻成功！已完美挂钩 {linkedCount} 个音符事件！", "配对成功");

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