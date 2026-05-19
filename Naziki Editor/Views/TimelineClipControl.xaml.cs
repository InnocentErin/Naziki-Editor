using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Naziki_Editor.Models; // 🌟 引入咱们刚才建的 TimelineClipModels (视图枚举所在处)

namespace Naziki_Editor.Views
{
    public partial class TimelineClipControl : UserControl
    {
        // ==========================================
        // 🌟 核心状态与记忆中枢
        // ==========================================
        private ClipViewMode _currentViewMode = ClipViewMode.Keyframe;
        private List<Thumb> _nodeThumbs = new List<Thumb>();
        private Thumb _selectedNode = null; // 当前被选中的那个幸运儿节点

        // 宏观拖拽状态 (移动整个方块)
        private bool _isDraggingClip = false;
        private Point _clipDragStartPoint;

        public TimelineClipControl()
        {
            InitializeComponent();

            // 为了测试，咱们在方块刚出生时，强行往里面塞两个测试节点看看效果！
            this.Loaded += (s, e) =>
            {
                // 假设方块宽 200，我们在 X=50 和 X=150 处各放一个节点
                AddTestNode(50, 0.2);
                AddTestNode(150, 0.8);
                UpdateViewMode(_currentViewMode); // 刷新一下外观
            };
        }

        // ==========================================
        // 🪄 核心法术 1：视图形态切换引擎
        // ==========================================
        public void UpdateViewMode(ClipViewMode newMode)
        {
            _currentViewMode = newMode;
            Style nodeStyle = (Style)Application.Current.FindResource(
                newMode == ClipViewMode.Keyframe ? "KeyframeThumbStyle" : "OpacityThumbStyle");

            foreach (var thumb in _nodeThumbs)
            {
                thumb.Style = nodeStyle;

                if (newMode == ClipViewMode.Keyframe)
                {
                    // 🌟 关键帧模式：强行把节点锁死在 Y 轴正中间！
                    Canvas.SetTop(thumb, this.ActualHeight / 2 - 6);
                }
                else
                {
                    // 🌟 透明度模式：根据节点身上绑定的真实 Value (0~1) 恢复它的高度
                    double value = (double)thumb.Tag;
                    Canvas.SetTop(thumb, (1.0 - value) * this.ActualHeight - 7);
                }
            }

            // 折线图的显示与隐藏
            OpacityCurve.Visibility = newMode == ClipViewMode.Opacity ? Visibility.Visible : Visibility.Collapsed;
            if (newMode == ClipViewMode.Opacity) RedrawOpacityCurve();
        }

        // ==========================================
        // 🧱 核心法术 2：“叹息之墙”绝对边界节点拖拽
        // ==========================================
        private void NodeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (sender is Thumb thumb)
            {
                // 1. 获取现在的坐标
                double currentX = Canvas.GetLeft(thumb);
                double currentY = Canvas.GetTop(thumb);

                // 2. 意图移动的新坐标
                double newX = currentX + e.HorizontalChange;
                double newY = currentY + e.VerticalChange;

                // 3. 🛑 绝对结界判定：绝不允许 X 轴飞出方块左右边缘！
                if (newX < 0) newX = 0;
                if (newX > this.ActualWidth - thumb.ActualWidth) newX = this.ActualWidth - thumb.ActualWidth;

                Canvas.SetLeft(thumb, newX);

                // 4. 如果是透明度模式，允许 Y 轴滑动，但同样不能飞出上下边缘！
                if (_currentViewMode == ClipViewMode.Opacity)
                {
                    if (newY < 0) newY = 0;
                    if (newY > this.ActualHeight - thumb.ActualHeight) newY = this.ActualHeight - thumb.ActualHeight;
                    Canvas.SetTop(thumb, newY);

                    // 实时反算更新节点代表的 0.0 ~ 1.0 的值 (Tag)
                    double value = 1.0 - (newY + thumb.ActualHeight / 2) / this.ActualHeight;
                    thumb.Tag = Math.Max(0, Math.Min(1, value));

                    // 既然动了，就重新连线！
                    RedrawOpacityCurve();
                }

                // TODO: 未来在这里加上：向外界广播“我的 JSON 数据被修改啦！”的事件
            }
        }

        // 画折线法术 (将所有的点按 X 轴排序并连起来)
        private void RedrawOpacityCurve()
        {
            OpacityCurve.Points.Clear();

            // 先按 X 轴坐标从小到大给节点排个序，防止折线乱穿
            var sortedThumbs = new List<Thumb>(_nodeThumbs);
            sortedThumbs.Sort((a, b) => Canvas.GetLeft(a).CompareTo(Canvas.GetLeft(b)));

            foreach (var thumb in sortedThumbs)
            {
                double centerX = Canvas.GetLeft(thumb) + thumb.ActualWidth / 2;
                double centerY = Canvas.GetTop(thumb) + thumb.ActualHeight / 2;
                OpacityCurve.Points.Add(new Point(centerX, centerY));
            }
        }

        // ==========================================
        // 🎯 核心法术 3：精密的焦点降级与点选机制
        // ==========================================

        // 当鼠标在方块的背景装甲上按下时...
        private void ClipBackground_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // 🌟 焦点降级法则：点击空白处，解除所有内部节点的选中状态！
            DeselectAllNodes();

            // 记录当前位置，准备开启宏观拖拽 (平移整个方块)
            _isDraggingClip = true;
            _clipDragStartPoint = e.GetPosition(this.Parent as UIElement); // 参考系为父级轨道
            RootGrid.CaptureMouse();
            e.Handled = true;

            // 让方块外壳高亮 (变色证明被选中)
            ClipBackground.BorderBrush = Brushes.White;
            ClipBackground.Background = new SolidColorBrush(Color.FromArgb(100, 77, 184, 255));
        }

        private void ClipBackground_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingClip)
            {
                Point currentPos = e.GetPosition(this.Parent as UIElement);
                double deltaX = currentPos.X - _clipDragStartPoint.X;

                // TODO: 未来在这里呼叫父级 (TimelineControl)，让它把这个方块整体往左右平移 deltaX 的距离！
                // 同时修改底层对象的初始 time！
            }
        }

        private void ClipBackground_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingClip)
            {
                _isDraggingClip = false;
                RootGrid.ReleaseMouseCapture();
            }
        }

        // 当点击某个具体的内部节点时...
        private void NodeThumb_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Thumb thumb)
            {
                DeselectAllNodes();
                _selectedNode = thumb;

                // 节点变白发光，表示被选中！
                thumb.Opacity = 1.0;
                thumb.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.White, BlurRadius = 5, ShadowDepth = 0 };
            }
        }

        private void DeselectAllNodes()
        {
            foreach (var thumb in _nodeThumbs)
            {
                thumb.Opacity = 0.8;
                thumb.Effect = null;
            }
            _selectedNode = null;
        }

        // ==========================================
        // 🧪 测试法术：为了让指挥官能马上看到效果
        // ==========================================
        private void AddTestNode(double x, double opacityValue)
        {
            Thumb thumb = new Thumb();
            thumb.Tag = opacityValue; // 把真实数据藏在影子里
            thumb.Opacity = 0.8;

            // 挂载交互事件
            thumb.DragDelta += NodeThumb_DragDelta;
            thumb.PreviewMouseDown += NodeThumb_PreviewMouseDown;

            NodeCanvas.Children.Add(thumb);
            Canvas.SetLeft(thumb, x);
            _nodeThumbs.Add(thumb);
        }
    }
}