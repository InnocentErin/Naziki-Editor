using Microsoft.Win32;
using Naziki_Editor.Core;
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
        public ProjectDataContext Context { get; private set; } = new ProjectDataContext();

        private HashSet<C2Note> _selectedNotes = new HashSet<C2Note>();
        private double _maxChartTime = 0;

        // 兼容原有的公开属性（直接指向 Context）
        public string CurrentProjectFilePath => Context.ProjectFilePath;
        public NazikiProjectModel CurrentProjectData => Context.ProjectData;

        private Core.UndoRedoManager _undoRedoManager = new Core.UndoRedoManager();
        private bool _isVisualDirty = false;

        // ==========================================
        // 📥 顶部菜单栏：共享职能的音频导入法术
        // ==========================================
        private async void MenuImportAudio_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "音频文件 (*.mp3;*.wav;*.ogg)|*.mp3;*.wav;*.ogg",
                Title = "从菜单栏选择关卡音乐"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                await Core.AudioSyncEngine.Instance.LoadAudioAsync(openFileDialog.FileName);
            }
        }

        // ==========================================
        // 💾 核心加装：.nep 工程物理存盘记账引擎
        // ==========================================
        private void SaveProjectNepFile()
        {
            if (string.IsNullOrEmpty(Context.ProjectFilePath) || Context.ProjectData == null) return;
            try
            {
                Context.ProjectData.LastModifiedTime = DateTime.Now;
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(Context.ProjectData, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(Context.ProjectFilePath, json);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"物理写入工程配置文件 (.nep) 失败 QAQ：\n{ex.Message}", "工程记账失败");
            }
        }

        public void RefreshAllAssets()
        {
            if (string.IsNullOrEmpty(Context.ProjectFilePath) || Context.ProjectData == null) return;
            string projectDir = System.IO.Path.GetDirectoryName(Context.ProjectFilePath);
            string matFolder = Context.ProjectData.MaterialFolderPath;
            var bundle = Core.AssetScanner.ScanProjectAssets(projectDir, matFolder);
            AssetList.RefreshAssetListUI(bundle);
        }

        // ==========================================
        // 📂 打开 .nep 核心工程文件！
        // ==========================================
        private void MenuOpenProject_Click(object sender, RoutedEventArgs e)
        {
            // 1. 🛡️ 启动保护结界：先检查当前代码画板有没有未保存的冲突
            if (!ResolveDataConflictIfNeeded()) return;

            // 2. 🪄 召唤文件选择魔法阵，专门只抓取 .nep 后缀的工程账本
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Naziki 工程文件 (*.nep)|*.nep",
                Title = "请选择你要打开的工程宇宙"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    // 3. 📖 读取物理文件，并用 Newtonsoft 还原出工程模型基因
                    string jsonText = System.IO.File.ReadAllText(openFileDialog.FileName);
                    var projectData = Newtonsoft.Json.JsonConvert.DeserializeObject<Naziki_Editor.Models.NazikiProjectModel>(jsonText);

                    if (projectData != null)
                    {
                        // 4. 🚀 完美闭环：呼叫主战舰早已备好的港口入城式法术！
                        LoadProject(openFileDialog.FileName, projectData);
                    }
                    else
                    {
                        MessageBox.Show("这个工程文件似乎是个空壳子哦！", "解析失败");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"解析 .nep 工程文件时发生爆炸 QAQ：\n{ex.Message}", "读取错误");
                }
            }
        }






        // ==========================================
        // ⚓ 公开港口入城式：一体化无缝扫描完全体
        // ==========================================
        public void LoadProject(string projectPath, NazikiProjectModel projectData)
        {
            if (projectData == null) return;

            Context.ProjectFilePath = projectPath;
            Context.ProjectData = projectData;

            this.Title = $"Naziki Editor - {projectData.ProjectName} ［{projectPath}］";

            if (!string.IsNullOrEmpty(projectData.StoryboardExportPath) && File.Exists(projectData.StoryboardExportPath))
            {
                try
                {
                    Context.StoryboardPath = projectData.StoryboardExportPath;
                    // ✨ 核心修正：修复找不到 filePath 的时空错误，完美接入新序列化配置！
                    string jsonText = File.ReadAllText(projectData.StoryboardExportPath);
                    Context.Storyboard = JsonConvert.DeserializeObject<StoryboardRoot>(jsonText, StoryboardSerializer.GetSettings());

                    // 🌟 核心接入：在打开已有项目故事板时，立刻根据加载进来的 projectData 留痕账本全量复活控制板 ID！
                    StoryboardParser.StandardizeStoryboardIds(Context.Storyboard, projectData);


                    // 📒【点亮科技树】：在此处级联捞起元数据小账本！
                    TryLoadStoryboardMetaFile();

                    EventList.LoadStoryboardUI();
                    CanvasArea.TrackSelectedObject(null);
                    CanvasArea.RefreshJsonView();
                    _isVisualDirty = false;

                    _undoRedoManager.Reset();
                    _undoRedoManager.RecordSnapshot(Context.Storyboard);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"读取工程内关联的故事板文件失败 QAQ：\n{ex.Message}", "同步失败");
                }
            }
            else
            {
                EventList.LoadStoryboardUI();
                CanvasArea.TrackSelectedObject(null);
                CanvasArea.RefreshJsonView();
                TimelineConsole.LoadStoryboardTimeline(Context); // ✨ 补在这里！

                _undoRedoManager.Reset();
                _undoRedoManager.RecordSnapshot(Context.Storyboard);
            }

            if (!string.IsNullOrEmpty(projectData.ChartFilePath) && File.Exists(projectData.ChartFilePath))
            {
                SilentImportChart(projectData.ChartFilePath);
            }

            RefreshAllAssets();
            // 🌟 所有数据就位后，强行通电叫醒时间轴排版！
            if (Context.HasStoryboard)
            {
                TimelineConsole.LoadStoryboardTimeline(Context);
            }
        }

        // ========================================================
        // 📒【小账本核心法术】：随盘自动读取函数（写在 MainWindow 的类体内部任意位置即可）
        // ========================================================
        private void TryLoadStoryboardMetaFile()
        {
            if (string.IsNullOrEmpty(Context.StoryboardPath)) return;


            // 🌟 终极死锁留痕：在写盘保存前夕，根据当前的内存物理顺序，将控制板ID全量锁死进 .nep 账本中！
            if (Context.ProjectData != null)
            {
                StoryboardParser.SyncControlBoardIdMaps(Context.Storyboard, Context.ProjectData);
            }

            string metaPath = Context.StoryboardPath + "_meta.json";
            try
            {
                if (File.Exists(metaPath))
                {
                    // 📖 账本存在，直接读入内存房间！
                    string metaContent = File.ReadAllText(metaPath);
                    Context.StoryboardMeta = JsonConvert.DeserializeObject<Naziki_Editor.Models.StoryboardMeta>(metaContent)
                                             ?? new Naziki_Editor.Models.StoryboardMeta();
                }
                else
                {
                    // 🆕 账本不存在（可能是外来的野生独立 JSON 谱面），小艾乖巧地原地捏一个空账本！
                    Context.StoryboardMeta = new Naziki_Editor.Models.StoryboardMeta();
                }
            }
            catch (Exception ex)
            {
                // 呆胶布！账本损坏绝不卡死主界面，悄悄记录日志并回退到干净状态
                Context.StoryboardMeta = new Naziki_Editor.Models.StoryboardMeta();
                System.Diagnostics.Debug.WriteLine($"[小艾账本探头] 读取元数据账本时发生了穿模: {ex.Message}");
            }
        }

        private void SilentImportChart(string chartPath)
        {
            try
            {
                string jsonText = File.ReadAllText(chartPath);
                C2Chart chart = Newtonsoft.Json.JsonConvert.DeserializeObject<C2Chart>(jsonText);
                if (chart != null)
                {
                    Context.Chart = chart;
                    Context.TimeEngine = new ChartTimeEngine(chart.tempo_list, chart.time_base);
                    NoteList.BuildFullNoteTree();
                    if (Context.Chart.note_list.Count > 0)
                        NoteList._maxChartTime = Context.TimeEngine.TickToSeconds(Context.Chart.note_list.Max(n => n.tick));
                    NoteList.RefreshNoteList();
                    // ✨ 【小艾的终极解锁法术】：谱面加载完毕，立刻通知事件列表解除红色锁定结界！
                    EventList.UpdateChartLockState(Context.HasChart);
                }
            }
            catch { }
        }

        public MainWindow()
        {
            InitializeComponent();

            // 🔌 ✨ 终极通电！主窗口一启动，就把数据包分发给所有小弟！
            EventList.LoadContext(Context);
            NoteList.LoadContext(Context);
            CanvasArea.LoadContext(Context);
            PropertyPanel.LoadContext(Context);

            // ==========================================
            // 让主窗口订阅 Context 的数据修改广播！
            // ==========================================
            Context.OnDataModified += () =>
            {
                // 标记视觉画面变脏（需要保存）
                _isVisualDirty = true;

                // 如果 JSON 编辑器那边没有未应用的冲突代码，我们就自动刷新画面！
                if (!CanvasArea.HasUnappliedChanges)
                {
                    CanvasArea.RefreshJsonView();
                    _isVisualDirty = false;
                }
            };

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
                    _undoRedoManager.RecordSnapshot(Context.Storyboard);
                    _isVisualDirty = true;

                    // 2. 🏃 循环开始！为每一个选中的音符，在内存中原地捏出真实的物理对象
                    foreach (var note in selectedNotes)
                    {
                        // 🧱 实例化故事板官方认可的音符控制器包装盒
                        var noteCtrl = new C2NoteController
                        {
                            Id = $"note_ctrl_{note.id}_{DateTime.Now.Ticks}" // 赋予唯一的灵魂身份证
                        };

                        // 🔍 利用反射，安全、智能地把音符 ID 拍进它的 BaseState.Note 属性里
                        var baseState = noteCtrl.GetType().GetProperty("BaseState")?.GetValue(noteCtrl);
                        if (baseState != null)
                        {
                            var noteProp = baseState.GetType().GetProperty("Note");
                            if (noteProp != null)
                            {
                                // 根据底层模型的实际定义（string / int / object）自动适配，稳如老狗！
                                if (noteProp.PropertyType == typeof(string))
                                    noteProp.SetValue(baseState, note.id.ToString());
                                else if (noteProp.PropertyType == typeof(int))
                                    noteProp.SetValue(baseState, note.id);
                                else
                                    noteProp.SetValue(baseState, note.id); // 完美兼容 object
                            }
                        }

                        // 📥 正式编入故事板的核心全量军队中！
                        Context.Storyboard.note_controllers.Add(noteCtrl);
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
                _undoRedoManager.Reset();
                _undoRedoManager.RecordSnapshot(Context.Storyboard);

                TriggerAutoLinkIfReady();
                // ✨ 宇宙苏醒，通知时间轴画板开工！
                TimelineConsole.LoadStoryboardTimeline(Context);
            };

            PropertyPanel.OnApplyPropertiesRequested += () =>
            {
                if (ResolveDataConflictIfNeeded())
                {
                    _undoRedoManager.RecordSnapshot(Context.Storyboard);
                    CanvasArea.RefreshJsonView();
                    _isVisualDirty = false;
                    MessageBox.Show("属性修改已成功应用并同步至源代码！(๑•̀ㅂ•́)و✧", "应用成功");
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

                if (string.IsNullOrEmpty(matType)) return;

                try
                {
                    string projectDir = Path.GetDirectoryName(Context.ProjectFilePath);
                    string materialsDir = Path.Combine(projectDir, Context.ProjectData.MaterialFolderPath);
                    if (!Directory.Exists(materialsDir)) Directory.CreateDirectory(materialsDir);

                    string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string fileName = $"{matType}_Preset_{timeStamp}.nem";

                    StoryboardRoot miniRoot = new StoryboardRoot();

                    if (obj is C2Sprite s) miniRoot.sprites = new List<C2Sprite> { s };
                    else if (obj is C2Text t) miniRoot.texts = new List<C2Text> { t };
                    else if (obj is C2Line l) miniRoot.lines = new List<C2Line> { l };
                    else if (obj is C2Video v) miniRoot.videos = new List<C2Video> { v };
                    else if (obj is C2SceneController c) miniRoot.controllers = new List<C2SceneController> { c };
                    else if (obj is C2NoteController nc) miniRoot.note_controllers = new List<C2NoteController> { nc };

                    // 使用小艾为你打造的全新官方格式转换序列化器！
                    string pureJson = StoryboardSerializer.ToJson(miniRoot);

                    File.WriteAllText(Path.Combine(materialsDir, fileName), pureJson);
                    MessageBox.Show($"素材制造成功！(≧∇≦)ﾉ\n已安全存入沙盒：\n{fileName}", "纯净资产封装完成");

                    RefreshAllAssets();
                }
                catch (Exception ex) { MessageBox.Show($"胶囊压制失败 QAQ：{ex.Message}"); }
            };

            EventList.OnAddTextRequested += AddNewTextEvent;
            EventList.OnAddLineRequested += AddNewLineEvent;
            EventList.OnAddSceneRequested += AddNewSceneControllerEvent;
            EventList.OnAddTemplateRequested += AddNewTemplateEvent;

            CanvasArea.OnBeforeActionCheckConflict = () => ResolveDataConflictIfNeeded();

            CanvasArea.OnApplyJsonSuccess += (newRoot) =>
            {
                _undoRedoManager.RecordSnapshot(Context.Storyboard);
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

        private void MenuUndo_Click(object sender, RoutedEventArgs e) => ExecuteGlobalUndo();
        private void MenuRedo_Click(object sender, RoutedEventArgs e) => ExecuteGlobalRedo();

        private void ExecuteGlobalUndo()
        {
            bool success;
            StoryboardRoot prevState = _undoRedoManager.Undo(Context.Storyboard, out success);
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
                MessageBox.Show("已经没有更古老的修改痕迹可以撤回啦~", "时空尽头");
            }
        }

        private void ExecuteGlobalRedo()
        {
            bool success;
            StoryboardRoot nextState = _undoRedoManager.Redo(Context.Storyboard, out success);
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
                MessageBox.Show("设计师，这已经是当前宇宙最前沿的最新数据啦！", "时空尽头");
            }
        }

        private void MenuAbout_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("🛸 Naziki Editor v1.0.0\n\n一款专为 Cytoid 故事板设计师打造的可视化编辑器。\nPowered by Erin & You！\n\n祝您顺利创作出神级故事板分镜~ (★ω★)源", "关于 Naziki Studio");
        }

        public bool ResolveDataConflictIfNeeded()
        {
            if (CanvasArea.HasUnappliedChanges && _isVisualDirty)
            {
                var result = MessageBox.Show(
                    "检测到您同时修改了【属性】和【源代码】！请选择保留哪个版本：\n\n[ 是 (Yes) ] —— 保留：a. 源代码\n[ 否 (No) ] —— 保留：b. 事件属性\n[ 取消 ] —— 中止操作",
                    "写入保护：数据分歧警告", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    bool success = CanvasArea.ForceApplyJson();
                    if (success) _isVisualDirty = false;
                    return success;
                }
                else if (result == MessageBoxResult.No)
                {
                    CanvasArea.RefreshJsonView();
                    _isVisualDirty = false;
                    return true;
                }
                return false;
            }
            return true;
        }

        // ==========================================
        // 🎬 导入独立的故事板文件 (.json)
        // ==========================================
        private void MenuImportStoryboard_Click(object sender, RoutedEventArgs e)
        {
            // 🛑 【小艾的物理拦截结界】：如果还没导入谱面，直接拦截弹窗，拒绝执行！
            if (!Context.HasChart)
            {
                MessageBox.Show("纳尼？必须先导入谱面文件，才能导入故事板哦！(｀•ω•´)ゞ", "逻辑锁拦截", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 1. 启动保护结界：检查源码编辑器是否有未保存的冲突
            if (ResolveDataConflictIfNeeded())
            {
                // 2. 呼叫事件列表里改名后的全新专属法术！
                EventList.ExecuteImportStoryboard();
            }
        }

        private void MenuExit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
        private void MenuImportChart_Click(object sender, RoutedEventArgs e) { if (ResolveDataConflictIfNeeded()) ExecuteImportChart(); }

        public void ExecuteImportChart()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog { Filter = "Cytus II 谱面 (*.json)|*.json", Title = "请选择你的谱面文件" };
            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    string jsonText = File.ReadAllText(openFileDialog.FileName);
                    C2Chart chart = Newtonsoft.Json.JsonConvert.DeserializeObject<C2Chart>(jsonText);
                    if (chart == null || chart.time_base == 0) return;

                    Context.Chart = chart;
                    Context.TimeEngine = new ChartTimeEngine(chart.tempo_list, chart.time_base);
                    NoteList.BuildFullNoteTree();

                    if (Context.Chart.note_list.Count > 0)
                        NoteList._maxChartTime = Context.TimeEngine.TickToSeconds(Context.Chart.note_list.Max(n => n.tick));
                    NoteList.RefreshNoteList();

                    // ✨ 【小艾的终极解锁法术】：谱面加载完毕，立刻通知事件列表解除红色锁定结界！
                    EventList.UpdateChartLockState(Context.HasChart);

                    if (Context.ProjectData != null)
                    {
                        Context.ProjectData.ChartFilePath = openFileDialog.FileName;
                        SaveProjectNepFile();
                    }

                    string bpmText = ChartLogic.GetBpmText(chart.tempo_list);
                    MessageBox.Show($"谱面加载成功！\n🎵 音符数：{chart.note_list.Count} 个\n📄 谱面页数：{chart.page_list.Count} 页\n⏱️ 歌曲 BPM：{bpmText}", "情报解析成功");
                }
                catch (Exception ex) { MessageBox.Show($"解析发生爆炸 QAQ：\n{ex.Message}"); }
            }
            TriggerAutoLinkIfReady();
        }

        // ✨ 核心修正：将残留的旧工厂创建方法升级，全面适配 IStoryboardEntity 通用接口！
        private void CreateAndInjectObject(IStoryboardEntity obj)
        {
            if (!ResolveDataConflictIfNeeded() || obj == null) return;

            _undoRedoManager.RecordSnapshot(Context.Storyboard);

            if (obj is C2Text txt) Context.Storyboard.texts.Add(txt);
            else if (obj is C2Line line) Context.Storyboard.lines.Add(line);
            else if (obj is C2SceneController ctrl) Context.Storyboard.controllers.Add(ctrl);

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

            var text = new C2Text { Id = "text_" + DateTime.Now.Ticks };
            text.BaseState.TextContent = "默认文本";
            text.BaseState.Size = 30f;
            text.BaseState.Color = "#FFFFFF";
            text.BaseState.X = new UnitFloat { Value = 0, Unit = ReferenceUnit.World };
            text.BaseState.Y = new UnitFloat { Value = 0, Unit = ReferenceUnit.World };

            Context.Storyboard.texts.Add(text);
            EventList.LoadStoryboardUI();
            Context.MarkAsModified();
            TimelineConsole.LoadStoryboardTimeline(Context); // ✨ 补在这里！
        }

        // 🌟 2. 动态添加线条
        private void AddNewLineEvent()
        {
            if (!Context.HasStoryboard) return;

            var line = new C2Line { Id = "line_" + DateTime.Now.Ticks };
            line.BaseState.Width = 2.0f;
            line.BaseState.Color = "#FFFFFF";
            // 默认给它分配左右两个端点，拼成一条基础的线段：
            line.BaseState.Pos = new List<LinePosition>
            {
                new LinePosition { X = new UnitFloat { Value = -100, Unit = ReferenceUnit.World }, Y = new UnitFloat { Value = 0, Unit = ReferenceUnit.World } },
                new LinePosition { X = new UnitFloat { Value = 100, Unit = ReferenceUnit.World }, Y = new UnitFloat { Value = 0, Unit = ReferenceUnit.World } }
            };

            Context.Storyboard.lines.Add(line);
            EventList.LoadStoryboardUI();
            Context.MarkAsModified();
            TimelineConsole.LoadStoryboardTimeline(Context); // ✨ 补在这里！
        }

        // 🌟 3. 动态添加场景控制器
        private void AddNewSceneControllerEvent()
        {
            if (!Context.HasStoryboard) return;

            var controller = new C2SceneController { Id = "scene_" + DateTime.Now.Ticks };
            controller.BaseState.StoryboardOpacity = 1.0f;
            controller.BaseState.UiOpacity = 1.0f;
            controller.BaseState.BackgroundDim = 0.85f;
            controller.BaseState.ScanlineOpacity = 1.0f;

            Context.Storyboard.controllers.Add(controller);
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
            if (root.templates == null) root.templates = new System.Collections.Generic.Dictionary<string, Naziki_Editor.Models.C2Template>();

            // 1. 生成不冲突的初始名字
            string newKey = "generic_" + Guid.NewGuid().ToString().Substring(0, 4);
            while (root.templates.ContainsKey(newKey))
            {
                newKey = "generic_" + Guid.NewGuid().ToString().Substring(0, 5);
            }

            // 2. 赋予纯净的数据灵魂
            var newTemplate = new Naziki_Editor.Models.C2Template();
            root.templates[newKey] = newTemplate;

            // 3. 在大本营的顺位账本上登记造册
            if (Context.ProjectData != null && Context.ProjectData.TemplateTypes != null)
            {
                Context.ProjectData.TemplateTypes[newKey] = Naziki_Editor.Models.TemplateType.Generic;
            }

            // 4. 惊醒时光机！让包括时间轴在内的所有全局视图准备刷新！
            Context.MarkAsModified();

            // 5. 刷新左侧 UI（因为数据变了，通知 UI 重新加载）
            EventList.LoadStoryboardUI();

            // 🌟 6. 【极度丝滑交互】：造出来的瞬间，直接弹出属性编辑器，不用打谱师再去双击！
            OpenTemplatePropertyEditor(newKey, newTemplate);
            TimelineConsole.LoadStoryboardTimeline(Context); // ✨ 补在这里！
        }

        private void MenuSave_Click(object sender, RoutedEventArgs e)
        {
            if (!ResolveDataConflictIfNeeded()) return;

            if (string.IsNullOrEmpty(Context.StoryboardPath))
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog { Filter = "Cytoid 故事板 (*.json)|*.json", Title = "选择保存位置", FileName = "storyboard.json" };
                if (saveFileDialog.ShowDialog() == true)
                {
                    Context.StoryboardPath = saveFileDialog.FileName;
                    if (Context.ProjectData != null)
                        Context.ProjectData.StoryboardExportPath = Context.StoryboardPath;
                }
                else return;
            }

            try
            {
                // ========================================================
                // 🧙‍♂️ 1. 影子分离术 (Shadow Clone)
                // ========================================================
                // 先用定制配置把当前的内存大账本序列化，再反序列化出一份纯净不影响界面的“影子故事板”
                string rawJson = StoryboardSerializer.ToJson(Context.Storyboard);
                var shadowStoryboard = JsonConvert.DeserializeObject<Naziki_Editor.Models.StoryboardRoot>(
                    rawJson,
                    StoryboardSerializer.GetSettings()
                );

                // ========================================================
                // 🚀 2. 启动时空编译器进行全量展平与降维打击
                // ========================================================
                var compiler = new Naziki_Editor.Core.Compiler.StoryboardCompiler(
                    Context.Chart,
                    Context.TimeEngine,
                    shadowStoryboard.templates // 将影子故事板的模板字典喂给编译器
                );

                // 执行物理展平 (法则 B：附加时间展平法)
                compiler.FlattenStoryboard(shadowStoryboard);

                // ========================================================
                // 📡 3. 惊醒静态安检雷达，如果有 Bug 或者是 destroy 踩踏，可爱警告！
                // ========================================================
                if (compiler.CompileWarnings.Count > 0)
                {
                    string warningMsg = "🌟 设计师！时空安检雷达在展平落盘时发现了一些瑕疵，不过呆胶布（没关系），文件已安全生成：\n\n" +
                                        string.Join("\n", compiler.CompileWarnings.Take(5)); // 最多显示前5条，防弹窗炸裂
                    if (compiler.CompileWarnings.Count > 5)
                        warningMsg += $"\n... 以及其他 {compiler.CompileWarnings.Count - 5} 条时空安检警报。";

                    MessageBox.Show(warningMsg, "小艾的时空安检报告", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                // ========================================================
                // 💾 4. 谱面主文件物理落盘 (纯净无套娃官方格式)
                // ========================================================
                string jsonOutput = StoryboardSerializer.ToJson(shadowStoryboard);
                File.WriteAllText(Context.StoryboardPath, jsonOutput);

                // ========================================================
                // 📒 5.【核心新增】：动态测绘全量模板并安全写入元数据小账本！
                // ========================================================
                if (Context.StoryboardMeta == null) Context.StoryboardMeta = new StoryboardMeta();

                // 预清洗旧账本，防止残留已经被删掉的模板名称
                Context.StoryboardMeta.TemplateOverrides.Clear();

                if (Context.Storyboard.templates != null)
                {
                    foreach (var kvp in Context.Storyboard.templates)
                    {
                        if (kvp.Value != null && kvp.Value.BaseState != null)
                        {
                            // 呼叫雷达测绘当前的流派
                            var deducedType = Core.Compiler.TemplateClassifier.AnalyzeTemplate(kvp.Value.BaseState);
                            Context.StoryboardMeta.TemplateOverrides[kvp.Key] = deducedType;
                        }
                    }
                }

                // 计算小账本应该躺的物理路径（跟主 json 文件在同一个文件夹下，后缀为 .storyboard_meta.json）
                string metaPath = Context.StoryboardPath + "_meta.json";
                string metaJson = JsonConvert.SerializeObject(Context.StoryboardMeta, Formatting.Indented);
                File.WriteAllText(metaPath, metaJson);

                // 保存原本的工程配置文件 `.nep`
                SaveProjectNepFile();

                // ========================================================
                // 🎉 6. 刷新界面，华丽收尾
                // ========================================================
                _isVisualDirty = false;
                CanvasArea.RefreshJsonView();

                MessageBox.Show("故事板已完美展平，元数据小账本也已同步写入硬盘！(๑>ᴗ<๑)✧", "全盘保存成功");
            }
            catch (Exception ex)
            {
                MessageBox.Show("时空网关在写入磁盘时爆炸啦 QAQ：\n" + ex.Message, "物理写盘错误");
            }
        }

        private void TriggerAutoLinkIfReady()
        {
            if (Context.HasChart && Context.HasStoryboard)
                ChartStoryboardLink.TryTriggerAutoLink(Context.Chart, Context.Storyboard, Context.TimeEngine, EventList.NoteCtrlListBox, EventList.UpdateEmptyHintVisibility);
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;
        private void BtnMaximize_Click(object sender, RoutedEventArgs e) => this.WindowState = (this.WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
        private void BtnClose_Click(object sender, RoutedEventArgs e) { if (ResolveDataConflictIfNeeded()) Application.Current.Shutdown(); }

        public void OpenPropertyEditor(IStoryboardEntity targetObj)
        {
            if (targetObj == null) return;

            var editorWindow = new Naziki_Editor.Views.PropertyEditor.PropertyEditorWindow(targetObj, Context)
            {
                Owner = this,
                Title = $"属性编辑器 - [修改对象: {targetObj.Id}]"
            };

            if (editorWindow.ShowDialog() == true)
            {
                var modifiedObj = editorWindow.Tag as IStoryboardEntity;
                if (modifiedObj != null)
                {
                    UpdateStoryboardObjectInRoot(targetObj, modifiedObj);

                    PropertyPanel.SetSelectedObject(modifiedObj);
                    EventList.LoadStoryboardUI();
                    Context.MarkAsModified();
                    TimelineConsole.LoadStoryboardTimeline(Context); // ✨ 补在这里！
                }
            }
        }

        public void OpenTemplatePropertyEditor(string templateName, C2Template targetTemplate)
        {
            if (string.IsNullOrEmpty(templateName) || targetTemplate == null) return;

            var editorWindow = new Naziki_Editor.Views.PropertyEditor.PropertyEditorWindow(templateName, targetTemplate, Context)
            {
                Owner = this,
                Title = $"模板编辑器 - [✨ 调整预设: {templateName}]"
            };

            if (editorWindow.ShowDialog() == true)
            {
                EventList.LoadStoryboardUI();
                PropertyPanel.SetSelectedObject(null);
                Context.MarkAsModified();
                TimelineConsole.LoadStoryboardTimeline(Context); // ✨ 补在这里！
            }
        }

        public void CreateNewEventFromAsset(IStoryboardEntity newObj)
        {
            if (newObj == null || !Context.HasStoryboard) return;

            Naziki_Editor.Views.PropertyEditor.PropertyEditorWindow editor =
                 new Naziki_Editor.Views.PropertyEditor.PropertyEditorWindow(newObj, Context)
                 {
                     Owner = this,
                     Title = "属性编辑器 - [✨ 导入新素材并设置]"
                 };

            if (editor.ShowDialog() == true)
            {
                IStoryboardEntity modifiedObj = editor.Tag as IStoryboardEntity;
                if (modifiedObj == null) return;

                if (modifiedObj is C2Sprite s) Context.Storyboard.sprites.Add(s);
                else if (modifiedObj is C2Text t) Context.Storyboard.texts.Add(t);
                else if (modifiedObj is C2Video v) Context.Storyboard.videos.Add(v);
                else if (modifiedObj is C2Line l) Context.Storyboard.lines.Add(l);
                else if (modifiedObj is C2SceneController c) Context.Storyboard.controllers.Add(c);
                else if (modifiedObj is C2NoteController nc) Context.Storyboard.note_controllers.Add(nc);

                EventList.LoadStoryboardUI();
                PropertyPanel.SetSelectedObject(modifiedObj);
                Context.MarkAsModified();
                TimelineConsole.LoadStoryboardTimeline(Context); // ✨ 补在这里！
            }
        }

        // ==========================================
        // ⚡ 核心基站：用修改后的完全体包装盒替换大本营里的旧包装盒
        // ==========================================
        public void UpdateStoryboardObjectInRoot(IStoryboardEntity oldObj, IStoryboardEntity newObj)
        {
            var root = Context.Storyboard;
            if (root == null) return;

            if (oldObj is C2Sprite sOld && newObj is C2Sprite sNew) { int i = root.sprites.IndexOf(sOld); if (i >= 0) root.sprites[i] = sNew; }
            else if (oldObj is C2Text tOld && newObj is C2Text tNew) { int i = root.texts.IndexOf(tOld); if (i >= 0) root.texts[i] = tNew; }
            else if (oldObj is C2Video vOld && newObj is C2Video vNew) { int i = root.videos.IndexOf(vOld); if (i >= 0) root.videos[i] = vNew; }
            else if (oldObj is C2Line lOld && newObj is C2Line lNew) { int i = root.lines.IndexOf(lOld); if (i >= 0) root.lines[i] = lNew; }
            else if (oldObj is C2SceneController cOld && newObj is C2SceneController cNew) { int i = root.controllers.IndexOf(cOld); if (i >= 0) root.controllers[i] = cNew; }
            else if (oldObj is C2NoteController nOld && newObj is C2NoteController nNew) { int i = root.note_controllers.IndexOf(nOld); if (i >= 0) root.note_controllers[i] = nNew; }
        }
    }
}