using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Common;
using Naziki_Editor.Core.Messaging;
using Naziki_Editor.Core.Timeline;
using Naziki_Editor.Core.Timeline.Abstractions;
using Naziki_Editor.Core.Timeline.Models;
using Naziki_Editor.Core.Timeline.Shared;
using Naziki_Editor.Models;
using Naziki_Editor.UI.ViewModels;
using Naziki_Editor.State;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Naziki_Editor.Views.MicroTimeline
{
    public partial class PropertyTrackControl : UserControl
    {
        private ProjectDataContext _context;
        private string _propertyName;              // 属性名字，比如 "X" 或 "Opacity"
        private MicroEditorContext _editorContext;
        private double _currentStartTime;           // 可变的起始时间（MicroEditorContext.MacroStartTime 是 init-only）
        private double _pixelsPerSecond = 100.0;
        private IMicroTimelineService _microService;
        private IPropertyEditorService _propertyEditorService;
        private List<Thumb> _nodes = new List<Thumb>();
        private readonly Dictionary<Thumb, object> _nodeValues = new();
        private readonly IMessageBroker _messageBroker;
        private readonly IDialogService _dialogService;
        private IReadOnlyList<DecodedKeyframeBox>? _projectedFrames;

        private KeyframeNodeRenderer _keyframeRenderer;

        // 📋 全局时空剪贴板 (存放复制的关键帧状态)
        private static Models.ObjectState _clipboardState = null;
        private static string _clipboardSourcePropertyName = ""; // 记录复制来源的属性名


        public PropertyTrackControl()
        {
            InitializeComponent();
            // ✨ 1. 防穿透涂层：必须给画板一个颜色（哪怕是透明的），它才能拥有实体，拦住鼠标点击！
            KeyframeNodeCanvas.Background = Brushes.Transparent;

            // ✨ 2. 接通神经：解除注释封印，把右键点击事件正式绑定到画板上！
            KeyframeNodeCanvas.MouseRightButtonDown += TrackRow_MouseRightButtonDown;

            _keyframeRenderer = new KeyframeNodeRenderer(KeyframeNodeCanvas);
        }

        public PropertyTrackControl(IMicroTimelineService microService, IMessageBroker messageBroker, IDialogService dialogService, IPropertyEditorService propertyEditorService) : this()
        {
            _microService = microService;
            _messageBroker = messageBroker;
            _dialogService = dialogService;
            _propertyEditorService = propertyEditorService;
        }




        public void Init(string propertyName, MicroEditorContext editorContext, ProjectDataContext context, double pixelsPerSecond)
            => Init(propertyName, editorContext, context, pixelsPerSecond, null);

        public void Init(
            string propertyName,
            MicroEditorContext editorContext,
            ProjectDataContext context,
            double pixelsPerSecond,
            IReadOnlyList<DecodedKeyframeBox>? projectedFrames)
        {
            _propertyName = propertyName;
            _editorContext = editorContext;
            _currentStartTime = editorContext.MacroStartTime;
            _context = context;
            _pixelsPerSecond = pixelsPerSecond;
            _projectedFrames = projectedFrames;
            // _microService is injected via DI - update its zoom level and context
            _microService.SetContext(context);
            _microService.UpdatePixelsPerSecond(pixelsPerSecond);

            RenderTrackKeyframes();
        }




        // 🌟 大一统法则：真理之眼！无视宏观假象，直接提取 BaseState 里真实的绝对秒数！
        private double GetTrueBaseTimeSec()
        {
            double fallback = _currentStartTime;
            if (_editorContext?.Entity == null) return fallback;

            var baseState = _editorContext.Entity.GetBaseState();
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
            _nodeValues.Clear();

            if (_editorContext?.Entity == null) return;

            var rule = _propertyEditorService.GetConstraint(_propertyName);
            bool isSlider = rule != null && rule.UIType == Core.PropertyUIType.Slider;

            // 🚀 【性能优化 1】：将资源查找移出循环！WPF 查找资源的耗时在循环内是毁灭性的！
            Style nodeStyle = Application.Current.TryFindResource(isSlider ? "OpacityThumbStyle" : "KeyframeThumbStyle") as Style;

            // ==========================================
            // 渲染 BaseState (初始钮扣)
            // ==========================================
            double trueBaseTimeSec = GetTrueBaseTimeSec(); // 🌟 提取真实起跑线！
            var baseState = _editorContext.Entity.GetBaseState();
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
                _nodeValues[initialNode] = baseVal;
            }

            // ==========================================
            // 渲染其它解码后的普通关键帧
            // ==========================================
            var decodedFrames = _projectedFrames ?? _microService.DecodeKeyframes(
                _editorContext.Entity,
                _propertyName,
                trueBaseTimeSec);

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
                    Margin = new Thickness(-6, 0, 0, 0),
                    Background = box.IsTemplateExpanded ? Brushes.MediumPurple : null,
                    ToolTip = box.IsTemplateExpanded
                        ? $"模板展开帧（只读）\n来源：{string.Join(" → ", box.TemplateSourcePath)}\n解绑模板实例后可独立编辑"
                        : $"关键帧：{box.VisualRelTime:0.###}s"
                };

                if (!box.IsTemplateExpanded)
                {
                    node.DragDelta += Node_DragDelta;
                    node.MouseRightButtonDown += Node_MouseRightButtonDown;
                    node.DragCompleted += (s, ev) => { RenderTrackKeyframes(); };
                }

                // 🚀 【性能优化 2】：先设定位置，再塞进画板！
                Canvas.SetLeft(node, xPos);
                Canvas.SetTop(node, yPos);
                KeyframeNodeCanvas.Children.Add(node);
                _nodes.Add(node);
                _nodeValues[node] = box.Value;
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
                    bool isController = _editorContext.Entity.GetType().Name.Contains("Controller");
                    if (!isController)
                    {
                        _currentStartTime += deltaSec;
                    }

                    var baseState = _editorContext.Entity.GetBaseState();
                    if (baseState != null &&
                        _propertyEditorService.TryGetValue(baseState, "Time", out object oldTime))
                    {
                        object newTimeStr = TimeExpressionUpdater.UpdateTimeExpressionByDelta(oldTime, deltaSec);
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

                    _microService.WriteBackVisualTime(
                        _editorContext.Entity,
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
                        var baseState = _editorContext.Entity.GetBaseState();
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
                    if (_editorContext?.Entity != null)
                    {
                        _messageBroker.Publish("RequestOpenPropertyEditor", (object)_editorContext.Entity);
                    }
                };
                menu.Items.Add(editItem);

                if (_nodeValues.TryGetValue(node, out var nodeValue) && nodeValue is bool booleanValue)
                {
                    var toggleItem = new MenuItem
                    {
                        Header = booleanValue ? "关闭此布尔状态" : "启用此布尔状态"
                    };
                    toggleItem.Click += (_, _) =>
                    {
                        AppServices.GetService<IHistoryService>().RecordSnapshot(_context.Storyboard);
                        var state = isBaseStateNode
                            ? _editorContext.Entity.GetBaseState()
                            : node.Tag as Models.ObjectState;
                        if (state != null &&
                            _propertyEditorService.TrySetValue(state, _propertyName, !booleanValue))
                        {
                            _context.MarkAsModified();
                            _messageBroker.Publish("DataModified");
                            RenderTrackKeyframes();
                        }
                    };
                    menu.Items.Add(toggleItem);
                }

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
                double newRelTime = newAbsTime - _currentStartTime;
                if (newRelTime < 0) newRelTime = 0;

                Type stateType = _editorContext.Entity.GetBaseState().GetType();
                var newFrame = Activator.CreateInstance(stateType) as Models.ObjectState;
                if (newFrame == null) return;
                newFrame.RelativeTime = (float)newRelTime;

                // Inherit the value active immediately before the insertion point.
                if (TryGetValueAtTime(newAbsTime, out var inheritedValue))
                {
                    _propertyEditorService.TrySetValue(newFrame, _propertyName, inheritedValue);
                }

                AppServices.GetService<IHistoryService>().RecordSnapshot(_context.Storyboard);
                _editorContext.Entity.GetKeyframes().Add(newFrame);
                _context?.MarkAsModified();
                _messageBroker.Publish("DataModified");
                RenderTrackKeyframes();
            };
            menu.Items.Add(newItem);




            // 2. 智能分支：只有剪贴板里有东西时，才追加"新建并粘贴"按钮，绝对不让 return 熔断整个菜单！
            if (_clipboardState != null)
            {
                var pasteNewItem = new MenuItem { Header = "📥 在此处新建关键帧并粘贴" };
                pasteNewItem.Click += (s, ev) =>
                {
                    double clickX = e.GetPosition(KeyframeNodeCanvas).X;
                    double newAbsTime = clickX / _pixelsPerSecond;
                    double newRelTime = newAbsTime - _currentStartTime;
                    if (newRelTime < 0) return;

                    Type stateType = _editorContext.Entity.GetBaseState().GetType();
                    var newFrame = Activator.CreateInstance(stateType) as Models.ObjectState;
                    newFrame.RelativeTime = (float)newRelTime;

                    if (_propertyEditorService.TryGetValue(_clipboardState, _propertyName, out object copiedVal) && copiedVal != null)
                    {
                        _propertyEditorService.TrySetValue(newFrame, _propertyName, copiedVal);
                    }

                    _editorContext.Entity.GetKeyframes().Add(newFrame);
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

        private bool TryGetValueAtTime(double absoluteTime, out object? value)
        {
            value = null;
            var baseState = _editorContext.Entity.GetBaseState();
            if (_propertyEditorService.TryGetValue(baseState, _propertyName, out var baseValue) &&
                baseValue != null)
                value = baseValue;

            var trueBaseTime = GetTrueBaseTimeSec();
            foreach (var frame in _microService.DecodeKeyframes(
                         _editorContext.Entity, _propertyName, trueBaseTime)
                         .Where(frame => trueBaseTime + frame.VisualRelTime <= absoluteTime)
                         .OrderBy(frame => frame.VisualRelTime))
            {
                if (frame.Value != null)
                    value = frame.Value;
            }
            return value != null;
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

            if (sortedNodes.All(n => _nodeValues.TryGetValue(n, out var value) && value is bool))
            {
                for (var i = 0; i < sortedNodes.Count; i++)
                {
                    var node = sortedNodes[i];
                    var start = Canvas.GetLeft(node);
                    var end = i + 1 < sortedNodes.Count
                        ? Canvas.GetLeft(sortedNodes[i + 1])
                        : Math.Max(start + 24, ActualWidth);
                    var enabled = (bool)_nodeValues[node];
                    var segment = new Rectangle
                    {
                        Width = Math.Max(1, end - start),
                        Height = Math.Max(1, ActualHeight - 2),
                        Fill = enabled ? Brushes.MediumSeaGreen : Brushes.Black,
                        Opacity = enabled ? .28 : .18,
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(segment, start);
                    Canvas.SetTop(segment, 1);
                    CurveRenderCanvas.Children.Add(segment);
                }
                return;
            }

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
            _microService.UpdatePixelsPerSecond(newPixelsPerSecond);
            RedrawPropertyCurves();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {

        }
    }
}

