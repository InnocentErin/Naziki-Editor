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
    public partial class TimelineControl : UserControl
    {
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

        // 🎬 详细调整模式（微观时光屋）状态锁
        private TimelineClip.ClipDetailedEditor _detailedEditor = null;
        private bool _isDetailedEditMode = false;
        private Models.TimelineClipModel _editingClipModel = null;
        private State.ProjectDataContext _context = null; // 记住当前的大本营上下文！

        // ✨ 追加：时空隔离注册表结构，记住每个轨道在全局的物理辖区边界
        private class TrackRegistryItem
        {
            public Border TrackBorder { get; set; }
            public Models.TimelineTrackGroupModel Group { get; set; }
            public Models.TimelineTrackModel Track { get; set; }
        }
        private List<TrackRegistryItem> _upperTrackRegistry = new List<TrackRegistryItem>(); // 上半宇宙（画面实体）
        private List<TrackRegistryItem> _lowerTrackRegistry = new List<TrackRegistryItem>(); // 下半宇宙（控制器）

        // 🌍 宇宙数据源：全景与微观的所有轨道，全靠它驱动！
        public ObservableCollection<Models.TimelineTrackGroupModel> TrackGroups { get; private set; } = new ObservableCollection<Models.TimelineTrackGroupModel>();
        // ✨ 追加：向大本营汇报“某对象被选中”的神经接口
        public event Action<object> OnTimelineObjectSelected;
        // 🚀 追加：向大本营汇报“请求打开属性编辑器”的神经接口 (Ctrl+单击)
        public event Action<object> OnTimelineRequestPropertyEditor;
        public TimelineControl()
        {
            InitializeComponent();
            InitializeAudioEngine();
            UpdateTimelineWidth();
        }

        // =========================================================================
        // 📡 神级联机中枢：一键接通底层大本营，全自动生成排版！
        // =========================================================================
        public void LoadStoryboardTimeline(State.ProjectDataContext context)
        {
            _context = context;

            // 2. 🌟 拔掉严苛的 return！只要接通，哪怕没有谱面数据，也要让大脑画出空壳！
            var storyboard = (context != null && context.HasStoryboard) ? context.Storyboard : null;

            // 3. 呼叫 Core 大脑，根据大本营的实体计算中心辐射排版数据
            var calculatedGroups = Core.Timeline.TimelineDataEngine.BuildMacroTimeline(context);

            // 4. 把打包好的数据包交接给万能数据源
            TrackGroups.Clear();
            foreach (var g in calculatedGroups) TrackGroups.Add(g);

            // 5. 让画笔动起来！
            RefreshTimelineUI();
            DrawNoteRuler();
        }

        // =========================================================================
        // 🎨 终极渲染引擎：根据 TrackGroups 数据源，傻瓜式平地起高楼！
        // =========================================================================
        public void RefreshTimelineUI()
        {
            if (TrackHeadersContainer == null || TrackGroupsContainer == null) return;

            // ✨ 【精准追加】：每次重新盖楼前，清空旧的辖区注册表
            _upperTrackRegistry.Clear();
            _lowerTrackRegistry.Clear();

            // 1. 净化废墟
            TrackHeadersContainer.Children.Clear();
            TrackGroupsContainer.Children.Clear();
            if (BottomTrackHeadersContainer != null) BottomTrackHeadersContainer.Children.Clear();
            if (BottomTrackGroupsContainer != null) BottomTrackGroupsContainer.Children.Clear();

            // 2. 按 GroupIndex 降序排列 (数值越大的图层越在上面)
            var sortedGroups = TrackGroups.OrderByDescending(g => g.GroupIndex).ToList();

            foreach (var group in sortedGroups)
            {
                // ✨ 引力分发：>= 0 塞给上半区，< 0 塞给下半区
                StackPanel targetHeader = group.GroupIndex >= 0 ? TrackHeadersContainer : BottomTrackHeadersContainer;
                StackPanel targetTrack = group.GroupIndex >= 0 ? TrackGroupsContainer : BottomTrackGroupsContainer;

                if (targetHeader == null) targetHeader = TrackHeadersContainer;
                if (targetTrack == null) targetTrack = TrackGroupsContainer;

                // A. 📦 渲染大组头部
                var headerLeft = new Border { Height = 26, Background = (Brush)Application.Current.FindResource("MenuBgColor"), BorderBrush = (Brush)Application.Current.FindResource("BorderColor"), BorderThickness = new Thickness(0, 0, 0, 1) };
                headerLeft.Child = new TextBlock { Text = group.GroupName, Foreground = (Brush)Application.Current.FindResource("HighlightBorderColor"), FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0) };

                // 标题也必须跟着目标引力走，不能写死！
                targetHeader.Children.Add(headerLeft);

                var headerRight = new Border { Height = 26, Background = (Brush)Application.Current.FindResource("MenuBgColor"), BorderBrush = (Brush)Application.Current.FindResource("BorderColor"), BorderThickness = new Thickness(0, 0, 0, 1) };

                // 占位也必须跟着目标引力走！
                targetTrack.Children.Add(headerRight);

                if (!group.IsExpanded) continue; // 折叠的组直接跳过其内部的微观渲染

                // B. 🚂 渲染小轨道
                // 🚂 智能渲染小轨道：根据图层的引力方向决定排序！
                var sortedTracks = group.Tracks.OrderByDescending(t => t.TrackIndex).ToList();

                // 🚂 先渲染轨道头，再渲染轨道内容，保持视觉上的层次感和正确的交互区域划分！
                foreach (var track in sortedTracks)
                {
                    // 左侧：轨道名
                    var trackLeft = new Border { Height = 40, BorderBrush = (Brush)Application.Current.FindResource("BorderColor"), BorderThickness = new Thickness(0, 0, 0, 1) };
                    trackLeft.Child = new TextBlock { Text = track.TrackName, Foreground = (Brush)Application.Current.FindResource("MainTextColor"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(15, 0, 0, 0) };

                    // ✨ 修复：使用动态分配的 targetHeader，而不是写死的 TrackHeadersContainer！
                    targetHeader.Children.Add(trackLeft);

                    // 右侧：万能 Canvas 画板
                    var trackCanvas = new Canvas { Height = 40, Background = Brushes.Transparent, ClipToBounds = true, Width = _totalDurationSeconds * _pixelsPerSecond + 200 };
                    var trackRight = new Border { Height = 40, BorderBrush = (Brush)Application.Current.FindResource("BorderColor"), BorderThickness = new Thickness(0, 0, 0, 1), Child = trackCanvas };
                    
                    // ✨ 【精准追加】：在塞入 StackPanel 之前，将轨道真名和物理容器登记进对应的隔离注册表中！
                    var registryItem = new TrackRegistryItem { TrackBorder = trackRight, Group = group, Track = track };
                    if (group.GroupIndex >= 0) _upperTrackRegistry.Add(registryItem);
                    else _lowerTrackRegistry.Add(registryItem);

                    // ✨ 修复：使用动态分配的 targetTrack，而不是写死的 TrackGroupsContainer！
                    targetTrack.Children.Add(trackRight);

                    // C. 🧩 将方块 (Clip) 渲染到对应的画板上
                    foreach (var clip in track.Clips)
                    {
                        var clipCtrl = new TimelineClipControl();
                        clipCtrl.Tag = clip; // ✨ 小艾新增：给方块贴上名片，方便极速缩放！

                        // 预留接口：将大本营上下文传进去
                        if (Window.GetWindow(this) is MainWindow mainWin)
                        {
                            clipCtrl.Init(clip, mainWin.Context, _pixelsPerSecond, clip.TrackIndex, 999);
                        }

                        // 双击：微观变身监听
                        clipCtrl.OnRequestDetailedEditMode += (targetModel) => { EnterDetailedEditMode(targetModel); };

                        // ✨ 单击：接通中继反射弧，把模型里的原生 Cytoid 对象传出去！
                        clipCtrl.OnClipSelected += (targetModel) => {
                            OnTimelineObjectSelected?.Invoke(targetModel.AssociatedObject);
                        };

                        // 🚀 追加：接通 Ctrl+Click 的高级召唤法术！
                        clipCtrl.OnRequestPropertyEditor += (targetModel) => {
                            OnTimelineRequestPropertyEditor?.Invoke(targetModel.AssociatedObject);
                        };

                        // ✨ 【核心追加接线】：接通方块的宏观跨轨上下揉搓、拉扯运动信号！
                        clipCtrl.OnMacroGridDrag += ClipCtrl_OnMacroGridDrag;


                        Canvas.SetLeft(clipCtrl, clip.StartTime * _pixelsPerSecond);
                        Canvas.SetTop(clipCtrl, 6); // 轨道高度 40，居中留白

                        // ✨ 核心修复：注入物理碰撞体积！限制最大长度防止显存爆炸！
                        double clipDuration = clip.EndTime - clip.StartTime;
                        if (clipDuration > 300) clipDuration = 300; // 兜底：如果控制器是无限长，最多只画300秒
                        clipCtrl.Width = Math.Max(10, clipDuration * _pixelsPerSecond);

                        trackCanvas.Children.Add(clipCtrl);
                    }
                }

            }
        }



        // =========================================================================
        // 📡 ✨ 满配完全体：全景宏观换轨隔离雷达（核心换层与隔离防穿透盾落地！）
        // =========================================================================
        private void ClipCtrl_OnMacroGridDrag(TimelineClipControl clipCtrl, MouseEventArgs e, TimelineClipControl.MacroDragStage stage)
        {
            if (_context == null || clipCtrl.Tag is not Models.TimelineClipModel clipModel) return;

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
                        if (Core.FastReflectionHelper.TryGetValue(baseState, "Layer", out object lObj))
                            currentLayer = Convert.ToInt32(lObj);

                        // 只有当打谱师真的跨越了物理边界，才触发时空改写
                        if (currentLayer != targetLayer || currentOrder != targetOrder)
                        {
                            var propLayer = baseState.GetType().GetProperty("Layer");
                            var propOrder = baseState.GetType().GetProperty("Order");

                            if (propLayer != null && propLayer.CanWrite)
                            {
                                Type t = Nullable.GetUnderlyingType(propLayer.PropertyType) ?? propLayer.PropertyType;
                                propLayer.SetValue(baseState, Convert.ChangeType(targetLayer, t));
                            }
                            if (propOrder != null && propOrder.CanWrite)
                            {
                                Type t = Nullable.GetUnderlyingType(propOrder.PropertyType) ?? propOrder.PropertyType;
                                propOrder.SetValue(baseState, Convert.ChangeType(targetOrder, t));
                            }

                            dataChanged = true;
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
                        if (entity is Models.C2SceneController ctrl && root.controllers != null)
                        {
                            if (currentIndex >= 0 && currentIndex < root.controllers.Count && targetIndex >= 0 && targetIndex < root.controllers.Count)
                            {
                                root.controllers.Remove(ctrl);
                                root.controllers.Insert(targetIndex, ctrl);
                                dataChanged = true;
                            }
                        }
                        else if (entity is Models.C2NoteController noteCtrl && root.note_controllers != null)
                        {
                            if (currentIndex >= 0 && currentIndex < root.note_controllers.Count && targetIndex >= 0 && targetIndex < root.note_controllers.Count)
                            {
                                root.note_controllers.Remove(noteCtrl);
                                root.note_controllers.Insert(targetIndex, noteCtrl);
                                dataChanged = true;
                            }
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
                    // 同轨平移或纯单点，优雅在原地立正对齐
                    Canvas.SetLeft(clipCtrl, clipModel.StartTime * _pixelsPerSecond);
                    Canvas.SetTop(clipCtrl, 6);

                    double clipDuration = clipModel.EndTime - clipModel.StartTime;
                    if (clipDuration > 300) clipDuration = 300;
                    clipCtrl.Width = Math.Max(10, clipDuration * _pixelsPerSecond);
                }
            }
        }






        // ==========================================
        // 🔬 微观变身与退出
        // ==========================================
        // 🚀 多标签宇宙：微观变身引擎重写
        private void EnterDetailedEditMode(Models.TimelineClipModel targetModel)
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
            var detailEditor = new TimelineClip.ClipDetailedEditor();
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
            Core.GlobalRenderEngine.Instance.OnRenderTick += () => {
                if (Core.AudioSyncEngine.Instance.IsPlaying && !_isDraggingPlayhead)
                    UpdatePlayheadPosition(Core.AudioSyncEngine.Instance.GetCurrentSmoothTime() * _pixelsPerSecond);
            };

            Core.AudioSyncEngine.Instance.OnTimeChanged += (currentSeconds) => {
                if (!Core.AudioSyncEngine.Instance.IsPlaying && !_isDraggingPlayhead)
                    UpdatePlayheadPosition(currentSeconds * _pixelsPerSecond);
            };

            Core.AudioSyncEngine.Instance.OnPlayStateChanged += (isPlaying) => {
                BtnPlay.Foreground = isPlaying ? Brushes.LightGreen : (Brush)Application.Current.Resources["MainTextColor"];
            };

            Core.AudioSyncEngine.Instance.OnAudioLoaded += () => {
                if (BtnImportAudio != null) BtnImportAudio.Visibility = Visibility.Collapsed;
                if (_totalDurationSeconds < Core.AudioSyncEngine.Instance.Duration)
                {
                    _totalDurationSeconds = Core.AudioSyncEngine.Instance.Duration + 2.0;
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
            if (Core.AudioSyncEngine.Instance.IsPlaying && !_isDraggingPlayhead && ScrollTimelineTracks != null)
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
                Core.AudioSyncEngine.Instance.Seek(_currentPlayheadSeconds);
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
                Core.AudioSyncEngine.Instance.Seek(_currentPlayheadSeconds);
            }
        }

        private void BtnPlay_Click(object sender, RoutedEventArgs e) => Core.AudioSyncEngine.Instance.Play();
        private void BtnPause_Click(object sender, RoutedEventArgs e) => Core.AudioSyncEngine.Instance.Pause();

        private async void BtnImportAudio_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog { Filter = "音频文件 (*.mp3;*.wav;*.ogg)|*.mp3;*.wav;*.ogg", Title = "请选择关卡音乐" };
            if (openFileDialog.ShowDialog() == true) { if (BtnImportAudio != null) BtnImportAudio.Visibility = Visibility.Collapsed; await Core.AudioSyncEngine.Instance.LoadAudioAsync(openFileDialog.FileName); }
        }

        private void AudioMinimapGrid_SizeChanged(object sender, SizeChangedEventArgs e) { DrawWaveform(); UpdateAudioViewportBox(); }

        private void DrawWaveform()
        {
            if (WaveformPath == null || Core.AudioSyncEngine.Instance.WaveformSamples == null || AudioMinimapGrid.ActualWidth <= 0) return;
            var samples = Core.AudioSyncEngine.Instance.WaveformSamples;
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
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                e.Handled = true;
                double newPixels = _pixelsPerSecond * (e.Delta > 0 ? 1.2 : (1.0 / 1.2));
                if (Math.Abs(newPixels - _pixelsPerSecond) > 0.01) { _pixelsPerSecond = Math.Max(MinPixelsPerSecond, Math.Min(MaxPixelsPerSecond, newPixels)); UpdateTimelineWidth(); }
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

            MessageBox.Show("✨ 智能排版完成！\n所有挤在一起的方块已经根据时间自动分配到不同的 Order 轨道啦！", "排版大成功");
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
            Core.Timeline.NoteVisualEngine.RenderNoteRuler(NotePreviewCanvas, _context.Chart.note_list, _context.TimeEngine, _pixelsPerSecond, false);
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
                        trackCanvas.Width = newWidth; // 延长轨道
                        foreach (UIElement clipObj in trackCanvas.Children)
                        {
                            if (clipObj is TimelineClipControl clipCtrl && clipCtrl.Tag is Models.TimelineClipModel clip)
                            {
                                // 重新计算物理坐标
                                Canvas.SetLeft(clipCtrl, clip.StartTime * _pixelsPerSecond);
                                double clipDuration = clip.EndTime - clip.StartTime;
                                if (clipDuration > 300) clipDuration = 300;
                                clipCtrl.Width = Math.Max(10, clipDuration * _pixelsPerSecond);
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