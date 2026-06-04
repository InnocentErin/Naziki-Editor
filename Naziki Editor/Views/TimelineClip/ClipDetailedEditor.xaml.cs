using Naziki_Editor.Core.Timeline;
using Naziki_Editor.Models;
using Naziki_Editor.State;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Naziki_Editor.Core;

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
                if (Core.FastReflectionHelper.TryGetValue(baseState, prop, out object bVal) && bVal != null) hasAnim = true;
                if (!hasAnim && keyframes != null)
                {
                    foreach (var frame in keyframes)
                    {
                        if (Core.FastReflectionHelper.TryGetValue(frame, prop, out object fVal) && fVal != null) { hasAnim = true; break; }
                    }
                }
                if (hasAnim) mainAnimatedProps.Add(prop);
            }

            // B. 时空雷达：利用已有的时间解码引擎，找出所有触发了 Template 的绝对时间点！
            var templateBoxes = Core.StoryboardTimeConverter.DecodeTimelineKeyframes(
                _clipModel.AssociatedObject, "Template", _context.TimeEngine, _context.Chart?.note_list, _clipModel.StartTime);

            // 按时间严格排序（越早触发的排在越前面，完全符合大大要求！）
            var sortedTriggers = templateBoxes.Where(b => b.Value != null && !string.IsNullOrEmpty(b.Value.ToString()))
                                              .OrderBy(b => b.VisualRelTime).ToList();

            foreach (var triggerBox in sortedTriggers)
            {
                string tName = triggerBox.Value.ToString();
                double triggerAbsTime = _clipModel.StartTime + triggerBox.VisualRelTime;

                // C. 基因冲突探测（暂时的轻量级雷达，后续移入 Validator）
                bool hasConflict = false;
                List<string> conflictProps = new List<string>();
                Models.C2Template tData = null;

                if (_context.Storyboard.templates != null && _context.Storyboard.templates.ContainsKey(tName))
                {
                    tData = _context.Storyboard.templates[tName];
                    var tProps = tData.GetBaseState().GetType().GetProperties();
                    foreach (var tp in tProps)
                    {
                        if (tp.Name == "Time" || tp.Name == "Easing" || tp.Name == "Template") continue;

                        bool tHasAnim = false;
                        if (Core.FastReflectionHelper.TryGetValue(tData.GetBaseState(), tp.Name, out object tbVal) && tbVal != null) tHasAnim = true;
                        if (!tHasAnim && tData.GetKeyframes() != null)
                        {
                            foreach (var tf in tData.GetKeyframes())
                            {
                                if (Core.FastReflectionHelper.TryGetValue(tf, tp.Name, out object tfVal) && tfVal != null) { tHasAnim = true; break; }
                            }
                        }

                        // 如果模板动了这个属性，且主事件也动了，警报拉响！
                        if (tHasAnim && mainAnimatedProps.Contains(tp.Name))
                        {
                            hasConflict = true;
                            conflictProps.Add(tp.Name);
                        }
                    }
                }

                // D. 盖楼：动态生成独立组头
                Brush headerBgBrush = hasConflict ? new SolidColorBrush(Color.FromArgb(80, 220, 50, 50)) : (Brush)Application.Current.FindResource("MenuBgColor");
                string conflictTip = hasConflict ? $"⚠️ 警告：检测到属性冲突风险！\n此模板与主事件共同竞争了以下属性：{string.Join(", ", conflictProps)}" : "✨ 基因纯净无冲突";

                Border tplHeaderLeft = new Border { Height = 28, Background = headerBgBrush, BorderBrush = (Brush)Application.Current.FindResource("BorderColor"), BorderThickness = new Thickness(0, 0, 0, 1), ToolTip = conflictTip };
                Grid tplHeaderGrid = new Grid();
                tplHeaderGrid.Children.Add(new TextBlock { Text = $"🌟 模板: {tName}", Foreground = Brushes.Gold, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0) });

                Button unbindBtn = new Button
                {
                    Content = "✂️ 烘焙解绑",
                    FontSize = 10,
                    Padding = new Thickness(5, 1, 5, 1),
                    Margin = new Thickness(0, 0, 5, 0),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = Cursors.Hand,
                    Background = Brushes.Transparent,
                    Foreground = (Brush)Application.Current.FindResource("MainTextColor"),
                    BorderBrush = (Brush)Application.Current.FindResource("BorderColor"),
                    ToolTip = $"将 [{tName}] 的关键帧直接融入主事件中"
                };
                unbindBtn.Click += (s, ev) =>
                {
                    if (tData == null || triggerBox.State == null) return;

                    var mainEntity = _clipModel.AssociatedObject;
                    var mainKeyframes = mainEntity.GetKeyframes();
                    if (mainKeyframes == null) return;

                    // 提取主实体关键帧的强类型 DNA (比如 SpriteState 或 TextState)
                    Type stateType = mainEntity.GetBaseState().GetType();

                    // 🛠️ 定义神圣的基因拷贝法术 (忽略时间/模板等控制属性，只拷贝真正的数值！)
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

                    // 🌟 1. 提取模板初始状态 (BaseState) -> 转化为触发点绝对时间帧
                    var tBase = tData.GetBaseState();
                    if (tBase != null)
                    {
                        var startFrame = Activator.CreateInstance(stateType) as Models.ObjectState;
                        startFrame.Time = (float)triggerAbsTime; // 钉死在触发的那一瞬间
                        copyGenetics(tBase, startFrame);
                        mainKeyframes.Add(startFrame);
                    }

                    // 🌟 2. 提取模板关键帧 (Keyframes) -> 逐个转化为绝对时间帧，带入主事件
                    if (tData.GetKeyframes() != null)
                    {
                        double accumulatedRel = 0;
                        foreach (var tkf in tData.GetKeyframes())
                        {
                            if (tkf is Models.TemplateState ts)
                            {
                                // 解算模板内部的相对时间
                                if (ts.RelativeTime.HasValue) accumulatedRel += ts.RelativeTime.Value;
                                else if (ts.AddTime.HasValue) accumulatedRel += ts.AddTime.Value;
                                else if (ts.Time != null && double.TryParse(ts.Time.ToString(), out double absT)) accumulatedRel = absT;

                                // 映射到宏观绝对时间
                                double frameAbsTime = triggerAbsTime + accumulatedRel;
                                var animFrame = Activator.CreateInstance(stateType) as Models.ObjectState;
                                animFrame.Time = (float)frameAbsTime;

                                // 复制缓动曲线
                                if (!string.IsNullOrEmpty(ts.Easing))
                                    animFrame.Easing = ts.Easing;

                                copyGenetics(tkf, animFrame);
                                mainKeyframes.Add(animFrame);
                            }
                        }
                    }

                    // 🌟 3. 斩断羁绊！剥离宿主身上的 Template 寄生基因
                    if (triggerBox.State is Models.ObjectState trueState)
                    {
                        trueState.Template = null; // 🚀 直接通过基类属性赋值，抛弃反射，更安全极速！
                    }

                    // 🌟 4. 重建宇宙闭环
                    _context.MarkAsModified();

                    // 呼叫主时间轴重绘（因为方块的外观需要消去 🪄 角标）
                    if (Window.GetWindow(this) is MainWindow mainWin)
                    {
                        mainWin.TimelineConsole.LoadStoryboardTimeline(mainWin.Context);
                    }

                    // 原地重新加载微观时光屋！
                    LoadClipData(_clipModel, _context, _pixelsPerSecond);

                    MessageBox.Show($"✨ [{tName}] 模板的全部基因已成功降维剥离，并烘焙为当前事件的私有关键帧！\n\n您可以直接在下方的【主事件关键帧】轨道中对它们进行细微调整啦！", "烘焙解绑成功");
                };
                tplHeaderGrid.Children.Add(unbindBtn);
                tplHeaderLeft.Child = tplHeaderGrid;
                PropHeadersStackPanel.Children.Add(tplHeaderLeft);

                Border tplHeaderRight = new Border { Height = 28, Background = headerBgBrush, BorderBrush = (Brush)Application.Current.FindResource("BorderColor"), BorderThickness = new Thickness(0, 0, 0, 1) };
                PropTracksStackPanel.Children.Add(tplHeaderRight);

                // E. 盖楼：模板内部时空延展轨
                Border tplTrackLeft = new Border { Height = 40, BorderBrush = (Brush)Application.Current.FindResource("BorderColor"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(20, 0, 0, 0) };
                tplTrackLeft.Child = new TextBlock { Text = $"触发于 {triggerAbsTime:0.00}s", Foreground = Brushes.DarkKhaki, FontSize = 10, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
                PropHeadersStackPanel.Children.Add(tplTrackLeft);

                Border tplTrackRight = new Border { Height = 40, BorderBrush = (Brush)Application.Current.FindResource("BorderColor"), BorderThickness = new Thickness(0, 0, 0, 1) };
                Canvas tplCanvas = new Canvas { Width = targetPhysicalWidth, IsHitTestVisible = false }; // 绝对只读护盾

                // ✨ 绘制模板内部的小星星
                if (tData != null)
                {
                    // 1. 绘制起点降临星 (BaseState)
                    DrawTemplateStar(tplCanvas, triggerAbsTime * _pixelsPerSecond);

                    // 2. 绘制后续相对星 (展开模板自身的长度！)
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

                                double starX = (triggerAbsTime + accumulatedRel) * _pixelsPerSecond;
                                DrawTemplateStar(tplCanvas, starX);
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




        private void DrawTemplateStar(Canvas canvas, double x)
        {
            TextBlock star = new TextBlock
            {
                Text = "✦",
                Foreground = Brushes.Gold,
                FontSize = 14,
                FontWeight = FontWeights.Bold
            };
            Canvas.SetLeft(star, x - 6);
            Canvas.SetTop(star, 10);
            canvas.Children.Add(star);
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