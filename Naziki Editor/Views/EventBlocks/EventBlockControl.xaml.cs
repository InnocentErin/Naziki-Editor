using Naziki_Editor.Core;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Chart;
using Naziki_Editor.Core.Messaging;
using Naziki_Editor.Core.Timeline.Shared;
using Naziki_Editor.Core.Timeline.EventBlocks.Abstractions;
using Naziki_Editor.Core.Timeline.EventBlocks.Services;
using Naziki_Editor.Models;
using Naziki_Editor.UI.ViewModels;
using Naziki_Editor.State;
using Naziki_Editor.Views.EventBlocks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Naziki_Editor.Views
{
    public partial class EventBlockControl : UserControl
    {
        // ==========================================
        // 🌟 核心接入管道与上下文状态锁
        // ==========================================
        private EventBlockViewModel _model;
        private ProjectDataContext _context;
        private double _pixelsPerSecond = 100.0;
        private IEventBlockService _clipService;
        private INoteSelectorService _noteSelectorService;
        private readonly IMessageBroker _messageBroker;
        private readonly IDialogService _dialogService;
        private UI.Rendering.NoteVisualEngine _noteVisualEngine;

        private ClipDragHandler _dragHandler;
        private ClipResizeHandler _resizeHandler;

        // ✨ 追加：宏观跨轨道换层多态通信协议
        public enum MacroDragStage { Started, Moving, Completed }
        public event Action<EventBlockControl, MouseEventArgs, MacroDragStage> OnMacroGridDrag;


        // ✨ 追加：轨道感知与父级通讯枢纽
        public int CurrentTrackIndex { get; private set; }
        public int MaxTrackIndex { get; set; }

        public event Action<EventBlockControl, int> OnTrackIndexChanged; // 换轨通知
        public event Action<EventBlockControl> OnRequestNewTrack;        // 越界修路请求
        public event Action<EventBlockViewModel> OnRequestDetailedEditMode;  // 双击进入微观世界
        public event Action<EventBlockViewModel> OnClipSelected;             // 单点选中反射信号
        
        public event Action<EventBlockViewModel> OnRequestPropertyEditor;    // 请求打开属性编辑器的独立信号

        // ==========================================
        // 🔧 Internal accessors for handler classes
        // ==========================================
        public EventBlockViewModel Model => _model;
        public double PixelsPerSecond => _pixelsPerSecond;

        internal void ReleaseMouseCaptureInternal() => RootGrid.ReleaseMouseCapture();
        internal void CaptureMouseInternal() => RootGrid.CaptureMouse();
        internal void SetOpacityInternal(double opacity) => ClipBackground.Opacity = opacity;
        internal void InvokeRequestDetailedEdit(EventBlockViewModel model) => OnRequestDetailedEditMode?.Invoke(model);
        internal void InvokeClipSelected(EventBlockViewModel model) => OnClipSelected?.Invoke(model);
        internal void InvokeMacroGridDrag(MouseEventArgs e, MacroDragStage stage) => OnMacroGridDrag?.Invoke(this, e, stage);
        internal void InvokeMarkAsModified() => _context?.MarkAsModified();
        internal void InvokeEvaluateValidationWarning() => EvaluateValidationWarning();
        internal void InvokeUpdateXPositionAndWidth() => UpdateXPositionAndWidth();
        internal void PublishMessageInternal(string key, object payload) => _messageBroker?.Publish(key, payload);


        public EventBlockControl()
        {
            InitializeComponent();
            _clipService = new EventBlockService();
        }

        public EventBlockControl(IMessageBroker messageBroker, IDialogService dialogService, UI.Rendering.NoteVisualEngine noteVisualEngine, IEventBlockService clipService) : this()
        {
            _messageBroker = messageBroker;
            _dialogService = dialogService;
            _noteVisualEngine = noteVisualEngine;
            _clipService = clipService;
            _dragHandler = new ClipDragHandler(this, _clipService);
            _resizeHandler = new ClipResizeHandler(this, _clipService);

            // Wire up resize thumb events through the resize handler
            ResizeLeftThumb.DragStarted += (s, ev) => { _resizeHandler.OnResizeLeftStarted(_model); };
            ResizeLeftThumb.DragCompleted += (s, ev) => { _resizeHandler.OnResizeLeftCompleted(_model); };
            ResizeRightThumb.DragStarted += (s, ev) => { _resizeHandler.OnResizeRightStarted(_model); };
            ResizeRightThumb.DragCompleted += (s, ev) => { _resizeHandler.OnResizeRightCompleted(_model); };
        }
        private void DrawDiscreteRipples()
        {
            if (!(_model.AssociatedObject is C2NoteController noteCtrl)) return;

            // 🔍 1. 提取 NoteTarget 目标参数
            var targetProp = noteCtrl.BaseState?.GetType().GetProperty("NoteTarget");
            object targetObj = targetProp?.GetValue(noteCtrl.BaseState);
            if (targetObj == null || _context?.Chart == null) return;

            string targetStr = targetObj.ToString().Trim();
            var selector = _noteSelectorService.ParseSelector(targetStr);
            if (selector == null) return;

            // 🔬 2. 使用 Core 层音符选择器服务过滤音符
            var matchedNotes = _noteSelectorService.SelectNotes(_context.Chart, selector);
            if (matchedNotes.Count == 0) return; // 没匹配到，保持方块安静

            // 📐 3. 绘制高亮背景区间 (时空边界结界！)
            var (minSec, maxSec) = _noteSelectorService.GetMatchedTimeRange(_context.Chart, selector, _context.TimeEngine);

            double startX = minSec * _pixelsPerSecond;
            double endX = maxSec * _pixelsPerSecond;
            double width = endX - startX;

            // ✨ 空间膨胀修复：如果是单点音符，宽度接近 0。
            // 我们强行把结界撑开，给它一个 24 像素的华丽底座，并让它完美居中！
            if (width < 20)
            {
                double center = startX + (width / 2.0);
                width = 24;
                startX = center - 12;
            }

            var highlightRect = new System.Windows.Shapes.Rectangle
            {
                Width = width,
                Height = 36,
                Fill = new SolidColorBrush(Color.FromArgb(40, 135, 206, 250)), // 半透明天蓝结界
                Stroke = (Brush)Application.Current.FindResource("HighlightBorderColor") ?? Brushes.LightSkyBlue,
                StrokeThickness = 1,
                RadiusX = 4,
                RadiusY = 4,
                IsHitTestVisible = false // 绝不阻挡底层的操作
            };
            Canvas.SetLeft(highlightRect, startX);
            Canvas.SetTop(highlightRect, 2);
            NodeCanvas.Children.Add(highlightRect);

            // 🎵 5. 终极偷天换日投影：召唤底部音符刻度工厂！
            var subCanvas = new Canvas { IsHitTestVisible = false };
            _noteVisualEngine.RenderNoteRuler(subCanvas, matchedNotes, _context.TimeEngine, _pixelsPerSecond, false);
            NodeCanvas.Children.Add(subCanvas);
        }



        /// <summary>
        /// 📥 唯一交接关口：由父轨道将强类型模型、上下文基站以及缩放比例喂给方块
        /// </summary>
        public void Init(EventBlockViewModel model, ProjectDataContext context, double pixelsPerSecond, int trackIndex, int maxTrackIndex, UI.Rendering.NoteVisualEngine noteVisualEngine = null)
        {
            _model = model;
            _context = context;
            _pixelsPerSecond = pixelsPerSecond;
            _noteVisualEngine = noteVisualEngine;
            _clipService.SetContext(context);
            _clipService.SetPixelsPerSecond(pixelsPerSecond);
            _noteSelectorService = new NoteSelectorService();

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

            // 5. 画出音符雷达波纹！
            if (_model.AssociatedObject is C2NoteController)
            {
                DrawDiscreteRipples();
            }
        }

        private void UpdateXPositionAndWidth()
        {
            // 🌟 如果是神明级控制器，把排版全权交给父级 TimelineControl！自己不准计算！
            bool isGlobalController = (_model.AssociatedObject is C2SceneController || _model.AssociatedObject is C2NoteController) && string.IsNullOrEmpty(_model.AssociatedObject.TargetId);
            if (isGlobalController) return;

            // 初始化我们的核心时空像素转换官
            var coordEngine = new Core.Timeline.Shared.TimelineCoordEngine(_pixelsPerSecond);
            double left = coordEngine.TimeToX(_model.StartTime);
            Canvas.SetLeft(this, left);

            // ✨ 轨道高度吸附：方块自我校准 Y 坐标
            Canvas.SetTop(this, 6);

            // 零时长事件保留最小命中宽度，但不伪造其语义时长。
            if (_model.EndTime <= _model.StartTime)
            {
                Width = AppServices.GetService<Core.Timeline.Settings.ITimelineSettings>().Current.ZeroDurationMarkerWidth;
                VirtualEndLine.Visibility = Visibility.Collapsed;
            }
            else
            {
                double width = coordEngine.TimeToX(_model.EndTime - _model.StartTime);
                this.Width = Math.Max(
                    AppServices.GetService<Core.Timeline.Settings.ITimelineSettings>().Current.ZeroDurationMarkerWidth,
                    width);
                VirtualEndLine.Visibility = Visibility.Collapsed;
            }

            ToolTip = _model.HasTimeError
                ? string.Join(Environment.NewLine, _model.TimeDiagnostics)
                : $"{_model.StartTime:0.###}s – {_model.EndTime:0.###}s";
        }

        private void InspectEntityGenetics()
        {
            if (_model.AssociatedObject == null) return;

            bool isMacro = false;
            dynamic baseState = _model.AssociatedObject.GetBaseState();
            if (baseState != null)
            {
                try
                {
                    string rawTime = baseState.Time?.ToString() ?? "";
                    if (rawTime.Contains("$note")) isMacro = true;
                }
                catch { }
            }

            // 🌟 【新增基因判定】：神明级全局控制器（不带 TargetId 的场景/音符控制器）
            bool isGlobalController = (_model.AssociatedObject is C2SceneController || _model.AssociatedObject is C2NoteController) && string.IsNullOrEmpty(_model.AssociatedObject.TargetId);

            if (isMacro)
            {
                // 🔒 【边缘封印】：禁止左右边缘拉伸，并在角标打上 ♪ 烙印
                ResizeLeftThumb.Visibility = Visibility.Collapsed;
                ResizeRightThumb.Visibility = Visibility.Collapsed;
                TxtModeIcon.Text = "♪";
                DashBorderShape.Stroke = (Brush)Application.Current.FindResource("HighlightBorderColor") ?? Brushes.DodgerBlue;
                DashBorderShape.StrokeDashArray = new DoubleCollection() { 3, 2 };
                ClipBackground.BorderBrush = Brushes.Transparent;
            }
            else if (isGlobalController)
            {
                // 🌟 【手术 B 锁定】：彻底没收控制器的左右伸缩把手！
                ResizeLeftThumb.Visibility = Visibility.Collapsed;
                ResizeRightThumb.Visibility = Visibility.Collapsed;
                TxtModeIcon.Text = _model.AssociatedObject is C2SceneController ? "🎛️" : "🎵";
                DashBorderShape.Stroke = Brushes.Transparent;
                DashBorderShape.StrokeDashArray = null;
                // 让它看起来像一条实心的无敌轨！
                ClipBackground.BorderBrush = (Brush)Application.Current.FindResource("HighlightBorderColor") ?? Brushes.DodgerBlue;
            }
            else
            {
                // 🌟 恢复普通方块的把手显示
                ResizeLeftThumb.Visibility = Visibility.Visible;
                ResizeRightThumb.Visibility = Visibility.Visible;
                TxtModeIcon.Text = "⏱";
                DashBorderShape.Stroke = Brushes.Transparent;
                DashBorderShape.StrokeDashArray = null;
                ClipBackground.BorderBrush = (Brush)Application.Current.FindResource("HighlightBorderColor") ?? Brushes.DodgerBlue;
            }
        }

        private void EvaluateValidationWarning()
        {
            var documentDiagnostics = _model.AssociatedObject?.AllDiagnostics() ?? Array.Empty<StoryboardDiagnostic>();
            if (documentDiagnostics.Count > 0)
            {
                ClipBackground.BorderBrush = Brushes.Crimson;
                DashBorderShape.Stroke = Brushes.Crimson;
                DashBorderShape.StrokeDashArray = new DoubleCollection { 3, 2 };
                TxtModeIcon.Text = "⚠";
                if (documentDiagnostics.Any(item =>
                        item.Severity == StoryboardDiagnosticSeverity.Error))
                {
                    ResizeLeftThumb.Visibility = Visibility.Collapsed;
                    ResizeRightThumb.Visibility = Visibility.Collapsed;
                }
                ToolTip = string.Join(Environment.NewLine,
                    documentDiagnostics.Select(item => $"{item.Path}: {item.Message}"));
                return;
            }
            if (_model.HasTimeError)
            {
                ClipBackground.BorderBrush = Brushes.Crimson;
                DashBorderShape.Stroke = Brushes.Crimson;
                DashBorderShape.StrokeDashArray = new DoubleCollection { 3, 2 };
                TxtModeIcon.Text = "⚠";
                ResizeLeftThumb.Visibility = Visibility.Collapsed;
                ResizeRightThumb.Visibility = Visibility.Collapsed;
                ToolTip = string.Join(Environment.NewLine, _model.TimeDiagnostics);
                return;
            }

            ApplyThematicColors();
            this.ToolTip = _model.DisplayName;
        }

        // ==========================================
        // 🔮 交互核心：宏观移动与边缘微调
        // ==========================================
        private void ClipBackground_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _dragHandler.OnMouseDown(e, _model, _pixelsPerSecond);
        }

        private void ClipBackground_MouseMove(object sender, MouseEventArgs e)
        {
            _dragHandler.OnMouseMove(e, _model, _pixelsPerSecond);
        }

        private void ClipBackground_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _dragHandler.OnMouseUp(e, _model, _pixelsPerSecond);
        }



        // 左右边缘伸缩处理器
        private void ResizeLeft_DragDelta(object sender, DragDeltaEventArgs e)
        {
            _resizeHandler.OnResizeLeftDelta(e, _model, _pixelsPerSecond);
        }

        private void ResizeRight_DragDelta(object sender, DragDeltaEventArgs e)
        {
            _resizeHandler.OnResizeRightDelta(e, _model, _pixelsPerSecond);
        }

        // ==========================================
        // ✨ 右键菜单：一键重新锚定至最近音符算法 (5.3.2 节落地)
        // ==========================================
        private void MenuReanchor_Click(object sender, RoutedEventArgs e)
        {
            ClipContextMenu.HandleReanchor(_model, _context, _dialogService);
        }


        // ==========================================
        // ✨ 右键菜单：精准锁定最后一个关键帧，并悄悄把它的 Destroy 属性设为 true
        // ==========================================
        private void MenuDestroyAtLastFrame_Click(object sender, RoutedEventArgs e)
        {
            ClipContextMenu.HandleDestroyAtLastFrame(_model, _context, _messageBroker);
        }


        // ==========================================
        // 🎨 主题色彩适配器：根据不同的 AssociatedObject 类型，自动切换方块的背景与边框颜色资源
        // ==========================================
        private void ApplyThematicColors()
        {
            ClipThemeAdapter.ApplyTheme(ClipBackground, _model.AssociatedObject);
        }
    }
}



