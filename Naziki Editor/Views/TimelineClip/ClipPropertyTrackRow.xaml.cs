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


        // 📋 全局时空剪贴板 (存放复制的关键帧状态)
        private static Models.ObjectState _clipboardState = null;
        private static string _clipboardSourcePropertyName = ""; // 记录复制来源的属性名


        public ClipPropertyTrackRow()
        {
            InitializeComponent();
            // ✨ 1. 防穿透涂层：必须给画板一个颜色（哪怕是透明的），它才能拥有实体，拦住鼠标点击！
            KeyframeNodeCanvas.Background = Brushes.Transparent;

            // ✨ 2. 接通神经：解除注释封印，把右键点击事件正式绑定到画板上！
            KeyframeNodeCanvas.MouseRightButtonDown += TrackRow_MouseRightButtonDown;
        }




        public void Init(string propertyName, Models.TimelineClipModel clipModel, ProjectDataContext context, double pixelsPerSecond)
        {
            _propertyName = propertyName;
            _clipModel = clipModel;
            _context = context;
            _pixelsPerSecond = pixelsPerSecond;

            RenderTrackKeyframes();
        }



        // ==========================================
        // 🎨 核心重绘引擎：初始属性与关键帧完美独立！
        // ==========================================
        public void RenderTrackKeyframes()
        {
            KeyframeNodeCanvas.Children.Clear();
            _nodes.Clear();

            if (_clipModel?.AssociatedObject == null) return;

            // 1. 🛡️ 询问大管家：这个属性是不是 Slider（有极值限制的数值型）？
            var rule = Core.PropertyConstraintManager.GetConstraint(_propertyName);
            bool isSlider = rule != null && rule.UIType == Core.PropertyUIType.Slider;

            // ==========================================
            // ✨ 核心修正：无条件独立渲染最左侧的【初始属性钮扣】
            // ==========================================
            var baseState = _clipModel.AssociatedObject.GetBaseState();
            if (baseState != null && Core.FastReflectionHelper.TryGetValue(baseState, _propertyName, out object baseVal) && baseVal != null)
            {
                // ⏱️ 时空锚定：初始属性的 X 轴永远钉死在方块的 StartTime 起点！
                double initialAbsX = _clipModel.StartTime * _pixelsPerSecond;

                // 📐 Y 轴映射
                double initialY = 14;
                if (isSlider)
                {
                    double numVal = Convert.ToDouble(baseVal);
                    if (numVal < rule.Min) numVal = rule.Min;
                    if (numVal > rule.Max) numVal = rule.Max;
                    double ratio = (numVal - rule.Min) / (rule.Max - rule.Min);
                    initialY = 28 * (1.0 - ratio);
                }

                // 🌟 生成初始专属钮扣 (给它打上专属的标签)
                Thumb initialNode = new Thumb
                {
                    Tag = "BASE_STATE_NODE", // 【核心暗号】代表它是神圣初始状态，非普通关键帧！
                    Style = Application.Current.TryFindResource(isSlider ? "OpacityThumbStyle" : "KeyframeThumbStyle") as Style
                };

                initialNode.DragDelta += Node_DragDelta;
                initialNode.MouseRightButtonDown += Node_MouseRightButtonDown;
                // ✨ 当拖拽松开时，强制进行一次身份刷新与主权隔离！
                initialNode.DragCompleted += (s, ev) => { RenderTrackKeyframes(); };

                KeyframeNodeCanvas.Children.Add(initialNode);
                Canvas.SetLeft(initialNode, initialAbsX);
                Canvas.SetTop(initialNode, initialY);
                _nodes.Add(initialNode);
            }

            // ==========================================
            // 🧬 ⚡ 呼叫独立时间转换引擎：一次性安全压平获取所有可见帧
            // ==========================================
            var decodedFrames = Core.Timeline.StoryboardTimeConverter.DecodeTimelineKeyframes(
    _clipModel.AssociatedObject, 
    _propertyName, 
    _context.TimeEngine,           // 喂入大大的 ChartTimeEngine！
    _context.Chart?.note_list,     // 喂入 C2Chart 的强类型音符列表！
    _clipModel.StartTime           // 喂入方块自己的宏观起点
);

            foreach (var box in decodedFrames)
            {
                // 如果有关键帧不小心堆在了出生点，为了视觉不冲突，跳过渲染
                if (box.VisualRelTime <= 0.001) continue;

                double absoluteTime = _clipModel.StartTime + box.VisualRelTime;
                double xPos = absoluteTime * _pixelsPerSecond;

                double yPos = 14;
                if (isSlider)
                {
                    double numVal = Convert.ToDouble(box.Value);
                    if (numVal < rule.Min) numVal = rule.Min;
                    if (numVal > rule.Max) numVal = rule.Max;
                    double ratio = (numVal - rule.Min) / (rule.Max - rule.Min);
                    yPos = 28 * (1.0 - ratio);
                }

                Thumb node = new Thumb
                {
                    Tag = box.State, // 真实关键帧的 Tag 依然装着它自己的状态对象
                    Style = Application.Current.TryFindResource(isSlider ? "OpacityThumbStyle" : "KeyframeThumbStyle") as Style
                };

                node.DragDelta += Node_DragDelta;
                node.MouseRightButtonDown += Node_MouseRightButtonDown;
                // 拖拽结束后，让小菱形在轨道上根据最新时间重新洗牌、对齐站好！
                node.DragCompleted += (s, ev) => { RenderTrackKeyframes(); };

                KeyframeNodeCanvas.Children.Add(node);
                Canvas.SetLeft(node, xPos);
                Canvas.SetTop(node, yPos);
                _nodes.Add(node);
            }

            // 6. 🧶 画出命运的连线！
            RedrawPropertyCurves();
        }

















        /// <summary>
        /// 🚀 【补丁2核心】：点击轨道空白处，直接继承并创建新关键帧
        /// </summary>
        private void TrackRow_MouseDown(object sender, MouseButtonEventArgs e)
        {
            
        }

        private void Node_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (sender is Thumb node)
            {
                int index = _nodes.IndexOf(node);
                if (index < 0) return;

                // 🛡️ 判定：它是不是我们独立出来的初始属性钮扣？
                bool isBaseStateNode = (node.Tag is string str && str == "BASE_STATE_NODE");

                // ==========================================
                // 1. ⏱️ X 轴水平绝对移动控制（全面满足大大的多态时间滑动需求！）
                // ==========================================
                if (isBaseStateNode)
                {
                    // 初始核心纽扣：绝对领域防线，强行钉死在方块微观起点，完全封印左右位移
                    double initialAbsX = _clipModel.StartTime * _pixelsPerSecond;
                    Canvas.SetLeft(node, initialAbsX);
                }
                else if (node.Tag is Models.ObjectState state)
                {
                    // 普通关键帧：执行严格的“绝对不超车防线（叹息之墙 2.0）”
                    double currentX = Canvas.GetLeft(node);
                    double newX = currentX + e.HorizontalChange;

                    double minX = _clipModel.StartTime * _pixelsPerSecond;
                    double maxX = double.MaxValue;

                    // 碰撞界限：严禁超越左边的物理节点或右边的物理节点
                    if (index > 0) minX = Canvas.GetLeft(_nodes[index - 1]) + 1;
                    if (index < _nodes.Count - 1) maxX = Canvas.GetLeft(_nodes[index + 1]) - 1;

                    if (newX < minX) newX = minX;
                    if (newX > maxX) newX = maxX;

                    Canvas.SetLeft(node, newX);

                    // 🧙‍♂️ 计算调整后的绝对秒数与视觉相对时间
                    double newAbsTime = newX / _pixelsPerSecond;
                    double newVisualRelTime = newAbsTime - _clipModel.StartTime;

                    // 🚀 呼叫满配核心反写引擎：内部自动识别【绝对秒数/音符锚点延迟/相对级联】，精准重写！
                    Core.Timeline.StoryboardTimeConverter.WriteBackVisualTime(
                        _clipModel.AssociatedObject,
                        state,
                        newVisualRelTime,
                        _context.TimeEngine,        // 喂入音符换算引擎
                        _context.Chart?.note_list,  // 喂入全量音符列表
                        _clipModel.StartTime        // 喂入方块起点秒数
                    );
                }

                // ==========================================
                // 2. 🚦 Y 轴纵向拉扯（Slider 属性两路分流反写）
                // ==========================================
                var rule = Core.PropertyConstraintManager.GetConstraint(_propertyName);
                if (rule != null && rule.UIType == Core.PropertyUIType.Slider)
                {
                    double currentY = Canvas.GetTop(node);
                    double newY = currentY + e.VerticalChange;

                    if (newY < 0) newY = 0;
                    if (newY > this.Height - 12) newY = this.Height - 12;
                    Canvas.SetTop(node, newY);

                    double ratio = 1.0 - (newY / (this.Height - 12));
                    float newValue = (float)(rule.Min + (rule.Max - rule.Min) * ratio);

                    if (isBaseStateNode)
                    {
                        var baseState = _clipModel.AssociatedObject.GetBaseState();
                        baseState?.GetType().GetProperty(_propertyName)?.SetValue(baseState, newValue);
                    }
                    else if (node.Tag is Models.ObjectState state)
                    {
                        state.GetType().GetProperty(_propertyName)?.SetValue(state, newValue);
                    }
                }

                _context?.MarkAsModified();

                // ✨ 拖拽中途只刷新贝塞尔折线，绝不过河拆桥销毁控件，保证丝滑抓取手感
                RedrawPropertyCurves();
            }
        }



        // 给小菱形绑定右键事件
        // node.MouseRightButtonDown += Node_MouseRightButtonDown;

        // 右键菜单：编辑属性 / 复制属性 / 粘贴属性（带冲突检测）
        private void Node_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (sender is Thumb node)
            {
                var menu = new ContextMenu();


                // ✨ 【就是这里！】补上这行暗号解码线，红线瞬间消失！
                bool isBaseStateNode = (node.Tag is string str && str == "BASE_STATE_NODE");

                // 1. 打开属性编辑器
                var editItem = new MenuItem { Header = "⚙️ 打开属性编辑器" };
                editItem.Click += (s, ev) => {
                    if (Window.GetWindow(this) is MainWindow main && _clipModel?.AssociatedObject != null)
                    {
                        main.OpenPropertyEditor(_clipModel.AssociatedObject);
                    }
                };
                menu.Items.Add(editItem);

                // ✨ 核心保护：初始点（BASE_STATE_NODE）不允许复制和粘贴关键帧属性
                if (!isBaseStateNode)
                {
                    // 2. 复制属性
                    var copyItem = new MenuItem { Header = "📋 复制关键帧属性" };
                    copyItem.Click += (s, ev) => {
                        if (node.Tag is Models.ObjectState state)
                        {
                            string json = Newtonsoft.Json.JsonConvert.SerializeObject(state);
                            _clipboardState = Newtonsoft.Json.JsonConvert.DeserializeObject(json, state.GetType()) as Models.ObjectState;
                            _clipboardSourcePropertyName = _propertyName;
                            MessageBox.Show("卡哇伊！属性信息复制成功啦！", "复制成功");
                        }
                    };
                    menu.Items.Add(copyItem);

                    // 3. 粘贴属性（冲突检测）
                    var pasteItem = new MenuItem { Header = "📥 粘贴属性", IsEnabled = _clipboardState != null };
                    pasteItem.Click += (s, ev) => {
                        if (_clipboardState != null && node.Tag is Models.ObjectState targetState)
                        {
                            CheckAndPasteProperties(targetState);
                        }
                    };
                    menu.Items.Add(pasteItem);
                }

                node.ContextMenu = menu;
            }
        }

        // 给小菱形绑定右键事件
        // node.MouseRightButtonDown += Node_MouseRightButtonDown;






        // ==========================================
        // ⚔️ 核心冲突检测与粘贴法术
        // ==========================================
        private void CheckAndPasteProperties(Models.ObjectState targetState)
        {
            // 尝试读取剪贴板里这个属性的值
            if (Core.FastReflectionHelper.TryGetValue(_clipboardState, _propertyName, out object copiedVal) && copiedVal != null)
            {
                // 检查目标帧是不是已经有这个属性了
                if (Core.FastReflectionHelper.TryGetValue(targetState, _propertyName, out object existingVal) && existingVal != null)
                {
                    var result = MessageBox.Show(
                        $"纳尼？当前关键帧的 [{_propertyName}] 属性已经有值 ({existingVal}) 啦！\n是否要用复制的值 ({copiedVal}) 替换它？",
                        "时空冲突确认", MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (result == MessageBoxResult.No) return;
                }

                // 强行写入新值！
                var propInfo = targetState.GetType().GetProperty(_propertyName);
                if (propInfo != null && propInfo.CanWrite)
                {
                    propInfo.SetValue(targetState, copiedVal);
                    // 呼叫大本营的 MarkAsModified 并重绘当前行
                    _context?.MarkAsModified();
                    RenderTrackKeyframes();
                }
            }
        }




        // 绑定在 Canvas 上的右键事件
        // KeyframeNodeCanvas.MouseRightButtonDown += TrackRow_MouseRightButtonDown;

        private void TrackRow_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var menu = new ContextMenu();

            // 核心修复：允许在空白处新建，并自动继承事件的初始属性
            var newItem = new MenuItem { Header = "➕ 在此处新建关键帧" };
            newItem.Click += (s, ev) => {
                double clickX = e.GetPosition(KeyframeNodeCanvas).X;
                double newAbsTime = clickX / _pixelsPerSecond;
                double newRelTime = newAbsTime - _clipModel.StartTime;
                if (newRelTime < 0) newRelTime = 0;

                Type stateType = _clipModel.AssociatedObject.GetBaseState().GetType();
                var newFrame = Activator.CreateInstance(stateType) as Models.ObjectState;
                newFrame.RelativeTime = (float)newRelTime;

                // 继承事件的初始属性 (BaseState)
                if (Core.FastReflectionHelper.TryGetValue(_clipModel.AssociatedObject.GetBaseState(), _propertyName, out object baseVal) && baseVal != null)
                {
                    stateType.GetProperty(_propertyName)?.SetValue(newFrame, baseVal);
                }

                _clipModel.AssociatedObject.GetKeyframes().Add(newFrame);
                _context?.MarkAsModified();
                RenderTrackKeyframes();
            };
            menu.Items.Add(newItem);




            // 2. 智能分支：只有剪贴板里有东西时，才追加“新建并粘贴”按钮，绝对不让 return 熔断整个菜单！
            if (_clipboardState != null)
            {
                var pasteNewItem = new MenuItem { Header = "📥 在此处新建关键帧并粘贴" };
                pasteNewItem.Click += (s, ev) => {
                    double clickX = e.GetPosition(KeyframeNodeCanvas).X;
                    double newAbsTime = clickX / _pixelsPerSecond;
                    double newRelTime = newAbsTime - _clipModel.StartTime;
                    if (newRelTime < 0) return;

                    Type stateType = _clipModel.AssociatedObject.GetBaseState().GetType();
                    var newFrame = Activator.CreateInstance(stateType) as Models.ObjectState;
                    newFrame.RelativeTime = (float)newRelTime;

                    if (Core.FastReflectionHelper.TryGetValue(_clipboardState, _propertyName, out object copiedVal) && copiedVal != null)
                    {
                        stateType.GetProperty(_propertyName)?.SetValue(newFrame, copiedVal);
                    }

                    _clipModel.AssociatedObject.GetKeyframes().Add(newFrame);
                    _context?.MarkAsModified();
                    RenderTrackKeyframes();
                };
                menu.Items.Add(pasteNewItem);
            }

            // 3. 强行在当前行控件的中心召唤结界
            KeyframeNodeCanvas.ContextMenu = menu;
            menu.IsOpen = true;

            // 阻断冒泡，让这行轨道独自享有这个右键特权！
            e.Handled = true;

        }













        // =========================================================================
        // 🔮 辅助数学桩（真实开发中对接 Cytoid_StoryboardModel 的 States 列表）
        // =========================================================================
        
        private void RedrawPropertyCurves()
        {
            CurveRenderCanvas.Children.Clear();
            if (_nodes.Count < 2) return;

            // 按时间 (X坐标) 从左到右严格排序
            var sortedNodes = System.Linq.Enumerable.OrderBy(_nodes, n => Canvas.GetLeft(n)).ToList();

            // 暂且使用高亮直线把它们串联起来
            System.Windows.Shapes.Polyline curve = new System.Windows.Shapes.Polyline
            {
                Stroke = (Brush)Application.Current.FindResource("HighlightBorderColor") ?? Brushes.DodgerBlue,
                StrokeThickness = 2,
                IsHitTestVisible = false
            };

            foreach (var node in sortedNodes)
            {
                // 连接节点的正中心 (加上半径偏移)
                double x = Canvas.GetLeft(node) + 6;
                double y = Canvas.GetTop(node) + 6;
                curve.Points.Add(new Point(x, y));
            }

            CurveRenderCanvas.Children.Add(curve);
        }





        // 🚀 响应滚轮缩放，光速重新摆放小菱形的位置！
        // 🚀 响应滚轮缩放，调用引擎彻底重绘，安全又省心！
        public void UpdateZoom(double newPixelsPerSecond)
        {
            _pixelsPerSecond = newPixelsPerSecond;
            RenderTrackKeyframes();
        }
    }
}