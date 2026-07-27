using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Common;
using Naziki_Editor.Core.Messaging;
using Naziki_Editor.Core.Storyboard;
using Naziki_Editor.Core.Timeline.Models;
using Naziki_Editor.Core.Timeline.EventBlocks.Abstractions;
using Naziki_Editor.Core.Shortcuts;
using Naziki_Editor.Models;
using Naziki_Editor.UI.ViewModels;
using Naziki_Editor.Views.MainTimeline;
using Naziki_Editor.Core.Timeline.Settings;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Newtonsoft.Json;

namespace Naziki_Editor.Views
{
    public partial class TimelineControl : UserControl, IShortcutAware
    {
        public ShortcutContext ShortcutContext =>
            TimelineTabs?.SelectedIndex > 0
                ? ShortcutContext.MicroTimeline
                : ShortcutContext.MainTimeline;
        public bool OnShortcutFocusGained() => true;
        public void OnShortcutFocusLost() { }

        // ==========================================
        // 🌟 核心引擎与基建锁
        // ==========================================
        private bool _isSyncingScroll = false;
        private double _pixelsPerSecond = 100.0;
        private double MinPixelsPerSecond => _timelineSettings?.Current.MinimumPixelsPerSecond ?? 10.0;
        private double MaxPixelsPerSecond => _timelineSettings?.Current.MaximumPixelsPerSecond ?? 1000.0;
        private double _totalDurationSeconds = 60.0;
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
        private IEventBlockService _clipService;

        // Sub-controls for modular timeline rendering
        private TimelineRuler _timelineRuler;
        private TimelinePlayhead _timelinePlayhead;
        private TimelineAudioBar _timelineAudioBar;
        private TimelineNoteRuler _timelineNoteRuler;
        private TimelineTrackRenderer _timelineTrackRenderer;

        // 🌍 宇宙数据源：全景与微观的所有轨道，全靠它驱动！
        public ObservableCollection<MainTimelineGroupViewModel> TrackGroups { get; private set; } = new ObservableCollection<MainTimelineGroupViewModel>();
        // ✨ 追加：向大本营汇报“某对象被选中”的神经接口
        public event Action<object> OnTimelineObjectSelected;
        private EventBlockViewModel? _selectedClipModel;
        private ITimelineSettings? _timelineSettings;
        private string? _timelineClipboardJson;
        private Type? _timelineClipboardType;
        // 🚀 追加：向大本营汇报“请求打开属性编辑器”的神经接口 (Ctrl+单击)
        public event Action<object> OnTimelineRequestPropertyEditor;
        public event Action<MicroEditorContext, TabItem>? OpenMicroTimelineRequested;
        public TimelineControl()
        {
            InitializeComponent();
        }

        public TimelineControl(IAudioSyncEngine audioEngine, IMessageBroker messageBroker, IDialogService dialogService, UI.Rendering.NoteVisualEngine noteVisualEngine, IStoryboardRepository storyboardRepository, IPropertyEditorService propertyEditorService, UI.Rendering.GlobalRenderEngine renderEngine, INotificationService notificationService, IEventBlockService clipService) : this()
        {
            Initialize(audioEngine, messageBroker, dialogService, noteVisualEngine, storyboardRepository, propertyEditorService, renderEngine, notificationService, clipService);
        }

        public void Initialize(IAudioSyncEngine audioEngine, IMessageBroker messageBroker, IDialogService dialogService, UI.Rendering.NoteVisualEngine noteVisualEngine, IStoryboardRepository storyboardRepository, IPropertyEditorService propertyEditorService, UI.Rendering.GlobalRenderEngine renderEngine, INotificationService notificationService, IEventBlockService clipService)
        {
            _audioEngine = audioEngine;
            _messageBroker = messageBroker;
            _dialogService = dialogService;
            _noteVisualEngine = noteVisualEngine;
            _storyboardRepository = storyboardRepository;
            _propertyEditorService = propertyEditorService;
            _renderEngine = renderEngine;
            _notificationService = notificationService;
            _clipService = clipService;
            _timelineSettings = AppServices.GetService<ITimelineSettings>();
            _timelineSettings.Changed += (_, _) => Dispatcher.Invoke(ApplyTimelineSettings);
            ApplyTimelineSettings();
            _viewModel = new TimelineViewModel(_messageBroker);
            DataContext = _viewModel;
            InitializeAudioEngine();

            _timelineRuler = new TimelineRuler(RulerCanvas);
            _timelinePlayhead = new TimelinePlayhead(TransRulerHead, PlayheadMarker, TxtCurrentTime, AudioPlayheadLine, AudioMinimapGrid, ScrollTimelineTracks);
            _timelinePlayhead.OnPlayheadTimeChanged += (seconds) => { _audioEngine?.Seek(seconds); };
            _timelineAudioBar = new TimelineAudioBar(AudioMinimapGrid, WaveformPath, AudioViewportBox, ScrollTimelineTracks, AudioPlayheadLine);
            _timelineNoteRuler = new TimelineNoteRuler(NotePreviewCanvas, _noteVisualEngine);
            _timelineTrackRenderer = new TimelineTrackRenderer(TrackHeadersContainer, TrackGroupsContainer, BottomTrackHeadersContainer, BottomTrackGroupsContainer, _clipService, _noteVisualEngine, _messageBroker, _dialogService);
            _timelineTrackRenderer.OnRequestDetailedEditMode += (m) => OnClipRequestDetailedEdit(m);
            _timelineTrackRenderer.OnClipSelected += (m) => OnClipSelected(m);
            _timelineTrackRenderer.OnRequestPropertyEditor += (m) => OnClipRequestPropertyEditor(m);
            _timelineTrackRenderer.OnMacroGridDrag += ClipCtrl_OnMacroGridDrag;

            UpdateTimelineWidth();
        }

        public double TotalTrackWidth => _totalDurationSeconds * _pixelsPerSecond + 200;

        // =========================================================================
        // 📡 神级联机中枢：一键接通底层大本营，全自动生成排版！
        // =========================================================================
        public void LoadStoryboardTimeline(State.ProjectDataContext context)
        {
            _context = context;
            AppServices.GetService<IStoryboardDocumentValidator>()
                .Validate(context.Storyboard, context);
            var calculatedGroups = new UI.Services.TimelineDataEngine().BuildMainTimeline(context);

            TrackGroups.Clear();
            foreach (var g in calculatedGroups)
            {
                TrackGroups.Add(g);
            }

            RefreshTimelineUI();
            _timelineNoteRuler.Draw(_context, _pixelsPerSecond, _totalDurationSeconds);
        }

        // =========================================================================
        // 🎨 终极渲染引擎：根据 TrackGroups 数据源，傻瓜式平地起高楼！
        // =========================================================================
        public void RefreshTimelineUI()
        {
            _timelineTrackRenderer?.Update(_context, _pixelsPerSecond, _totalDurationSeconds);
            _timelineTrackRenderer?.RefreshUI(TrackGroups);
        }

        // =========================================================================
        // 🎨 ItemsControl 事件：当 EventBlockControl 被加载时初始化
        // =========================================================================
        private void OnClipControlLoaded(object sender, RoutedEventArgs e)
        {
            var clipCtrl = (EventBlockControl)sender;
            var clipModel = clipCtrl.DataContext as EventBlockViewModel;
            if (clipModel == null) return;

            // Skip if already initialized for this model
            if (clipCtrl.Tag is EventBlockViewModel lastModel && ReferenceEquals(lastModel, clipModel)) return;

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

        private void OnClipRequestDetailedEdit(EventBlockViewModel targetModel) => EnterDetailedEditMode(targetModel);
        private void OnClipSelected(EventBlockViewModel targetModel)
        {
            _selectedClipModel = targetModel;
            OnTimelineObjectSelected?.Invoke(targetModel.AssociatedObject);
        }
        private void OnClipRequestPropertyEditor(EventBlockViewModel targetModel) => OnTimelineRequestPropertyEditor?.Invoke(targetModel.AssociatedObject);

        // =========================================================================
        // 📡 ✨ 全景宏观换轨隔离雷达（核心换层与隔离防穿透盾落地！）
        // =========================================================================
        private void ClipCtrl_OnMacroGridDrag(EventBlockControl clipCtrl, MouseEventArgs e, EventBlockControl.MacroDragStage stage)
        {
            if (_context == null || clipCtrl.Tag is not EventBlockViewModel clipModel) return;

            var entity = clipModel.AssociatedObject;
            if (entity == null) return;

            // 🛡️ 1. 启动基因身份识别：区分当前方块是【画面视觉实体】还是【逻辑控制器】
            bool isUpperZone = (entity is Models.C2Sprite || entity is Models.C2Text || entity is Models.C2Video || entity is Models.C2Line);

            // 根据身份，将雷达指针分流到对应的安全宇宙，异种图层绝不交叉，实现绝对防穿透！
            var registry = isUpperZone ? _timelineTrackRenderer.UpperTrackRegistry : _timelineTrackRenderer.LowerTrackRegistry;
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
            if (stage == EventBlockControl.MacroDragStage.Started)
            {
                return; // 预留开始拖拽特效空间
            }
            if (stage == EventBlockControl.MacroDragStage.Moving)
            {
                return; // 预留中途拖拽悬停高亮轨道特效空间
            }
            if (stage == EventBlockControl.MacroDragStage.Completed)
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
                            var oldRegistryItem = _timelineTrackRenderer.LowerTrackRegistry.FirstOrDefault(r => r.Track.TrackIndex == currentIndex);
                            var newRegistryItem = closestItem; // 目标轨道

                            if (oldRegistryItem != null && newRegistryItem != null)
                            {
                                var oldCanvas = oldRegistryItem.TrackBorder.Child as Canvas;
                                var newCanvas = newRegistryItem.TrackBorder.Child as Canvas;

                                // 抓出目标轨道里原来住着的那个方块 (也就是被挤掉的那个)
                                var otherClipCtrl = newCanvas?.Children.OfType<EventBlockControl>().FirstOrDefault();

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
                                if (otherClipCtrl?.Tag is EventBlockViewModel otherModel)
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
                        if (clipDuration <= 0)
                        {
                            clipCtrl.Width = _timelineSettings?.Current.ZeroDurationMarkerWidth ?? 8;
                        }
                        else
                        {
                            if (clipDuration > 300) clipDuration = 300;
                            clipCtrl.Width = Math.Max(10, clipDuration * _pixelsPerSecond);
                        }
                    }

                    Canvas.SetTop(clipCtrl, 6);
                }
            }
        }






        // ==========================================
        // 🔬 微观变身与退出
        // ==========================================
        // 🚀 多标签宇宙：微观变身引擎重写
        private void EnterDetailedEditMode(EventBlockViewModel targetModel)
        {
            foreach (var element in TimelineTabs.Items)
            {
                if (element is TabItem item && ReferenceEquals(item.Tag, targetModel.AssociatedObject))
                {
                    TimelineTabs.SelectedItem = item;
                    return;
                }
            }

            var newTab = new TabItem
            {
                Tag = targetModel.AssociatedObject,
                ToolTip = $"{targetModel.DisplayName} · {targetModel.AssociatedObject.GetType().Name}"
            };

            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
            headerPanel.Children.Add(new TextBlock
            {
                Text = GetEntityTabIcon(targetModel.AssociatedObject),
                Margin = new Thickness(0, 0, 5, 0)
            });
            headerPanel.Children.Add(new TextBlock
            {
                Text = targetModel.DisplayName,
                MaxWidth = 155,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontWeight = FontWeights.SemiBold
            });
            var closeBtn = new Button
            {
                Content = "×",
                Style = (Style)FindResource("TimelineTabCloseButtonStyle"),
                ToolTip = "关闭微观时间轴"
            };
            closeBtn.Click += (_, e) =>
            {
                e.Handled = true;
                CloseMicroTimelineTab(newTab);
            };
            headerPanel.Children.Add(closeBtn);
            newTab.Header = headerPanel;
            newTab.MouseDown += (_, e) =>
            {
                if (e.ChangedButton == MouseButton.Middle)
                {
                    e.Handled = true;
                    CloseMicroTimelineTab(newTab);
                }
            };
            newTab.ContextMenu = BuildTimelineTabContextMenu(newTab);

            newTab.Content = new Grid
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = "正在加载微观时间轴…",
                        Foreground = (Brush)FindResource("SecTextColor"),
                        FontSize = 13,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            };

            TimelineTabs.Items.Add(newTab);
            TimelineTabs.SelectedItem = newTab;

            var request = new Core.Timeline.Models.MicroEditorContext
            {
                Entity = targetModel.AssociatedObject,
                DisplayName = targetModel.DisplayName,
                MacroStartTime = targetModel.StartTime,
                MacroEndTime = targetModel.EndTime,
                InitialPixelsPerSecond = _pixelsPerSecond
            };
            if (OpenMicroTimelineRequested == null)
            {
                newTab.Content = new TextBlock
                {
                    Text = "微观时间轴导航服务尚未连接。",
                    Foreground = Brushes.OrangeRed,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                return;
            }
            OpenMicroTimelineRequested.Invoke(request, newTab);
        }

        private static string GetEntityTabIcon(object entity) => entity.GetType().Name switch
        {
            "C2Text" => "T",
            "C2Sprite" => "▧",
            "C2Line" => "╱",
            "C2Video" => "▶",
            "C2SceneController" => "◉",
            "C2NoteController" => "♪",
            _ => "◆"
        };

        private ContextMenu BuildTimelineTabContextMenu(TabItem tab)
        {
            var menu = new ContextMenu();
            var close = new MenuItem { Header = "关闭" };
            close.Click += (_, _) => CloseMicroTimelineTab(tab);
            var closeOthers = new MenuItem { Header = "关闭其他微观标签" };
            closeOthers.Click += (_, _) =>
            {
                foreach (var item in TimelineTabs.Items.OfType<TabItem>()
                             .Where(item => item != MainTimelineTab && item != tab).ToList())
                    CloseMicroTimelineTab(item);
            };
            var closeRight = new MenuItem { Header = "关闭右侧标签" };
            closeRight.Click += (_, _) =>
            {
                var index = TimelineTabs.Items.IndexOf(tab);
                foreach (var item in TimelineTabs.Items.OfType<TabItem>()
                             .Skip(index + 1).ToList())
                    CloseMicroTimelineTab(item);
            };
            var main = new MenuItem { Header = "返回主时间轴" };
            main.Click += (_, _) => TimelineTabs.SelectedItem = MainTimelineTab;
            menu.Items.Add(close);
            menu.Items.Add(closeOthers);
            menu.Items.Add(closeRight);
            menu.Items.Add(new Separator());
            menu.Items.Add(main);
            return menu;
        }

        private void CloseMicroTimelineTab(TabItem tab)
        {
            if (tab == null || tab == MainTimelineTab)
                return;
            if (tab.Content is MicroTimeline.MicroTimelineEditor editor)
                editor.CancelPendingLoad();
            TimelineTabs.Items.Remove(tab);
            if (TimelineTabs.SelectedItem == null)
                TimelineTabs.SelectedItem = MainTimelineTab;
        }

        private void TimelineTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(e.Source, TimelineTabs))
                return;
            _messageBroker?.Publish(
                TimelineTabs.SelectedItem == MainTimelineTab
                    ? "Timeline.Context.Main"
                    : "Timeline.Context.Micro");
        }



        // =========================================================================================
        // 🎵 音频基建、滚动同步、游标换算、缩放（此处完美保留大大之前的顶级基建，已剔除旧有硬编码冲突）
        // =========================================================================================

        private void InitializeAudioEngine()
        {
            _renderEngine.OnRenderTick += () => {
                if (_audioEngine.IsPlaying && !(_timelinePlayhead?.IsDragging ?? false))
                    UpdatePlayheadPosition(_audioEngine.GetCurrentSmoothTime() * _pixelsPerSecond);
            };

            _audioEngine.OnTimeChanged += (currentSeconds) => {
                if (!_audioEngine.IsPlaying && !(_timelinePlayhead?.IsDragging ?? false))
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
            _timelinePlayhead?.UpdatePosition(xPos);

            // 4. ✨ 智能跟随摄像机（居中推流）
            if (_audioEngine.IsPlaying && !(_timelinePlayhead?.IsDragging ?? false) && ScrollTimelineTracks != null)
            {
                double currentOffset = ScrollTimelineTracks?.HorizontalOffset ?? 0;
                double viewWidth = ScrollTimelineTracks.ViewportWidth;
                if (viewWidth > 0)
                {
                    double visualX = xPos - currentOffset;
                    if (visualX > viewWidth - 20)
                    {
                        double targetOffset = xPos - (viewWidth / 2.0);
                        ScrollTimelineTracks.ScrollToHorizontalOffset(targetOffset);
                    }
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
            _timelinePlayhead?.HandleRulerMouseDown(e, sender as Border, ScrollTimelineTracks);
        }

        private void Playhead_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _timelinePlayhead?.HandlePlayheadMouseDown(e);
        }

        private void Playhead_MouseMove(object sender, MouseEventArgs e)
        {
            _timelinePlayhead?.HandlePlayheadMouseMove(e, ScrollRuler);
        }

        private void Playhead_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _timelinePlayhead?.HandlePlayheadMouseUp(e);
        }

        private void BtnPlay_Click(object sender, RoutedEventArgs e) => _audioEngine.Play();
        private void BtnPause_Click(object sender, RoutedEventArgs e) => _audioEngine.Pause();

        private async void BtnImportAudio_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog { Filter = "音频文件 (*.mp3;*.wav;*.ogg)|*.mp3;*.wav;*.ogg", Title = "请选择关卡音乐" };
            if (openFileDialog.ShowDialog() == true) { if (BtnImportAudio != null) BtnImportAudio.Visibility = Visibility.Collapsed; await _audioEngine.LoadAudioAsync(openFileDialog.FileName); }
        }

        private void AudioMinimapGrid_SizeChanged(object sender, SizeChangedEventArgs e) { _timelineAudioBar?.DrawWaveform(); _timelineAudioBar?.UpdateViewportBox(); }

        private void DrawWaveform()
        {
            _timelineAudioBar?.SetWaveformSamples(_audioEngine.WaveformSamples);
            _timelineAudioBar?.DrawWaveform();
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
                TransRulerHead.X = (_timelinePlayhead?.CurrentPlayheadSeconds ?? 0) * _pixelsPerSecond - ScrollTimelineTracks.HorizontalOffset;
            }


            _isSyncingScroll = false;
            UpdateAudioViewportBox();
        }

        private void OnTimelineMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // 🛡️ 如果当前在微观时光屋里，不拦截滚轮事件
            if (TimelineTabs != null && TimelineTabs.SelectedIndex > 0) return;

            var modifier = _timelineSettings?.Current.MouseWheelZoomModifier ?? "Ctrl";
            var shouldZoom = modifier switch
            {
                "None" => true,
                "Alt" => (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt,
                "Disabled" => false,
                _ => (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
            };
            if (shouldZoom)
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
            double factor = 1 + (_timelineSettings?.Current.ZoomStepPercent ?? 20) / 100d;
            double newPixels = _pixelsPerSecond * factor;
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
            double factor = 1 + (_timelineSettings?.Current.ZoomStepPercent ?? 20) / 100d;
            double newPixels = _pixelsPerSecond / factor;
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
            var initial = _timelineSettings?.Current.InitialPixelsPerSecond ?? 100;
            if (Math.Abs(_pixelsPerSecond - initial) > 0.01)
            {
                _pixelsPerSecond = initial;
                UpdateTimelineWidth();
            }
        }

        private void ApplyTimelineSettings()
        {
            if (_timelineSettings == null) return;
            _pixelsPerSecond = Math.Clamp(_pixelsPerSecond, MinPixelsPerSecond, MaxPixelsPerSecond);
            UpdateTimelineWidth();
        }

        public void FitAll()
        {
            var viewport = ScrollTimelineTracks?.ViewportWidth ?? 0;
            if (viewport <= 0 || _totalDurationSeconds <= 0) return;
            _pixelsPerSecond = Math.Clamp((viewport - 20) / _totalDurationSeconds, MinPixelsPerSecond, MaxPixelsPerSecond);
            UpdateTimelineWidth();
            ScrollTimelineTracks.ScrollToHorizontalOffset(0);
        }

        public void FocusSelection()
        {
            if (_selectedClipModel == null) return;
            var center = ((_selectedClipModel.StartTime + _selectedClipModel.EndTime) / 2d) * _pixelsPerSecond;
            ScrollTimelineTracks.ScrollToHorizontalOffset(Math.Max(0, center - ScrollTimelineTracks.ViewportWidth / 2d));
        }

        public void OpenSelectedInMicroTimeline()
        {
            if (_selectedClipModel != null)
                EnterDetailedEditMode(_selectedClipModel);
        }

        public void ReturnToMainTimeline()
        {
            if (TimelineTabs.Items.Count > 0)
                TimelineTabs.SelectedIndex = 0;
        }

        public void SelectAllTimelineItems()
        {
            foreach (var group in TrackGroups)
                foreach (var track in group.Tracks)
                    foreach (var clip in track.Clips)
                        clip.IsSelected = true;
        }

        public void NudgeSelection(double deltaSeconds)
        {
            if (_selectedClipModel == null || _context == null || Math.Abs(deltaSeconds) < .0000001) return;
            var oldStart = _selectedClipModel.StartTime;
            var oldEnd = _selectedClipModel.EndTime;
            AppServices.GetService<IHistoryService>().RecordSnapshot(_context.Storyboard);
            _clipService.SettleDrag(
                _selectedClipModel.AssociatedObject,
                oldStart,
                oldEnd,
                oldStart + deltaSeconds,
                oldEnd + deltaSeconds);
            LoadStoryboardTimeline(_context);
            _messageBroker?.Publish("DataModified");
        }

        public void CancelCurrentInteraction()
        {
            Mouse.Capture(null);
            if (_context != null)
                LoadStoryboardTimeline(_context);
        }

        public void CopySelection()
        {
            if (_selectedClipModel?.AssociatedObject == null) return;
            _timelineClipboardType = _selectedClipModel.AssociatedObject.GetType();
            _timelineClipboardJson = JsonConvert.SerializeObject(_selectedClipModel.AssociatedObject);
        }

        public void PasteSelection()
        {
            if (_context?.Storyboard == null || _timelineClipboardType == null || string.IsNullOrEmpty(_timelineClipboardJson))
                return;

            if (JsonConvert.DeserializeObject(_timelineClipboardJson, _timelineClipboardType) is not IStoryboardEntity clone)
                return;

            AppServices.GetService<IHistoryService>().RecordSnapshot(_context.Storyboard);
            if (!string.IsNullOrEmpty(clone.Id))
            {
                var existing = _storyboardRepository.GetAllEntities(_context.Storyboard)
                    .Select(e => e.Id)
                    .ToHashSet(StringComparer.Ordinal);
                var baseId = clone.Id + "_copy";
                var candidate = baseId;
                var suffix = 2;
                while (existing.Contains(candidate))
                    candidate = $"{baseId}_{suffix++}";
                clone.Id = candidate;
            }

            _storyboardRepository.Add(_context.Storyboard, clone);
            var projection = new Core.Timeline.Projection.TimelineProjectionService()
                .BuildEntityProjection(clone, _context);
            _clipService.SettleDrag(
                clone,
                projection.BaseStateTime,
                projection.LastStateTime,
                projection.BaseStateTime + (_timelineSettings?.Current.LargeNudgeStepSeconds ?? .1),
                projection.LastStateTime + (_timelineSettings?.Current.LargeNudgeStepSeconds ?? .1));
            LoadStoryboardTimeline(_context);
            _messageBroker?.Publish("DataModified");
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
                _timelinePlayhead?.UpdatePosition(0);
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
                _timelinePlayhead?.UpdatePosition(endTime * _pixelsPerSecond);
            }
        }


        // ==========================================\
        // 🧙‍♂️ 唤醒俄罗斯方块：一键智能整理所有重叠图层！
        // ==========================================\
        private void BtnAutoLayout_Click(object sender, RoutedEventArgs e)
        {
            if (_context == null || !_context.HasStoryboard) return;

            // 1. 让大脑执行俄罗斯方块无损排版法术！
            Core.Timeline.Shared.TimelineLayoutEngine.AutoAssignOrderForVisualEntities(_context);

            // 2. 标记大本营数据已修改（这样左侧列表和JSON预览也会同步，且触发保存状态）
            _context.MarkAsModified();

            // 3. 重新读取并刷新整个时间轴宇宙！
            LoadStoryboardTimeline(_context);

            _notificationService.ShowSuccess("✨ 智能排版完成！所有挤在一起的方块已经根据时间自动分配到不同的 Order 轨道啦！");
        }

        private void UpdateTimelineWidth()
        {
            _timelineRuler?.Update(_pixelsPerSecond, _totalDurationSeconds);
            _timelineRuler?.DrawRuler();
            _timelineAudioBar?.Update(_pixelsPerSecond, _totalDurationSeconds);
            _timelineAudioBar?.UpdateViewportBox();

            double newWidth = _totalDurationSeconds * _pixelsPerSecond + 200;
            if (ScrollRuler?.Content is Border rBorder) rBorder.Width = newWidth;
            // 🚀 同步渲染器内部缩放状态，确保上下轨道缩放一致
            _timelineTrackRenderer?.Update(_context, _pixelsPerSecond, _totalDurationSeconds);
            // 极速坐标位移法术！
            FastUpdateZoomVisuals();
        }

        private void DrawTimelineRuler()
        {
            _timelineRuler?.DrawRuler();
        }


        // ==========================================
        // 🎹 音符雷达尺：在底部画布精准画出谱面音符！(大一统工厂模式接入)
        // ==========================================
        public void DrawNoteRuler()
        {
            _timelineNoteRuler?.Draw(_context, _pixelsPerSecond, _totalDurationSeconds);
        }

        // ==========================================
        // 🏄‍♂️ 滑块联动引擎：绝对精准的坐标系
        // ==========================================
        private void UpdateAudioViewportBox()
        {
            _timelineAudioBar?.UpdateViewportBox();
        }

        private void AudioViewportBox_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            _timelineAudioBar?.HandleViewportBoxDragDelta(e);
        }

        // ==========================================
        // 🚀 ✨ 极速缩放引擎：拒绝摧毁重建，仅更新物理坐标！
        // ==========================================
        private void FastUpdateZoomVisuals()
        {
            _timelineTrackRenderer?.FastUpdateZoom();
            _timelineNoteRuler?.FastUpdateZoom(_context, _pixelsPerSecond, _totalDurationSeconds);
        }
    }
}



