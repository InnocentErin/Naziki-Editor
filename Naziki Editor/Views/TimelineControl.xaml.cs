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

        // =========================================================================
        // 🌍 宇宙数据源：全景与微观的所有轨道，全靠它驱动！
        // =========================================================================
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
        }

        // =========================================================================
        // 🎨 终极渲染引擎：根据 TrackGroups 数据源，傻瓜式平地起高楼！
        // =========================================================================
        public void RefreshTimelineUI()
        {
            if (TrackHeadersContainer == null || TrackGroupsContainer == null) return;

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

                    // ✨ 修复：使用动态分配的 targetTrack，而不是写死的 TrackGroupsContainer！
                    targetTrack.Children.Add(trackRight);

                    // C. 🧩 将方块 (Clip) 渲染到对应的画板上
                    foreach (var clip in track.Clips)
                    {
                        var clipCtrl = new TimelineClipControl();

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
                else DrawWaveform();
            };
        }

        private void UpdatePlayheadPosition(double xPos)
        {
            double maxWidth = _totalDurationSeconds * _pixelsPerSecond;
            if (xPos < 0) xPos = 0;
            if (xPos > maxWidth) xPos = maxWidth;

            if (TransRulerHead != null) TransRulerHead.X = xPos;
            if (AudioMinimapGrid != null && AudioPlayheadLine != null && _totalDurationSeconds > 0)
            {
                double ratio = _currentPlayheadSeconds / _totalDurationSeconds;
                AudioPlayheadLine.X1 = ratio * AudioMinimapGrid.ActualWidth;
                AudioPlayheadLine.X2 = AudioPlayheadLine.X1;
            }

            _currentPlayheadSeconds = xPos / _pixelsPerSecond;
            UpdatePlaybackTimeDisplay(_currentPlayheadSeconds);
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
                UpdatePlayheadPosition(e.GetPosition(rulerBorder).X);
                Core.AudioSyncEngine.Instance.Seek(_currentPlayheadSeconds);
            }
        }

        private void Playhead_MouseDown(object sender, MouseButtonEventArgs e) { _isDraggingPlayhead = true; PlayheadMarker.CaptureMouse(); e.Handled = true; }
        private void Playhead_MouseMove(object sender, MouseEventArgs e) { if (_isDraggingPlayhead && ScrollRuler?.Content is Border rBorder) UpdatePlayheadPosition(e.GetPosition(rBorder).X); }
        private void Playhead_MouseUp(object sender, MouseButtonEventArgs e) { if (_isDraggingPlayhead) { _isDraggingPlayhead = false; PlayheadMarker.ReleaseMouseCapture(); Core.AudioSyncEngine.Instance.Seek(_currentPlayheadSeconds); } }

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

            // 重新刷新轨道的长度
            RefreshTimelineUI();
            DrawTimelineRuler();
            DrawWaveform();
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
    }
}