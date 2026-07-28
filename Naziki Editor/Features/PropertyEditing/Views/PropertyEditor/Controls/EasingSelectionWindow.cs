using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Naziki_Editor.Views.PropertyEditor
{
    // ==========================================================
    // 🌌 6. EasingSelectionWindow (史诗级视觉曲线矩阵面板 - 纯代码实时算力绘图！)
    // ==========================================================
    public class EasingSelectionWindow : Window
    {
        public Models.EasingFunction.Ease SelectedEase { get; private set; }

        public EasingSelectionWindow(string initialEaseStr)
        {
            Title = "选择缓动魔法 (Easing Selector)";
            Width = 720; Height = 560;
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
            WindowStyle = WindowStyle.ToolWindow;

            if (Enum.TryParse(initialEaseStr, true, out Models.EasingFunction.Ease parsedEase)) SelectedEase = parsedEase;
            else if (int.TryParse(initialEaseStr, out int easeInt)) SelectedEase = (Models.EasingFunction.Ease)easeInt;
            else SelectedEase = Models.EasingFunction.Ease.None;

            var scrollViewer = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var wrapPanel = new System.Windows.Controls.Primitives.UniformGrid { Columns = 5, Margin = new Thickness(10) };
            scrollViewer.Content = wrapPanel;
            Content = scrollViewer;

            // 遍历所有 34 种缓动，动态生成视觉方块！
            foreach (Models.EasingFunction.Ease ease in Enum.GetValues(typeof(Models.EasingFunction.Ease)))
            {
                var card = CreateEaseCard(ease);
                wrapPanel.Children.Add(card);
            }
        }

        private Border CreateEaseCard(Models.EasingFunction.Ease ease)
        {
            bool isSelected = (ease == SelectedEase);

            var border = new Border
            {
                Width = 120,
                Height = 100,
                Margin = new Thickness(8),
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                BorderBrush = isSelected ? Brushes.DeepSkyBlue : Brushes.DimGray,
                BorderThickness = new Thickness(isSelected ? 2 : 1),
                CornerRadius = new CornerRadius(6),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            var grid = new Grid { Margin = new Thickness(5) };
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 🎨 核心：绘制实时函数曲线的画布
            var canvas = new Canvas { ClipToBounds = true, Margin = new Thickness(5, 10, 5, 5) };
            var polyline = new System.Windows.Shapes.Polyline
            {
                Stroke = isSelected ? Brushes.DeepSkyBlue : Brushes.MediumSpringGreen,
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round
            };

            // 利用算法生成 30 个点，连成平滑曲线
            for (int i = 0; i <= 30; i++)
            {
                double t = i / 30.0;
                double v = GetApproximatedEaseValue(ease, t);

                // 坐标映射：画布宽100，高60，留出一点溢出空间展示 Bounce 和 Back
                double x = t * 100;
                double y = 60 - (v * 40 + 10); // 基础区间在中间 40 像素，上下各留 10 像素用于弹跳溢出
                polyline.Points.Add(new Point(x, y));
            }
            canvas.Children.Add(polyline);
            grid.Children.Add(canvas);

            // 文字标签
            var txt = new TextBlock
            {
                Text = $"{(int)ease} - {ease}",
                Foreground = isSelected ? Brushes.White : Brushes.LightGray,
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetRow(txt, 1);
            grid.Children.Add(txt);

            border.Child = grid;

            // 🖱️ 交互动画与点击
            border.MouseEnter += (s, e) => { if (!isSelected) border.BorderBrush = Brushes.LightGray; };
            border.MouseLeave += (s, e) => { if (!isSelected) border.BorderBrush = Brushes.DimGray; };
            border.MouseLeftButtonUp += (s, e) => { SelectedEase = ease; this.DialogResult = true; this.Close(); };

            return border;
        }

        // 🧠 小艾的超算中心：提供用于视觉预览的函数插值算法！(仅供绘图展示，极其轻量)
        private double GetApproximatedEaseValue(Models.EasingFunction.Ease ease, double t)
        {
            switch (ease)
            {
                case Models.EasingFunction.Ease.Linear: case Models.EasingFunction.Ease.None: return t;
                case Models.EasingFunction.Ease.EaseInQuad: return t * t;
                case Models.EasingFunction.Ease.EaseOutQuad: return t * (2 - t);
                case Models.EasingFunction.Ease.EaseInOutQuad: return t < 0.5 ? 2 * t * t : -1 + (4 - 2 * t) * t;
                case Models.EasingFunction.Ease.EaseInCubic: return t * t * t;
                case Models.EasingFunction.Ease.EaseOutCubic: return (--t) * t * t + 1;
                case Models.EasingFunction.Ease.EaseInSine: return 1 - Math.Cos(t * Math.PI / 2);
                case Models.EasingFunction.Ease.EaseOutSine: return Math.Sin(t * Math.PI / 2);
                case Models.EasingFunction.Ease.EaseInOutSine: return -(Math.Cos(Math.PI * t) - 1) / 2;
                case Models.EasingFunction.Ease.EaseInExpo: return t == 0 ? 0 : Math.Pow(2, 10 * (t - 1));
                case Models.EasingFunction.Ease.EaseOutExpo: return t == 1 ? 1 : 1 - Math.Pow(2, -10 * t);
                case Models.EasingFunction.Ease.EaseInBack: { double s = 1.70158; return t * t * ((s + 1) * t - s); }
                case Models.EasingFunction.Ease.EaseOutBack: { double s = 1.70158; t--; return (t * t * ((s + 1) * t + s) + 1); }
                case Models.EasingFunction.Ease.Blink: return t < 0.5 ? 0 : 1;
                // 其他未精细化书写的复杂缓动，默认用一条稍微带点弧度的通用曲线平替，防止卡死
                default:
                    if (ease.ToString().Contains("Out")) return Math.Sin(t * Math.PI / 2);
                    if (ease.ToString().Contains("InOut")) return -(Math.Cos(Math.PI * t) - 1) / 2;
                    return t * t;
            }
        }
    }
}
