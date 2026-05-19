using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using NAudio.Wave;
using System.Threading.Tasks;

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

        // 时间轴播放记忆锁
        private string _lastTimeText = ""; 

        public TimelineControl()
        {
            InitializeComponent();
            InitializeAudioEngine();
            UpdateTimelineWidth();
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
            if (TransAudioLine != null) TransAudioLine.X = xPos;
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

        // ==========================================
        // 🎵 终极性能版：视口剔除 + 智能峰值拾取波形渲染
        // ==========================================
        private void DrawWaveform(bool forceRebuild = false)
        {
            var samples = Core.AudioSyncEngine.Instance.WaveformSamples;
            if (samples == null || WaveformPath == null) return;

            // 获取当前屏幕的滚动位置和可视宽度
            // 🌟 获取底层真正的采样率！
            int engineSampleRate = Core.AudioSyncEngine.Instance.WaveformSampleRate;

            double offset = MasterHorizontalScroll?.HorizontalOffset ?? 0;
            double viewport = MasterHorizontalScroll?.ViewportWidth ?? 1500;
            if (viewport == 0) viewport = 1500; // 窗口刚启动还没布局时的默认视口

            // 🌟 核心逻辑：如果非强制重绘，且当前视口还舒舒服服地呆在我们的缓冲区内，就直接打卡下班！不重画！
            if (!forceRebuild && offset > (_renderedWaveformStartX + 500) && (offset + viewport) < (_renderedWaveformEndX - 500))
            {
                return;
            }

            // ⚠️ 触发重绘！计算新的三屏缓冲区（左预读一屏，右预读一屏）
            double realWidth = Core.AudioSyncEngine.Instance.Duration * _pixelsPerSecond;
            double startX = Math.Max(0, offset - viewport);
            double endX = Math.Min(realWidth, offset + viewport * 2);

            StreamGeometry geometry = new StreamGeometry();
            using (StreamGeometryContext ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(startX, 20), false, false);

                double visualPixelStep = 1.0; // 像素跨度
                double timeStepSeconds = visualPixelStep / _pixelsPerSecond;

                // 乘上真实的 engineSampleRate！
                int sampleStep = Math.Max(1, (int)(timeStepSeconds * engineSampleRate));

                // 🌟 只在这个“三屏视口”范围内进行数学运算！外面的 50000 像素数据直接无视！
                for (double x = startX; x <= endX; x += visualPixelStep)
                {
                    double currentSecond = x / _pixelsPerSecond;
                    // 🌟 核心修复：乘上真实的 engineSampleRate！
                    int startIndex = (int)(currentSecond * engineSampleRate);
                    int endIndex = startIndex + sampleStep;

                    float maxVolumeInStep = 0;
                    for (int i = startIndex; i < endIndex && i < samples.Length; i++)
                    {
                        if (samples[i] > maxVolumeInStep) maxVolumeInStep = samples[i];
                    }

                    double height = maxVolumeInStep * 40;
                    if (height < 1) height = 1;

                    ctx.LineTo(new Point(x, 20 - (height / 2)), true, false);
                }
            }
            WaveformPath.Data = geometry;

            // 更新探针记忆！
            _renderedWaveformStartX = startX;
            _renderedWaveformEndX = endX;
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
            if (_isSyncingScroll || e.HorizontalChange == 0) return;
            _isSyncingScroll = true;

            double targetOffset = e.HorizontalOffset;
            if (sender != ScrollRuler && ScrollRuler != null) ScrollRuler.ScrollToHorizontalOffset(targetOffset);
            if (sender != ScrollSprites && ScrollSprites != null) ScrollSprites.ScrollToHorizontalOffset(targetOffset);
            if (sender != ScrollAudio && ScrollAudio != null) ScrollAudio.ScrollToHorizontalOffset(targetOffset);
            if (sender != ScrollControllers && ScrollControllers != null) ScrollControllers.ScrollToHorizontalOffset(targetOffset);
            if (sender != MasterHorizontalScroll && MasterHorizontalScroll != null) MasterHorizontalScroll.ScrollToHorizontalOffset(targetOffset);

            _isSyncingScroll = false;
            // 🌟 性能法术：通知波形图检查是否超出了缓冲区边界！
            DrawWaveform();
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
            if (AudioTrackGrid != null) AudioTrackGrid.Width = newWidth;
            if (ControllerClipCanvas != null) ControllerClipCanvas.Width = newWidth;
            if (MasterHorizontalScroll?.Content is Canvas masterCanvas) masterCanvas.Width = newWidth;

            DrawTimelineRuler();
            DrawWaveform(true);
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
    }
}