using Microsoft.Win32;
using Naziki_Editor.Core;
using Naziki_Editor.Models;
using Naziki_Editor.State; // ✨ 新增：引入 State 别墅区的命名空间！
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
                    StoryboardRoot root = StoryboardParser.Load(projectData.StoryboardExportPath);
                    Context.Storyboard = root;

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

                _undoRedoManager.Reset();
                _undoRedoManager.RecordSnapshot(Context.Storyboard);
            }

            if (!string.IsNullOrEmpty(projectData.ChartFilePath) && File.Exists(projectData.ChartFilePath))
            {
                SilentImportChart(projectData.ChartFilePath);
            }

            RefreshAllAssets();
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


            EventList.OnAssetScanned += (bundle) => AssetList.RefreshAssetListUI(bundle);

            NoteList.OnNoteGroupExported += (groupItem) =>
            {
                if (ResolveDataConflictIfNeeded())
                {
                    _undoRedoManager.RecordSnapshot(Context.Storyboard);
                    _isVisualDirty = true;
                    EventList.AddNoteGroupToTree(groupItem);
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

            PropertyPanel.OnSaveAsMaterialRequested += (obj) =>
            {
                if (string.IsNullOrEmpty(Context.ProjectFilePath) || Context.ProjectData == null) return;

                string matType = "";
                if (obj is Sprite) matType = "Image";
                else if (obj is Text) matType = "Text";
                else if (obj is Line) matType = "Line";
                else if (obj is Video) matType = "Video";
                else if (obj is Controller || obj is NoteController) matType = "Scene";

                if (string.IsNullOrEmpty(matType)) return;

                try
                {
                    string projectDir = Path.GetDirectoryName(Context.ProjectFilePath);
                    string materialsDir = Path.Combine(projectDir, Context.ProjectData.MaterialFolderPath);
                    if (!Directory.Exists(materialsDir)) Directory.CreateDirectory(materialsDir);

                    string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string fileName = $"{matType}_Preset_{timeStamp}.nem";

                    StoryboardRoot miniRoot = new StoryboardRoot();

                    if (obj is Sprite s) miniRoot.sprites = new List<Sprite> { s };
                    else if (obj is Text t) miniRoot.texts = new List<Text> { t };
                    else if (obj is Line l) miniRoot.lines = new List<Line> { l };
                    else if (obj is Video v) miniRoot.videos = new List<Video> { v };
                    else if (obj is Controller c) miniRoot.controllers = new List<Controller> { c };
                    else if (obj is NoteController nc) miniRoot.note_controllers = new List<NoteController> { nc };

                    string pureJson = Newtonsoft.Json.JsonConvert.SerializeObject(miniRoot, Newtonsoft.Json.Formatting.Indented);

                    File.WriteAllText(Path.Combine(materialsDir, fileName), pureJson);
                    MessageBox.Show($"素材制造成功！(≧∇≦)ﾉ\n已安全存入沙盒：\n{fileName}", "纯净资产封装完成");

                    RefreshAllAssets();
                }
                catch (Exception ex) { MessageBox.Show($"胶囊压制失败 QAQ：{ex.Message}"); }
            };

            EventList.OnAddTextRequested += () => CreateAndInjectObject(new Text
            {
                Id = "text_" + (Context.Storyboard.texts.Count + 1),
                States = new List<TextState>
                {
                    new TextState
                    {
                        Time = 0.0f,
                        Text = "Naziki Text",
                        X = new UnitFloat { Value = 0.0f, Unit = ReferenceUnit.StageX },
                        Y = new UnitFloat { Value = 0.0f, Unit = ReferenceUnit.StageY },
                        Color = new CytoidColor()
                    }
                }
            });
            EventList.OnAddLineRequested += () => CreateAndInjectObject(new Line
            {
                Id = "line_" + (Context.Storyboard.lines.Count + 1),
                States = new List<LineState>
                {
                    new LineState
                    {
                        Time = 0.0f,
                        Width = new UnitFloat { Value = 0.05f, Unit = ReferenceUnit.StageX },
                        Color = new CytoidColor()
                    }
                }
            });
            EventList.OnAddSceneRequested += () => CreateAndInjectObject(new Controller
            {
                States = new List<ControllerState>
                {
                    new ControllerState { Time = 0.0f }
                }
            });

            CanvasArea.OnBeforeActionCheckConflict = () => ResolveDataConflictIfNeeded();

            CanvasArea.OnApplyJsonSuccess += (newRoot) =>
            {
                _undoRedoManager.RecordSnapshot(Context.Storyboard);
                Context.Storyboard = newRoot;
                EventList.LoadStoryboardUI();
                _isVisualDirty = false;
            };

            PropertyPanel.OnDataModified += () =>
            {
                _isVisualDirty = true;
                if (!CanvasArea.HasUnappliedChanges)
                {
                    CanvasArea.RefreshJsonView();
                    _isVisualDirty = false;
                }
            };

            EventList.OnEventNodeSelected += (obj) =>
            {
                PropertyPanel.SetSelectedObject(obj);
                CanvasArea.TrackSelectedObject(obj);
            };
        }

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
            }
            else
            {
                MessageBox.Show("呆胶布？已经没有更古老的修改痕迹可以撤回啦~", "时空尽头");
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
            }
            else
            {
                MessageBox.Show("指挥官，这已经是当前宇宙最前沿的最新数据啦！", "时空尽头");
            }
        }

        private void MenuAbout_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("🛸 Naziki Editor v1.0.0\n\n一款专为 Cytoid 故事板设计师打造的可视化编辑器。\nPowered by Erin & You！\n\n祝您顺利创作出神级故事板分镜~ (★ω★)ノ", "关于 Naziki Studio");
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

        private void MenuOpenProject_Click(object sender, RoutedEventArgs e) { if (ResolveDataConflictIfNeeded()) EventList.ExecuteOpenProject(); }
        private void MenuExit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
        private void MenuImportChart_Click(object sender, RoutedEventArgs e) { if (ResolveDataConflictIfNeeded()) ExecuteImportChart(); }

        private void ExecuteImportChart()
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

        private void CreateAndInjectObject(object obj)
        {
            if (!ResolveDataConflictIfNeeded()) return;

            _undoRedoManager.RecordSnapshot(Context.Storyboard);

            if (obj is Text txt) Context.Storyboard.texts.Add(txt);
            else if (obj is Line line) Context.Storyboard.lines.Add(line);
            else if (obj is Controller ctrl) Context.Storyboard.controllers.Add(ctrl);

            _isVisualDirty = true;
            EventList.LoadStoryboardUI();
            if (!CanvasArea.HasUnappliedChanges)
            {
                CanvasArea.RefreshJsonView();
                _isVisualDirty = false;
            }
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
                string jsonOutput = Newtonsoft.Json.JsonConvert.SerializeObject(Context.Storyboard, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(Context.StoryboardPath, jsonOutput);

                SaveProjectNepFile();

                _isVisualDirty = false;
                CanvasArea.RefreshJsonView();
                MessageBox.Show("故事板与工程配置文件均已物理写入硬盘！(๑•̀ㅂ•́)و✧", "全盘保存成功");
            }
            catch (Exception ex) { MessageBox.Show("写入磁盘爆炸啦 QAQ：\n" + ex.Message); }
        }

        private void TriggerAutoLinkIfReady()
        {
            if (Context.HasChart && Context.HasStoryboard)
                ChartStoryboardLink.TryTriggerAutoLink(Context.Chart, Context.Storyboard, Context.TimeEngine, EventList.NoteCtrlListBox, EventList.UpdateEmptyHintVisibility);
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;
        private void BtnMaximize_Click(object sender, RoutedEventArgs e) => this.WindowState = (this.WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
        private void BtnClose_Click(object sender, RoutedEventArgs e) { if (ResolveDataConflictIfNeeded()) Application.Current.Shutdown(); }

        public void OpenPropertyEditor(StoryboardObject targetObj)
        {
            if (targetObj == null) return;

            // ✨ 注意：我们现在使用的是 Context.Storyboard 和 Context.Chart，而不是以前的野变量！
            Naziki_Editor.Views.PropertyEditor.PropertyEditorWindow editor =
                new Naziki_Editor.Views.PropertyEditor.PropertyEditorWindow(targetObj, Context)
                {
                    Owner = this
                };

            if (editor.ShowDialog() == true)
            {
                StoryboardObject modifiedObj = (StoryboardObject)editor.Tag;
                string modifiedJson = JsonConvert.SerializeObject(modifiedObj);
                JsonConvert.PopulateObject(modifiedJson, targetObj);

                Core.UndoRedoManager.Global.RecordSnapshot(Context.Storyboard);
                EventList.LoadStoryboardUI();
                CanvasArea.RefreshJsonView();
                _isVisualDirty = false;
            }
        }

        public void CreateNewEventFromAsset(StoryboardObject newObj)
        {
            if (newObj == null || !Context.HasStoryboard) return;

            // ✨ 注意：同上，依然使用解包后的数据调用弹窗
            Naziki_Editor.Views.PropertyEditor.PropertyEditorWindow editor =
                 new Naziki_Editor.Views.PropertyEditor.PropertyEditorWindow(newObj, Context)
                 {
                     Owner = this,
                     Title = "属性编辑器 - [✨ 导入新素材并设置]"
                 };

            if (editor.ShowDialog() == true)
            {
                StoryboardObject modifiedObj = (StoryboardObject)editor.Tag;

                if (modifiedObj is Sprite s) Context.Storyboard.sprites.Add(s);
                else if (modifiedObj is Text t) Context.Storyboard.texts.Add(t);
                else if (modifiedObj is Video v) Context.Storyboard.videos.Add(v);
                else if (modifiedObj is Line l) Context.Storyboard.lines.Add(l);

                Core.UndoRedoManager.Global.RecordSnapshot(Context.Storyboard);
                EventList.LoadStoryboardUI();
                CanvasArea.RefreshJsonView();
            }
        }
    }
}