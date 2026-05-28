using Naziki_Editor.Models;
using Naziki_Editor.State;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Naziki_Editor.Views.TimelineClip
{
    public partial class ClipPropertyTrackRow : UserControl
    {
        private ProjectDataContext _context;
        private string _propertyName;              // 属性名字，比如 "X" 或 "Opacity"
        private Models.TimelineClipModel _clipModel; // 所属的方块模型
        private double _pixelsPerSecond = 100.0;
        private List<Thumb> _nodes = new List<Thumb>();

        public void Init(string propertyName, Models.TimelineClipModel clipModel, ProjectDataContext context, double pixelsPerSecond)
        {
            _propertyName = propertyName;
            _clipModel = clipModel;
            _context = context;
            _pixelsPerSecond = pixelsPerSecond;

            RenderTrackKeyframes();
        }

        /// <summary>
        /// 🎨 核心重绘：严格执行大大的【补丁1法则】
        /// </summary>
        private void RenderTrackKeyframes()
        {
            KeyframeNodeCanvas.Children.Clear();
            _nodes.Clear();

            double duration = _clipModel.EndTime - _clipModel.StartTime;
            double totalWidth = duration * _pixelsPerSecond;
            this.Width = totalWidth;

            // 1. 🏰 【左侧不可拖拽的永恒神庙】：初始状态 t = 0
            AddKeyframeNode(0, isLocked: true);

            // 🔍 嗅探该属性在未来是否有被创作者临幸调整过
            bool hasFutureTweaks = CheckIfPropertyHasTweaksLater();

            if (hasFutureTweaks)
            {
                // 2. 🧬 读取中间所有曾经被打过点的历史帧
                List<double> middleTimes = GetMiddleKeyframeTimes();
                foreach (double relTime in middleTimes)
                {
                    AddKeyframeNode(relTime * _pixelsPerSecond, isLocked: false);
                }

                // 3. 🏰 【右侧不可拖拽的永恒神庙】：最后状态 t = Duration
                AddKeyframeNode(totalWidth, isLocked: true);
            }

            // 4. 🖌️ 联动呼叫曲线画笔，在 ♦ 之间勾勒缓动连线
            RedrawPropertyCurves();
        }

        /// <summary>
        /// 💎 辅助探头：生成 ♦ 节点
        /// </summary>
        private void AddKeyframeNode(double xPos, bool isLocked)
        {
            Thumb node = new Thumb
            {
                Width = 12,
                Height = 12,
                // 如果锁死，用低调的灰色；如果可动，用高亮主题色
                Style = (Style)Application.Current.FindResource(isLocked ? "LockedKeyframeStyle" : "ActiveKeyframeStyle")
            };

            if (!isLocked)
            {
                node.DragDelta += Node_DragDelta;
            }

            // ✨ 【补丁2落地】：为节点注册双击事件，弹出精调面板
            node.MouseDoubleClick += (s, e) =>
            {
                // TODO: PopupPropertyEditorWindow(_propertyName, node.Tag);
                MessageBox.Show($"🔮 召唤属性编辑器弹窗！\n正在精准调校属性 [{_propertyName}] 这一帧的数值。", "微观属性精调");
                e.Handled = true;
            };

            Canvas.SetLeft(node, xPos - 6);
            Canvas.SetTop(node, 14); // 轨道高度 40，使其在 Y 轴中心对齐

            KeyframeNodeCanvas.Children.Add(node);
            _nodes.Add(node);
        }

        /// <summary>
        /// 🚀 【补丁2核心】：点击轨道空白处，直接继承并创建新关键帧
        /// </summary>
        private void TrackRow_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
            {
                Point clickPoint = e.GetPosition(KeyframeNodeCanvas);
                double clickX = clickPoint.X;

                // 边界叹息之墙保护
                if (clickX <= 0 || clickX >= this.Width) return;

                // 🔍 寻找它在物理时空上的“前序关键帧”
                Thumb preNode = FindPreviousNode(clickX);

                // 🚀 核心：无缝继承前一个关键帧的所有参数！
                object inheritedValue = preNode?.Tag;

                // 降临新节点！
                AddKeyframeNode(clickX, isLocked: false);

                // 更新底层 Cytoid 数据模型切片，并在当前时间点强行打洞注入新状态
                double relTime = clickX / _pixelsPerSecond;
                InjectNewStateIntoCytoidModel(relTime, inheritedValue);

                // 重新洗盘连线
                _nodes.Sort((a, b) => Canvas.GetLeft(a).CompareTo(Canvas.GetLeft(b)));
                RedrawPropertyCurves();
            }
        }

        private void Node_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (sender is Thumb node)
            {
                double currentX = Canvas.GetLeft(node) + 6;
                double newX = currentX + e.HorizontalChange;

                // 🛑 叹息之墙：微观关键帧绝对无法超越方块的首尾端点
                if (newX < 0) newX = 0;
                if (newX > this.Width) newX = this.Width;

                Canvas.SetLeft(node, newX - 6);

                // 同步更新底层 Cytoid 对应的 time 字段
                double newRelTime = newX / _pixelsPerSecond;
                UpdateCytoidStateTime(node, newRelTime);

                RedrawPropertyCurves();
            }
        }

        // =========================================================================
        // 🔮 辅助数学桩（真实开发中对接 Cytoid_StoryboardModel 的 States 列表）
        // =========================================================================
        private bool CheckIfPropertyHasTweaksLater() => _clipModel.AssociatedObject?.GetKeyframes()?.Count > 1;
        private List<double> GetMiddleKeyframeTimes() => new List<double>();
        private Thumb FindPreviousNode(double x) => _nodes[0]; // 默认拿第 0 个初始帧兜底
        private void InjectNewStateIntoCytoidModel(double relTime, object val)
        {
            // ✨ 缝合修复：直接呼叫局部上下文基站！
            _context?.MarkAsModified();
        }
        private void UpdateCytoidStateTime(Thumb node, double newTime) { }
        private void RedrawPropertyCurves() { /* 使用 StreamGeometry 绘制缓动线 */ }
    }
}