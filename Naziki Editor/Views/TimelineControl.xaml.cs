using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Common;
using Naziki_Editor.Core.Messaging;
using Naziki_Editor.Core.Storyboard;
using Naziki_Editor.Core.Shortcuts;
using Naziki_Editor.Models;
using Naziki_Editor.UI.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Naziki_Editor.Views
{
    public partial class TimelineControl : UserControl, IShortcutAware
    {
        public ShortcutContext ShortcutContext => ShortcutContext.Timeline;
        public bool OnShortcutFocusGained() => true;
        public void OnShortcutFocusLost() { }

        // ==========================================
        // 🌟 核心引擎与基建锁
        // ==========================================
        private bool _isSyncingScroll = false;
        private double _pixelsPerSecond = 100.0;
        private const double MinPixelsPerSecond = 10.0;
        private const double MaxPixelsPerSecond = 1000.0;
        private double _totalDurationSeconds = 60.0;
        private bool _isDraggingPlayhead = false;
        private double _currentPlayheadSeconds = 0.0;
        private string _lastTimeText = "";

        private State.ProjectDataContext _context = null; // 记住当前的大本营上下文！
        private TimelineViewModel _viewModel;
        private IAudioSyncEngine _audioEngine;
        private IMessageBroker _messageBroker;
        private IDialogService _dialogService;
        private UI.Rendering.NoteVisualEngine _noteVisualEngine;
        private IStoryboardRepository _storyboardRepository;
        private IPropertyEditorService _propertyEditorService;
        private UI.Rendering.GlobalRenderEngine _renderEngine;
        private INotificationService _notificationService;

        // ✨ 追加：时空隔离注册表结构，记住每个轨道在全局的物理辖区边界
        private class TrackRegistryItem
        {
            public Border TrackBorder { get; set; }
            public TextBlock HeaderTextBlock { get; set; } 
            public TimelineTrackGroupModel Group { get; set; }
            public TimelineTrackModel Track { get; set; }
        }
        private List<TrackRegistryItem> _upperTrackRegistry = new List<TrackRegistryItem>(); // 上半宇宙（画面实体）
        private List<TrackRegistryItem> _lowerTrackRegistry = new List<TrackRegistryItem>(); // 下半宇宙（控制器）

        // 🌍 宇宙数据源：全景与微观的所有轨道，全靠它驱动！
        public ObservableCollection<TimelineTrackGroupModel> TrackGroups { get; private set; } = new ObservableCollection<TimelineTrackGroupModel>();
        // ✨ 追加：向大本营汇报“某对象被选中”的神经接口
        public event Action<object> OnTimelineObjectSelected;
        // 🚀 追加：向大本营汇报“请求打开属性编辑器”的神经接口 (Ctrl+单击)
        public event Action<object> OnTimelineRequestPropertyEditor;
        public TimelineControl()
        {
            InitializeComponent();
        }

        public TimelineControl(IAudioSyncEngine audioEngine, IMessageBroker messageBroker, IDialogService dialogService, UI.Rendering.NoteVisualEngine noteVisualEngine, IStoryboardRepository storyboardRepository, IPropertyEditorService propertyEditorService, UI.Rendering.GlobalRenderEngine renderEngine, INotificationService notificationService) : this()
        {
            Initialize(audioEngine, messageBroker, dialogService, noteVisualEngine, storyboardRepository, propertyEditorService, renderEngine, notificationService);
        }

        public void Initialize(IAudioSyncEngine audioEngine, IMessageBroker messageBroker, IDialogService dialogService, UI.Rendering.NoteVisualEngine noteVisualEngine, IStoryboardRepository storyboardRepository, IPropertyEditorService propertyEditorService, UI.Rendering.GlobalRenderEngine renderEngine, INotificationService notificationService)
        {
            _audioEngine = audioEngine;
            _messageBroker = messageBroker;
            _dialogService = dialogService;
            _noteVisualEngine = noteVisualEngine;
            _storyboardRepository = storyboardRepository;
            _propertyEditorService = propertyEditorService;
            _renderEngine = renderEngine;
            _notificationService = notificationService;
            _viewModel = new TimelineViewModel(_messageBroker);
            DataContext = _viewModel;
            InitializeAudioEngine();
            UpdateTimelineWidth();
        }

        public double TotalTrackWidth => _totalDurationSeconds * _pixelsPerSecond + 200;

        // =========================================================================
        // 📡 神级联机中枢：一键接通底层大本营，全自动生成排版！
        // =========================================================================
        public void LoadStoryboardTimeline(State.ProjectDataContext context)
        {
            _context = context;
            var calculatedGroups = new UI.Services.TimelineDataEngine().BuildMacroTimeline(context);

            TrackGroups.Clear();
            foreach (var g in calculatedGroups)
            {
                TrackGroups.Add(g);
            }

            RefreshTimelineUI();
            DrawNoteRuler();
        }

        // =========================================================================
        // 🎨 终极渲染引擎：根据 TrackGroups 数据源，傻瓜式平地起高楼！
        // =========================================================================
        public void RefreshTimelineUI()
        {
            if (TrackHeadersContainer == null || TrackGroupsContainer == null) return;

            _upperTrackRegistry.Clear();
            _lowerTrackRegistry.Clear();

            TrackHeadersContainer.Children.Clear();
            TrackGroupsContainer.Children.Clear();
            if (BottomTrackHeadersContainer != null) BottomTrackHeadersContainer.Children.Clear();
            if (BottomTrackGroupsContainer != null) BottomTrackGroupsContainer.Children.Clear();

            var sortedGroups = TrackGroups.OrderByDescending(g => g.GroupIndex).ToList();

            foreach (var group in sortedGroups)
            {
                StackPanel targetHeader = group.GroupIndex >= 0 ? TrackHeadersContainer : BottomTrackHeadersContainer;
                StackPanel targetTrack = group.GroupIndex >= 0 ? TrackGroupsContainer : BottomTrackGroupsContainer;

                targetHeader ??= TrackHeadersContainer;
                targetTrack ??= TrackGroupsContainer;

                var headerLeft = new Border
                {
                    Height = 26,
                    Background = (Brush)Application.Current.FindResource("MenuBgColor"),
                    BorderBrush = (Brush)Application.Current.FindResource("BorderColor"),
                    BorderThickness = new Thickness(0, 0, 0, 1)
                };
                headerLeft.Child = new TextBlock
                {
                    Text = group.GroupName,
                    Foreground = (Brush)Application.Current.FindResource("HighlightBorderColor"),
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(5, 0, 0, 0)
                };
                targetHeader.Children.Add(headerLeft);

                var headerRight = new Border
                {
                    Height = 26,
                    Background = (Brush)Application.Current.FindResource("MenuBgColor"),
                    BorderBrush = (Brush)Application.Current.FindResource("BorderColor"),
                    BorderThickness = new Thickness(0, 0, 0, 1)
                };
                targetTrack.Children.Add(headerRight);

                if (!group.IsExpanded) continue;

                var sortedTracks = group.SortTracksAscending
                    ? group.Tracks.OrderBy(t => t.TrackIndex).ToList()
                    : group.Tracks.OrderByDescending(t => t.TrackIndex).ToList();

                foreach (var track in sortedTracks)
                {
                    var headerText = new TextBlock
                    {
                        Text = track.TrackName,
                        Foreground = (Brush)Application.Current.FindResource("MainTextColor"),
                        FontSize = 11,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(15, 0, 0, 0)
                    };
                    var trackLeft = new Border
                    {
                        Height = 40,
                        BorderBrush = (Brush)Application.Current.FindResource("BorderColor"),
                        BorderThickness = new Thickness(0, 0, 0, 1),
                        Child = headerText
                    };
                    targetHeader.Children.Add(trackLeft);

                    var trackCanvas = new Canvas
                    {
                        Height = 40,
                        Background = Brushes.Transparent,
                        ClipToBounds = true,
                        Width = _totalDurationSeconds * _pixelsPerSecond + 200
                    };
                    var trackRight = new Border
                    {
                        Height = 40,
                        BorderBrush = (Brush)Application.Current.FindResource("BorderColor"),
                        BorderThickness = new Thickness(0, 0, 0, 1),
                        Child = trackCanvas
                    };

                    var registryItem = new TrackRegistryItem
                    {
                        TrackBorder = trackRight,
                        HeaderTextBlock = headerText,
                        Group = group,
                        Track = track
                    };
                    if (group.GroupIndex >= 0) _upperTrackRegistry.Add(registryItem);
                    else _lowerTrackRegistry.Add(registryItem);

                    targetTrack.Children.Add(trackRight);

                    foreach (var clip in track.Clips)
                    {
                        var clipCtrl = new TimelineClipControl();
                        clipCtrl.Tag = clip;
                        clipCtrl.Init(clip, _context, _pixelsPerSecond, clip.TrackIndex, 999, _noteVisualEngine);

                        clipCtrl.OnRequestDetailedEditMode += OnClipRequestDetailedEdit;
                        clipCtrl.OnClipSelected += OnClipSelected;
                        clipCtrl.OnRequestPropertyEditor += OnClipRequestPropertyEditor;
                        clipCtrl.OnMacroGridDrag += ClipCtrl_OnMacroGridDrag;

                        bool isGlobalController =
                            (clip.AssociatedObject is C2SceneController || clip.AssociatedObject is C2NoteController) &&
                            string.IsNullOrEmpty(clip.AssociatedObject.TargetId);

                        if (isGlobalController)
                        {
                            Canvas.SetLeft(clipCtrl, 0);
                            Canvas.SetTop(clipCtrl, 6);
                            clipCtrl.Width = _totalDurationSeconds * _pixelsPerSecond + 200;
                        }
                        else
                        {
                            Canvas.SetLeft(clipCtrl, clip.StartTime * _pixelsPerSecond);
                            Canvas.SetTop(clipCtrl, 6);

                            double clipDuration = clip.EndTime - clip.StartTime;
                            if (clipDuration > 300) clipDuration = 300;
                            clipCtrl.Width = Math.Max(10, clipDuration * _pixelsPerSecond);
                        }

                        trackCanvas.Children.Add(clipCtrl);
                    }
                }
            }
        }

        // =========================================================================
        // 🎨 ItemsControl 事件：当 TimelineClipControl 被加载时初始化
        // =========================================================================
        private void OnClipControlLoaded(object sender, RoutedEventArgs e)
        {
            var clipCtrl = (TimelineClipControl)sender;
            var clipModel = clipCtrl.DataContext as TimelineClipModel;
            if (clipModel == null) return;

            // Skip if already initialized for this model
            if (clipCtrl.Tag is TimelineClipModel lastModel && ReferenceEquals(lastModel, clipModel)) return;

            clipCtrl.Tag = clipModel;
            clipCtrl.Init(clipModel, _context, _pixelsPerSecond, clipModel.TrackIndex, 999, _noteVisualEngine);

            clipCtrl.OnRequestDetailedEditMode -= OnClipRequestDetailedEdit;
            clipCtrl.OnClipSelected -= OnClipSelected;
            clipCtrl.OnRequestPropertyEditor -= OnClipRequestPropertyEditor;
            clipCtrl.OnMacroGridDrag -= ClipCtrl_OnMacroGridDrag;

            clipCtrl.OnRequestDetailedEditMode += OnClipRequestDetailedEdit;
            clipCtrl.OnClipSelected += OnClipSelected;
            clipCtrl.OnRequestPropertyEditor += OnClipRequestPropertyEditor;
            clipCtrl.OnMacroGridDrag += ClipCtrl_OnMacroGridDrag;
        }

        private void OnClipRequestDetailedEdit(TimelineClipModel targetModel) => EnterDetailedEditMode(targetModel);
        private void OnClipSelected(TimelineClipModel targetModel) => OnTimelineObjectSelected?.Invoke(targetModel.AssociatedObject);
        private void OnClipRequestPropertyEditor(TimelineClipModel targetModel) => OnTimelineRequestPropertyEditor?.Invoke(targetModel.AssociatedObject);

        // =========================================================================
        // 📡 ✨ 全景宏观换轨隔离雷达（核心换层与隔离防穿透盾落地！）
        // =========================================================================
        private void ClipCtrl_OnMacroGridDrag(TimelineClipControl clipCtrl, MouseEventArgs e, TimelineClipControl.MacroDragStage stage)
        {
            if (_context == null || clipCtrl.Tag is not TimelineClipModel clipModel) return;

            var entity = clipModel.AssociatedObject;
            if (entity == null) return;

            // 🛡️ 1. 启动基因身份识别：区分当前方块是【画面视觉实体】还是【逻辑控制器】
            bool isUpperZone = (entity is Models.C2Sprite || entity is Models.C2Text || entity is Models.C2Video || entity is Models.C2Line);

            // 根据身份，将雷达指针分流到对应的安全宇宙，异种图层绝不交叉，实现绝对防穿透！
            var registry = isUpperZone ? _upperTrackRegistry : _lowerTrackRegistry;
            var container = isUpperZone ? TrackGroupsContainer : BottomTrackGroupsContainer;

            if (container == null || registry.Count == 0) return;

            // 2. 🎯 获取当前鼠标相对于对应 StackPanel 容器的实时物理 Y 坐标
            Point mousePos = e.GetPosition(container);

            // 3. 🔮 顺位力场测算：全量扫盘当前可见的所有合法轨道，找出垂直距离最近的那条“真命轨道”
            TrackRegistryItem closestItem = null;
            double minDistance = double.MaxValue;

            foreach (var item in registry)
            {
                try
                {
                    if (item.TrackBorder == null) continue;
                    // 换算出该轨道相对于父级容器的绝对 Y 原点
                    var transform = item.TrackBorder.TransformToAncestor(container);
                    Point trackTopLeft = transform.Transform(new Point(0, 0));

                    double trackMidY = trackTopLeft.Y + (item.TrackBorder.ActualHeight / 2.0);
                    double distance = Math.Abs(mousePos.Y - trackMidY);

                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        closestItem = item;
                    }
                }
                catch { }
            }

            // 4. 🚀 根据拖拽阶段，执行动态换轨或最终时空落盘
            if (stage == TimelineClipControl.MacroDragStage.Started)
            {
                return; // 预留开始拖拽特效空间
            }
            if (stage == TimelineClipControl.MacroDragStage.Moving)
            {
                return; // 预留中途拖拽悬停高亮轨道特效空间
            }
            if (stage == TimelineClipControl.MacroDragStage.Completed)
            {
                // 如果松开鼠标时完全没有命中任何有效边界（理论上 closestItem 必然有保底），则安全重回原点
                if (closestItem == null)
                {
                    if (closestItem == null)
                    {
                        Canvas.SetLeft(clipCtrl, clipModel.StartTime * _pixelsPerSecond);
                        Canvas.SetTop(clipCtrl, 6);
                        return;
                    }
                }

                bool dataChanged = false;

                // 🌌 场景 A：画面实体跨越轨道组穿梭（改写 Layer 和 Order）
                if (isUpperZone)
                {
                    // 核心几何公式：根据 GroupIndex 完美反算出底层的 Layer (10->0, 20->1, 30->2)
                    int targetLayer = (closestItem.Group.GroupIndex / 10) - 1;
                    int targetOrder = closestItem.Track.TrackIndex;

                    var baseState = entity.GetBaseState();
                    if (baseState != null)
                    {
                        int currentLayer = 0;
                        int currentOrder = clipModel.TrackIndex;
                        if (_propertyEditorService.TryGetValue(baseState, "Layer", out object lObj))
                            currentLayer = Convert.ToInt32(lObj);

                        // 只有当打谱师真的跨越了物理边界，才触发时空改写
                        if (currentLayer != targetLayer || currentOrder != targetOrder)
                        {
                            if (_propertyEditorService.TrySetValue(baseState, "Layer", targetLayer)) dataChanged = true;
                            if (_propertyEditorService.TrySetValue(baseState, "Order", targetOrder)) dataChanged = true;
                        }
                    }
                }
                // 🎛️ 场景 B：控制器在专属隔离区上下调换顺位轨道
                else
                {
                    var root = _context?.Storyboard;
                    int targetIndex = closestItem.Track.TrackIndex;
                    int currentIndex = clipModel.TrackIndex;

                    if (root != null && currentIndex != targetIndex)
                    {
                        bool swapped = false;
                        if (entity is Models.C2SceneController || entity is Models.C2NoteController)
                        {
                            var list = _storyboardRepository.GetListByType(root, entity.GetType()) as System.Collections.IList;
                            if (list != null && currentIndex >= 0 && currentIndex < list.Count && targetIndex >= 0 && targetIndex < list.Count)
                            {
                                _storyboardRepository.MoveEntityToIndex(root, entity, targetIndex);
                                swapped = true;
                            }
                        }

                        if (swapped)
                        {
                            _context.MarkAsModified();

                            // 🚀 0ms 极限优化：局部 UI DOM 互换法术！绝不触发全局重绘！
                            var oldRegistryItem = _lowerTrackRegistry.FirstOrDefault(r => r.Track.TrackIndex == currentIndex);
                            var newRegistryItem = closestItem; // 目标轨道

                            if (oldRegistryItem != null && newRegistryItem != null)
                            {
                                var oldCanvas = oldRegistryItem.TrackBorder.Child as Canvas;
                                var newCanvas = newRegistryItem.TrackBorder.Child as Canvas;

                                // 抓出目标轨道里原来住着的那个方块 (也就是被挤掉的那个)
                                var otherClipCtrl = newCanvas?.Children.OfType<TimelineClipControl>().FirstOrDefault();

                                // 🧳 物理搬家
                                if (oldCanvas != null) oldCanvas.Children.Remove(clipCtrl);
                                if (newCanvas != null && otherClipCtrl != null) newCanvas.Children.Remove(otherClipCtrl);

                                // 🔄 互相拎到对方的房间里，并强制解除幽灵坐标！
                                if (newCanvas != null)
                                {
                                    newCanvas.Children.Add(clipCtrl);
                                    // ✨ 修复：落地瞬间，重置被拖动方块的局部坐标！
                                    Canvas.SetLeft(clipCtrl, 0);
                                    Canvas.SetTop(clipCtrl, 6);
                                }
                                if (oldCanvas != null && otherClipCtrl != null)
                                {
                                    oldCanvas.Children.Add(otherClipCtrl);
                                    // ✨ 修复：落地瞬间，重置被挤掉方块的局部坐标！
                                    Canvas.SetLeft(otherClipCtrl, 0);
                                    Canvas.SetTop(otherClipCtrl, 6);
                                }

                                // 同步更新两个方块的模型内驻留索引，并让方块自己刷新状态！
                                clipModel.TrackIndex = targetIndex;
                                if (otherClipCtrl?.Tag is TimelineClipModel otherModel)
                                {
                                    otherModel.TrackIndex = currentIndex;
                                    otherClipCtrl.Init(otherModel, _context, _pixelsPerSecond, currentIndex, 999);
                                }
                                clipCtrl.Init(clipModel, _context, _pixelsPerSecond, targetIndex, 999);

                                // ✨ 核心修复：直接交换左侧的轨道名字！
                                string tempText = oldRegistryItem.HeaderTextBlock.Text;
                                oldRegistryItem.HeaderTextBlock.Text = newRegistryItem.HeaderTextBlock.Text;
                                newRegistryItem.HeaderTextBlock.Text = tempText;
                            }

                            return; // 时空互换完毕，直接结束！
                        }
                    }
                }

                // 5. 💫 数据洗净重生闭环
                if (dataChanged)
                {
                    _context?.MarkAsModified();

                    // 🧙‍♂️ 0ms 跨轨物理搬家法术：直接在 UI 树上完成局部宿舍迁移，彻底消灭换轨大卡顿！
                    if (clipCtrl.Parent is Canvas oldCanvas)
                    {
                        oldCanvas.Children.Remove(clipCtrl); // 搬出旧宿舍 Canvas
                    }

                    if (closestItem.TrackBorder.Child is Canvas newCanvas)
                    {
                        newCanvas.Children.Add(clipCtrl); // 丝滑拎包入住新宿舍 Canvas！
                    }

                    // 1:1 同步刷新模型内存里的当前轨道索引绑定
                    clipModel.TrackIndex = closestItem.Track.TrackIndex;

                    // 🚀 核心状态同步：重新呼叫 Init 让方块自己刷新物理跨度、基因嗅探并完美适应新宿舍！
                    clipCtrl.Init(clipModel, _context, _pixelsPerSecond, closestItem.Track.TrackIndex, 999);

                    // 轨道高度固定居中留白修正，防止被 Init 内部的旧有绝对波及带偏
                    Canvas.SetTop(clipCtrl, 6);
                }
                else
                {
                    // 🌟 同轨平移或纯单点，优雅在原地立正对齐
                    bool isGlobalController = (clipModel.AssociatedObject is C2SceneController || clipModel.AssociatedObject is C2NoteController) && string.IsNullOrEmpty(clipModel.AssociatedObject.TargetId);

                    if (isGlobalController)
                    {
                        Canvas.SetLeft(clipCtrl, 0); // 永远在最左边
                        clipCtrl.Width = _totalDurationSeconds * _pixelsPerSecond + 200; // 保持撑满
                    }
                    else
                    {
                        Canvas.SetLeft(clipCtrl, clipModel.StartTime * _pixelsPerSecond);
                        double clipDuration = clipModel.EndTime - clipModel.StartTime;
                        if (clipDuration > 300) clipDuration = 300;
                        clipCtrl.Width = Math.Max(10, clipDuration * _pixelsPerSecond);
                    }

                    Canvas.SetTop(clipCtrl, 6);
                }
            }
        }






        // ==========================================
        // 🔬 微观变身与退出
        // ==========================================
        // 🚀 多标签宇宙：微观变身引擎重写
        private void EnterDetailedEditMode(TimelineClipModel targetModel)
        {



            // 1. 查户口：✨ 【完美修复穿模】：温柔地检查类型，不要强转！
            foreach (var element in TimelineTabs.Items)
            {
                if (element is TabItem item && item.Tag == targetModel.AssociatedObject)
                {
                    TimelineTabs.SelectedItem = item;
                    return;
                }
            }

            // 2. 凭空捏造全新的宇宙标签
            var newTab = new TabItem
            {
                Tag = targetModel.AssociatedObject,
                Foreground = Brushes.MediumPurple,
                FontWeight = FontWeights.Bold
            };

            // 🌟 纯代码捏出一个自带“✖ 关闭按钮”的漂亮头部！
            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
            headerPanel.Children.Add(new TextBlock { Text = $"🎬 {targetModel.DisplayName}", Margin = new Thickness(0, 0, 10, 0) });
            var closeBtn = new Button { Content = "✖", Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Brushes.Gray, Cursor = Cursors.Hand };
            closeBtn.Click += (s, e) => { TimelineTabs.Items.Remove(newTab); }; // 点 X 就销毁这个时空！
            headerPanel.Children.Add(closeBtn);
            newTab.Header = headerPanel;

            // 3. 召唤大大的微观神兵
            var detailEditor = new TimelineClip.ClipDetailedEditor(_messageBroker, _dialogService, _noteVisualEngine);
            // 直接让百叶窗自己去读数据画图，完全不污染主轴的 TrackGroups！
            detailEditor.LoadClipData(targetModel, _context, _pixelsPerSecond);

            newTab.Content = detailEditor;

            // 4. 把新宇宙挂载到战舰上并跳转
            TimelineTabs.Items.Add(newTab);
            TimelineTabs.SelectedItem = newTab;
        }



        // =========================================================================================
        // 🎵 音频基建、滚动同步、游标换算、缩放（此处完美保留大大之前的顶级基建，已剔除旧有硬编码冲突）
        // =========================================================================================

        private void InitializeAudioEngine()
        {
            _renderEngine.OnRenderTick += () => {
                if (_audioEngine.IsPlaying && !_isDraggingPlayhead)
                    UpdatePlayheadPosition(_audioEngine.GetCurrentSmoothTime() * _pixelsPerSecond);
            };

            _audioEngine.OnTimeChanged += (currentSeconds) => {
                if (!_audioEngine.IsPlaying && !_isDraggingPlayhead)
                    UpdatePlayheadPosition(currentSeconds * _pixelsPerSecond);
            };

            _audioEngine.OnPlayStateChanged += (isPlaying) => {
                BtnPlay.Foreground = isPlaying ? Brushes.LightGreen : (Brush)Application.Current.Resources["MainTextColor"];
            };

            _audioEngine.OnAudioLoaded += () => {
                if (BtnImportAudio != null) BtnImportAudio.Visibility = Visibility.Collapsed;
                if (_totalDurationSeconds < _audioEngine.Duration)
                {
                    _totalDurationSeconds = _audioEngine.Duration + 2.0;
                    UpdateTimelineWidth();
                }
                // ✨ 小艾的终极补丁：不论是否拉长了时间轴，音乐加载完必须强制画波形！
                // 并且使用 BeginInvoke 延迟一丢丢，确保 UI 的宽度已经完全舒展成型，防止宽度为 0 画不出东西~
                Application.Current.Dispatcher.BeginInvoke(new Action(() => {
                    DrawWaveform();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            };
        }

        private void UpdatePlayheadPosition(double xPos)
        {
            double maxWidth = _totalDurationSeconds * _pixelsPerSecond;
            if (xPos < 0) xPos = 0;
            if (xPos > maxWidth) xPos = maxWidth;

            // 1. 🌟 获取主轴当前的真实物理滚动偏移量（摄像机位置）
            double currentOffset = ScrollTimelineTracks != null ? ScrollTimelineTracks.HorizontalOffset : 0;

            // 2. ✨ 【时空相对论】：红线游标的物理 X 减去摄像机的偏移，才是它在屏幕上真正的正确位置！
            if (TransRulerHead != null) TransRulerHead.X = xPos - currentOffset;

            // 3. 蓝线（全局缩略图游标）照旧
            if (AudioMinimapGrid != null && AudioPlayheadLine != null && _totalDurationSeconds > 0)
            {
                double ratio = xPos / maxWidth; // 修复：直接用 xPos，更精准
                AudioPlayheadLine.X1 = ratio * AudioMinimapGrid.ActualWidth;
                AudioPlayheadLine.X2 = AudioPlayheadLine.X1;
            }

            _currentPlayheadSeconds = xPos / _pixelsPerSecond;
            UpdatePlaybackTimeDisplay(_currentPlayheadSeconds);

            // 4. ✨ 智能跟随摄像机（居中推流）
            if (_audioEngine.IsPlaying && !_isDraggingPlayhead && ScrollTimelineTracks != null)
            {
                double viewWidth = ScrollTimelineTracks.ViewportWidth;
                if (viewWidth > 0)
                {
                    // 🌟 核心：判断游标在屏幕上的实际视觉位置！
                    double visualX = xPos - currentOffset;

                    // ➡️ 向右越界：当游标距离右侧边缘不足 20 像素时，触发居中推流
                    if (visualX > viewWidth - 20)
                    {
                        double targetOffset = xPos - (viewWidth / 2.0);
                        ScrollTimelineTracks.ScrollToHorizontalOffset(targetOffset);
                    }
                    // ⬅️ 向左越界：当游标跑到屏幕左侧外面时，同样触发居中
                    else if (visualX < 0)
                    {
                        double targetOffset = Math.Max(0, xPos - (viewWidth / 2.0));
                        ScrollTimelineTracks.ScrollToHorizontalOffset(targetOffset);
                    }
                }
            }
        }





        public void UpdatePlaybackTimeDisplay(double currentSeconds)
        {
            if (TxtCurrentTime != null)
            {
                string newText = currentSeconds.ToString("0.000") + "s";
                if (_lastTimeText != newText) { TxtCurrentTime.Text = newText; _lastTimeText = newText; }
            }
        }

        // --- 以下为原汁原味的拖拽和绘制交互，完美保留 ---
        private void Ruler_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border rulerBorder)
            {
                // ✨ 我们点到的只是屏幕坐标，必须加上底下的真实滚动距离，才是绝对时间坐标！
                double visualX = e.GetPosition(rulerBorder).X;
                double offset = ScrollTimelineTracks != null ? ScrollTimelineTracks.HorizontalOffset : 0;

                UpdatePlayheadPosition(visualX + offset);
                _audioEngine.Seek(_currentPlayheadSeconds);
            }
        }

        private void Playhead_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingPlayhead = true;
            PlayheadMarker.CaptureMouse();
            e.Handled = true;
        }

        private void Playhead_MouseMove(object sender, MouseEventArgs e)
        {
            // ✨ 修复：原本这里写的是 is Border rBorder，但其实 XAML 里装它的是 Grid，导致拖拽彻底失效！
            if (_isDraggingPlayhead && ScrollRuler != null)
            {
                // 同理，鼠标拖拽的是屏幕坐标，必须换算成绝对坐标！
                double visualX = e.GetPosition(ScrollRuler).X;
                double offset = ScrollTimelineTracks != null ? ScrollTimelineTracks.HorizontalOffset : 0;

                UpdatePlayheadPosition(visualX + offset);
            }
        }

        private void Playhead_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingPlayhead)
            {
                _isDraggingPlayhead = false;
                PlayheadMarker.ReleaseMouseCapture();
                _audioEngine.Seek(_currentPlayheadSeconds);
            }
        }

        private void BtnPlay_Click(object sender, RoutedEventArgs e) => _audioEngine.Play();
        private void BtnPause_Click(object sender, RoutedEventArgs e) => _audioEngine.Pause();

        private async void BtnImportAudio_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog { Filter = "音频文件 (*.mp3;*.wav;*.ogg)|*.mp3;*.wav;*.ogg", Title = "请选择关卡音乐" };
            if (openFileDialog.ShowDialog() == true) { if (BtnImportAudio != null) BtnImportAudio.Visibility = Visibility.Collapsed; await _audioEngine.LoadAudioAsync(openFileDialog.FileName); }
        }

        private void AudioMinimapGrid_SizeChanged(object sender, SizeChangedEventArgs e) { DrawWaveform(); UpdateAudioViewportBox(); }

        private void DrawWaveform()
        {
            if (WaveformPath == null || _audioEngine.WaveformSamples == null || AudioMinimapGrid.ActualWidth <= 0) return;
            var samples = _audioEngine.WaveformSamples;
            double width = AudioMinimapGrid.ActualWidth, height = 40, midY = height / 2;
            int step = Math.Max(1, samples.Length / (int)width);
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(0, midY), false, false);
                for (int i = 0; i < samples.Length; i += step) ctx.LineTo(new Point((double)i / samples.Length * width, midY - (samples[i] * midY)), true, false);
            }
            geometry.Freeze(); WaveformPath.Data = geometry;
        }

        private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // 同步上半部分
            if (sender == ScrollTimelineTracks && ScrollTrackHeaders != null)
                ScrollTrackHeaders.ScrollToVerticalOffset(e.VerticalOffset);

            // ✨ 同步下半部分
            if (sender == ScrollBottomTimelineTracks && ScrollBottomTrackHeaders != null)
                ScrollBottomTrackHeaders.ScrollToVerticalOffset(e.VerticalOffset);

            if (_isSyncingScroll || Math.Abs(e.HorizontalChange) < 0.001) return;
            _isSyncingScroll = true;

            if (sender != ScrollRuler && ScrollRuler != null) ScrollRuler.ScrollToHorizontalOffset(e.HorizontalOffset);
            if (sender != ScrollTimelineTracks && ScrollTimelineTracks != null) ScrollTimelineTracks.ScrollToHorizontalOffset(e.HorizontalOffset);

            // ✨ 同步下半部分的横向滚动
            if (sender != ScrollBottomTimelineTracks && ScrollBottomTimelineTracks != null) ScrollBottomTimelineTracks.ScrollToHorizontalOffset(e.HorizontalOffset);

            // 🚀 【神级补线】：让包着底部音符刻度尺的容器，也跟着大部队一起绝对横向平移！
            if (sender != ScrollNotes && ScrollNotes != null) ScrollNotes.ScrollToHorizontalOffset(e.HorizontalOffset);

            // 让顶部的刻度尺 (RulerCanvas) 也跟着反向平移...

            // 让顶部的刻度尺 (RulerCanvas) 也跟着反向平移，保证上方时间线和下方轨道永远对齐！
            if (RulerCanvas != null)
            {
                if (!(RulerCanvas.RenderTransform is TranslateTransform))
                    RulerCanvas.RenderTransform = new TranslateTransform();

                ((TranslateTransform)RulerCanvas.RenderTransform).X = -ScrollTimelineTracks.HorizontalOffset;
            }

            // 确保在您拖动底部滚动条时，红游标也能死死地钉在正确的相对位置上！
            if (TransRulerHead != null)
            {
                TransRulerHead.X = _currentPlayheadSeconds * _pixelsPerSecond - ScrollTimelineTracks.HorizontalOffset;
            }


            _isSyncingScroll = false;
            UpdateAudioViewportBox();
        }

        private void OnTimelineMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // 🛡️ 如果当前在微观时光屋里，不拦截滚轮事件
            if (TimelineTabs != null && TimelineTabs.SelectedIndex > 0) return;

            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                e.Handled = true;
                if (e.Delta > 0)
                    ZoomIn();
                else
                    ZoomOut();
            }
        }

        /// <summary>
        /// 时间轴放大（供快捷键系统调用）。
        /// </summary>
        public void ZoomIn()
        {
            double newPixels = _pixelsPerSecond * 1.2;
            if (Math.Abs(newPixels - _pixelsPerSecond) > 0.01)
            {
                _pixelsPerSecond = Math.Min(MaxPixelsPerSecond, newPixels);
                UpdateTimelineWidth();
            }
        }

        /// <summary>
        /// 时间轴缩小（供快捷键系统调用）。
        /// </summary>
        public void ZoomOut()
        {
            double newPixels = _pixelsPerSecond / 1.2;
            if (Math.Abs(newPixels - _pixelsPerSecond) > 0.01)
            {
                _pixelsPerSecond = Math.Max(MinPixelsPerSecond, newPixels);
                UpdateTimelineWidth();
            }
        }

        /// <summary>
        /// 重置缩放至默认值（供快捷键系统调用）。
        /// </summary>
        public void ResetZoom()
        {
            if (Math.Abs(_pixelsPerSecond - 100.0) > 0.01)
            {
                _pixelsPerSecond = 100.0;
                UpdateTimelineWidth();
            }
        }

        // ==========================================
        // ▶️ 播放控制（供快捷键系统调用）
        // ==========================================

        /// <summary>
        /// 切换播放/暂停状态（供快捷键系统调用）。
        /// </summary>
        public void TogglePlayPause()
        {
            if (_audioEngine == null) return;

            if (_audioEngine.IsPlaying)
                _audioEngine.Pause();
            else
                _audioEngine.Play();
        }

        /// <summary>
        /// 跳转到时间轴开头（供快捷键系统调用）。
        /// </summary>
        public void GoToStart()
        {
            if (_audioEngine != null)
            {
                _audioEngine.Seek(0);
                _currentPlayheadSeconds = 0;
            }
        }

        /// <summary>
        /// 跳转到时间轴结尾（供快捷键系统调用）。
        /// </summary>
        public void GoToEnd()
        {
            if (_audioEngine != null && _audioEngine.IsLoaded)
            {
                double endTime = _audioEngine.Duration;
                _audioEngine.Seek(endTime);
                _currentPlayheadSeconds = endTime;
            }
        }


        // ==========================================\
        // 🧙‍♂️ 唤醒俄罗斯方块：一键智能整理所有重叠图层！
        // ==========================================\
        private void BtnAutoLayout_Click(object sender, RoutedEventArgs e)
        {
            if (_context == null || !_context.HasStoryboard) return;

            // 1. 让大脑执行俄罗斯方块无损排版法术！
            Core.Timeline.TimelineLayoutEngine.AutoAssignOrderForVisualEntities(_context);

            // 2. 标记大本营数据已修改（这样左侧列表和JSON预览也会同步，且触发保存状态）
            _context.MarkAsModified();

            // 3. 重新读取并刷新整个时间轴宇宙！
            LoadStoryboardTimeline(_context);

            _notificationService.ShowSuccess("✨ 智能排版完成！所有挤在一起的方块已经根据时间自动分配到不同的 Order 轨道啦！");
        }














        private void UpdateTimelineWidth()
        {
            double newWidth = _totalDurationSeconds * _pixelsPerSecond + 200;
            if (ScrollRuler?.Content is Border rBorder) rBorder.Width = newWidth;
            // 极速坐标位移法术！
            FastUpdateZoomVisuals();
            // 标尺里面的白线不多，暂时保留它的重绘，不会卡顿
            DrawTimelineRuler();

            // 仅更新上方迷你缩略图的“红色视野框”位置
            UpdateAudioViewportBox();
        }

        private void DrawTimelineRuler()
        {
            if (RulerCanvas == null) return;
            RulerCanvas.Children.Clear();
            double majorStep = _pixelsPerSecond >= 100 ? 1.0 : (_pixelsPerSecond >= 40 ? 5.0 : 10.0);
            double minorStep = majorStep / 10.0;

            for (double time = 0; time <= _totalDurationSeconds; time += minorStep)
            {
                double xPos = time * _pixelsPerSecond;
                bool isMajor = Math.Abs(time % majorStep) < 0.001 || Math.Abs((time % majorStep) - majorStep) < 0.001;
                RulerCanvas.Children.Add(new Line { X1 = xPos, Y1 = isMajor ? 15 : 24, X2 = xPos, Y2 = 30, Stroke = (Brush)Application.Current.Resources["BorderColor"], StrokeThickness = isMajor ? 1.2 : 0.6, Opacity = isMajor ? 1 : 0.5 });
                if (isMajor) RulerCanvas.Children.Add(new TextBlock { Text = $"{time:0.#}s", FontSize = 9, Foreground = (Brush)Application.Current.Resources["SecTextColor"], RenderTransform = new TranslateTransform { X = xPos + 4, Y = 2 } });
            }
        }


        // ==========================================
        // 🎹 音符雷达尺：在底部画布精准画出谱面音符！(大一统工厂模式接入)
        // ==========================================
        public void DrawNoteRuler()
        {
            if (NotePreviewCanvas == null) return;
            NotePreviewCanvas.Children.Clear();

            if (_context == null || !_context.HasChart || _context.Chart.note_list == null) return;

            // 动态对齐物理长度
            double totalWidth = _totalDurationSeconds * _pixelsPerSecond + 200;
            NotePreviewCanvas.Width = totalWidth;

            // 🚀 一键呼叫核心测绘工厂！最后一个参数传 false，代表宏观主轴模式
            _noteVisualEngine.RenderNoteRuler(NotePreviewCanvas, _context.Chart.note_list, _context.TimeEngine, _pixelsPerSecond, false);
        }









        // ==========================================
        // 🏄‍♂️ 滑块联动引擎：绝对精准的坐标系
        // ==========================================
        private void UpdateAudioViewportBox()
        {
            // 1. 防空指针：如果画板还没刷出来，或者总时长是 0，直接返回
            if (AudioMinimapGrid.ActualWidth == 0 || _totalDurationSeconds <= 0 || ScrollTimelineTracks == null) return;

            // 2. 算账：宇宙总长度 vs 当前可见的物理长度
            double totalWidth = _totalDurationSeconds * _pixelsPerSecond + 200;
            double visibleWidth = ScrollTimelineTracks.ViewportWidth == 0 ? AudioMinimapGrid.ActualWidth : ScrollTimelineTracks.ViewportWidth;
            double scale = AudioMinimapGrid.ActualWidth / totalWidth;

            // 3. 赋形：计算滑块的宽度，最小不能低于 10 像素
            AudioViewportBox.Width = Math.Max(10, Math.Min(AudioMinimapGrid.ActualWidth, visibleWidth * scale));

            // ✨ 核心修复：重新接通 Canvas 移动神经！
            Canvas.SetLeft(AudioViewportBox, ScrollTimelineTracks.HorizontalOffset * scale);
        }

        private void AudioViewportBox_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            if (AudioMinimapGrid.ActualWidth == 0 || _totalDurationSeconds <= 0 || ScrollTimelineTracks == null) return;

            double totalWidth = _totalDurationSeconds * _pixelsPerSecond + 200;

            // ✨ 核心修复：根据鼠标水平位移，反算出轨道应该滚动的绝对距离！
            double newOffset = ScrollTimelineTracks.HorizontalOffset + e.HorizontalChange * (totalWidth / AudioMinimapGrid.ActualWidth);

            ScrollTimelineTracks.ScrollToHorizontalOffset(Math.Max(0, Math.Min(newOffset, totalWidth - ScrollTimelineTracks.ViewportWidth)));
        }





        // ==========================================
        // 🚀 ✨ 极速缩放引擎：拒绝摧毁重建，仅更新物理坐标！
        // ==========================================
        private void FastUpdateZoomVisuals()
        {
            double newWidth = _totalDurationSeconds * _pixelsPerSecond + 200;

            // 1. 内部特工法术：穿梭各个轨道，光速修改方块位置
            Action<StackPanel> updateTracks = (container) =>
            {
                if (container == null) return;
                foreach (UIElement child in container.Children)
                {
                    if (child is Border border && border.Child is Canvas trackCanvas)
                    {
                        trackCanvas.Width = newWidth;
                        foreach (UIElement clipObj in trackCanvas.Children)
                        {
                            if (clipObj is TimelineClipControl clipCtrl && clipCtrl.Tag is TimelineClipModel clip)
                            {
                                bool isGlobalController = (clip.AssociatedObject is C2SceneController || clip.AssociatedObject is C2NoteController) && string.IsNullOrEmpty(clip.AssociatedObject.TargetId);

                                if (isGlobalController)
                                {
                                    Canvas.SetLeft(clipCtrl, 0);
                                    clipCtrl.Width = newWidth;
                                }
                                else
                                {
                                    Canvas.SetLeft(clipCtrl, clip.StartTime * _pixelsPerSecond);
                                    double clipDuration = clip.EndTime - clip.StartTime;
                                    if (clipDuration > 300) clipDuration = 300;
                                    clipCtrl.Width = Math.Max(10, clipDuration * _pixelsPerSecond);
                                }
                            }
                        }
                    }
                }
            };

            // 分发给上下两层宇宙
            updateTracks(TrackGroupsContainer);
            updateTracks(BottomTrackGroupsContainer);

            // 2. 光速更新底部音符尺 (全量支持 Image缩放、ID文字跟随、Hold长轨拉伸、以及 Drag 全息虚线极速形变！)
            if (NotePreviewCanvas != null)
            {
                NotePreviewCanvas.Width = newWidth;
                foreach (UIElement child in NotePreviewCanvas.Children)
                {
                    if (child is FrameworkElement fe && fe.Tag is Models.C2Note note)
                    {
                        double seconds = _context.TimeEngine.TickToSeconds(note.tick);
                        double absoluteX = seconds * _pixelsPerSecond;

                        // 📐 【多态时空对齐与拉伸公式】：根据组件的物理形态，执行降维形变算法！
                        if (child is Image img)
                        {
                            // ✨ 极致对齐：自动根据图片当前尺寸（子音符会自动变小）的一半进行精准动态居中！
                            Canvas.SetLeft(img, absoluteX - (img.Width / 2.0));
                        }
                        else if (child is TextBlock)
                        {
                            Canvas.SetLeft(fe, absoluteX - 5.0); // ID文字保持美观居中
                        }
                        else if (child is Line line && line.DataContext is Models.C2Note lastChild)
                        {
                            // 🚀 【神级补线】：从 DataContext 中瞬间抓回隐藏的末端子节点，跨越维度重算物理跨度！
                            double lastChildSeconds = _context.TimeEngine.TickToSeconds(lastChild.tick);
                            line.X1 = absoluteX;                             // 虚线的左端点锁定在滑条头部
                            line.X2 = lastChildSeconds * _pixelsPerSecond;   // 虚线的右端点紧紧咬住最后一位子节点！
                        }
                        else if (child is Rectangle rect)
                        {
                            if (rect.Height == 2)
                            {
                                Canvas.SetLeft(rect, absoluteX);
                                double endSec = _context.TimeEngine.TickToSeconds(note.tick + note.hold_tick);
                                double durSec = endSec - seconds;
                                rect.Width = durSec * _pixelsPerSecond; // Hold 光轨等比拉伸
                            }
                            else
                            {
                                Canvas.SetLeft(rect, absoluteX - (rect.Width / 2.0)); // 兜底方块智能对称
                            }
                        }
                    }
                }
            }
        }
    }
}
