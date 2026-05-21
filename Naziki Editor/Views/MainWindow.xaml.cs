using Microsoft.Win32;
using Naziki_Editor.Core;
using Naziki_Editor.Models;
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
        private C2Chart _currentChart;
        private ChartTimeEngine _currentEngine;
        private HashSet<C2Note> _selectedNotes = new HashSet<C2Note>();
        private double _maxChartTime = 0;

        // 🌟 物理航线缓存
        private string _currentProjectFilePath = null;
        private NazikiProjectModel _currentProjectData = null;

        public string CurrentProjectFilePath => _currentProjectFilePath;
        public NazikiProjectModel CurrentProjectData => _currentProjectData;

        private string _currentStoryboardPath = null;
        private StoryboardRoot _currentStoryboardRoot = new StoryboardRoot();
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
                // 🚀 直接喂给全局单例大总管！
                // 剩下的“隐藏时间轴按钮”、“拉伸画布”、“重绘刻度”、“重绘波形”全都会触发连锁反应自动完成！
                await Core.AudioSyncEngine.Instance.LoadAudioAsync(openFileDialog.FileName);
            }
        }



        // ==========================================
        // 💾 核心加装：.nep 工程物理存盘记账引擎 (彻底击碎白板 Bug)
        // ==========================================
        private void SaveProjectNepFile()
        {
            if (string.IsNullOrEmpty(_currentProjectFilePath) || _currentProjectData == null) return;
            try
            {
                _currentProjectData.LastModifiedTime = DateTime.Now;
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(_currentProjectData, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(_currentProjectFilePath, json);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"物理写入工程配置文件 (.nep) 失败 QAQ：\n{ex.Message}", "工程记账失败");
            }
        }

        public void RefreshAllAssets()
        {
            if (string.IsNullOrEmpty(_currentProjectFilePath) || _currentProjectData == null) return;
            string projectDir = System.IO.Path.GetDirectoryName(_currentProjectFilePath);
            string matFolder = _currentProjectData.MaterialFolderPath;
            var bundle = Core.AssetScanner.ScanProjectAssets(projectDir, matFolder);
            AssetList.RefreshAssetListUI(bundle);
        }

        // ==========================================
        // ⚓ 公开港口入城式：一体化无缝扫描完全体
        // ==========================================
        public void LoadProject(string projectPath, NazikiProjectModel projectData)
        {
            if (projectData == null) return;

            _currentProjectFilePath = projectPath;
            _currentProjectData = projectData;

            // 1. 🦾 悬挂主权徽章标题 text
            this.Title = $"Naziki Editor - {projectData.ProjectName} ［{projectPath}］";

            // 2. 🔍 分流读取故事板历史
            if (!string.IsNullOrEmpty(projectData.StoryboardExportPath) && File.Exists(projectData.StoryboardExportPath))
            {
                try
                {
                    _currentStoryboardPath = projectData.StoryboardExportPath;
                    StoryboardRoot root = StoryboardParser.Load(projectData.StoryboardExportPath);
                    _currentStoryboardRoot = root;

                    EventList.LoadStoryboardUI(_currentStoryboardRoot);
                    // 🌟 新增：强制雷达归零，切断历史残影！
                    CanvasArea.TrackSelectedObject(null);
                    CanvasArea.RefreshJsonView();
                    _isVisualDirty = false;

                    // 录入【第0步历史底稿】
                    _undoRedoManager.Reset();
                    _undoRedoManager.RecordSnapshot(_currentStoryboardRoot);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"读取工程内关联的故事板文件失败 QAQ：\n{ex.Message}", "同步失败");
                }
            }
            else
            {
                // 如果是全新项目，初始化白板环境
                EventList.LoadStoryboardUI(_currentStoryboardRoot);
                // 🌟 新增：强制雷达归零，切断历史残影！
                CanvasArea.TrackSelectedObject(null);
                CanvasArea.RefreshJsonView();

                // 录入【第0步真空底稿】
                _undoRedoManager.Reset();
                _undoRedoManager.RecordSnapshot(_currentStoryboardRoot);
            }

            // 3. 🎵 自动加载遗留的谱面血脉
            if (!string.IsNullOrEmpty(projectData.ChartFilePath) && File.Exists(projectData.ChartFilePath))
            {
                SilentImportChart(projectData.ChartFilePath);
            }

            // =======================================================
            // 🌟 核心修改：只保留刷新素材网格，彻底删除原本躺在这里的 TriggerAutoLinkIfReady();
            // =======================================================
            RefreshAllAssets();
        }

        // 静默后台加载已有谱面，防止二次确认骚扰
        private void SilentImportChart(string chartPath)
        {
            try
            {
                string jsonText = File.ReadAllText(chartPath);
                C2Chart chart = Newtonsoft.Json.JsonConvert.DeserializeObject<C2Chart>(jsonText);
                if (chart != null)
                {
                    _currentChart = chart;
                    _currentEngine = new ChartTimeEngine(chart.tempo_list, chart.time_base);
                    NoteList._currentChart = _currentChart;
                    NoteList._currentEngine = _currentEngine;
                    NoteList.BuildFullNoteTree();
                    if (_currentChart.note_list.Count > 0)
                        NoteList._maxChartTime = _currentEngine.TickToSeconds(_currentChart.note_list.Max(n => n.tick));
                    NoteList.RefreshNoteList();

                    // 🌟 核心修改：彻底删除原本躺在这里的 TriggerAutoLinkIfReady();
                    // 确保老项目启动数据恢复时，绝对不会触发任何弹窗！
                }
            }
            catch { }
        }

        public MainWindow()
        {
            InitializeComponent();

            EventList.OnAssetScanned += (bundle) => AssetList.RefreshAssetListUI(bundle);

            NoteList.OnNoteGroupExported += (groupItem) =>
            {
                if (ResolveDataConflictIfNeeded())
                {
                    // 录入音符批量灌入快照
                    _undoRedoManager.RecordSnapshot(_currentStoryboardRoot);
                    _isVisualDirty = true;
                    EventList.AddNoteGroupToTree(groupItem);
                }
            };

            EventList.OnStoryboardLoaded += (path, root) =>
            {
                // 录入故事板导入快照
                _currentStoryboardPath = path;
                _currentStoryboardRoot = root;
                // 🌟 新增：强制雷达归零，切断历史残影！
                CanvasArea.TrackSelectedObject(null);
                CanvasArea.RefreshJsonView();
                _isVisualDirty = false;

                // 🌟 核心增补：手动外部导入 json 时，也同步更新工程账本并记录底稿！
                if (_currentProjectData != null)
                {
                    _currentProjectData.StoryboardExportPath = path;
                    SaveProjectNepFile();
                }
                _undoRedoManager.Reset();
                _undoRedoManager.RecordSnapshot(_currentStoryboardRoot);

                TriggerAutoLinkIfReady();
            };
            // 🌟 核心修改：将应用属性修改的时机，改为专门的【应用】按钮，彻底删除原本躺在这里的自动刷新逻辑！
            PropertyPanel.OnApplyPropertiesRequested += () =>
            {
                if (ResolveDataConflictIfNeeded())
                {
                    // 可视化修改应用前快照
                    _undoRedoManager.RecordSnapshot(_currentStoryboardRoot);
                    CanvasArea.RefreshJsonView();
                    _isVisualDirty = false;
                    MessageBox.Show("属性修改已成功应用并同步至源代码！(๑•̀ㅂ•́)و✧", "应用成功");
                }
            };
            // 🌟 核心增补：新增【另存为素材】功能，允许用户将当前选中事件封装成独立的胶囊文件，方便在其他项目中复用！(≧∇≦)
            PropertyPanel.OnSaveAsMaterialRequested += (obj) =>
            {
                if (string.IsNullOrEmpty(_currentProjectFilePath) || _currentProjectData == null) return;

                // 智能判定对象类型
                string matType = "";
                if (obj is Sprite) matType = "Image";
                else if (obj is Text) matType = "Text";
                else if (obj is Line) matType = "Line";
                else if (obj is Video) matType = "Video";
                else if (obj is Controller || obj is NoteController) matType = "Scene";

                if (string.IsNullOrEmpty(matType)) return;

                try
                {
                    string projectDir = Path.GetDirectoryName(_currentProjectFilePath);
                    string materialsDir = Path.Combine(projectDir, _currentProjectData.MaterialFolderPath);
                    if (!Directory.Exists(materialsDir)) Directory.CreateDirectory(materialsDir);

                    string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string fileName = $"{matType}_Preset_{timeStamp}.nem";

                    // ✨ 核心重构：抛弃 NemDocument，直接创造一个微型的纯净宇宙！
                    StoryboardRoot miniRoot = new StoryboardRoot();

                    // 把选中的对象放进宇宙对应的空间里 (确保 List 不为 null)
                    if (obj is Sprite s) miniRoot.sprites = new List<Sprite> { s };
                    else if (obj is Text t) miniRoot.texts = new List<Text> { t };
                    else if (obj is Line l) miniRoot.lines = new List<Line> { l };
                    else if (obj is Video v) miniRoot.videos = new List<Video> { v };
                    else if (obj is Controller c) miniRoot.controllers = new List<Controller> { c };
                    else if (obj is NoteController nc) miniRoot.note_controllers = new List<NoteController> { nc };

                    // ✨ 将微型宇宙直接序列化为最纯正的 JSON 文本！
                    string pureJson = Newtonsoft.Json.JsonConvert.SerializeObject(miniRoot, Newtonsoft.Json.Formatting.Indented);

                    File.WriteAllText(Path.Combine(materialsDir, fileName), pureJson);
                    MessageBox.Show($"素材制造成功！(≧∇≦)ﾉ\n已安全存入沙盒：\n{fileName}", "纯净资产封装完成");

                    // 重新扫描呼叫雷达！
                    RefreshAllAssets();
                }
                catch (Exception ex) { MessageBox.Show($"胶囊压制失败 QAQ：{ex.Message}"); }
            };

            EventList.OnAddTextRequested += () => CreateAndInjectObject(new Text
            {
                Id = "text_" + (_currentStoryboardRoot.texts.Count + 1),
                States = new List<TextState>
    {
        new TextState
        {
            Time = 0.0f, // 绝对时间
            Text = "Naziki Text", 
            // 坐标现在必须用 UnitFloat 包装！
            X = new UnitFloat { Value = 0.0f, Unit = ReferenceUnit.StageX },
            Y = new UnitFloat { Value = 0.0f, Unit = ReferenceUnit.StageY },
            // 颜色默认用 CytoidColor (默认就是白色 R=255, G=255, B=255, A=1)
            Color = new CytoidColor()
        }
    }
            });
            EventList.OnAddLineRequested += () => CreateAndInjectObject(new Line
            {
                Id = "line_" + (_currentStoryboardRoot.lines.Count + 1),
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
                // 场景控制器没有 ID，只有状态列表
                States = new List<ControllerState>
    {
        new ControllerState { Time = 0.0f }
    }
            });

            CanvasArea.RequestCurrentStoryboardRoot = () => _currentStoryboardRoot;
            CanvasArea.OnBeforeActionCheckConflict = () => ResolveDataConflictIfNeeded();

            // 🌟 核心修改：将应用 JSON 修改的时机，改为专门的【应用】按钮，彻底删除原本躺在这里的自动刷新逻辑！
            CanvasArea.OnApplyJsonSuccess += (newRoot) =>
            {
                _undoRedoManager.RecordSnapshot(_currentStoryboardRoot); // 代码块录入快照
                _currentStoryboardRoot = newRoot;
                EventList.LoadStoryboardUI(_currentStoryboardRoot);
                _isVisualDirty = false;
            };

            // 🌟 核心增补：属性面板修改事件改为单纯的“修改了”，不直接刷新界面，彻底删除原本躺在这里的自动刷新逻辑！
            PropertyPanel.OnDataModified += () =>
            {
                _isVisualDirty = true;
                if (!CanvasArea.HasUnappliedChanges)
                {
                    CanvasArea.RefreshJsonView();
                    _isVisualDirty = false;
                }
            };

            // 🌟 核心增补：当事件节点被选中时，除了在属性面板显示对应属性外，还让画布追踪选中状态，彻底删除原本躺在这里的自动刷新逻辑！
            EventList.OnEventNodeSelected += (obj) =>
            {
                PropertyPanel.SetSelectedObject(obj);
                CanvasArea.TrackSelectedObject(obj);
            };
        }

        // ==========================================
        // ⌨️ 核心加装：【方案 b：双轨自治热键嗅探模块】
        // ==========================================
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (e.Key == Key.Z)
                {
                    // 如果焦点正死死停留在 AvalonEdit 源码编辑器内部，双手双脚放开，任由它自治打字撤回！
                    if (CanvasArea != null && CanvasArea.JsonEditor != null && CanvasArea.JsonEditor.IsKeyboardFocusWithin)
                        return;

                    // 反之，强行拦截，调用全局模型时空回溯！
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




        // ==========================================
        // 🌟 核心加装：全局撤销重做执行器，彻底解放双手双脚，专注于创作本体！
        // ==========================================
        private void ExecuteGlobalUndo()
        {
            bool success;
            StoryboardRoot prevState = _undoRedoManager.Undo(_currentStoryboardRoot, out success);
            if (success)
            {
                _currentStoryboardRoot = prevState;
                EventList.LoadStoryboardUI(_currentStoryboardRoot);
                CanvasArea.RefreshJsonView();
                _isVisualDirty = false;
            }
            else
            {
                MessageBox.Show("呆胶布？已经没有更古老的修改痕迹可以撤回啦~", "时空尽头");
            }
        }

        // 🌟 核心加装：全局撤销重做执行器，彻底解放双手双脚，专注于创作本体！
        private void ExecuteGlobalRedo()
        {
            bool success;
            StoryboardRoot nextState = _undoRedoManager.Redo(_currentStoryboardRoot, out success);
            if (success)
            {
                _currentStoryboardRoot = nextState;
                EventList.LoadStoryboardUI(_currentStoryboardRoot);
                CanvasArea.RefreshJsonView();
                _isVisualDirty = false;
            }
            else
            {
                MessageBox.Show("指挥官，这已经是当前宇宙最前沿的最新数据啦！", "时空尽头");
            }
        }

        // ==========================================
        // 🌟 核心加装：关于界面，增加一点点人情味小彩蛋~
        // ==========================================
        private void MenuAbout_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("🛸 Naziki Editor v1.0.0\n" +
                "\n一款专为 Cytoid 故事板设计师打造的" +
                "\nCytoid 故事板可视化编辑器。" +
                "\nPowered by Erin & You！\n" +
                "\n祝您顺利创作出神级故事板分镜~ (★ω★)ノ",
                "关于 Naziki Studio");
        }

        // =========================================
        // 🌟 核心加装：数据分歧智能侦测与友好调解机制
        // =========================================
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


        // 🌟 核心加装：独立谱面导入执行器，彻底解放菜单事件，专注于业务逻辑！
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

                    _currentChart = chart;
                    _currentEngine = new ChartTimeEngine(chart.tempo_list, chart.time_base);
                    NoteList._currentChart = _currentChart;
                    NoteList._currentEngine = _currentEngine;
                    NoteList.BuildFullNoteTree();
                    if (_currentChart.note_list.Count > 0)
                        NoteList._maxChartTime = _currentEngine.TickToSeconds(_currentChart.note_list.Max(n => n.tick));
                    NoteList.RefreshNoteList();

                    // 🌟 核心增补：导入谱面文件后，立刻写入工程账本物理存盘！
                    if (_currentProjectData != null)
                    {
                        _currentProjectData.ChartFilePath = openFileDialog.FileName;
                        SaveProjectNepFile();
                    }

                    string bpmText = ChartLogic.GetBpmText(chart.tempo_list);
                    MessageBox.Show($"谱面加载成功！\n🎵 音符数：{chart.note_list.Count} 个\n📄 谱面页数：{chart.page_list.Count} 页\n⏱️ 歌曲 BPM：{bpmText}", "情报解析成功");
                }
                catch (Exception ex) { MessageBox.Show($"解析发生爆炸 QAQ：\n{ex.Message}"); }
            }
            TriggerAutoLinkIfReady();
        }

        // 🌟 核心加装：创造物体并注入全局模型的统一执行器，彻底解放菜单事件，专注于业务逻辑！
        private void CreateAndInjectObject(object obj)
        {
            if (!ResolveDataConflictIfNeeded()) return;

            // 创造物体前拍照存证
            _undoRedoManager.RecordSnapshot(_currentStoryboardRoot);

            if (obj is Text txt) _currentStoryboardRoot.texts.Add(txt);
            else if (obj is Line line) _currentStoryboardRoot.lines.Add(line);
            else if (obj is Controller ctrl) _currentStoryboardRoot.controllers.Add(ctrl);

            _isVisualDirty = true;
            EventList.LoadStoryboardUI(_currentStoryboardRoot);
            if (!CanvasArea.HasUnappliedChanges)
            {
                CanvasArea.RefreshJsonView();
                _isVisualDirty = false;
            }
        }

        // 🌟 核心加装：全盘保存执行器，彻底解放菜单事件，专注于业务逻辑！
        private void MenuSave_Click(object sender, RoutedEventArgs e)
        {
            if (!ResolveDataConflictIfNeeded()) return;

            if (string.IsNullOrEmpty(_currentStoryboardPath))
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog { Filter = "Cytoid 故事板 (*.json)|*.json", Title = "选择保存位置", FileName = "storyboard.json" };
                if (saveFileDialog.ShowDialog() == true)
                {
                    _currentStoryboardPath = saveFileDialog.FileName;
                    if (_currentProjectData != null)
                        _currentProjectData.StoryboardExportPath = _currentStoryboardPath;
                }
                else return;
            }

            try
            {
                string jsonOutput = Newtonsoft.Json.JsonConvert.SerializeObject(_currentStoryboardRoot, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(_currentStoryboardPath, jsonOutput);

                // 🌟 核心修复：点击保存时，顺手把 .nep 账本也一并同步重写！
                SaveProjectNepFile();

                _isVisualDirty = false;
                CanvasArea.RefreshJsonView();
                MessageBox.Show("故事板与工程配置文件均已物理写入硬盘！(๑•̀ㅂ•́)و✧", "全盘保存成功");
            }
            catch (Exception ex) { MessageBox.Show("写入磁盘爆炸啦 QAQ：\n" + ex.Message); }
        }

        // 🌟 核心加装：自动链接触发器，彻底解放菜单事件，专注于业务逻辑！
        private void TriggerAutoLinkIfReady()
        {
            if (_currentChart != null && _currentStoryboardRoot != null)
                ChartStoryboardLink.TryTriggerAutoLink(_currentChart, _currentStoryboardRoot, _currentEngine, EventList.NoteCtrlListBox, EventList.UpdateEmptyHintVisibility);
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;
        private void BtnMaximize_Click(object sender, RoutedEventArgs e) => this.WindowState = (this.WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
        private void BtnClose_Click(object sender, RoutedEventArgs e) { if (ResolveDataConflictIfNeeded()) Application.Current.Shutdown(); }


        // ==========================================
        // 🚀 核心枢纽：呼唤属性编辑器并处理全局存档
        // ==========================================
        public void OpenPropertyEditor(StoryboardObject targetObj)
        {

            if (targetObj == null) return;

            // 1. 召唤弹窗，并设置 Owner 让它在主窗口正中间弹出
            // 注意中间多传了一个 _currentStoryboardRoot 进去！
            // 加上具体的命名空间和新类名
            Naziki_Editor.Views.PropertyEditor.PropertyEditorWindow editor =
                new Naziki_Editor.Views.PropertyEditor.PropertyEditorWindow(targetObj, _currentStoryboardRoot, _currentChart)
                {
                    Owner = this
                };
            editor.ShowDialog();

            // 2. 阻塞等待用户操作
            if (editor.ShowDialog() == true)
            {
                // 3. 用户点击了“保存魔法”！提取改好的克隆体
                StoryboardObject modifiedObj = (StoryboardObject)editor.Tag;

                // ✨ 黑科技：把修改后的数据化为流，直接“灌”回原本的对象内存中！原地升级！
                string modifiedJson = JsonConvert.SerializeObject(modifiedObj);
                JsonConvert.PopulateObject(modifiedJson, targetObj);

                // 4. 呼叫全局时光机，记录这次原子的打包操作！
                Core.UndoRedoManager.Global.RecordSnapshot(_currentStoryboardRoot);

                // 5. 刷新战舰上的所有雷达面板
                EventList.LoadStoryboardUI(_currentStoryboardRoot);
                CanvasArea.RefreshJsonView();
                _isVisualDirty = false;
            }
        }

        // ==========================================
        // 🚀 专供素材列表使用：先编辑，保存了才入库！
        // ==========================================
        public void CreateNewEventFromAsset(StoryboardObject newObj)
        {
            if (newObj == null || _currentStoryboardRoot == null) return;

            // 1. 带着全局字典弹窗！
            Naziki_Editor.Views.PropertyEditor.PropertyEditorWindow editor =
                 new Naziki_Editor.Views.PropertyEditor.PropertyEditorWindow(newObj, _currentStoryboardRoot, _currentChart)
                 {
                     Owner = this,
                     Title = "属性编辑器 - [✨ 导入新素材并设置]"
                 };

            // 2. 如果玩家点击了保存 (且通过了不重名的防线)
            if (editor.ShowDialog() == true)
            {
                StoryboardObject modifiedObj = (StoryboardObject)editor.Tag;

                // 3. 根据类型，正式将其加入到大宇宙中！
                if (modifiedObj is Sprite s) _currentStoryboardRoot.sprites.Add(s);
                else if (modifiedObj is Text t) _currentStoryboardRoot.texts.Add(t);
                else if (modifiedObj is Video v) _currentStoryboardRoot.videos.Add(v);
                else if (modifiedObj is Line l) _currentStoryboardRoot.lines.Add(l);
                // 如果是控制器等也可以在这里继续 else if...

                // 4. 存入时光机并刷新所有面板
                Core.UndoRedoManager.Global.RecordSnapshot(_currentStoryboardRoot);
                EventList.LoadStoryboardUI(_currentStoryboardRoot);
                CanvasArea.RefreshJsonView();
            }
            // 如果点击了取消，对象直接烟消云散，列表里干干净净！
        }
    }
}