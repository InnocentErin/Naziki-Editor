using Naziki_Editor.Core.Timeline;
using Naziki_Editor.Models;
using Naziki_Editor.UI.ViewModels;
using Naziki_Editor.State;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Naziki_Editor.Core;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Common;
using Naziki_Editor.Core.Messaging;
using Naziki_Editor.Core.Storyboard;

namespace Naziki_Editor.Views.TimelineClip
{
    public partial class ClipDetailedEditor : UserControl
    {
        private TimelineClipModel _clipModel;
        private ProjectDataContext _context;
        private double _pixelsPerSecond;
        private double _lastCalculatedMaxTime = 0;

        private ITimelineInteractionService _timelineService;
        private IPropertyEditorService _propertyEditorService;
        private IStoryboardRepository _storyboardRepository;
        private readonly IMessageBroker _messageBroker;
        private readonly IDialogService _dialogService;
        private readonly UI.Rendering.NoteVisualEngine _noteVisualEngine;

        private Point _panStartPoint;
        private double _panStartOffset;
        private bool _isPanning = false;

        public ClipDetailedEditor()
        {
            InitializeComponent();

            // ✨ 1. 标尺联动
            ScrollPropCanvas.ScrollChanged += (s, e) => {
                ScrollMicroRuler.ScrollToHorizontalOffset(e.HorizontalOffset);
            };

            // ✨ 2. 注入顶级工业手感：基于绝对静止层的左右平移引擎！
            PanCaptureLayer.MouseLeftButtonDown += (s, e) => {
                _isPanning = true;
                _panStartPoint = e.GetPosition(this);
                _panStartOffset = ScrollPropCanvas.HorizontalOffset;
                PanCaptureLayer.CaptureMouse();
                e.Handled = true;
            };

            PanCaptureLayer.MouseMove += (s, e) => {
                if (_isPanning)
                {
                    Point currentPos = e.GetPosition(this);
                    double deltaX = currentPos.X - _panStartPoint.X;
                    ScrollPropCanvas.ScrollToHorizontalOffset(_panStartOffset - deltaX);
                }
            };

            PanCaptureLayer.MouseLeftButtonUp += (s, e) => {
                if (_isPanning)
                {
                    _isPanning = false;
                    PanCaptureLayer.ReleaseMouseCapture();
                    e.Handled = true;
                }
            };

            // ✨ 3. 挂载神级缩放
            PanCaptureLayer.MouseWheel += Editor_PreviewMouseWheel;
            this.PreviewMouseWheel += Editor_PreviewMouseWheel;
        }

        public ClipDetailedEditor(IMessageBroker messageBroker, IDialogService dialogService, UI.Rendering.NoteVisualEngine noteVisualEngine) : this()
        {
            _messageBroker = messageBroker;
            _dialogService = dialogService;
            _noteVisualEngine = noteVisualEngine;
        }

        /// <summary>
        /// 🚀 【数据接线关口】：主轴双击方块后，此方法会被轰轰烈烈地激活！
        /// </summary>
        public void LoadClipData(TimelineClipModel clipModel, ProjectDataContext context, double pixelsPerSecond)
        {
            _clipModel = clipModel;
            _context = context;
            _pixelsPerSecond = pixelsPerSecond;

            _timelineService = new TimelineInteractionService(context, new TimelineCoordEngine(pixelsPerSecond));
            _propertyEditorService = new PropertyEditorService();
            _storyboardRepository = new StoryboardRepository();

            PropHeadersStackPanel.Children.Clear();
            PropTracksStackPanel.Children.Clear();

            if (_clipModel.AssociatedObject == null) return;

            // ==========================================
            // ✨ 宇宙测绘：寻找主时间轴的尽头，以及最后一个关键帧的时间！
            // ==========================================
            double maxTime = 10;
            if (_context.Chart?.note_list != null && _context.Chart.note_list.Count > 0)
                maxTime = _context.TimeEngine.TickToSeconds(_context.Chart.note_list[_context.Chart.note_list.Count - 1].tick) + 5;

            double lastFrameAbs = _timelineService.CalculateEntityEndTime(
                _clipModel.AssociatedObject,
                _clipModel.StartTime
            );

            // 🚀 【性能防爆屏障】：如果计算出的结束时间是未初始化的极大值（如 float.MaxValue），直接强制截断！
            // 防止后续生成宽度达到几百万像素的标尺，导致 WPF 测算引擎疯狂触发 SetValue 死循环！
            if (lastFrameAbs > 10000) lastFrameAbs = maxTime;

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

            // ==========================================
            // 🌟 4. 动态构建【多级分身：模板只读轨道组】
            // ==========================================
            var baseState = _clipModel.AssociatedObject.GetBaseState();
            var keyframes = _clipModel.AssociatedObject.GetKeyframes();

            // A. 预扫盘：当前主事件到底动了哪些属性？（用于提取主事件私有基因）
            HashSet<string> mainAnimatedProps = new HashSet<string>();
            foreach (string prop in supportedProperties)
            {
                bool hasAnim = false;
                if (_propertyEditorService.TryGetValue(baseState, prop, out object bVal) && bVal != null) hasAnim = true;
                if (!hasAnim && keyframes != null)
                {
                    foreach (var frame in keyframes)
                    {
                        if (_propertyEditorService.TryGetValue(frame, prop, out object fVal) && fVal != null) { hasAnim = true; break; }
                    }
                }
                if (hasAnim) mainAnimatedProps.Add(prop);
            }

            // B. 时空雷达：利用已有的时间解码引擎，找出所有触发了 Template 的绝对时间点！
            var templateBoxes = _timelineService.DecodeKeyframes(
                _clipModel.AssociatedObject, "Template", _clipModel.StartTime);

            // ✨ 【核心升级】：先按时间排序，然后用 GroupBy 强行把名字一样的模板合并到同一个维度里！
            var sortedTriggers = templateBoxes.Where(b => b.Value != null && !string.IsNullOrEmpty(b.Value.ToString()))
                                              .OrderBy(b => b.VisualRelTime).ToList();
            var groupedTemplates = sortedTriggers.GroupBy(b => b.Value.ToString()).ToList();

            foreach (var group in groupedTemplates)
            {
                string tName = group.Key;
                // C. 基因冲突探测（只需要拿这一组的第一个去探测一次就够了）
                bool hasConflict = false;
                List<string> conflictProps = new List<string>();
                Models.C2Template tData = null;

                tData = _storyboardRepository.GetTemplate(_context.Storyboard, tName);
                if (tData != null)
                {
                    var tProps = tData.GetBaseState().GetType().GetProperties();
                    foreach (var tp in tProps)
                    {
                        if (tp.Name == "Time" || tp.Name == "Easing" || tp.Name == "Template") continue;

                        bool tHasAnim = false;
                        if (_propertyEditorService.TryGetValue(tData.GetBaseState(), tp.Name, out object tbVal) && tbVal != null) tHasAnim = true;
                        if (!tHasAnim && tData.GetKeyframes() != null)
                        {
                            foreach (var tf in tData.GetKeyframes())
                                if (_propertyEditorService.TryGetValue(tf, tp.Name, out object tfVal) && tfVal != null) { tHasAnim = true; break; }
                        }

                        if (tHasAnim && mainAnimatedProps.Contains(tp.Name)) { hasConflict = true; conflictProps.Add(tp.Name); }
                    }
                }

                // D. 盖楼：动态生成独立组头 (合并版！)
                Brush headerBgBrush = hasConflict ? new SolidColorBrush(Color.FromArgb(80, 220, 50, 50)) : (Brush)Application.Current.FindResource("MenuBgColor");
                string conflictTip = hasConflict ? $"⚠️ 警告：检测到属性冲突风险！\n此模板与主事件共同竞争了以下属性：{string.Join(", ", conflictProps)}" : "✨ 基因纯净无冲突";

                Border tplHeaderLeft = new Border { Height = 28, Background = headerBgBrush, BorderBrush = (Brush)Application.Current.FindResource("BorderColor"), BorderThickness = new Thickness(0, 0, 0, 1), ToolTip = conflictTip };
                Grid tplHeaderGrid = new Grid();
                tplHeaderGrid.Children.Add(new TextBlock { Text = $"🌟 模板: {tName} (共触发 {group.Count()} 次)", Foreground = Brushes.Gold, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0) });

                Button unbindBtn = new Button
                {
                    Content = "✂️ 批量解绑",
                    FontSize = 10,
                    Padding = new Thickness(5, 1, 5, 1),
                    Margin = new Thickness(0, 0, 5, 0),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = Cursors.Hand,
                    Background = Brushes.Transparent,
                    Foreground = (Brush)Application.Current.FindResource("MainTextColor"),
                    BorderBrush = (Brush)Application.Current.FindResource("BorderColor"),
                    ToolTip = $"将 [{tName}] 的所有 {group.Count()} 次触发全部烘焙为私有关键帧！"
                };
                unbindBtn.Click += (s, ev) =>
                {
                    if (tData == null) return;
                    var mainEntity = _clipModel.AssociatedObject;
                    var mainKeyframes = mainEntity.GetKeyframes();
                    if (mainKeyframes == null) return;
                    Type stateType = mainEntity.GetBaseState().GetType();

                    Action<object, object> copyGenetics = (source, target) => {
                        var props = source.GetType().GetProperties();
                        foreach (var p in props)
                        {
                            if (p.Name == "Time" || p.Name == "RelativeTime" || p.Name == "AddTime" || p.Name == "Easing" || p.Name == "Template" || p.Name == "Destroy") continue;
                            var val = p.GetValue(source);
                            if (val != null)
                            {
                                var targetProp = stateType.GetProperty(p.Name);
                                if (targetProp != null && targetProp.CanWrite) targetProp.SetValue(target, val);
                            }
                        }
                    };

                    // ✨ 一次性烘焙该组里的所有触发点！
                    foreach (var triggerBox in group)
                    {
                        double triggerAbsTime = _clipModel.StartTime + triggerBox.VisualRelTime;
                        var tBase = tData.GetBaseState();
                        if (tBase != null)
                        {
                            var startFrame = Activator.CreateInstance(stateType) as Models.ObjectState;
                            startFrame.Time = (float)triggerAbsTime; copyGenetics(tBase, startFrame); mainKeyframes.Add(startFrame);
                        }
                        if (tData.GetKeyframes() != null)
                        {
                            double accumulatedRel = 0;
                            foreach (var tkf in tData.GetKeyframes())
                            {
                                if (tkf is Models.TemplateState ts)
                                {
                                    if (ts.RelativeTime.HasValue) accumulatedRel += ts.RelativeTime.Value;
                                    else if (ts.AddTime.HasValue) accumulatedRel += ts.AddTime.Value;
                                    else if (ts.Time != null && double.TryParse(ts.Time.ToString(), out double absT)) accumulatedRel = absT;
                                    double frameAbsTime = triggerAbsTime + accumulatedRel;
                                    var animFrame = Activator.CreateInstance(stateType) as Models.ObjectState;
                                    animFrame.Time = (float)frameAbsTime;
                                    if (!string.IsNullOrEmpty(ts.Easing)) animFrame.Easing = ts.Easing;
                                    copyGenetics(tkf, animFrame); mainKeyframes.Add(animFrame);
                                }
                            }
                        }
                        if (triggerBox.State is Models.ObjectState trueState) trueState.Template = null;
                    }

                    _context.MarkAsModified();
                    _messageBroker.Publish("RefreshTimeline");
                    LoadClipData(_clipModel, _context, _pixelsPerSecond);
                    _dialogService.ShowMessage($"✨ [{tName}] 的 {group.Count()} 次调用已全数降维剥离！", "批量解绑成功");
                };
                tplHeaderGrid.Children.Add(unbindBtn);
                tplHeaderLeft.Child = tplHeaderGrid;
                PropHeadersStackPanel.Children.Add(tplHeaderLeft);

                Border tplHeaderRight = new Border { Height = 28, Background = headerBgBrush, BorderBrush = (Brush)Application.Current.FindResource("BorderColor"), BorderThickness = new Thickness(0, 0, 0, 1) };
                PropTracksStackPanel.Children.Add(tplHeaderRight);

                // E. 盖楼：模板内部时空延展轨 (轨道上可以画出好几次触发的星星了！)
                Border tplTrackLeft = new Border { Height = 40, BorderBrush = (Brush)Application.Current.FindResource("BorderColor"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(20, 0, 0, 0) };
                tplTrackLeft.Child = new TextBlock { Text = $"✦ 共享轨", Foreground = Brushes.DarkKhaki, FontSize = 10, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
                PropHeadersStackPanel.Children.Add(tplTrackLeft);

                Border tplTrackRight = new Border { Height = 40, BorderBrush = (Brush)Application.Current.FindResource("BorderColor"), BorderThickness = new Thickness(0, 0, 0, 1) };
                Canvas tplCanvas = new Canvas { Width = targetPhysicalWidth, IsHitTestVisible = false };

                // ✨ 在同一条轨道上，遍历组内的每一次触发，点亮星星矩阵！
                if (tData != null)
                {
                    foreach (var triggerBox in group)
                    {
                        double triggerAbsTime = _clipModel.StartTime + triggerBox.VisualRelTime;
                        DrawTemplateStar(tplCanvas, triggerAbsTime * _pixelsPerSecond); // 起点星

                        if (tData.GetKeyframes() != null)
                        {
                            double accumulatedRel = 0;
                            foreach (var tkf in tData.GetKeyframes())
                            {
                                if (tkf is Models.TemplateState ts)
                                {
                                    if (ts.RelativeTime.HasValue) accumulatedRel += ts.RelativeTime.Value;
                                    else if (ts.AddTime.HasValue) accumulatedRel += ts.AddTime.Value;
                                    else if (ts.Time != null && double.TryParse(ts.Time.ToString(), out double absT)) accumulatedRel = absT;
                                    DrawTemplateStar(tplCanvas, (triggerAbsTime + accumulatedRel) * _pixelsPerSecond); // 后续星
                                }
                            }
                        }
                    }
                }
                tplTrackRight.Child = tplCanvas;
                PropTracksStackPanel.Children.Add(tplTrackRight);
            }


            // ==========================================
            // ⚙️ 5. 构建【主事件关键帧轨道组】
            // ==========================================
            Border mainHeaderLeft = new Border { Height = 28, Background = (Brush)Application.Current.FindResource("MenuBgColor"), BorderBrush = (Brush)Application.Current.FindResource("BorderColor"), BorderThickness = new Thickness(0, 0, 0, 1) };
            mainHeaderLeft.Child = new TextBlock { Text = "⚙️ 主事件私有关键帧", Foreground = (Brush)Application.Current.FindResource("HighlightBorderColor"), FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0) };
            PropHeadersStackPanel.Children.Add(mainHeaderLeft);

            Border mainHeaderRight = new Border { Height = 28, Background = (Brush)Application.Current.FindResource("MenuBgColor"), BorderBrush = (Brush)Application.Current.FindResource("BorderColor"), BorderThickness = new Thickness(0, 0, 0, 1) };
            PropTracksStackPanel.Children.Add(mainHeaderRight);

            // 6. 🧵 机械化流水线：批量手绘每一个普通属性的“表头 + 关键帧长轨”
            foreach (string prop in mainAnimatedProps) // ✨ 优化：只画真正被改动过的属性！
            {
                // A. 左侧：纯净的属性名文字边框 (同样加入 20 缩进，体现组级父子关系)
                Border headerBorder = new Border
                {
                    Height = 40,
                    BorderBrush = (Brush)Application.Current.FindResource("BorderColor"),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(20, 0, 0, 0)
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
                ClipPropertyTrackRow trackRow = new ClipPropertyTrackRow(_messageBroker, _dialogService);
                trackRow.Width = targetPhysicalWidth;
                trackRow.HorizontalAlignment = HorizontalAlignment.Left;

                // 把当前的属性名字塞进 Tag 里，让它拥有记忆！
                trackRow.Tag = prop;

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



        // 🚀 专业级缩放：锚定鼠标位置，全局极速排版！
        private void Editor_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                e.Handled = true;

                double zoomDelta = e.Delta > 0 ? 1.2 : (1.0 / 1.2);
                double oldPixels = _pixelsPerSecond;
                double newPixels = _pixelsPerSecond * zoomDelta;

                newPixels = Math.Max(10.0, Math.Min(2000.0, newPixels));
                if (Math.Abs(newPixels - oldPixels) < 0.01) return;

                // 核心：推算鼠标正下方指向的“绝对时间”
                Point mousePos = e.GetPosition(ScrollPropCanvas);
                double timeAtMouse = (ScrollPropCanvas.HorizontalOffset + mousePos.X) / oldPixels;

                _pixelsPerSecond = newPixels;
                double newPhysicalWidth = _lastCalculatedMaxTime * _pixelsPerSecond;

                // 1. 🚀 调用神级极速位移法术 (0 开销！)
                FastUpdateMicroRuler();

                // 2. ⚡ 极速重排下方所有的属性轨道！
                foreach (UIElement el in PropTracksStackPanel.Children)
                {
                    if (el is Border border && border.Child is Canvas tplCanvas)
                    {
                        tplCanvas.Width = newPhysicalWidth;
                        foreach (UIElement child in tplCanvas.Children)
                        {
                            if (child is TextBlock star && star.Tag is double absTimeSec)
                            {
                                Canvas.SetLeft(star, absTimeSec * _pixelsPerSecond - 6);
                            }
                        }
                    }
                    else if (el is ClipPropertyTrackRow trackRow)
                    {
                        trackRow.Width = newPhysicalWidth;
                        trackRow.FastUpdateZoom(_pixelsPerSecond);
                    }
                }

                // 3. 锚定回拨：把刚才鼠标指着的那一秒，重新拉回鼠标物理位置的下方！
                double newOffset = (timeAtMouse * _pixelsPerSecond) - mousePos.X;
                ScrollPropCanvas.ScrollToHorizontalOffset(newOffset);
            }
        }
        // 🚀 宏观轴同款：O(1) 极速坐标位移法术！绝不新建控件！
        private void FastUpdateMicroRuler()
        {
            double newPhysicalWidth = _lastCalculatedMaxTime * _pixelsPerSecond;
            MicroRulerCanvas.Width = newPhysicalWidth;

            foreach (UIElement child in MicroRulerCanvas.Children)
            {
                if (child is FrameworkElement fe)
                {
                    // A. 处理标尺刻度与蓝玻璃
                    if (fe.Tag is string tagStr)
                    {
                        if (tagStr == "HIGHLIGHT")
                        {
                            double startX = _clipModel.StartTime * _pixelsPerSecond;
                            double endX = _clipModel.EndTime * _pixelsPerSecond;
                            Canvas.SetLeft(fe, startX);
                            fe.Width = Math.Max(2, endX - startX);
                        }
                        else if (tagStr.StartsWith("TICK_LINE_"))
                        {
                            int s = int.Parse(tagStr.Substring(10));
                            ((System.Windows.Shapes.Line)fe).X1 = s * _pixelsPerSecond;
                            ((System.Windows.Shapes.Line)fe).X2 = s * _pixelsPerSecond;
                        }
                        else if (tagStr.StartsWith("TICK_TEXT_"))
                        {
                            int s = int.Parse(tagStr.Substring(10));
                            Canvas.SetLeft(fe, s * _pixelsPerSecond + 2);
                        }
                    }
                    // B. ✨ 完美复刻宏观轴：多态音符物理形变算法！
                    else if (fe.Tag is Models.C2Note note)
                    {
                        double seconds = _context.TimeEngine.TickToSeconds(note.tick);
                        double absoluteX = seconds * _pixelsPerSecond;

                        if (child is Image img)
                        {
                            Canvas.SetLeft(img, absoluteX - (img.Width / 2.0));
                        }
                        else if (child is TextBlock)
                        {
                            // 微观时光屋专属：文字的 LeftOffset 是 +3.0
                            Canvas.SetLeft(fe, absoluteX + 3.0);
                        }
                        else if (child is System.Windows.Shapes.Line line && line.DataContext is Models.C2Note lastChild)
                        {
                            double lastChildSeconds = _context.TimeEngine.TickToSeconds(lastChild.tick);
                            line.X1 = absoluteX;
                            line.X2 = lastChildSeconds * _pixelsPerSecond;
                        }
                        else if (child is System.Windows.Shapes.Rectangle rect)
                        {
                            if (rect.Height == 2)
                            {
                                Canvas.SetLeft(rect, absoluteX);
                                double endSec = _context.TimeEngine.TickToSeconds(note.tick + note.hold_tick);
                                rect.Width = (endSec - seconds) * _pixelsPerSecond;
                            }
                            else
                            {
                                Canvas.SetLeft(rect, absoluteX - (rect.Width / 2.0));
                            }
                        }
                    }
                }
            }
        }












        private void DrawTemplateStar(Canvas canvas, double xPos)
        {
            TextBlock star = new TextBlock
            {
                Text = "✦",
                Foreground = Brushes.Gold,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                // 🌟 黑科技：利用当前的物理坐标反推绝对秒数，并封存在神圣基因(Tag)里！
                // 这样在外部调用完全不用改代码，缩放引擎也能精准抓取它！
                Tag = xPos / _pixelsPerSecond
            };
            Canvas.SetLeft(star, xPos - 6);
            Canvas.SetTop(star, 10);
            canvas.Children.Add(star);
        }



        private void RenderMicroRulerTicks(double maxTime)
        {
            // ✨ 1. 必须先召唤音符雷达！
            // 提前让引擎画好音符（并执行它的 Canvas.Clear()），绝不影响后续图层！
            _noteVisualEngine.RenderNoteRuler(MicroRulerCanvas, _context?.Chart?.note_list, _context?.TimeEngine, _pixelsPerSecond, true);

            int maxSeconds = (int)Math.Ceiling(maxTime);

            // ✨ 2. 在音符画完之后，补上绝对秒数刻度（覆盖在音符之上，更清晰！）
            for (int s = 0; s <= maxSeconds; s++)
            {
                double x = s * _pixelsPerSecond;
                // 让刻度线从顶部往下画 20 像素
                MicroRulerCanvas.Children.Add(new System.Windows.Shapes.Line { X1 = x, X2 = x, Y1 = 0, Y2 = 20, Stroke = Brushes.Gray, StrokeThickness = 1, Tag = $"TICK_LINE_{s}" });

                var text = new TextBlock { Text = s + "s", FontSize = 10, Foreground = Brushes.Gray, Tag = $"TICK_TEXT_{s}" };
                Canvas.SetLeft(text, x + 2);
                Canvas.SetTop(text, 2);
                MicroRulerCanvas.Children.Add(text);
            }

            // ✨ 3. 最后铺上这块半透明的蓝色玻璃结界，标明方块的生命周期
            double startX = _clipModel.StartTime * _pixelsPerSecond;
            double endX = _clipModel.EndTime * _pixelsPerSecond;

            // 🚀 【二次防爆兜底】：为矩形宽度设下安全极限，防止 WPF 渲染核爆
            double rectWidth = endX - startX;
            if (rectWidth > 100000) rectWidth = 100000;

            var highlight = new System.Windows.Shapes.Rectangle
            {
                Width = Math.Max(2, rectWidth),
                Height = 50,
                Fill = new SolidColorBrush(Color.FromArgb(40, 77, 184, 255)),
                Tag = "HIGHLIGHT"
            };
            Canvas.SetLeft(highlight, startX);
            MicroRulerCanvas.Children.Add(highlight);
        }
    }
}