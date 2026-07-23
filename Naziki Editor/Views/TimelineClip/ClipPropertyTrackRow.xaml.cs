using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Common;
using Naziki_Editor.Core.Messaging;
using Naziki_Editor.Core.Timeline;
using Naziki_Editor.Models;
using Naziki_Editor.UI.ViewModels;
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
        private TimelineClipModel _clipModel; // 所属的方块模型
        private double _pixelsPerSecond = 100.0;
        private ITimelineInteractionService _timelineService;
        private IPropertyEditorService _propertyEditorService;
        private List<Thumb> _nodes = new List<Thumb>();
        private readonly IMessageBroker _messageBroker;
        private readonly IDialogService _dialogService;

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

        public ClipPropertyTrackRow(IMessageBroker messageBroker, IDialogService dialogService) : this()
        {
            _messageBroker = messageBroker;
            _dialogService = dialogService;
        }




        public void Init(string propertyName, TimelineClipModel clipModel, ProjectDataContext context, double pixelsPerSecond)
        {
            _propertyName = propertyName;
            _clipModel = clipModel;
            _context = context;
            _pixelsPerSecond = pixelsPerSecond;
            _timelineService = new TimelineInteractionService(context, new TimelineCoordEngine(pixelsPerSecond));
            _propertyEditorService = new PropertyEditorService();

            RenderTrackKeyframes();
        }




        // 🌟 大一统法则：真理之眼！无视宏观假象，直接提取 BaseState 里真实的绝对秒数！
        private double GetTrueBaseTimeSec()
        {
            double fallback = _clipModel.StartTime;
            if (_clipModel?.AssociatedObject == null) return fallback;

            var baseState = _clipModel.AssociatedObject.GetBaseState();
            if (baseState != null)
            {
                var timeProp = baseState.GetType().GetProperty("Time");
                if (timeProp != null)
                {
                    object timeVal = timeProp.GetValue(baseState);
                    if (timeVal != null)
                    {
                        // 呼叫大大的统一时间引擎，无论是 "$note" 还是 "10"，瞬间翻译为绝对秒数！
                        return _context.TimeEngine.ParseCytoidTimeExpression(timeVal.ToString(), _context.Chart?.note_list);
                    }
                }
            }
            return fallback;
        }

        // ==========================================
        // 🎨 核心重绘引擎：V8 极速防爆版！
        // ==========================================
        public void RenderTrackKeyframes()
        {
            KeyframeNodeCanvas.Children.Clear();
            _nodes.Clear();

            if (_clipModel?.AssociatedObject == null) return;

            var rule = _propertyEditorService.GetConstraint(_propertyName);
            bool isSlider = rule != null && rule.UIType == Core.PropertyUIType.Slider;

            // 🚀 【性能优化 1】：将资源查找移出循环！WPF 查找资源的耗时在循环内是毁灭性的！
            Style nodeStyle = Application.Current.TryFindResource(isSlider ? "OpacityThumbStyle" : "KeyframeThumbStyle") as Style;

            // ==========================================
            // 渲染 BaseState (初始钮扣)
            // ==========================================
            double trueBaseTimeSec = GetTrueBaseTimeSec(); // 🌟 提取真实起跑线！
            var baseState = _clipModel.AssociatedObject.GetBaseState();
            if (baseState != null && _propertyEditorService.TryGetValue(baseState, _propertyName, out object baseVal) && baseVal != null)
            {
                double initialAbsX = trueBaseTimeSec * _pixelsPerSecond;
                double initialY = 14;
                if (isSlider)
                {
                    double numVal = Convert.ToDouble(baseVal);
                    if (numVal < rule.Min) numVal = rule.Min;
                    if (numVal > rule.Max) numVal = rule.Max;
                    double ratio = (numVal - rule.Min) / (rule.Max - rule.Min);
                    initialY = 28 * (1.0 - ratio);
                }

                Thumb initialNode = new Thumb
                {
                    Tag = "BASE_STATE_NODE",
                    Style = nodeStyle, // ✨ 使用缓存的样式！
                    Background = Brushes.Gold,
                    BorderBrush = Brushes.White,
                    ToolTip = $"🌟 初始状态锚点 (Base State)\n时间: {trueBaseTimeSec:F3}s\n(拖动我将直接改变整个事件方块的起点哦！)",
                    Margin = new Thickness(-6, 0, 0, 0)
                };

                initialNode.DragDelta += Node_DragDelta;
                initialNode.MouseRightButtonDown += Node_MouseRightButtonDown;
                initialNode.DragCompleted += (s, ev) => { RenderTrackKeyframes(); };

                // 🚀 【性能优化 2】：必须先设定依赖属性(SetLeft/SetTop)，最后再 Add！
                // 这样元素在内存中组装好，进入屏幕时只会引发 1 次轻量级渲染！绝不卡顿！
                Canvas.SetLeft(initialNode, initialAbsX);
                Canvas.SetTop(initialNode, initialY);
                KeyframeNodeCanvas.Children.Add(initialNode);
                _nodes.Add(initialNode);
            }

            // ==========================================
            // 渲染其它解码后的普通关键帧
            // ==========================================
            var decodedFrames = _timelineService.DecodeKeyframes(
                _clipModel.AssociatedObject,
                _propertyName,
                trueBaseTimeSec
            );

            foreach (var box in decodedFrames)
            {
                if (box.VisualRelTime <= 0.001) continue;

                double absoluteTime = trueBaseTimeSec + box.VisualRelTime;
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
                    Tag = box.State,
                    Style = nodeStyle, // ✨ 使用缓存的样式！
                    Margin = new Thickness(-6, 0, 0, 0)
                };

                node.DragDelta += Node_DragDelta;
                node.MouseRightButtonDown += Node_MouseRightButtonDown;
                node.DragCompleted += (s, ev) => { RenderTrackKeyframes(); };

                // 🚀 【性能优化 2】：先设定位置，再塞进画板！
                Canvas.SetLeft(node, xPos);
                Canvas.SetTop(node, yPos);
                KeyframeNodeCanvas.Children.Add(node);
                _nodes.Add(node);
            }

            // 画线逻辑不变
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
                // 1. ⏱️ X 轴水平绝对移动控制（大一统法则完美升级版！）
                // ==========================================
                double trueBaseTimeSec = GetTrueBaseTimeSec(); // 🌟 提取真理

                if (isBaseStateNode)
                {
                    double currentX = Canvas.GetLeft(node);
                    double newX = currentX + e.HorizontalChange;

                    double minX = 0;
                    double maxX = double.MaxValue;
                    if (_nodes.Count > 1 && _nodes[1] != node) maxX = Canvas.GetLeft(_nodes[1]) - 1;

                    if (newX < minX) newX = minX;
                    if (newX > maxX) newX = maxX;

                    Canvas.SetLeft(node, newX);

                    double deltaSec = (newX - currentX) / _pixelsPerSecond;

                    // 🌟 身份鉴定：普通对象拖动初始点会改变寿命边界，但永生控制器绝对不改边界！
                    bool isController = _clipModel.AssociatedObject.GetType().Name.Contains("Controller");
                    if (!isController)
                    {
                        _clipModel.StartTime += deltaSec;
                    }

                    var baseState = _clipModel.AssociatedObject.GetBaseState();
                    if (baseState != null &&
                        _propertyEditorService.TryGetValue(baseState, "Time", out object oldTime))
                    {
                        object newTimeStr = _timelineService.UpdateTimeExpressionByDelta(oldTime, deltaSec);
                        _propertyEditorService.TrySetValue(baseState, "Time", newTimeStr);
                    }
                }
                else if (node.Tag is Models.ObjectState state)
                {
                    double currentX = Canvas.GetLeft(node);
                    double newX = currentX + e.HorizontalChange;

                    double minX = 0;
                    double maxX = double.MaxValue;

                    if (index > 0) minX = Canvas.GetLeft(_nodes[index - 1]) + 1;
                    if (index < _nodes.Count - 1) maxX = Canvas.GetLeft(_nodes[index + 1]) - 1;

                    if (newX < minX) newX = minX;
                    if (newX > maxX) newX = maxX;

                    Canvas.SetLeft(node, newX);

                    double newAbsTime = newX / _pixelsPerSecond;
                    // 🌟 计算视差相对时间，必须减去真实的起跑线！
                    double newVisualRelTime = newAbsTime - trueBaseTimeSec;

                    _timelineService.WriteBackVisualTime(
                        _clipModel.AssociatedObject,
                        state,
                        newVisualRelTime,
                        trueBaseTimeSec             // 🌟 喂给底层的基准点！
                    );
                }

                // ==========================================
                // 2. 🚦 Y 轴纵向拉扯（Slider 属性两路分流反写）
                // ==========================================
                var rule = _propertyEditorService.GetConstraint(_propertyName);
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
                        if (baseState != null) _propertyEditorService.TrySetValue(baseState, _propertyName, newValue);
                    }
                    else if (node.Tag is Models.ObjectState state)
                    {
                        _propertyEditorService.TrySetValue(state, _propertyName, newValue);
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
                editItem.Click += (s, ev) =>
                {
                    if (_clipModel?.AssociatedObject != null)
                    {
                        _messageBroker.Publish("RequestOpenPropertyEditor", (object)_clipModel.AssociatedObject);
                    }
                };
                menu.Items.Add(editItem);

                // ✨ 核心保护：初始点（BASE_STATE_NODE）不允许复制和粘贴关键帧属性
                if (!isBaseStateNode)
                {
                    // 2. 复制属性
                    var copyItem = new MenuItem { Header = "📋 复制关键帧属性" };
                    copyItem.Click += (s, ev) =>
                    {
                        if (node.Tag is Models.ObjectState state)
                        {
                            string json = Newtonsoft.Json.JsonConvert.SerializeObject(state);
                            _clipboardState = Newtonsoft.Json.JsonConvert.DeserializeObject(json, state.GetType()) as Models.ObjectState;
                            _clipboardSourcePropertyName = _propertyName;
                            _dialogService.ShowMessage("卡哇伊！属性信息复制成功啦！", "复制成功");
                        }
                    };
                    menu.Items.Add(copyItem);

                    // 3. 粘贴属性（冲突检测）
                    var pasteItem = new MenuItem { Header = "📥 粘贴属性", IsEnabled = _clipboardState != null };
                    pasteItem.Click += (s, ev) =>
                    {
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
            if (_propertyEditorService.TryGetValue(_clipboardState, _propertyName, out object copiedVal) && copiedVal != null)
            {
                // 检查目标帧是不是已经有这个属性了
                if (_propertyEditorService.TryGetValue(targetState, _propertyName, out object existingVal) && existingVal != null)
                {
                    var result = _dialogService.ShowYesNo(
                        $"纳尼？当前关键帧的 [{_propertyName}] 属性已经有值 ({existingVal}) 啦！\n是否要用复制的值 ({copiedVal}) 替换它？",
                        "时空冲突确认");

                    if (!result) return;
                }

                // 强行写入新值！
                if (_propertyEditorService.TrySetValue(targetState, _propertyName, copiedVal))
                {
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
            newItem.Click += (s, ev) =>
            {
                double clickX = e.GetPosition(KeyframeNodeCanvas).X;
                double newAbsTime = clickX / _pixelsPerSecond;
                double newRelTime = newAbsTime - _clipModel.StartTime;
                if (newRelTime < 0) newRelTime = 0;

                Type stateType = _clipModel.AssociatedObject.GetBaseState().GetType();
                var newFrame = Activator.CreateInstance(stateType) as Models.ObjectState;
                newFrame.RelativeTime = (float)newRelTime;

                // 继承事件的初始属性 (BaseState)
                if (_propertyEditorService.TryGetValue(_clipModel.AssociatedObject.GetBaseState(), _propertyName, out object baseVal) && baseVal != null)
                {
                    _propertyEditorService.TrySetValue(newFrame, _propertyName, baseVal);
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
                pasteNewItem.Click += (s, ev) =>
                {
                    double clickX = e.GetPosition(KeyframeNodeCanvas).X;
                    double newAbsTime = clickX / _pixelsPerSecond;
                    double newRelTime = newAbsTime - _clipModel.StartTime;
                    if (newRelTime < 0) return;

                    Type stateType = _clipModel.AssociatedObject.GetBaseState().GetType();
                    var newFrame = Activator.CreateInstance(stateType) as Models.ObjectState;
                    newFrame.RelativeTime = (float)newRelTime;

                    if (_propertyEditorService.TryGetValue(_clipboardState, _propertyName, out object copiedVal) && copiedVal != null)
                    {
                        _propertyEditorService.TrySetValue(newFrame, _propertyName, copiedVal);
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

                double x = Canvas.GetLeft(node);
                double y = Canvas.GetTop(node) + 6; // Y 轴我们没动 Margin，所以依然加 6 寻找中心。
                curve.Points.Add(new Point(x, y));
            }

            CurveRenderCanvas.Children.Add(curve);
        }





        // 🚀 响应滚轮缩放，调用引擎彻底重绘，安全又省心！
        // 🚀 宏观级极速缩放引擎：绝对不摧毁重建，仅做数学坐标变换！
        public void FastUpdateZoom(double newPixelsPerSecond)
        {
            if (Math.Abs(_pixelsPerSecond - newPixelsPerSecond) < 0.001) return;

            // 1. 算出新旧宇宙的膨胀/收缩比例
            double scale = newPixelsPerSecond / _pixelsPerSecond;

            // 2. 极速位移所有小菱形（只需修改 Canvas.Left，0 毫秒开销！）
            foreach (var node in _nodes)
            {
                double oldX = Canvas.GetLeft(node);
                Canvas.SetLeft(node, oldX * scale);
            }

            // 3. 存下新倍率，并极速重绘折线！
            _pixelsPerSecond = newPixelsPerSecond;
            RedrawPropertyCurves();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {

        }
    }
}