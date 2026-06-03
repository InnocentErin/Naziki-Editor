using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Naziki_Editor.Models;
using Naziki_Editor.State;
using Naziki_Editor.Core;

namespace Naziki_Editor.Views
{
    public partial class TimelineClipControl : UserControl
    {
        // ==========================================
        // 🌟 核心接入管道与上下文状态锁
        // ==========================================
        private TimelineClipModel _model;
        private ProjectDataContext _context;
        private double _pixelsPerSecond = 100.0;

        private ClipViewMode _currentViewMode = ClipViewMode.Keyframe;
        private List<Thumb> _nodeThumbs = new List<Thumb>();
        private Thumb _selectedNode = null;

        // 宏观平移状态锁
        private bool _isDraggingClip = false;
        private Point _clipDragStartPoint;
        private double _originalStartTime;


        // ✨ 追加：轨道感知与父级通讯枢纽
        public int CurrentTrackIndex { get; private set; }
        public int MaxTrackIndex { get; set; }

        public event Action<TimelineClipControl, int> OnTrackIndexChanged; // 换轨通知
        public event Action<TimelineClipControl> OnRequestNewTrack;        // 越界修路请求
        public event Action<TimelineClipModel> OnRequestDetailedEditMode;  // 双击进入微观世界
        public event Action<TimelineClipModel> OnClipSelected;             // 单点选中反射信号
        
        public event Action<TimelineClipModel> OnRequestPropertyEditor;    // 请求打开属性编辑器的独立信号

        private double _originalY; // 记录拖拽前的 Y 坐标


        public TimelineClipControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 📥 唯一交接关口：由父轨道将强类型模型、上下文基站以及缩放比例喂给方块
        /// </summary>
        public void Init(TimelineClipModel model, ProjectDataContext context, double pixelsPerSecond, int trackIndex, int maxTrackIndex)
        {
            _model = model;
            _context = context;
            _pixelsPerSecond = pixelsPerSecond;

            CurrentTrackIndex = trackIndex;
            MaxTrackIndex = maxTrackIndex;


            // 1. 刷新基础文字外观
            ClipNameText.Text = _model.DisplayName;

            // 2. 刷新物理位置与宽度
            UpdateXPositionAndWidth();

            // 3. 🛡️ 【智能基因嗅探】：检查是否是 $note 宏或常驻元素
            InspectEntityGenetics();

            // 4. 读取校验状态：如果底层数据本身有悖论，外壳闪烁红框！
            EvaluateValidationWarning();

            // 5. 还原内部关键帧渲染
            RebuildInternalKeyframes();
        }

        private void UpdateXPositionAndWidth()
        {
            // 初始化我们的核心时空像素转换官
            var coordEngine = new Core.Timeline.TimelineCoordEngine(_pixelsPerSecond);
            double left = coordEngine.TimeToX(_model.StartTime);
            Canvas.SetLeft(this, left);

            // ✨ 轨道高度吸附：方块自我校准 Y 坐标
            Canvas.SetTop(this, CurrentTrackIndex * 40);

            // 如果是永生常驻元素
            if (_model.EndTime >= 999999 || _model.EndTime <= _model.StartTime)
            {
                this.Width = 250;
                VirtualEndLine.Visibility = Visibility.Visible;

                // ✨ 正确修复：直接修改 Line 的 X1/X2 基因属性，不再通过 Canvas 传话！
                double virtualX = coordEngine.CalculateVirtualEndPosition(_model.StartTime, _model.StartTime + 1.8);
                VirtualEndLine.X1 = virtualX;
                VirtualEndLine.X2 = virtualX;
            }
            else
            {
                double width = coordEngine.TimeToX(_model.EndTime - _model.StartTime);
                this.Width = Math.Max(15, width);
                VirtualEndLine.Visibility = Visibility.Collapsed;
            }
        }

        private void InspectEntityGenetics()
        {
            if (_model.AssociatedObject == null) return;

            // 判定 A：是否是 $note 寄生宏？
            bool isMacro = false;
            dynamic baseState = _model.AssociatedObject.GetBaseState();

            // 优雅反射探头：探测 Time 或者是特定的 NoteTarget 是否属于宏指令
            if (baseState != null)
            {
                try
                {
                    string rawTime = baseState.Time?.ToString() ?? "";
                    if (rawTime.Contains("$note")) isMacro = true;
                }
                catch { }
            }

            if (isMacro)
            {
                // 🔒 【边缘封印】：禁止左右边缘拉伸，并在角标打上 ♪ 烙印
                ResizeLeftThumb.Visibility = Visibility.Collapsed;
                ResizeRightThumb.Visibility = Visibility.Collapsed;
                TxtModeIcon.Text = "♪";
                // 让内嵌的 Rectangle 闪烁可爱的音符虚线！
                DashBorderShape.Stroke = (Brush)Application.Current.FindResource("HighlightBorderColor") ?? Brushes.DodgerBlue;
                DashBorderShape.StrokeDashArray = new DoubleCollection() { 3, 2 };
                ClipBackground.BorderBrush = Brushes.Transparent; // 隐藏外层 Border 的实线边框
            }
            else
            {
                TxtModeIcon.Text = "⏱";
                // 绝对时间恢复原状
                DashBorderShape.Stroke = Brushes.Transparent;
                DashBorderShape.StrokeDashArray = null;
                ClipBackground.BorderBrush = (Brush)Application.Current.FindResource("HighlightBorderColor") ?? Brushes.DodgerBlue;
            }
        }

        private void EvaluateValidationWarning()
        {
            // 调用 Core 层写好的安全防爆雷达
            var check = StoryboardValidator.ValidateStateConflicts(_model.AssociatedObject);
            if (!check.IsValid)
            {
                // 🚨 发现时空悖论！边框强制化为警告红，并装填 Tooltip
                ClipBackground.BorderBrush = Brushes.Crimson;
                this.ToolTip = check.ErrorMessage;
            }
            else
            {
                ApplyThematicColors();
                this.ToolTip = _model.DisplayName;
            }
        }

        // ==========================================
        // 🔮 交互核心：宏观移动与边缘微调
        // ==========================================
        private void ClipBackground_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // 🌟 物理叹息之墙：在这里统一判断双击还是单点！
            if (e.ClickCount == 2)
            {
                // 如果第一次点击不小心触发了拖拽状态，赶紧强行释放！
                _isDraggingClip = false;
                RootGrid.ReleaseMouseCapture();
                ClipBackground.Opacity = 1.0;

                OnRequestDetailedEditMode?.Invoke(_model);
                e.Handled = true; // 吞掉事件，防止它继续冒泡
                return; // 直接退出，绝不执行下面的单点逻辑！
            }

            // 🚀 【新增】：拦截 Ctrl+单击！
            if (e.ClickCount == 1 && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                _isDraggingClip = false;
                ClipBackground.Opacity = 1.0;
                OnRequestPropertyEditor?.Invoke(_model); // 发射信号！
                e.Handled = true;
                return; // 直接退出，不执行普通的拖拽和单机选中！
            }




            // --- 以下是原本的单次点击选中 + 拖拽逻辑 ---
            _model.IsSelected = true;
            DeselectAllNodes();
            OnClipSelected?.Invoke(_model);

            _isDraggingClip = true;
            _clipDragStartPoint = e.GetPosition(this.Parent as UIElement);
            _originalStartTime = _model.StartTime;

            _originalY = Canvas.GetTop(this);
            if (double.IsNaN(_originalY)) _originalY = CurrentTrackIndex * 40.0; // 防御 NaN 穿模

            Panel.SetZIndex(this, 999); // ✨ 选中瞬间赋予最高特权，浮在最上层绝不消失！

            RootGrid.CaptureMouse();
            e.Handled = true;
            ClipBackground.Opacity = 0.7;
        }

        private void ClipBackground_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingClip)
            {
                Point currentPos = e.GetPosition(this.Parent as UIElement);
                // ✨ 物理防抖结界：如果鼠标移动距离小于 3 像素，判定为普通单击的微小手抖，绝不触发换轨和时间平移！
                if (Math.Abs(currentPos.X - _clipDragStartPoint.X) < 3 && Math.Abs(currentPos.Y - _clipDragStartPoint.Y) < 3)
                    return;

                // 1. ⏱️ X 轴时间平移
                double deltaX = currentPos.X - _clipDragStartPoint.X;
                double deltaTime = deltaX / _pixelsPerSecond;
                _model.StartTime = _originalStartTime + deltaTime;
                if (_model.StartTime < 0) _model.StartTime = 0;
                Canvas.SetLeft(this, _model.StartTime * _pixelsPerSecond);

                // 2. ↕️ Y 轴轨道吸附与越界嗅探
                double deltaY = currentPos.Y - _clipDragStartPoint.Y;
                double newY = _originalY + deltaY;

                // 计算目标轨道的 Index (每轨高度 40)
                int targetTrack = (int)Math.Round(newY / 40.0);
                if (targetTrack < 0) targetTrack = 0;

                // 🚨 越界修路判定：最多只能比当前最大轨道多 1（一次新建一条）
                if (targetTrack > MaxTrackIndex)
                {
                    targetTrack = MaxTrackIndex + 1;
                    // 向大本营发送信号，请求铺设新轨道！
                    OnRequestNewTrack?.Invoke(this);
                }

                // 物理位置强制吸附
                if (targetTrack != CurrentTrackIndex)
                {
                    CurrentTrackIndex = targetTrack;
                    Canvas.SetTop(this, CurrentTrackIndex * 40);
                    OnTrackIndexChanged?.Invoke(this, CurrentTrackIndex);
                }
            }
        }

        private void ClipBackground_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingClip)
            {
                _isDraggingClip = false;
                RootGrid.ReleaseMouseCapture();
                ClipBackground.Opacity = 1.0;
                Panel.SetZIndex(this, 0); // ✨ 放下鼠标时，乖乖交出特权，回到普通层级

                // 3. ✨ 关键帧时间融合法术：带着内部的所有子节点一起相对平移！
                double finalDeltaTime = _model.StartTime - _originalStartTime;
                if (Math.Abs(finalDeltaTime) > 0.001 && _model.AssociatedObject != null)
                {
                    try
                    {
                        // 动态反射深入 Cytoid 数据模型，修改所有非 max 的绝对时间
                        dynamic obj = _model.AssociatedObject;
                        foreach (var state in obj.States)
                        {
                            // 浮点数时间且不是不可见占位符时才进行偏移
                            if (state.Time != null && state.Time is float && (float)state.Time != float.MaxValue)
                            {
                                state.Time = (float)state.Time + (float)finalDeltaTime;
                            }
                        }
                    }
                    catch { /* 如果包含 $note 等非数字宏指令，则不破坏原有结构 */ }
                }

                // 4. ✨ 图层深度绑定：将轨道编号映射给 order
                try
                {
                    dynamic baseState = _model.AssociatedObject.GetBaseState();
                    if (baseState != null && baseState.Order != CurrentTrackIndex)
                    {
                        baseState.Order = CurrentTrackIndex;
                        _context?.MarkAsModified();
                    }
                }
                catch { }

                _context?.MarkAsModified();
                EvaluateValidationWarning();
            }
        }



        // 左右边缘伸缩处理器
        private void ResizeLeft_DragDelta(object sender, DragDeltaEventArgs e)
        {
            double deltaTime = e.HorizontalChange / _pixelsPerSecond;
            _model.StartTime += deltaTime;
            if (_model.StartTime < 0) _model.StartTime = 0;
            UpdateXPositionAndWidth();
        }

        private void ResizeRight_DragDelta(object sender, DragDeltaEventArgs e)
        {
            double deltaTime = e.HorizontalChange / _pixelsPerSecond;
            _model.EndTime += deltaTime;
            UpdateXPositionAndWidth();
        }

        // ==========================================
        // ✨ 右键菜单：一键重新锚定至最近音符算法 (5.3.2 节落地)
        // ==========================================
        private void MenuReanchor_Click(object sender, RoutedEventArgs e)
        {
            if (_context == null || !_context.HasChart || _model.AssociatedObject == null)
            {
                MessageBox.Show("纳尼？！必须先在主界面加载对应的谱面文件才能触发锚定雷达哦！", "重算失败");
                return;
            }



            // 🚀 一键呼叫我们的 Core 算法中枢！
            string newExpression = Core.Timeline.TimelineAnchorEngine.CalculateNearestAnchorExpression(
                _model.StartTime,
                _context.Chart.note_list,
                _context.TimeEngine,
                out C2Note nearestNote,
                out double offset);

            if (newExpression != null && nearestNote != null)
            {
                dynamic baseState = _model.AssociatedObject.GetBaseState();
                if (baseState != null)
                {
                    baseState.Time = newExpression; // 完美重写不可变模型的字符串！
                    MessageBox.Show($"✨ 自动吸附配对成功！\\\\n\\\\n方块已被精准绑定至 [Note ID: {nearestNote.id}]\\\\n时间轴新表达式: {newExpression}", "时空锚定完毕");

                    _context.MarkAsModified();
                    InspectEntityGenetics(); // 重新刷新外壳形态
                }
            }
        }


        // ==========================================
        // ✨ 右键菜单：精准锁定最后一个关键帧，并悄悄把它的 Destroy 属性设为 true
        // ==========================================
        private void MenuDestroyAtLastFrame_Click(object sender, RoutedEventArgs e)
        {
            if (_model?.AssociatedObject == null) return;
            var kfs = _model.AssociatedObject.GetKeyframes();
            if (kfs == null || kfs.Count == 0) return;

            // 锁定最后一帧！
            var lastFrame = kfs[kfs.Count - 1];
            var propInfo = lastFrame.GetType().GetProperty("Destroy");

            if (propInfo != null && propInfo.CanWrite)
            {
                // 强制剥壳（处理 bool? 可空类型），并写入 true
                Type t = propInfo.PropertyType;
                if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>)) t = Nullable.GetUnderlyingType(t);
                propInfo.SetValue(lastFrame, Convert.ChangeType(true, t));

                // 刷新大本营！
                if (Window.GetWindow(this) is MainWindow mainWin)
                {
                    mainWin.Context.MarkAsModified();
                    mainWin.TimelineConsole.LoadStoryboardTimeline(mainWin.Context); // 重新渲染时间轴，让方块变短！
                }
            }
        }








        // ==========================================
        // 💎 内部状态帧节点管理 (详细调整模式的基石)
        // ==========================================
        private void RebuildInternalKeyframes()
        {
            NodeCanvas.Children.Clear();
            _nodeThumbs.Clear();

            var keyframes = _model.AssociatedObject?.GetKeyframes();
            if (keyframes == null) return;

            // 遍历所有帧状态，在方块内画出 ♦
            double totalDuration = _model.EndTime - _model.StartTime;
            if (totalDuration <= 0) totalDuration = 1.0;

            foreach (var frame in keyframes)
            {
                // 获取当前帧的相对秒数
                // 暂时用测试节点占位，实际用反射抓取 frame.Time 或算出来的绝对秒数
                AddKeyframeNodeToCanvas(50, 0.5);
            }
        }

        private void AddKeyframeNodeToCanvas(double relativeX, double value)
        {
            Thumb thumb = new Thumb
            {
                Tag = value,
                Style = (Style)Application.Current.FindResource(_currentViewMode == ClipViewMode.Keyframe ? "KeyframeThumbStyle" : "OpacityThumbStyle")
            };

            thumb.DragDelta += NodeThumb_DragDelta;

            NodeCanvas.Children.Add(thumb);
            Canvas.SetLeft(thumb, relativeX);
            Canvas.SetTop(thumb, this.ActualHeight / 2 - 6);
            _nodeThumbs.Add(thumb);
        }

        private void NodeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (sender is Thumb thumb)
            {
                double currentX = Canvas.GetLeft(thumb);
                double newX = currentX + e.HorizontalChange;

                // 🛑 【绝对叹息之墙】：关键帧绝对不能移出事件方块本身
                if (newX < 0) newX = 0;
                if (newX > this.ActualWidth - thumb.ActualWidth) newX = this.ActualWidth - thumb.ActualWidth;

                Canvas.SetLeft(thumb, newX);

                // 透明度上下拉扯逻辑
                if (_currentViewMode == ClipViewMode.Opacity)
                {
                    double currentY = Canvas.GetTop(thumb);
                    double newY = currentY + e.VerticalChange;
                    if (newY < 0) newY = 0;
                    if (newY > this.ActualHeight - thumb.ActualHeight) newY = this.ActualHeight - thumb.ActualHeight;
                    Canvas.SetTop(thumb, newY);

                    double val = 1.0 - (newY / this.ActualHeight);
                    thumb.Tag = Math.Max(0, Math.Min(1, val));
                    RedrawOpacityCurve();
                }

                _context?.MarkAsModified();
            }
        }



        // ==========================================
        // 🎨 主题色彩适配器：根据不同的 AssociatedObject 类型，自动切换方块的背景与边框颜色资源
        // ==========================================
        private void ApplyThematicColors()
        {
            if (_model.AssociatedObject == null) return;

            // 1. 🔍 【门派基因探测】：根据当前 AssociatedObject 的具体强类型，分发资源钥匙
            string baseResourceKey = "TextClip"; // 兜底钥匙

            string typeName = _model.AssociatedObject.GetType().Name;
            switch (typeName)
            {
                case "C2Sprite": baseResourceKey = "SpriteClip"; break;
                case "C2Text": baseResourceKey = "TextClip"; break;
                case "C2Video": baseResourceKey = "VideoClip"; break;
                case "C2Line": baseResourceKey = "LineClip"; break;
                case "C2SceneController": baseResourceKey = "ControllerClip"; break;
                case "C2NoteController": baseResourceKey = "NoteControllerClip"; break;
            }

            // 2. 🚀 【动态换肤交接】：通过 SetResourceReference 强行注册动态监听！
            // 这样一来，哪怕用户在主界面运行中一键切换皮肤，WPF 也会自动去新主题字典里抓取对应的颜色，绝不卡顿！
            ClipBackground.SetResourceReference(Border.BackgroundProperty, $"{baseResourceKey}BgBrush");
            ClipBackground.SetResourceReference(Border.BorderBrushProperty, $"{baseResourceKey}BorderBrush");
        }




        private void RedrawOpacityCurve()
        {
            OpacityCurve.Points.Clear();
            var sortedList = _nodeThumbs.OrderBy(t => Canvas.GetLeft(t)).ToList();
            foreach (var thumb in sortedList)
            {
                OpacityCurve.Points.Add(new Point(Canvas.GetLeft(thumb) + 5, Canvas.GetTop(thumb) + 5));
            }
        }

        private void DeselectAllNodes()
        {
            foreach (var t in _nodeThumbs) { t.Effect = null; t.Opacity = 0.8; }
            _selectedNode = null;
        }
    }
}