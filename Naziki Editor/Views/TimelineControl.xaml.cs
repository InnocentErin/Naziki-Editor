using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using NAudio.Wave;
using System.Threading.Tasks;
using System.Collections.ObjectModel;

namespace Naziki_Editor.Views
{
    public partial class TimelineControl : UserControl
    {
        private bool _isSyncingScroll = false;
        private double _pixelsPerSecond = 100.0;
        private const double MinPixelsPerSecond = 10.0;
        private const double MaxPixelsPerSecond = 1000.0;
        private double _totalDurationSeconds = 60.0;
        private bool _isDraggingPlayhead = false;
        private double _currentPlayheadSeconds = 0.0;
        // 🌟 性能法术：记录当前波形图已渲染的左右物理边界
        private double _renderedWaveformStartX = -1;
        private double _renderedWaveformEndX = -1;

        // 🎬 详细调整模式（微观时光屋）状态锁
        private TimelineClip.ClipDetailedEditor _detailedEditor = null;
        private bool _isDetailedEditMode = false;
        private Models.TimelineClipModel _editingClipModel = null;

        // 🔒 安全二击抹杀机制变量锁
        private Button _armedSpriteButton = null;
        private Models.TimelineTrackModel _armedSpriteTrack = null;
        private Button _armedCtrlButton = null;
        private Models.TimelineTrackModel _armedCtrlTrack = null;



        public ObservableCollection<Models.TimelineTrackModel> SpriteTracks { get; set; } = new ObservableCollection<Models.TimelineTrackModel>();
        public ObservableCollection<Models.TimelineTrackModel> ControllerTracks { get; set; } = new ObservableCollection<Models.TimelineTrackModel>();

        // 时间轴播放记忆锁
        private string _lastTimeText = "";

        public TimelineControl()
        {
            InitializeComponent();
            // ✨ 1. 将 WPF 前台控件绑定到我们身后的数据名册！
            SpriteTrackHeadersControl.ItemsSource = SpriteTracks;
            CtrlTrackHeadersControl.ItemsSource = ControllerTracks;


            // ✨ 2. 初始化几个默认空轨道给大大用
            InitDefaultTracks();

            InitializeAudioEngine();
            UpdateTimelineWidth();
        }
        // 1. 初始化与动态添加轨道
        private void InitDefaultTracks()
        {
            // ✨ Sprite 轨倒序添加：这样 Track 0 永远沉在最底部，最高编号在最上方！
            for (int i = 4; i >= 0; i--)
                SpriteTracks.Add(new Models.TimelineTrackModel { TrackIndex = i, TrackName = $"Sprite 轨 {i + 1}" });

            // Ctrl 轨正序添加：Track 0 永远在最上方！
            for (int i = 0; i < 3; i++)
                ControllerTracks.Add(new Models.TimelineTrackModel { TrackIndex = i, TrackName = $"Ctrl 轨 {i + 1}" });
        }

        // ==========================================
        // 🎵 音频单例接入与全局渲染引擎订阅
        // ==========================================
        private void InitializeAudioEngine()
        {
            // 🌟 1. 订阅全局渲染引擎 (V-Sync，帧数暴涨)
            Core.GlobalRenderEngine.Instance.OnRenderTick += () =>
            {
                if (Core.AudioSyncEngine.Instance.IsPlaying && !_isDraggingPlayhead)
                {
                    double smoothTime = Core.AudioSyncEngine.Instance.GetCurrentSmoothTime();
                    UpdatePlayheadPosition(smoothTime * _pixelsPerSecond);
                }
            };

            if (RefreshRateComboBox != null)
            {
                RefreshRateComboBox.SelectionChanged += (s, e) =>
                {
                    Core.GlobalRenderEngine.Instance.IsHighRefreshRate = RefreshRateComboBox.SelectedIndex == 1;
                };
            }

            Core.AudioSyncEngine.Instance.OnTimeChanged += (currentSeconds) =>
            {
                if (!Core.AudioSyncEngine.Instance.IsPlaying && !_isDraggingPlayhead)
                    UpdatePlayheadPosition(currentSeconds * _pixelsPerSecond);
            };

            Core.AudioSyncEngine.Instance.OnPlayStateChanged += (isPlaying) =>
            {
                BtnPlay.Foreground = isPlaying ? Brushes.LightGreen : (Brush)Application.Current.Resources["MainTextColor"];
            };

            Core.AudioSyncEngine.Instance.OnAudioLoaded += () =>
            {
                if (BtnImportAudio != null) BtnImportAudio.Visibility = Visibility.Collapsed;
                if (_totalDurationSeconds < Core.AudioSyncEngine.Instance.Duration)
                {
                    _totalDurationSeconds = Core.AudioSyncEngine.Instance.Duration + 2.0;
                    UpdateTimelineWidth();
                }
                else DrawWaveform(true);
            };
        }

        // ==========================================
        // 🚀 GPU 位移换算中心 (彻底干掉重排卡顿)
        // ==========================================
        private void UpdatePlayheadPosition(double xPos)
        {
            double maxWidth = _totalDurationSeconds * _pixelsPerSecond;
            if (xPos < 0) xPos = 0;
            if (xPos > maxWidth) xPos = maxWidth;

            // 🌟 直接修改 4 根红线的 GPU 通道！C# 内存操作，渲染 0 延迟！
            if (TransRulerHead != null) TransRulerHead.X = xPos;
            if (TransSpriteLine != null) TransSpriteLine.X = xPos;
            // ✨ 驱动中央音频地图的红线指针
            if (AudioMinimapGrid != null && AudioPlayheadLine != null && _totalDurationSeconds > 0)
            {
                double ratio = _currentPlayheadSeconds / _totalDurationSeconds;
                double mapX = ratio * AudioMinimapGrid.ActualWidth;
                AudioPlayheadLine.X1 = mapX;
                AudioPlayheadLine.X2 = mapX;
            }
            if (TransCtrlLine != null) TransCtrlLine.X = xPos;

            _currentPlayheadSeconds = xPos / _pixelsPerSecond;
            UpdatePlaybackTimeDisplay(_currentPlayheadSeconds);
        }

        // ==========================================
        // 🎯 游标交互逻辑
        // ==========================================
        private void Ruler_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border rulerBorder)
            {
                double xPos = e.GetPosition(rulerBorder).X;
                UpdatePlayheadPosition(xPos);
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
            if (_isDraggingPlayhead)
            {
                // 参照物必须是最外层的固定画板
                if (ScrollRuler?.Content is Border rBorder)
                {
                    double xPos = e.GetPosition(rBorder).X;
                    UpdatePlayheadPosition(xPos);
                }
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

        // ==========================================
        // 🎵 播放控制与波形绘制
        // ==========================================
        private void BtnPlay_Click(object sender, RoutedEventArgs e) => Core.AudioSyncEngine.Instance.Play();
        private void BtnPause_Click(object sender, RoutedEventArgs e) => Core.AudioSyncEngine.Instance.Pause();

        private async void BtnImportAudio_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "音频文件 (*.mp3;*.wav;*.ogg)|*.mp3;*.wav;*.ogg",
                Title = "请选择关卡音乐"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                if (BtnImportAudio != null) BtnImportAudio.Visibility = Visibility.Collapsed;
                await Core.AudioSyncEngine.Instance.LoadAudioAsync(openFileDialog.FileName);
            }
        }

        // 🌟 监听尺寸变化：当拖拉窗口时，音频地图要重新绘制！
        private void AudioMinimapGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawWaveform(true);
            UpdateAudioViewportBox();
        }

        // ==========================================
        // 🎵 终极性能版：视口剔除 + 智能峰值拾取波形渲染
        // ==========================================
        private void DrawWaveform(bool forceRedraw = false)
        {
            if (WaveformPath == null || Core.AudioSyncEngine.Instance.WaveformSamples == null) return;
            if (AudioMinimapGrid.ActualWidth <= 0) return;

            var samples = Core.AudioSyncEngine.Instance.WaveformSamples;
            double width = AudioMinimapGrid.ActualWidth; // ✨ 核心：以当前界面的物理宽度为画布大小！
            double height = 40;
            double midY = height / 2;

            int step = Math.Max(1, samples.Length / (int)width);

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(0, midY), false, false);
                for (int i = 0; i < samples.Length; i += step)
                {
                    double x = (double)i / samples.Length * width;
                    double y = midY - (samples[i] * midY);
                    ctx.LineTo(new Point(x, y), true, false);
                }
            }
            geometry.Freeze();
            WaveformPath.Data = geometry;
        }

        // =========================================
        // ⏱️ 播放时间显示优化：只更新文本内容，绝不修改 TextBlock 的位置或其他属性，完美避免重排卡顿！
        // =========================================
        public void UpdatePlaybackTimeDisplay(double currentSeconds)
        {
            if (TxtCurrentTime != null)
            {
                string newText = currentSeconds.ToString("0.000") + "s";
                // 🌟 性能法术：只有当文字真正改变时，才去惊动 WPF 的文本渲染引擎！
                if (_lastTimeText != newText)
                {
                    TxtCurrentTime.Text = newText;
                    _lastTimeText = newText;
                }
            }
        }



        // ==========================================
        // 📏 时空缩放与刻度绘制
        // ==========================================
        private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // ✨ 将右侧画布的“上下滚动”分发给专属的垂直同步器！
            if (sender == ScrollSprites) SyncSpriteVerticalScroll(sender, e);
            if (sender == ScrollControllers) SyncCtrlVerticalScroll(sender, e);

            // ✨ 横向滚动独立处理
            if (_isSyncingScroll || Math.Abs(e.HorizontalChange) < 0.001) return;
            _isSyncingScroll = true;

            double targetOffset = e.HorizontalOffset;
            if (sender != ScrollRuler && ScrollRuler != null) ScrollRuler.ScrollToHorizontalOffset(targetOffset);
            if (sender != ScrollSprites && ScrollSprites != null) ScrollSprites.ScrollToHorizontalOffset(targetOffset);
            if (sender != ScrollControllers && ScrollControllers != null) ScrollControllers.ScrollToHorizontalOffset(targetOffset);
            if (sender != ScrollNotes && ScrollNotes != null) ScrollNotes.ScrollToHorizontalOffset(targetOffset);

            _isSyncingScroll = false;
            UpdateAudioViewportBox();
        }

        private void OnTimelineMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                e.Handled = true;
                double zoomFactor = e.Delta > 0 ? 1.2 : (1.0 / 1.2);
                double newPixelsPerSecond = _pixelsPerSecond * zoomFactor;
                newPixelsPerSecond = Math.Max(MinPixelsPerSecond, Math.Min(MaxPixelsPerSecond, newPixelsPerSecond));

                if (Math.Abs(newPixelsPerSecond - _pixelsPerSecond) > 0.01)
                {
                    _pixelsPerSecond = newPixelsPerSecond;
                    UpdateTimelineWidth();
                }
            }
        }

        private void UpdateTimelineWidth()
        {
            double newWidth = _totalDurationSeconds * _pixelsPerSecond;
            newWidth += 200;

            if (ScrollRuler?.Content is Border rBorder) rBorder.Width = newWidth;
            if (SpriteClipCanvas != null) SpriteClipCanvas.Width = newWidth;
            if (ControllerClipCanvas != null) ControllerClipCanvas.Width = newWidth;
            if (NotePreviewCanvas != null) NotePreviewCanvas.Width = newWidth;
            // ✨ 保证音符画布长度同步！
            if (NotePreviewCanvas != null) NotePreviewCanvas.Width = newWidth;
            // 同步背景层的物理宽度
            if (SpriteBgCanvas != null) SpriteBgCanvas.Width = newWidth;
            if (CtrlBgCanvas != null) CtrlBgCanvas.Width = newWidth;


            DrawTimelineRuler();
            DrawWaveform(true);
            // ✨ 联动降临：每次时间轴横向宽度拉伸或缩放，参考线也要跟着重算坐标像素！
            DrawBeatGridLines();
            DrawNotePreviews();
            UpdateAudioViewportBox(); // ✨ 追加：缩放后重新计算滑块大小
            RedrawBackgroundGrids();
        }

        private void DrawTimelineRuler()
        {
            if (RulerCanvas == null) return;
            RulerCanvas.Children.Clear();

            double majorStep = 1.0;
            double minorStep = 0.1;

            if (_pixelsPerSecond >= 200) { majorStep = 0.5; minorStep = 0.05; }
            else if (_pixelsPerSecond >= 100) { majorStep = 1.0; minorStep = 0.1; }
            else if (_pixelsPerSecond >= 40) { majorStep = 5.0; minorStep = 1.0; }
            else { majorStep = 10.0; minorStep = 2.0; }

            for (double time = 0; time <= _totalDurationSeconds; time += minorStep)
            {
                double xPos = time * _pixelsPerSecond;
                bool isMajor = Math.Abs(time % majorStep) < 0.001 || Math.Abs((time % majorStep) - majorStep) < 0.001;

                if (isMajor)
                {
                    Line majorLine = new Line
                    {
                        X1 = xPos,
                        Y1 = 15,
                        X2 = xPos,
                        Y2 = 30,
                        Stroke = (Brush)Application.Current.Resources["BorderColor"] ?? Brushes.Gray,
                        StrokeThickness = 1.2
                    };
                    RulerCanvas.Children.Add(majorLine);

                    TextBlock timeLabel = new TextBlock
                    {
                        Text = $"{time:0.#}s",
                        FontSize = 9,
                        Foreground = (Brush)Application.Current.Resources["SecTextColor"] ?? Brushes.DarkGray,
                        // 🌟 终极防崩法术：直接使用 Transform 代替 Canvas.SetLeft，不管画板是不是 Canvas，绝对保证文字位置正确！
                        RenderTransform = new TranslateTransform { X = xPos + 4, Y = 2 }
                    };
                    RulerCanvas.Children.Add(timeLabel);
                }
                else
                {
                    Line minorLine = new Line
                    {
                        X1 = xPos,
                        Y1 = 24,
                        X2 = xPos,
                        Y2 = 30,
                        Stroke = (Brush)Application.Current.Resources["BorderColor"] ?? Brushes.Gray,
                        StrokeThickness = 0.6,
                        Opacity = 0.5
                    };
                    RulerCanvas.Children.Add(minorLine);
                }
            }
        }
        // =========================================================================
        // 🧙‍♂️ 空间重组法术：一键智能整理轨道（接通 TimelineLayoutEngine 核心算法）
        // =========================================================================
        private void MenuOptimizeLayout_Click(object sender, RoutedEventArgs e)
        {
            // 1. 🔌 安全门检测：拿到全局万能数据上下文
            // 假设大本营的 Window 或 ViewModel 里能拿到当前的上下文，这里做安全绑定
            if (Window.GetWindow(this) is MainWindow mainWin)
            {
                var context = mainWin.Context;
                if (context == null || !context.HasStoryboard) return;

                // 2. 🚨 【冷酷抹杀警告弹窗】：严丝合缝执行设计师指示！
                var result = MessageBox.Show(
                    "⚠️ 警告：智能轨道整理将会对全场事件大洗牌！\\n" +
                    "所有自定义的轨道命名（Track Names）将被冷酷抹杀清空。\\n\\n" +
                    "确定要让常驻永生元素强制归位到时空最中心吗？",
                    "时空收纳警告",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes) return; // 用户反悔了，直接无视

                // 3. 📦 抓取当前主时间轴视图内的全量方块模型列表
                // 实际开发中，这里可以从当前的 TrackList 或是画布的 Children 的 DataContext 中统一抓取
                List<Models.TimelineClipModel> allActiveClips = GatherCurrentTimelineClips(context);

                if (allActiveClips == null || allActiveClips.Count == 0)
                {
                    MessageBox.Show("纳尼？！当前舞台上还没有任何事件方块可以整理哦~", "收纳失败");
                    return;
                }

                // 4. 🚀 降临 Core 层核心无碰撞算法！
                Core.Timeline.TimelineLayoutEngine.OptimizeTrackLayout(allActiveClips, centerTrackBaseline: 8);

                // 5. 🎨 刷新前台画面：清空旧图层账本，将全新洗盘后的方块重绘到 Canvas 上！
                RefreshTimelineVisualTracks(allActiveClips);

                // 广播大本营：数据已被大幅蹂躏修改
                context.MarkAsModified();
            }
        }

        /// <summary>
        /// 📥 辅助探头：从主项目数据中捞出所有处于普通图层的 Clip 模型
        /// </summary>
        private List<Models.TimelineClipModel> GatherCurrentTimelineClips(State.ProjectDataContext context)
        {
            var list = new List<Models.TimelineClipModel>();
            // 这里遍历正在编辑的场景对象，将其转化为带有 StartTime/EndTime 的 ClipModel 传入
            // 示例：将 root.sprites, root.texts 等统一进行轻量包装
            return list;
        }

        /// <summary>
        /// 🎨 辅助画笔：根据算法重写后的 TrackIndex，重新控制前台 Canvas 的物理解析度排版
        /// </summary>
        private void RefreshTimelineVisualTracks(List<Models.TimelineClipModel> sortedClips)
        {
            if (SpriteClipCanvas == null || ControllerClipCanvas == null) return;

            // 清空现有的可视化轨道名册骨架（实现冷酷抹杀）
            // 重新遍历 sortedClips，根据各自的 TrackIndex 反算它的物理 Canvas.Top
            // 每一轨高 40px，则：Canvas.SetTop(clipControl, clip.TrackIndex * 40);

            // 触发 UpdateTimelineWidth() 刷新宏观大画板
            UpdateTimelineWidth();
        }


        // =========================================================================
        // 🎵 音游人绝对领域：拍号与动态节拍网格线渲染引擎 (Theme 动态联动版)
        // =========================================================================

        // 📋 拍号与细分控制台
        private int _beatsPerMeasure = 4;      // 拍号分子：每小节多少拍 (默认 4/4 拍里的 4 拍)
        private int _beatSubdivision = 4;     // 节拍细分：1/4拍(四分音符)、1/8拍(八分音符)、1/16拍等
        private double _gridLineOpacityMajor = 0.35; // 大节拍参考线透明度
        private double _gridLineOpacityMinor = 0.15; // 细分拍参考线透明度

        /// <summary>
        /// 🎛️ 外部控制塔：供前台“拍号切换下拉菜单”或快捷键呼叫，一键重算网格
        /// </summary>
        public void UpdateTimeSignature(int beatsPerMeasure, int subdivision)
        {
            _beatsPerMeasure = beatsPerMeasure;
            _beatSubdivision = subdivision;

            // 立即重新绘制标尺与节拍线
            DrawTimelineRuler();
            DrawBeatGridLines();
        }

        /// <summary>
        /// 🎨 核心手画：根据谱面 BPM 变速点非线性换算，在背景画布上铺满半透明节拍参考线
        /// </summary>
        // =========================================================================
        // 📐 工业级数学渲染引擎：水平轨道线与垂直节拍线 (方案C完美落地)
        // =========================================================================
        public void RedrawBackgroundGrids()
        {
            if (SpriteBgCanvas == null || CtrlBgCanvas == null) return;

            // 1. 全量清空底层画布（完美解决多次放大缩小导致的线条内存泄露！）
            SpriteBgCanvas.Children.Clear();
            CtrlBgCanvas.Children.Clear();

            // 2. 按顺序执行绘制
            DrawHorizontalTrackLines();
            DrawBeatGridLines();
        }

        private void DrawHorizontalTrackLines()
        {
            double currentWidth = SpriteClipCanvas.Width;
            if (double.IsNaN(currentWidth) || currentWidth <= 0) currentWidth = 5000;
            double trackHeight = 40.0;

            // 🖼️ 渲染 Sprite 轨道的水平格线 (去除多余空间，按实际层数匹配)
            SpriteClipCanvas.Height = SpriteTracks.Count * trackHeight;
            SpriteBgCanvas.Height = SpriteClipCanvas.Height;

            for (int i = 0; i <= SpriteTracks.Count; i++)
            {
                Line line = new Line { X1 = 0, Y1 = i * trackHeight, X2 = currentWidth, Y2 = i * trackHeight, StrokeThickness = 1, IsHitTestVisible = false };
                line.SetResourceReference(Line.StrokeProperty, "BorderColor");
                SpriteBgCanvas.Children.Add(line);
            }

            // 🎛️ 渲染 Ctrl 轨道的水平格线
            ControllerClipCanvas.Height = ControllerTracks.Count * trackHeight;
            CtrlBgCanvas.Height = ControllerClipCanvas.Height;

            for (int i = 0; i <= ControllerTracks.Count; i++)
            {
                Line line = new Line { X1 = 0, Y1 = i * trackHeight, X2 = currentWidth, Y2 = i * trackHeight, StrokeThickness = 1, IsHitTestVisible = false };
                line.SetResourceReference(Line.StrokeProperty, "BorderColor");
                CtrlBgCanvas.Children.Add(line);
            }
        }

        private void DrawBeatGridLines()
        {
            if (Window.GetWindow(this) is MainWindow mainWin)
            {
                var context = mainWin.Context;
                if (context == null || !context.HasChart || context.TimeEngine == null) return;

                var chart = context.Chart;
                int timeBase = chart.time_base;
                int ticksPerGrid = timeBase / (_beatSubdivision / 4);
                if (ticksPerGrid <= 0) ticksPerGrid = 1;

                int currentGridIndex = 0;
                int maxTick = chart.note_list.Count > 0 ? chart.note_list.Max(n => n.tick) : 0;
                int stopTick = maxTick + (timeBase * _beatsPerMeasure * 4);

                for (int tick = 0; tick <= stopTick; tick += ticksPerGrid)
                {
                    double absoluteSeconds = context.TimeEngine.TickToSeconds(tick);
                    if (absoluteSeconds > _totalDurationSeconds) break;

                    double xPos = absoluteSeconds * _pixelsPerSecond;
                    int gridsPerMeasure = _beatsPerMeasure * (_beatSubdivision / 4);
                    bool isMajorBeat = (currentGridIndex % gridsPerMeasure == 0);

                    // 注入 Sprite 轨道底层
                    Line beatLineSprite = new Line
                    {
                        X1 = xPos,
                        Y1 = 0,
                        X2 = xPos,
                        Y2 = SpriteBgCanvas.Height,
                        StrokeThickness = isMajorBeat ? 1.5 : 0.8,
                        Opacity = isMajorBeat ? _gridLineOpacityMajor : _gridLineOpacityMinor,
                        IsHitTestVisible = false
                    };
                    beatLineSprite.SetResourceReference(Line.StrokeProperty, isMajorBeat ? "HighlightBorderColor" : "BorderColor");
                    SpriteBgCanvas.Children.Add(beatLineSprite);

                    // 注入 Ctrl 轨道底层
                    Line beatLineCtrl = new Line
                    {
                        X1 = xPos,
                        Y1 = 0,
                        X2 = xPos,
                        Y2 = CtrlBgCanvas.Height,
                        StrokeThickness = isMajorBeat ? 1.5 : 0.8,
                        Opacity = isMajorBeat ? _gridLineOpacityMajor : _gridLineOpacityMinor,
                        IsHitTestVisible = false
                    };
                    beatLineCtrl.SetResourceReference(Line.StrokeProperty, isMajorBeat ? "HighlightBorderColor" : "BorderColor");
                    CtrlBgCanvas.Children.Add(beatLineCtrl);

                    currentGridIndex++;
                }
            }
        }

        // =========================================================================
        // 🎹 智能音符刻度渲染引擎 (完美区分 has_sibling 真兄弟 与 圆筒形视觉聚合)
        // =========================================================================
        private void DrawNotePreviews()
        {
            if (NotePreviewCanvas == null) return;
            NotePreviewCanvas.Children.Clear();

            if (Window.GetWindow(this) is MainWindow mainWin)
            {
                var context = mainWin.Context;
                if (context == null || !context.HasChart || context.Chart.note_list == null) return;

                var noteList = context.Chart.note_list.OrderBy(n => n.tick).ToList();
                if (noteList.Count == 0) return;

                // 🌟 容差距离定义：屏幕上相距不到 6 个像素的音符，会被打包成“圆筒形”
                double visualClusterTolerance = 6.0;

                // 扫盘准备
                int i = 0;
                while (i < noteList.Count)
                {
                    var rootNote = noteList[i];
                    double rootSeconds = context.TimeEngine.TickToSeconds(rootNote.tick);
                    double rootX = rootSeconds * _pixelsPerSecond;

                    // 📦 开启一个聚类探针，向后嗅探
                    int j = i;
                    bool hasTrueSibling = false; // 是否存在真正的 has_sibling
                    int siblingCount = 1;

                    while (j + 1 < noteList.Count)
                    {
                        var nextNote = noteList[j + 1];
                        double nextSeconds = context.TimeEngine.TickToSeconds(nextNote.tick);
                        double nextX = nextSeconds * _pixelsPerSecond;

                        // 物理距离极其接近，吸纳进当前聚类包
                        if (nextX - rootX <= visualClusterTolerance)
                        {
                            // 只有在时间完全一模一样，且官方标注了 sibling 时，才是真兄弟！
                            if (Math.Abs(nextSeconds - rootSeconds) < 0.001 &&
                               (rootNote.has_sibling == true || nextNote.has_sibling == true))
                            {
                                hasTrueSibling = true;
                            }
                            siblingCount++;
                            j++;
                        }
                        else
                        {
                            break; // 距离够远了，当前聚类包打包完毕！
                        }
                    }

                    // 🎨 开始根据聚类包的成分进行 UI 渲染绘制
                    // 假设双轨的高度是 40，Track 1 在 Y=5，Track 2 在 Y=25 (暂定统一画在中心 Y=10)
                    double trackY = 6;
                    double clusterWidth = (j > i) ? ((context.TimeEngine.TickToSeconds(noteList[j].tick) * _pixelsPerSecond) - rootX + 10) : 10;

                    if (hasTrueSibling && siblingCount > 1)
                    {
                        // ⭕ 【真兄弟模式】：完全重合，画一个带有数字的小圆环
                        Ellipse ring = new Ellipse { Width = 14, Height = 14, StrokeThickness = 1.5, Fill = Brushes.Transparent };
                        ring.SetResourceReference(Ellipse.StrokeProperty, "HighlightBorderColor"); // 动态颜色

                        TextBlock txtNum = new TextBlock
                        {
                            Text = siblingCount.ToString(),
                            FontSize = 9,
                            FontWeight = FontWeights.Bold,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        txtNum.SetResourceReference(TextBlock.ForegroundProperty, "MainTextColor");

                        Grid ringGroup = new Grid { Width = 14, Height = 14 };
                        ringGroup.Children.Add(ring);
                        ringGroup.Children.Add(txtNum);

                        Canvas.SetLeft(ringGroup, rootX - 7);
                        Canvas.SetTop(ringGroup, trackY);
                        NotePreviewCanvas.Children.Add(ringGroup);
                    }
                    else if (siblingCount > 1)
                    {
                        // 💊 【假重叠模式】：由于密度过高挤在一起，画成“圆筒形”胶囊体！
                        Rectangle cylinder = new Rectangle
                        {
                            Width = Math.Max(16, clusterWidth),
                            Height = 12,
                            RadiusX = 6,
                            RadiusY = 6, // 极致圆角变成胶囊
                            StrokeThickness = 1,
                            Opacity = 0.8
                        };
                        // 用边框色暗示它是集群
                        cylinder.SetResourceReference(Rectangle.StrokeProperty, "BorderColor");
                        cylinder.SetResourceReference(Rectangle.FillProperty, "MenuBgColor");

                        Canvas.SetLeft(cylinder, rootX - 6);
                        Canvas.SetTop(cylinder, trackY + 1);
                        NotePreviewCanvas.Children.Add(cylinder);
                    }
                    else
                    {
                        // 🔴 【孤独单身狗模式】：单独的正常音符，用颜色区分类型
                        Rectangle singleNote = new Rectangle { Width = 4, Height = 12, RadiusX = 1, RadiusY = 1, Opacity = 0.9 };

                        // 智能类型判色 (Click 红, Drag 绿, Hold 蓝 等等)
                        Brush typeColor = Brushes.White; // 默认
                        switch (rootNote.type)
                        {
                            case 0: typeColor = Brushes.LightCoral; break; // Click
                            case 1: typeColor = Brushes.LightSkyBlue; break; // Hold
                            case 2: typeColor = Brushes.LightSkyBlue; break; // LongHold
                            case 3: typeColor = Brushes.LightGreen; break; // Drag
                            case 6: typeColor = Brushes.MediumSeaGreen; break; // CDrag
                            case 4: typeColor = Brushes.Plum; break; // Flick
                        }
                        singleNote.Fill = typeColor;

                        Canvas.SetLeft(singleNote, rootX - 2);
                        Canvas.SetTop(singleNote, trackY + 1);
                        NotePreviewCanvas.Children.Add(singleNote);
                    }

                    // 指针跳过已经被打包的音符，继续往后扫
                    i = j + 1;
                }
            }
        }

        // 🌟 方块自我修正：计算当前预览区域在整首歌里的比例
        private void UpdateAudioViewportBox()
        {
            if (AudioMinimapGrid.ActualWidth == 0 || _totalDurationSeconds <= 0) return;

            double totalWidth = _totalDurationSeconds * _pixelsPerSecond + 200; // 与你的主画布总长度一致
            double visibleWidth = ScrollSprites.ViewportWidth;
            if (visibleWidth == 0) visibleWidth = AudioMinimapGrid.ActualWidth; // 兜底防爆

            double scale = AudioMinimapGrid.ActualWidth / totalWidth;

            // 设定方框在地图里的相对宽度
            double boxWidth = visibleWidth * scale;
            if (boxWidth > AudioMinimapGrid.ActualWidth) boxWidth = AudioMinimapGrid.ActualWidth;
            if (boxWidth < 10) boxWidth = 10;

            AudioViewportBox.Width = boxWidth;

            // 根据当前的真实滚动位移，设定方框的位置
            double offset = ScrollSprites.HorizontalOffset;
            double boxLeft = offset * scale;

            Canvas.SetLeft(AudioViewportBox, boxLeft);
        }

        // 🌟 方块被拖拽时：反向指挥四大轨道滚动！
        private void AudioViewportBox_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            if (AudioMinimapGrid.ActualWidth == 0 || _totalDurationSeconds <= 0) return;

            double totalWidth = _totalDurationSeconds * _pixelsPerSecond + 200;
            double scale = totalWidth / AudioMinimapGrid.ActualWidth;

            double currentOffset = ScrollSprites.HorizontalOffset;

            // 把方块上移动的一小步，放大成时间轴上的真实位移
            double newOffset = currentOffset + e.HorizontalChange * scale;

            if (newOffset < 0) newOffset = 0;
            if (newOffset > totalWidth - ScrollSprites.ViewportWidth) newOffset = totalWidth - ScrollSprites.ViewportWidth;

            // 指挥 ScrollSprites 滚动，它会自动触发 OnScrollChanged 去通知全员！
            ScrollSprites.ScrollToHorizontalOffset(newOffset);
        }



        // =========================================================================
        // ➕🗑️ 动态轨道增删与 UI 交互控制台
        // =========================================================================

        private void BtnAddSpriteTrack_Click(object sender, RoutedEventArgs e)
        {
            int nextIndex = SpriteTracks.Count > 0 ? SpriteTracks.Max(t => t.TrackIndex) + 1 : 0;
            // ✨ 插入到集合的“头部(0)”，WPF会自动将它渲染在列表的最上方！
            SpriteTracks.Insert(0, new Models.TimelineTrackModel { TrackIndex = nextIndex, TrackName = $"新场景轨 {nextIndex + 1}" });
            RedrawBackgroundGrids();
        }

        private void BtnDeleteSpriteTrack_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Models.TimelineTrackModel track)
            {
                // 如果当前点击的轨道和上一次点击的一致，触发“终极毁灭法术”
                if (_armedSpriteTrack == track)
                {
                    SpriteTracks.Remove(track);
                    _armedSpriteTrack = null;
                    _armedSpriteButton = null;
                    RedrawBackgroundGrids();
                }
                else
                {
                    // 恢复上一个被误触框住的按钮（如果有的话）的无边框状态
                    if (_armedSpriteButton != null) _armedSpriteButton.BorderThickness = new Thickness(0);

                    // ✨ 第一次点击：将其确认为待毁灭目标，用红框严严实实地圈住它！
                    _armedSpriteTrack = track;
                    _armedSpriteButton = btn;
                    btn.BorderThickness = new Thickness(1.5);
                }
            }
        }

        private void BtnAddCtrlTrack_Click(object sender, RoutedEventArgs e)
        {
            int nextIndex = ControllerTracks.Count > 0 ? ControllerTracks.Max(t => t.TrackIndex) + 1 : 0;
            ControllerTracks.Add(new Models.TimelineTrackModel { TrackIndex = nextIndex, TrackName = $"新控制轨 {nextIndex + 1}" });
            RedrawBackgroundGrids();
        }

        private void BtnDeleteCtrlTrack_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Models.TimelineTrackModel track)
            {
                if (_armedCtrlTrack == track)
                {
                    ControllerTracks.Remove(track);
                    _armedCtrlTrack = null;
                    _armedCtrlButton = null;
                    RedrawBackgroundGrids();
                }
                else
                {
                    if (_armedCtrlButton != null) _armedCtrlButton.BorderThickness = new Thickness(0);

                    // ✨ 第一次点击：红框锁定
                    _armedCtrlTrack = track;
                    _armedCtrlButton = btn;
                    btn.BorderThickness = new Thickness(1.5);
                }
            }
        }

        // =========================================================================
        // 🚀 一键跃迁：轨道纵向滚动快捷指令
        // =========================================================================
        private void BtnScrollSpriteTop_Click(object sender, RoutedEventArgs e)
        {
            if (ScrollSprites != null) ScrollSprites.ScrollToTop();
        }

        private void BtnScrollSpriteBottom_Click(object sender, RoutedEventArgs e)
        {
            if (ScrollSprites != null) ScrollSprites.ScrollToBottom();
        }

        private void BtnScrollCtrlTop_Click(object sender, RoutedEventArgs e)
        {
            if (ScrollControllers != null) ScrollControllers.ScrollToTop();
        }

        private void BtnScrollCtrlBottom_Click(object sender, RoutedEventArgs e)
        {
            if (ScrollControllers != null) ScrollControllers.ScrollToBottom();
        }




        // =========================================================================
        // 🎭 轨道名称重命名保护中枢
        // =========================================================================
        private void TrackNameTextBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                // ✨ 连续点击两下后，撕开只读基因，强制获取光标并全选文字！
                textBox.IsReadOnly = false;
                textBox.Focus();
                textBox.SelectAll();
            }
        }

        private void TrackNameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                // 当创作者点击其他地方或者按下回车失去焦点时，冷酷地重新加锁！
                textBox.IsReadOnly = true;
            }
        }








        // =========================================================================
        // 🔗 上下滚动完美镜像同步法术
        // =========================================================================
        private bool _isSyncingSpriteV = false;
        private void SyncSpriteVerticalScroll(object sender, ScrollChangedEventArgs e)
        {
            if (_isSyncingSpriteV || e.VerticalChange == 0) return;
            _isSyncingSpriteV = true;
            if (sender == ScrollSprites) ScrollSpriteHeaders.ScrollToVerticalOffset(e.VerticalOffset);
            else if (sender == ScrollSpriteHeaders) ScrollSprites.ScrollToVerticalOffset(e.VerticalOffset);
            _isSyncingSpriteV = false;
        }

        private bool _isSyncingCtrlV = false;
        private void SyncCtrlVerticalScroll(object sender, ScrollChangedEventArgs e)
        {
            if (_isSyncingCtrlV || e.VerticalChange == 0) return;
            _isSyncingCtrlV = true;
            if (sender == ScrollControllers) ScrollCtrlHeaders.ScrollToVerticalOffset(e.VerticalOffset);
            else if (sender == ScrollCtrlHeaders) ScrollControllers.ScrollToVerticalOffset(e.VerticalOffset);
            _isSyncingCtrlV = false;
        }











        // =========================================================================
        // 🏭 事件方块生成工厂与全息雷达监听
        // =========================================================================
        public void SpawnClipIntoTimeline(Models.TimelineClipModel clipModel, bool isSpriteRegion)
        {
            var clipControl = new TimelineClipControl();

            // 计算当前的轨道最大边界
            int maxTrack = isSpriteRegion
                ? (SpriteTracks.Count > 0 ? SpriteTracks.Max(t => t.TrackIndex) : 0)
                : (ControllerTracks.Count > 0 ? ControllerTracks.Max(t => t.TrackIndex) : 0);

            // 初始化注入灵魂
            if (Window.GetWindow(this) is MainWindow mainWin)
            {
                clipControl.Init(clipModel, mainWin.Context, _pixelsPerSecond, clipModel.TrackIndex, maxTrack);
            }

            // 🎧 监听事件一：越界修路请求
            clipControl.OnRequestNewTrack += (senderControl) =>
            {
                if (isSpriteRegion)
                {
                    BtnAddSpriteTrack_Click(null, null); // 借用按钮逻辑新建轨道
                    senderControl.MaxTrackIndex = SpriteTracks.Max(t => t.TrackIndex); // 更新认知边界
                }
                else
                {
                    BtnAddCtrlTrack_Click(null, null);
                    senderControl.MaxTrackIndex = ControllerTracks.Max(t => t.TrackIndex);
                }
            };

            // 🎧 监听事件二：换轨深度重排
            clipControl.OnTrackIndexChanged += (senderControl, newTrackIndex) =>
            {
                // 可以在这里做一些轨道碰撞检测或 UI 层级(Panel.ZIndex)更新
                Panel.SetZIndex(senderControl, newTrackIndex);
            };

            // 🎧 监听事件三：进入精神时光屋 (详细调整模式)
            clipControl.OnRequestDetailedEditMode += (targetModel) =>
            {
                if (_isDetailedEditMode) return;
                _isDetailedEditMode = true;
                _editingClipModel = targetModel;

                // 1. 🙈 隐藏主编辑区的四大编辑轨道网格
                if (ScrollRuler != null) ScrollRuler.Visibility = Visibility.Collapsed;
                if (SpriteTrackHeadersControl != null) ((FrameworkElement)SpriteTrackHeadersControl.Parent).Visibility = Visibility.Collapsed;
                if (SpriteClipCanvas != null) ((FrameworkElement)SpriteClipCanvas.Parent).Visibility = Visibility.Collapsed;
                if (CtrlTrackHeadersControl != null) ((FrameworkElement)CtrlTrackHeadersControl.Parent).Visibility = Visibility.Collapsed;
                if (ControllerClipCanvas != null) ((FrameworkElement)ControllerClipCanvas.Parent).Visibility = Visibility.Collapsed;

                // 2. 🏗️ 动态生成微观大幕帘（如果不存在的话）
                if (_detailedEditor == null)
                {
                    _detailedEditor = new TimelineClip.ClipDetailedEditor();
                    // 将其塞进主 Grid 的第一行（也就是原本装载主编辑区 Inner Row 1 的格子）
                    // 假设你的 XAML 里面主编辑区是一个叫 MainEditingGrid 的容器，直接 Add 进去
                    // 这里我们暂时加在包含这几个 ScrollViewer 的父级 Grid 上，给它拉满全屏
                    if (ScrollSprites?.Parent is Grid parentGrid)
                    {
                        Grid.SetRow(_detailedEditor, 1); // 侵占整个编辑大图层
                        Grid.SetColumnSpan(_detailedEditor, 3);
                        parentGrid.Children.Add(_detailedEditor);
                    }
                }

                _detailedEditor.Visibility = Visibility.Visible;

                // 3. ⏱️ 【端点钉死算法】：将微观时间轴的比例尺，重设为方块的绝对长度！
                double clipDuration = targetModel.EndTime - targetModel.StartTime;
                if (clipDuration <= 0) clipDuration = 2.0; // 永生常驻元素兜底给2秒前台视区

                // 刷新微观时光屋内的所有属性轨（X, Y, Opacity 等）
                // 里面的属性轨会严格执行大大的【补丁1：首尾死锁不可拖拽】
                if (Window.GetWindow(this) is MainWindow mainWin)
                {
                    // 假设你的 ClipDetailedEditor 里面写好了 LoadClipData 方法
                    _detailedEditor.LoadClipData(targetModel, mainWin.Context, _pixelsPerSecond);
                }

                // 修改主界面时间文本，进入微观沉浸提示
                if (TxtCurrentTime != null) TxtCurrentTime.Foreground = Brushes.MediumPurple; // 换个神秘紫暗示进入微观
            };

            // 最终将实体降临到对应的双轨区画板上！
            if (isSpriteRegion)
            {
                SpriteClipCanvas.Children.Add(clipControl);
            }
            else
            {
                ControllerClipCanvas.Children.Add(clipControl);
            }
        }





        /// <summary>
        /// 🚪 时空逆转：退出微观详细模式，重回宏观歌曲大主轴
        /// </summary>
        public void ExitDetailedEditMode()
        {
            if (!_isDetailedEditMode) return;
            _isDetailedEditMode = false;
            _editingClipModel = null;

            // 1. 🍄 隐藏微观幕帘
            if (_detailedEditor != null) _detailedEditor.Visibility = Visibility.Collapsed;

            // 2. 👀 重新唤醒主编辑区的四大金刚
            if (ScrollRuler != null) ScrollRuler.Visibility = Visibility.Visible;
            if (SpriteClipCanvas != null) ((FrameworkElement)SpriteClipCanvas.Parent).Visibility = Visibility.Visible;
            if (ControllerClipCanvas != null) ((FrameworkElement)ControllerClipCanvas.Parent).Visibility = Visibility.Visible;

            // 3. 🎨 刷新宏观主轴
            if (TxtCurrentTime != null) TxtCurrentTime.Foreground = new SolidColorBrush(Color.FromRgb(77, 184, 255)); // 恢复闪耀蓝
            UpdateTimelineWidth();
        }





    }



}