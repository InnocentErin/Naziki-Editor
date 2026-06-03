using Naziki_Editor.Core.Timeline;
using Naziki_Editor.Models;
using Naziki_Editor.State;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Naziki_Editor.Views.TimelineClip
{
    public partial class ClipDetailedEditor : UserControl
    {
        private TimelineClipModel _clipModel;
        private ProjectDataContext _context;
        private double _pixelsPerSecond;
        private double _lastCalculatedMaxTime = 0; // 记录总时空长度

        public ClipDetailedEditor()
        {
            InitializeComponent();

            // ✨ 1. 让顶部标尺和底部画板永远对齐联动！
            ScrollPropCanvas.ScrollChanged += (s, e) => {
                ScrollMicroRuler.ScrollToHorizontalOffset(e.HorizontalOffset);
            };

            // ✨ 2. 绑定滚轮缩放神技！
            this.PreviewMouseWheel += Editor_PreviewMouseWheel;


        }

        /// <summary>
        /// 🚀 【数据接线关口】：主轴双击方块后，此方法会被轰轰烈烈地激活！
        /// </summary>
        public void LoadClipData(TimelineClipModel clipModel, ProjectDataContext context, double pixelsPerSecond)
        {
            _clipModel = clipModel;
            _context = context;
            _pixelsPerSecond = pixelsPerSecond;

            PropHeadersStackPanel.Children.Clear();
            PropTracksStackPanel.Children.Clear();

            if (_clipModel.AssociatedObject == null) return;

            // ==========================================
            // ✨ 宇宙测绘：寻找主时间轴的尽头，以及最后一个关键帧的时间！
            // ==========================================
            double maxTime = 10;
            if (_context.Chart?.note_list != null && _context.Chart.note_list.Count > 0)
                maxTime = _context.TimeEngine.TickToSeconds(_context.Chart.note_list[context.Chart.note_list.Count - 1].tick) + 5;

            double lastFrameAbs = StoryboardTimeConverter.CalculateEntityEndTime(
                _clipModel.AssociatedObject,
                _clipModel.StartTime,
                _context.TimeEngine,
                _context.Chart?.note_list
            );

            // 联动修正：确保方块模型内存里的 EndTime 与核心同步刷新
            _clipModel.EndTime = lastFrameAbs;
            if (lastFrameAbs + 5 > maxTime) maxTime = lastFrameAbs + 5;

            _lastCalculatedMaxTime = maxTime;
            double targetPhysicalWidth = maxTime * _pixelsPerSecond;

            MicroRulerCanvas.Width = targetPhysicalWidth;

            // 3. 🔬 【智能门派拆分】：利用 Cytoid 强类型反射，分发不同的微观属性轨道！

            // ==========================================
            // ✨ 3. 🔬 【全量展开】：无条件显示所有支持的动画轨道！
            // ==========================================
            List<string> supportedProperties = new List<string>();
            if (_clipModel.AssociatedObject != null)
            {
                string typeName = _clipModel.AssociatedObject.GetType().Name;

                if (typeName == "C2Sprite" || typeName == "C2Text" || typeName == "C2Line" || typeName == "C2Video")
                {
                // 场景图层对象特有的几何运动属性
                    supportedProperties.AddRange(new[] { "X", "Y", "Z", "Opacity", "ScaleX", "ScaleY", "RotZ", "Order" });
                }
                else if (typeName == "C2SceneController")
                {
                // 场景控制器特有的全局黑科技属性
                    supportedProperties.AddRange(new[] { "Fov", "BackgroundDim", "UiOpacity", "StoryboardOpacity", "ScanlineOpacity", "Brightness", "GlitchIntensity" });
                }
                else if (typeName == "C2NoteController")
                {
                // 音符控制器的打击偏移属性
                    supportedProperties.AddRange(new[] { "X", "Y", "XMultiplier", "YMultiplier", "XOffset", "YOffset", "OpacityMultiplier" });
                }
            }

            // 4. 🧵 机械化流水线：批量手绘每一个属性的“表头 + 关键帧长轨”
            foreach (string prop in supportedProperties)
            {
                // A. 左侧：纯净的属性名文字边框
                Border headerBorder = new Border
                {
                    Height = 40,
                    BorderBrush = (Brush)Application.Current.FindResource("BorderColor"),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(10, 0, 0, 0)
                };
                TextBlock headerText = new TextBlock
                {
                    Text = prop,
                    Foreground = (Brush)Application.Current.FindResource("MainTextColor"),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeights.SemiBold
                };
                headerBorder.Child = headerText;
                PropHeadersStackPanel.Children.Add(headerBorder);

                // B. 右侧：降临单属性关键帧格线行！
                ClipPropertyTrackRow trackRow = new ClipPropertyTrackRow();
                trackRow.Width = targetPhysicalWidth;
                trackRow.HorizontalAlignment = HorizontalAlignment.Left;
                trackRow.Init(prop, _clipModel, _context, _pixelsPerSecond);
                PropTracksStackPanel.Children.Add(trackRow);
            }

            RenderMicroRulerTicks(maxTime);

            // ==========================================
            // 🚀 核心跳转：UI 就绪后，让摄像机光速飞向方块所在的时间节点！
            // ==========================================
            Application.Current.Dispatcher.BeginInvoke(new Action(() => {
                double targetOffset = _clipModel.StartTime * _pixelsPerSecond - 50; // 往前看 50 像素的余量
                if (targetOffset < 0) targetOffset = 0;
                ScrollPropCanvas.ScrollToHorizontalOffset(targetOffset);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }



        private void Editor_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                double zoomDelta = e.Delta > 0 ? 1.15 : 1 / 1.15;
                _pixelsPerSecond *= zoomDelta;
                if (_pixelsPerSecond < 10) _pixelsPerSecond = 10;
                if (_pixelsPerSecond > 2000) _pixelsPerSecond = 2000;

                double newWidth = _lastCalculatedMaxTime * _pixelsPerSecond;
                MicroRulerCanvas.Width = newWidth;
                RenderMicroRulerTicks(_lastCalculatedMaxTime);

                foreach (UIElement el in PropTracksStackPanel.Children)
                {
                    if (el is ClipPropertyTrackRow row)
                    {
                        row.Width = newWidth;
                        row.UpdateZoom(_pixelsPerSecond);
                    }
                }
                e.Handled = true;
            }
        }








        private void RenderMicroRulerTicks(double maxTime)
        {
            MicroRulerCanvas.Children.Clear();
            int maxSeconds = (int)Math.Ceiling(maxTime);

            // 1. 画秒数白线
            for (int s = 0; s <= maxSeconds; s++)
            {
                double x = s * _pixelsPerSecond;
                MicroRulerCanvas.Children.Add(new System.Windows.Shapes.Line { X1 = x, X2 = x, Y1 = 15, Y2 = 30, Stroke = Brushes.Gray, StrokeThickness = 1 });
                var text = new TextBlock { Text = s + "s", FontSize = 10, Foreground = Brushes.Gray };
                Canvas.SetLeft(text, x + 2);
                MicroRulerCanvas.Children.Add(text);
            }

            // 2. 补上五颜六色的音符
            if (_context?.Chart?.note_list != null)
            {
                foreach (var note in _context.Chart.note_list)
                {
                    double x = _context.TimeEngine.TickToSeconds(note.tick) * _pixelsPerSecond;
                    var rect = new System.Windows.Shapes.Rectangle { Width = 3, Height = 10, RadiusX = 1, RadiusY = 1 };

                    if (note.type == 1) rect.Fill = Brushes.LightGreen;
                    else if (note.type == 2) rect.Fill = Brushes.LightSkyBlue;
                    else if (note.type == 3 || note.type == 6) rect.Fill = Brushes.Gold;
                    else if (note.type == 4) rect.Fill = Brushes.Plum;
                    else rect.Fill = Brushes.White;

                    Canvas.SetLeft(rect, x - 1.5);
                    Canvas.SetTop(rect, 20);
                    MicroRulerCanvas.Children.Add(rect);
                }
            }

            // 3. ✨ 高光时刻：在标尺上画一块半透明的蓝色玻璃，明确标出方块的本体位置！
            double startX = _clipModel.StartTime * _pixelsPerSecond;
            double endX = _clipModel.EndTime * _pixelsPerSecond;
            var highlight = new System.Windows.Shapes.Rectangle
            {
                Width = Math.Max(2, endX - startX),
                Height = 30,
                Fill = new SolidColorBrush(Color.FromArgb(40, 77, 184, 255))
            };
            Canvas.SetLeft(highlight, startX);
            MicroRulerCanvas.Children.Add(highlight);
        }
    }
}