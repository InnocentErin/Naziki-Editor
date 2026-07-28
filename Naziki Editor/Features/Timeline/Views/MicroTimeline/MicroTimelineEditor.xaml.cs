using Naziki_Editor.Core.Timeline;
using Naziki_Editor.Core.Timeline.Abstractions;
using Naziki_Editor.Core.Timeline.Models;
using Naziki_Editor.Core.Timeline.Shared;
using Naziki_Editor.Models;
using Naziki_Editor.UI.ViewModels;
using Naziki_Editor.State;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Naziki_Editor.Core;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Common;
using Naziki_Editor.Core.Messaging;
using Naziki_Editor.Core.Storyboard;
using Naziki_Editor.Core.Shortcuts;
using Naziki_Editor.Core.Timeline.Projection;
using Naziki_Editor.Core.Timeline.Editing;
using Naziki_Editor.Core.Timeline.Settings;

namespace Naziki_Editor.Views.MicroTimeline
{
    public partial class MicroTimelineEditor : UserControl, IShortcutAware
    {
        public ShortcutContext ShortcutContext => ShortcutContext.MicroTimeline;
        public bool OnShortcutFocusGained() => true;
        public void OnShortcutFocusLost() { }
        private MicroEditorContext _editorContext;
        private ProjectDataContext _context;
        private double _pixelsPerSecond;
        private double _lastCalculatedMaxTime = 0;
        private double _lastCalculatedEndTime;

        private IMicroTimelineService _microService;
        private IPropertyEditorService _propertyEditorService;
        private IStoryboardRepository _storyboardRepository;
        private readonly IMessageBroker _messageBroker;
        private readonly IDialogService _dialogService;
        private readonly UI.Rendering.NoteVisualEngine _noteVisualEngine;

        private MicroRulerRenderer _microRulerRenderer;
        private TemplateOverlayRenderer _templateManager;

        private Point _panStartPoint;
        private double _panStartOffset;
        private bool _isPanning = false;
        private CancellationTokenSource? _loadCancellation;

        public MicroTimelineEditor()
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

        public MicroTimelineEditor(IMicroTimelineService microService, IMessageBroker messageBroker, IDialogService dialogService, UI.Rendering.NoteVisualEngine noteVisualEngine, IPropertyEditorService propertyEditorService, IStoryboardRepository storyboardRepository) : this()
        {
            _microService = microService;
            _messageBroker = messageBroker;
            _dialogService = dialogService;
            _noteVisualEngine = noteVisualEngine;
            _propertyEditorService = propertyEditorService;
            _storyboardRepository = storyboardRepository;

            if (_microService == null) throw new ArgumentNullException(nameof(microService));
            if (_messageBroker == null) throw new ArgumentNullException(nameof(messageBroker));
            if (_dialogService == null) throw new ArgumentNullException(nameof(dialogService));

            _microRulerRenderer = new MicroRulerRenderer(MicroRulerCanvas, _noteVisualEngine);
            _templateManager = new TemplateOverlayRenderer(null, _messageBroker, _dialogService);
            _messageBroker.Subscribe("Timeline.Command.DetachTemplate", DetachTemplateInstance);
        }

        private void DetachTemplateInstance()
        {
            if (!IsVisible || _editorContext?.Entity == null || _context == null)
                return;

            var settings = AppServices.GetService<ITimelineSettings>().Current;
            if (settings.ConfirmTemplateDetach &&
                _dialogService.ShowConfirm(
                    "将当前事件中的模板实例展开为独立关键帧？解绑后只影响当前事件，并可通过撤销恢复。",
                    "解绑模板实例") != ConfirmResult.Yes)
                return;

            var service = AppServices.GetService<ITemplateInstanceService>();
            AppServices.GetService<IHistoryService>().RecordSnapshot(_context.Storyboard);
            var result = service.DetachInstance(_editorContext.Entity, _context);
            if (!result.Success)
            {
                _dialogService.ShowMessage(result.Error ?? "模板解绑失败。", "解绑失败", DialogMessageType.Error);
                return;
            }

            _messageBroker.Publish("DataModified");
            LoadClipData(_editorContext, _context);
        }

        /// <summary>
        /// 🚀 【数据接线关口】：主轴双击方块后，此方法会被轰轰烈烈地激活！
        /// </summary>
        public async void LoadClipData(MicroEditorContext editorContext, ProjectDataContext context)
        {
            _loadCancellation?.Cancel();
            _loadCancellation?.Dispose();
            _loadCancellation = new CancellationTokenSource();
            _editorContext = editorContext;
            _context = context;
            _pixelsPerSecond = editorContext.InitialPixelsPerSecond;
            ShowLoadingState();
            try
            {
                var factory = AppServices.GetService<IMicroTimelineSessionFactory>();
                var token = _loadCancellation.Token;
                var session = await Task.Run(
                    () => factory.Build(editorContext, context, token),
                    token);
                token.ThrowIfCancellationRequested();
                await RenderSessionAsync(session, token);
                ShowReadyState();
            }
            catch (OperationCanceledException)
            {
                // The owning tab was closed or a newer load superseded this one.
            }
            catch (Exception ex)
            {
                ShowFailureState(ex);
            }
        }

        private async Task RenderSessionAsync(
            MicroTimelineSession session,
            CancellationToken cancellationToken)
        {
            PropHeadersStackPanel.Children.Clear();
            PropTracksStackPanel.Children.Clear();

            _lastCalculatedEndTime = session.EntityProjection.LastStateTime;
            _lastCalculatedMaxTime = session.ContentEndTime;
            var width = Math.Max(200, session.ContentEndTime * _pixelsPerSecond);
            MicroRulerCanvas.Width = width;

            var titleLeft = CreateSectionHeader(
                $"属性轨道 · {session.Tracks.Count} 轨",
                session.EntityProjection.HasErrors ? Brushes.OrangeRed : null);
            var titleRight = CreateSectionHeader(string.Empty, null);
            PropHeadersStackPanel.Children.Add(titleLeft);
            PropTracksStackPanel.Children.Add(titleRight);

            var dependencyMap = session.DependencyGroups.ToDictionary(group => group.SwitchProperty);
            var renderedGroups = new HashSet<string>();
            var groupRows = new Dictionary<string, List<UIElement>>();
            var renderedCount = 0;

            foreach (var track in session.Tracks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var descriptor = track.Descriptor;
                if (descriptor.IsDependencySwitch)
                    continue;

                if (descriptor.DependencyGroup is string groupKey &&
                    dependencyMap.TryGetValue(groupKey, out var group) &&
                    renderedGroups.Add(groupKey))
                {
                    var rows = new List<UIElement>();
                    groupRows[groupKey] = rows;
                    var enabled = TryGetLatestBooleanValue(
                        session.EditorContext.Entity, groupKey, out var current) && current;
                    AddEffectGroupHeader(group, enabled, rows);
                }

                var header = CreatePropertyHeader(descriptor);
                var row = new PropertyTrackControl(
                    _microService, _messageBroker, _dialogService, _propertyEditorService)
                {
                    Width = width,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Tag = descriptor.PropertyName
                };
                row.Init(
                    descriptor.PropertyName,
                    session.EditorContext,
                    _context,
                    _pixelsPerSecond,
                    track.Keyframes);
                PropHeadersStackPanel.Children.Add(header);
                PropTracksStackPanel.Children.Add(row);

                if (descriptor.DependencyGroup is string dependency &&
                    groupRows.TryGetValue(dependency, out var rowsForGroup))
                {
                    var expanded = TryGetLatestBooleanValue(
                        session.EditorContext.Entity, dependency, out var enabled) && enabled;
                    header.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
                    row.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
                    rowsForGroup.Add(header);
                    rowsForGroup.Add(row);
                }

                renderedCount++;
                if (renderedCount % 6 == 0)
                {
                    LoadStateDetails.Text = $"正在创建属性轨道… {renderedCount}/{session.Tracks.Count}";
                    await Dispatcher.Yield(DispatcherPriority.Background);
                }
            }

            _microRulerRenderer.RenderTicks(
                _pixelsPerSecond,
                0,
                session.ContentEndTime,
                session.EntityProjection.BaseStateTime,
                session.EntityProjection.LastStateTime,
                (int)Math.Min(int.MaxValue, width));
            _microRulerRenderer.RenderNoteMarkersCapped(
                _context, _pixelsPerSecond, 0, session.ContentEndTime, 800);

            await Dispatcher.Yield(DispatcherPriority.Loaded);
            var offset = Math.Max(
                0, session.EntityProjection.BaseStateTime * _pixelsPerSecond - 50);
            ScrollPropCanvas.ScrollToHorizontalOffset(offset);
        }

        private Border CreateSectionHeader(string text, Brush? foreground)
        {
            var border = new Border
            {
                Height = 28,
                Background = (Brush)Application.Current.FindResource("MenuBgColor"),
                BorderBrush = (Brush)Application.Current.FindResource("BorderColor"),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            if (!string.IsNullOrEmpty(text))
                border.Child = new TextBlock
                {
                    Text = text,
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = foreground ??
                                 (Brush)Application.Current.FindResource("HighlightColor")
                };
            return border;
        }

        private Border CreatePropertyHeader(PropertyTrackDescriptor descriptor)
        {
            return new Border
            {
                Height = AppServices.GetService<ITimelineSettings>().Current.MicroTrackHeight,
                BorderBrush = (Brush)Application.Current.FindResource("BorderColor"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(descriptor.DependencyGroup == null ? 12 : 24, 0, 0, 0),
                Child = new TextBlock
                {
                    Text = $"{GetTrackKindGlyph(descriptor.Kind)} {descriptor.DisplayName}",
                    Foreground = (Brush)Application.Current.FindResource("MainTextColor"),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
        }

        private void AddEffectGroupHeader(
            PropertyDependencyGroup group,
            bool enabled,
            List<UIElement> rows)
        {
            var button = new Button
            {
                Content = $"{(enabled ? "●" : "○")} {group.DisplayName}  {(enabled ? "已启用" : "已关闭")}  {(enabled ? "▾" : "▸")}",
                Height = 28,
                Padding = new Thickness(10, 0, 4, 0),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = (Brush)Application.Current.FindResource("MenuBgColor"),
                Foreground = enabled
                    ? Brushes.MediumSeaGreen
                    : (Brush)Application.Current.FindResource("SecTextColor"),
                BorderBrush = (Brush)Application.Current.FindResource("BorderColor"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Tag = enabled
            };
            var right = CreateSectionHeader(string.Empty, null);
            button.Click += (_, _) =>
            {
                var expanded = !(bool)button.Tag;
                button.Tag = expanded;
                button.Content = $"{(enabled ? "●" : "○")} {group.DisplayName}  {(enabled ? "已启用" : "已关闭")}  {(expanded ? "▾" : "▸")}";
                foreach (var row in rows)
                    row.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            };
            PropHeadersStackPanel.Children.Add(button);
            PropTracksStackPanel.Children.Add(right);
        }

        private void LoadClipDataCore(
            MicroEditorContext editorContext,
            ProjectDataContext context,
            CancellationToken cancellationToken)
        {
            _editorContext = editorContext;
            _context = context;

            if (_editorContext == null)
            {
                _dialogService?.ShowMessage("无法加载微观编辑器：上下文数据为空。", "加载失败", DialogMessageType.Error);
                return;
            }
            if (_editorContext.Entity == null)
            {
                _dialogService?.ShowMessage("无法加载微观编辑器：事件实体为空。", "加载失败", DialogMessageType.Error);
                return;
            }

            _pixelsPerSecond = editorContext.InitialPixelsPerSecond;
            cancellationToken.ThrowIfCancellationRequested();

            _microService.SetContext(context);

            PropHeadersStackPanel.Children.Clear();
            PropTracksStackPanel.Children.Clear();

            if (_editorContext.Entity == null) return;

            // ==========================================
            // ✨ 宇宙测绘：寻找主时间轴的尽头，以及最后一个关键帧的时间！
            // ==========================================
            double maxTime = 10;
            if (_context.Chart?.note_list != null && _context.Chart.note_list.Count > 0)
                maxTime = _context.TimeEngine.TickToSeconds(_context.Chart.note_list[_context.Chart.note_list.Count - 1].tick) + 5;

            double lastFrameAbs = new TimelineProjectionService()
                .BuildEntityProjection(_editorContext.Entity, _context)
                .LastStateTime;

            // 🚀 【性能防爆屏障】：如果计算出的结束时间是未初始化的极大值（如 float.MaxValue），直接强制截断！
            // 防止后续生成宽度达到几百万像素的标尺，导致 WPF 测算引擎疯狂触发 SetValue 死循环！
            if (lastFrameAbs > 10000) lastFrameAbs = maxTime;

            // 联动修正：确保方块模型内存里的 EndTime 与核心同步刷新
            _lastCalculatedEndTime = lastFrameAbs;
            if (lastFrameAbs + 5 > maxTime) maxTime = lastFrameAbs + 5;

            _lastCalculatedMaxTime = maxTime;
            double targetPhysicalWidth = maxTime * _pixelsPerSecond;

            MicroRulerCanvas.Width = targetPhysicalWidth;

            // 3. 🔬 【智能门派拆分】：利用 Cytoid 强类型反射，分发不同的微观属性轨道！

            // ==========================================
            // ✨ 3. 🔬 【全量展开】：无条件显示所有支持的动画轨道！
            // ==========================================
            var propertyCatalog = AppServices.GetService<IPropertyMetadataCatalog>();
            var propertyDescriptors = propertyCatalog.Discover(_editorContext.Entity);
            var supportedProperties = propertyDescriptors.Select(item => item.PropertyName).ToList();

            // ==========================================
            // 🌟 4. 动态构建【多级分身：模板只读轨道组】
            // ==========================================
            var baseState = _editorContext.Entity.GetBaseState();
            var keyframes = _editorContext.Entity.GetKeyframes();

            keyframes ??= new List<ObjectState>();

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
            var templateBoxes = _microService.DecodeKeyframes(
                _editorContext.Entity, "Template", _editorContext.MacroStartTime);

            // ✨ 【核心升级】：先按时间排序，然后用 GroupBy 强行把名字一样的模板合并到同一个维度里！
            var sortedTriggers = templateBoxes.Where(b => b.Value != null && !string.IsNullOrEmpty(b.Value.ToString()))
                                              .OrderBy(b => b.VisualRelTime).ToList();
            var groupedTemplates = sortedTriggers.GroupBy(b => b.Value.ToString()).ToList();

            foreach (var group in groupedTemplates)
            {
                cancellationToken.ThrowIfCancellationRequested();
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
                    var mainEntity = _editorContext.Entity;
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
                        double triggerAbsTime = _editorContext.MacroStartTime + triggerBox.VisualRelTime;
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
                    LoadClipData(_editorContext, _context);
                    _dialogService.ShowMessage($"✨ [{tName}] 的 {group.Count()} 次调用已全数降维剥离！", "批量解绑成功");
                };
                tplHeaderGrid.Children.Add(unbindBtn);
                tplHeaderLeft.Child = tplHeaderGrid;
                PropHeadersStackPanel.Children.Add(tplHeaderLeft);

                Border tplHeaderRight = new Border { Height = 28, Background = headerBgBrush, BorderBrush = (Brush)Application.Current.FindResource("BorderColor"), BorderThickness = new Thickness(0, 0, 0, 1) };
                PropTracksStackPanel.Children.Add(tplHeaderRight);

                // E. 盖楼：模板内部时空延展轨 (轨道上可以画出好几次触发的星星了！)
                var microTrackHeight = AppServices.GetService<ITimelineSettings>().Current.MicroTrackHeight;
                Border tplTrackLeft = new Border { Height = microTrackHeight, BorderBrush = (Brush)Application.Current.FindResource("BorderColor"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(20, 0, 0, 0) };
                tplTrackLeft.Child = new TextBlock { Text = $"✦ 共享轨", Foreground = Brushes.DarkKhaki, FontSize = 10, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
                PropHeadersStackPanel.Children.Add(tplTrackLeft);

                Border tplTrackRight = new Border { Height = microTrackHeight, BorderBrush = (Brush)Application.Current.FindResource("BorderColor"), BorderThickness = new Thickness(0, 0, 0, 1) };
                Canvas tplCanvas = new Canvas { Width = targetPhysicalWidth, IsHitTestVisible = false };

                // ✨ 在同一条轨道上，遍历组内的每一次触发，点亮星星矩阵！
                if (tData != null)
                {
                    foreach (var triggerBox in group)
                    {
                        double triggerAbsTime = _editorContext.MacroStartTime + triggerBox.VisualRelTime;
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

            // 6. 🧵 机械化流水线：批量手绘每一个普通属性的"表头 + 关键帧长轨"
            var descriptorByName = propertyDescriptors.ToDictionary(item => item.PropertyName);
            var effectGroups = propertyCatalog.DependencyGroups.ToDictionary(item => item.SwitchProperty);
            var effectRows = new Dictionary<string, List<UIElement>>();
            var renderedEffectHeaders = new HashSet<string>();

            foreach (string prop in mainAnimatedProps) // ✨ 优化：只画真正被改动过的属性！
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!descriptorByName.TryGetValue(prop, out var descriptor))
                    continue;

                // Effect switches live in their group header and do not consume a track.
                if (descriptor.IsDependencySwitch)
                    continue;

                if (descriptor.DependencyGroup is string groupKey &&
                    effectGroups.TryGetValue(groupKey, out var effectGroup) &&
                    renderedEffectHeaders.Add(groupKey))
                {
                    var groupRows = new List<UIElement>();
                    effectRows[groupKey] = groupRows;
                    var isEnabled = TryGetLatestBooleanValue(
                        _editorContext.Entity, effectGroup.SwitchProperty, out var enabled) && enabled;
                    var expandButton = new Button
                    {
                        Content = $"{(isEnabled ? "●" : "○")} {effectGroup.DisplayName}  {(isEnabled ? "已启用" : "已关闭")}  {(isEnabled ? "▾" : "▸")}",
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Padding = new Thickness(10, 0, 4, 0),
                        Height = 28,
                        Background = (Brush)Application.Current.FindResource("MenuBgColor"),
                        Foreground = isEnabled ? Brushes.MediumSeaGreen : (Brush)Application.Current.FindResource("SecTextColor"),
                        BorderBrush = (Brush)Application.Current.FindResource("BorderColor"),
                        BorderThickness = new Thickness(0, 0, 0, 1),
                        Tag = isEnabled
                    };
                    var rightHeader = new Border
                    {
                        Height = 28,
                        Background = (Brush)Application.Current.FindResource("MenuBgColor"),
                        BorderBrush = (Brush)Application.Current.FindResource("BorderColor"),
                        BorderThickness = new Thickness(0, 0, 0, 1)
                    };
                    expandButton.Click += (_, _) =>
                    {
                        var expanded = !(bool)expandButton.Tag;
                        expandButton.Tag = expanded;
                        expandButton.Content = $"{(isEnabled ? "●" : "○")} {effectGroup.DisplayName}  {(isEnabled ? "已启用" : "已关闭")}  {(expanded ? "▾" : "▸")}";
                        foreach (var row in groupRows)
                            row.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
                    };
                    PropHeadersStackPanel.Children.Add(expandButton);
                    PropTracksStackPanel.Children.Add(rightHeader);
                }

                // A. 左侧：纯净的属性名文字边框 (同样加入 20 缩进，体现组级父子关系)
                Border headerBorder = new Border
                {
                    Height = AppServices.GetService<ITimelineSettings>().Current.MicroTrackHeight,
                    BorderBrush = (Brush)Application.Current.FindResource("BorderColor"),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(20, 0, 0, 0)
                };
                TextBlock headerText = new TextBlock
                {
                    Text = $"{GetTrackKindGlyph(descriptor.Kind)} {descriptor.DisplayName}",
                    Foreground = (Brush)Application.Current.FindResource("MainTextColor"),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeights.SemiBold
                };
                headerBorder.Child = headerText;
                PropHeadersStackPanel.Children.Add(headerBorder);

                // B. 右侧：降临单属性关键帧格线行！
                PropertyTrackControl trackRow = new PropertyTrackControl(_microService, _messageBroker, _dialogService, _propertyEditorService);
                trackRow.Width = targetPhysicalWidth;
                trackRow.HorizontalAlignment = HorizontalAlignment.Left;

                // 把当前的属性名字塞进 Tag 里，让它拥有记忆！
                trackRow.Tag = prop;

                trackRow.Init(prop, _editorContext, _context, _pixelsPerSecond);
                PropTracksStackPanel.Children.Add(trackRow);

                if (descriptor.DependencyGroup is string dependencyGroup &&
                    effectRows.TryGetValue(dependencyGroup, out var rows))
                {
                    // Disabled effects start collapsed; enabled effects open automatically.
                    var startsExpanded = TryGetLatestBooleanValue(
                        _editorContext.Entity, dependencyGroup, out var enabled) && enabled;
                    headerBorder.Visibility = startsExpanded ? Visibility.Visible : Visibility.Collapsed;
                    trackRow.Visibility = startsExpanded ? Visibility.Visible : Visibility.Collapsed;
                    rows.Add(headerBorder);
                    rows.Add(trackRow);
                }
            }

            RenderMicroRulerTicks(maxTime);

            // ==========================================
            // 🚀 核心跳转：UI 就绪后，让摄像机光速飞向方块所在的时间节点！
            // ==========================================
            Application.Current.Dispatcher.BeginInvoke(new Action(() => {
                double targetOffset = _editorContext.MacroStartTime * _pixelsPerSecond - 50; // 往前看 50 像素的余量
                if (targetOffset < 0) targetOffset = 0;
                ScrollPropCanvas.ScrollToHorizontalOffset(targetOffset);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        public void CancelPendingLoad()
        {
            _loadCancellation?.Cancel();
        }

        private void ShowLoadingState()
        {
            LoadStateOverlay.Visibility = Visibility.Visible;
            LoadStateTitle.Text = "正在加载微观时间轴…";
            LoadStateDetails.Text = _editorContext?.DisplayName ?? string.Empty;
            RetryLoadButton.Visibility = Visibility.Collapsed;
        }

        private void ShowReadyState()
        {
            LoadStateOverlay.Visibility = Visibility.Collapsed;
        }

        private void ShowFailureState(Exception exception)
        {
            LoadStateOverlay.Visibility = Visibility.Visible;
            LoadStateTitle.Text = "微观时间轴加载失败";
            LoadStateDetails.Text = $"{exception.Message}\n事件：{_editorContext?.Entity?.Id ?? "未知"}";
            RetryLoadButton.Visibility = Visibility.Visible;
        }

        private void RetryLoadButton_Click(object sender, RoutedEventArgs e)
        {
            if (_editorContext != null && _context != null)
                LoadClipData(_editorContext, _context);
        }

        private static string GetTrackKindGlyph(PropertyTrackKind kind) => kind switch
        {
            PropertyTrackKind.BooleanSegments => "▰",
            PropertyTrackKind.ContinuousNumeric => "⌁",
            PropertyTrackKind.ColorSteps => "◈",
            PropertyTrackKind.DiscreteSteps => "▮",
            _ => "◇"
        };

        private bool TryGetLatestBooleanValue(
            IStoryboardEntity entity,
            string propertyName,
            out bool value)
        {
            value = false;
            var found = false;
            if (_propertyEditorService.TryGetValue(entity.GetBaseState(), propertyName, out var baseValue) &&
                baseValue is bool baseBoolean)
            {
                value = baseBoolean;
                found = true;
            }
            foreach (var state in entity.GetKeyframes() ?? Array.Empty<object>())
            {
                if (_propertyEditorService.TryGetValue(state, propertyName, out var stateValue) &&
                    stateValue is bool stateBoolean)
                {
                    value = stateBoolean;
                    found = true;
                }
            }
            return found;
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

                // 核心：推算鼠标正下方指向的"绝对时间"
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
                    else if (el is PropertyTrackControl trackRow)
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
            _microRulerRenderer.FastUpdateZoom(_pixelsPerSecond, 0, _lastCalculatedMaxTime,
                _editorContext.MacroStartTime, _lastCalculatedEndTime, (int)newPhysicalWidth);

            // Handle note visual elements (C2Note tags) - not covered by MicroRulerRenderer
            foreach (UIElement child in MicroRulerCanvas.Children)
            {
                if (child is FrameworkElement fe && fe.Tag is Models.C2Note note)
                {
                    double seconds = _context.TimeEngine.TickToSeconds(note.tick);
                    double absoluteX = seconds * _pixelsPerSecond;

                    if (child is Image img)
                    {
                        Canvas.SetLeft(img, absoluteX - (img.Width / 2.0));
                    }
                    else if (child is TextBlock)
                    {
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
            _microRulerRenderer.RenderNoteMarkers(_context, _pixelsPerSecond, 0, maxTime);

            // ✨ 2. 委托给专业渲染引擎绘制标尺刻度与蓝色高亮区
            _microRulerRenderer.RenderTicks(_pixelsPerSecond, 0, maxTime,
                _editorContext.MacroStartTime, _lastCalculatedEndTime, (int)(maxTime * _pixelsPerSecond));
        }
    }
}

