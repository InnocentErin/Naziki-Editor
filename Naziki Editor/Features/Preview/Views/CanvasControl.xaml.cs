using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Newtonsoft.Json;
using Naziki_Editor.Models;
using Naziki_Editor.Core;
using Naziki_Editor.State;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Shortcuts;
using System.Linq;
using System.Collections;
using System.Threading;
using System.Windows.Threading;
using Naziki_Editor.Features.Preview;
using Naziki_Editor.Features.Project.Resources;
using Naziki_Editor.Features.Audio.Playback;

namespace Naziki_Editor.Views
{
    public partial class CanvasControl : UserControl, IShortcutAware
    {
        public ShortcutContext ShortcutContext => ShortcutContext.Canvas;
        public bool OnShortcutFocusGained() => true;
        public void OnShortcutFocusLost() { }

        public Action<StoryboardRoot> OnApplyJsonSuccess;
        public Func<bool> OnBeforeActionCheckConflict;

        private bool _isRefreshing = false;
        public bool HasUnappliedChanges { get; set; } = false;
        private object _lastSelectedObject;
        private bool _isGlobalPreviewMode = false;
        public ProjectDataContext Context { get; private set; }
        private IStoryboardDocumentReader StoryboardReader => AppServices.GetService<IStoryboardDocumentReader>();
        private IStoryboardDocumentWriter StoryboardWriter => AppServices.GetService<IStoryboardDocumentWriter>();
        private IStoryboardDocumentValidator StoryboardValidatorService => AppServices.GetService<IStoryboardDocumentValidator>();
        private readonly UnityPreviewHwndHost _unityHost;
        private readonly IUnityPreviewSessionService _previewSession;
        private readonly IStoryboardPreviewHost _previewHost;
        private readonly IPreviewDiagnosticsService _previewDiagnostics;
        private readonly IPreviewPlaybackController _previewPlayback;
        private readonly IPlaybackCoordinator _playback;
        private readonly ISettingsStore _settings;
        private readonly ILoadingService _loading;
        private readonly IDialogService _dialogService;
        private string _aspectRatio = "16:9";
        private bool _reloadInProgress;
        private CancellationTokenSource _resizeCancellation = new();
        private bool _nativePreviewOpened;
        private readonly SemaphoreSlim _openPreviewGate = new(1, 1);

        public void LoadContext(ProjectDataContext context)
        {
            Context = context;
            if (_unityHost.HostHandle != IntPtr.Zero)
                _ = OpenNativePreviewAsync(_unityHost.HostHandle);
        }

        private void BtnPreviewGlobal_Click(object sender, RoutedEventArgs e)
        {
            _isGlobalPreviewMode = true;
            _lastSelectedObject = null;
            RefreshJsonView();
        }

        public CanvasControl()
        {
            InitializeComponent();
            _previewSession = AppServices.GetService<IUnityPreviewSessionService>();
            _previewHost = AppServices.GetService<IStoryboardPreviewHost>();
            _previewDiagnostics = AppServices.GetService<IPreviewDiagnosticsService>();
            _previewPlayback = AppServices.GetService<IPreviewPlaybackController>();
            _playback = AppServices.GetService<IPlaybackCoordinator>();
            _settings = AppServices.GetService<ISettingsStore>();
            _loading = AppServices.GetService<ILoadingService>();
            _dialogService = AppServices.GetService<IDialogService>();
            _loading.Register(this, LoadingOverlay);
            _aspectRatio = PreviewSettingsProvider.ParseAspectRatio(
                _settings.Get("Editor.PreviewAspectRatio", "16:9"));
            _unityHost = new UnityPreviewHwndHost();
            _unityHost.HorizontalAlignment = HorizontalAlignment.Center;
            _unityHost.VerticalAlignment = VerticalAlignment.Center;
            _unityHost.HostHandleCreated += UnityHost_HandleCreated;
            UnityHostContainer.Children.Add(_unityHost);
            _previewHost.Attach(
                AppServices.GetService<IStoryboardPreviewDataSource>(),
                AppServices.GetService<IStoryboardChangeFeed>());
            _previewDiagnostics.Changed += PreviewDiagnostics_Changed;
            Loaded += (_, _) =>
            {
                UpdateAspectButtons();
                ApplyAspectRatioLayout();
            };
        }

        private async void UnityHost_HandleCreated(object? sender, IntPtr handle)
        {
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            if (!IsLoaded || CanvasTabControl.SelectedIndex != 0 ||
                UnityHostContainer.ActualWidth < 32 || UnityHostContainer.ActualHeight < 32)
                return;
            await OpenNativePreviewAsync(handle);
        }

        private async Task OpenNativePreviewAsync(IntPtr handle)
        {
            if (_nativePreviewOpened || handle == IntPtr.Zero)
                return;
            await _openPreviewGate.WaitAsync();
            if (_nativePreviewOpened)
            {
                _openPreviewGate.Release();
                return;
            }
            using var loading = _loading.Begin(this, "请稍等，正在加载预览");
            try
            {
                ApplyAspectRatioLayout();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                var (width, height) = GetPreviewPixelSize();
                if (width < 32 || height < 32)
                    return;
                await _previewSession.AttachWindowAsync(handle, width, height);
                _nativePreviewOpened = true;
                if (Context is not null)
                {
                    var initialTime = Math.Max(0, Context.ProjectData?.LastTimelinePosition ?? 0);
                    _playback.Pause();
                    _playback.Seek(initialTime);
                    await _previewSession.OpenProjectAsync(Context, initialTime);
                }
                PreviewDiagnostics_Changed(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                TxtPreviewState.Text = "原生预览初始化失败：" + ex.Message;
                TxtPreviewState.Foreground = new SolidColorBrush(Colors.OrangeRed);
                BtnRetryPreview.Visibility = Visibility.Visible;
            }
            finally
            {
                _openPreviewGate.Release();
            }
        }

        private async void UnityHostContainer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyAspectRatioLayout();
            _resizeCancellation.Cancel();
            _resizeCancellation.Dispose();
            _resizeCancellation = new CancellationTokenSource();
            var cancellation = _resizeCancellation.Token;
            try
            {
                await Task.Delay(100, cancellation);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render, cancellation);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (_unityHost.HostHandle == IntPtr.Zero || !IsVisible ||
                UnityHostContainer.ActualWidth < 32 || UnityHostContainer.ActualHeight < 32)
                return;
            var (width, height) = GetPreviewPixelSize();
            if (!_nativePreviewOpened)
                await OpenNativePreviewAsync(_unityHost.HostHandle);
            else
                await _previewSession.ResizeAsync(width, height, CanvasTabControl.SelectedIndex == 0);
        }

        private (int Width, int Height) GetPreviewPixelSize()
        {
            var dpi = VisualTreeHelper.GetDpi(UnityHostContainer);
            return (
                Math.Max(1, (int)Math.Round(_unityHost.ActualWidth * dpi.DpiScaleX)),
                Math.Max(1, (int)Math.Round(_unityHost.ActualHeight * dpi.DpiScaleY)));
        }

        private void ApplyAspectRatioLayout()
        {
            if (UnityHostContainer is null || _unityHost is null)
                return;
            var availableWidth = Math.Max(1, UnityHostContainer.ActualWidth);
            var availableHeight = Math.Max(1, UnityHostContainer.ActualHeight);
            var ratio = _aspectRatio switch
            {
                "4:3" => 4d / 3d,
                "21:9" => 21d / 9d,
                _ => 16d / 9d
            };
            var width = Math.Min(availableWidth, availableHeight * ratio);
            _unityHost.Width = Math.Max(1, width);
            _unityHost.Height = Math.Max(1, width / ratio);
        }

        private async void AspectRatioButton_Click(object sender, RoutedEventArgs e)
        {
            if (_reloadInProgress || sender is not Button { Tag: string ratio })
                return;
            var previousRatio = _aspectRatio;
            var nextRatio = PreviewSettingsProvider.ParseAspectRatio(ratio);
            if (string.Equals(previousRatio, nextRatio, StringComparison.Ordinal))
                return;

            _reloadInProgress = true;
            using var loading = _loading.Begin(this, "请稍等，正在加载预览");
            SetReloadButtonsEnabled(false);
            var restoreTime = _playback.CurrentTime;
            var restorePlaying = _playback.IsPlaying;
            _playback.Pause();
            try
            {
                _aspectRatio = nextRatio;
                UpdateAspectButtons();
                ApplyAspectRatioLayout();
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                var (width, height) = GetPreviewPixelSize();
                await _previewSession.RefreshViewportAsync(_aspectRatio, width, height);
                _settings.Set("Editor.PreviewAspectRatio", _aspectRatio);
                RestoreEditorPlayback(restoreTime, restorePlaying);
            }
            catch (Exception ex)
            {
                _aspectRatio = previousRatio;
                UpdateAspectButtons();
                ApplyAspectRatioLayout();
                TxtPreviewState.Text = "预览比例刷新失败：" + ex.Message;
                TxtPreviewState.Foreground = new SolidColorBrush(Colors.OrangeRed);
                RestoreEditorPlayback(restoreTime, restorePlaying);
            }
            finally
            {
                _reloadInProgress = false;
                SetReloadButtonsEnabled(true);
            }
        }

        private void UpdateAspectButtons()
        {
            if (BtnAspect169 is null)
                return;
            foreach (var button in new[] { BtnAspect169, BtnAspect43, BtnAspect219 })
            {
                var selected = string.Equals(button.Tag as string, _aspectRatio, StringComparison.Ordinal);
                button.FontWeight = selected ? FontWeights.Bold : FontWeights.Normal;
                button.Opacity = selected ? 1 : .65;
            }
        }

        private async void BtnReloadPlayer_Click(object sender, RoutedEventArgs e) =>
            await RunReloadAsync(
                "正在重载 Unity 播放器…",
                () => _previewSession.RestartPlayerAsync());

        private async void BtnReloadLevel_Click(object sender, RoutedEventArgs e)
        {
            if (Context is null)
                return;
            await RunReloadAsync(
                "正在重新读取关卡资源…",
                () => _previewSession.ReloadLevelAsync(Context, _previewPlayback.CurrentTime));
        }

        private async Task RunReloadAsync(string status, Func<Task> action)
        {
            if (_reloadInProgress)
                return;
            _reloadInProgress = true;
            using var loading = _loading.Begin(this, "请稍等，正在加载预览");
            SetReloadButtonsEnabled(false);
            var restoreTime = _playback.CurrentTime;
            var restorePlaying = _playback.IsPlaying;
            _playback.Pause();
            TxtPreviewState.Text = status;
            try
            {
                await action();
                RestoreEditorPlayback(restoreTime, restorePlaying);
            }
            catch (Exception ex)
            {
                TxtPreviewState.Text = "预览重载失败：" + ex.Message;
                TxtPreviewState.Foreground = new SolidColorBrush(Colors.OrangeRed);
                RestoreEditorPlayback(restoreTime, restorePlaying);
            }
            finally
            {
                _reloadInProgress = false;
                SetReloadButtonsEnabled(true);
            }
        }

        private void RestoreEditorPlayback(double time, bool wasPlaying)
        {
            _playback.Seek(Math.Max(0, time));
            if (wasPlaying)
                _playback.Play();
        }

        private void SetReloadButtonsEnabled(bool enabled)
        {
            BtnReloadPlayer.IsEnabled = enabled;
            BtnReloadLevel.IsEnabled = enabled;
            BtnAspect169.IsEnabled = enabled;
            BtnAspect43.IsEnabled = enabled;
            BtnAspect219.IsEnabled = enabled;
        }

        private async void BtnRetryPreview_Click(object sender, RoutedEventArgs e)
        {
            BtnRetryPreview.IsEnabled = false;
            using var loading = _loading.Begin(this, "请稍等，正在加载预览");
            try { await _previewSession.RetryAsync(); }
            finally { BtnRetryPreview.IsEnabled = true; }
        }

        private void BtnPreviewDiagnostics_Click(object sender, RoutedEventArgs e)
        {
            var details = string.Join(
                Environment.NewLine + Environment.NewLine,
                _previewDiagnostics.Diagnostics.Select(item =>
                    $"[{item.Code}] {item.Message}" +
                    $"{Environment.NewLine}时间：{item.Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}" +
                    (string.IsNullOrWhiteSpace(item.Path) ? string.Empty : $"{Environment.NewLine}位置：{item.Path}") +
                    (string.IsNullOrWhiteSpace(item.EntityId) ? string.Empty : $"{Environment.NewLine}实体：{item.EntityId}") +
                    (item.SnapshotVersion is null ? string.Empty : $"{Environment.NewLine}快照版本：{item.SnapshotVersion}") +
                    (item.RepeatCount <= 1 ? string.Empty : $"{Environment.NewLine}重复次数：{item.RepeatCount}") +
                    (string.IsNullOrWhiteSpace(item.Suggestion) ? string.Empty : $"{Environment.NewLine}建议：{item.Suggestion}") +
                    (string.IsNullOrWhiteSpace(item.StackTrace) ? string.Empty : $"{Environment.NewLine}调用堆栈：{Environment.NewLine}{item.StackTrace}")));
            var summary = _previewDiagnostics.Summary;
            if (summary.ErrorCount > 0)
                _dialogService.ShowErrorDialog(
                    summary.Primary?.Message ?? "Unity 预览发生错误。",
                    $"Unity 预览诊断（{summary.ErrorCount} 个错误，{summary.WarningCount} 个警告）",
                    details);
            else if (summary.WarningCount > 0)
                _dialogService.ShowMessage(
                    $"{summary.Primary?.Message}{Environment.NewLine}{Environment.NewLine}{details}",
                    $"Unity 预览诊断（{summary.WarningCount} 个警告）",
                    DialogMessageType.Warning);
            else
                _dialogService.ShowMessage("当前没有 Unity 预览错误或警告。", "Unity 预览诊断");
        }

        private void BtnRepairResources_Click(object sender, RoutedEventArgs e)
        {
            if (Context is null) return;
            AppServices.GetService<IMessageBroker>().Publish("RequestOpenProjectRepair");
        }

        private void PreviewDiagnostics_Changed(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var diagnostic = _previewDiagnostics.Diagnostics.FirstOrDefault();
                TxtPreviewState.Text = _previewDiagnostics.Availability switch
                {
                    PreviewAvailabilityState.Ready => _previewDiagnostics.Diagnostics.Count == 0
                        ? "Unity Original Player 已就绪"
                        : $"预览可用（{_previewDiagnostics.Diagnostics.Count} 条提示）",
                    PreviewAvailabilityState.InvalidData when _previewDiagnostics.LastKnownGood is not null =>
                        $"当前数据无效，预览停留在版本 {_previewDiagnostics.LastKnownGood.Snapshot.Version}：{diagnostic?.Message}",
                    _ => diagnostic?.Message ?? $"预览状态：{_previewDiagnostics.Availability}"
                };
                TxtPreviewState.Text = _previewDiagnostics.SessionStatus.Phase switch
                {
                    PreviewSessionPhase.LaunchingProcess => "正在启动 Unity Original Player…",
                    PreviewSessionPhase.InitializingGraphics => "正在初始化 Unity 图形窗口…",
                    PreviewSessionPhase.ConnectingTransport => "正在连接 Unity 通信通道…",
                    PreviewSessionPhase.AuthenticatingHost => "Unity 已启动，正在完成协议握手…",
                    PreviewSessionPhase.HostReady => "Unity 已连接，正在准备预览数据…",
                    PreviewSessionPhase.ValidatingSnapshot => "正在校验谱面与故事板…",
                    PreviewSessionPhase.MaterializingVfs => "正在准备预览资源…",
                    PreviewSessionPhase.LoadingContent =>
                        $"Unity 已连接，正在加载谱面… {_previewDiagnostics.SessionStatus.Detail}",
                    PreviewSessionPhase.PreviewReady => "Unity Original Player 已就绪",
                    _ => TxtPreviewState.Text
                };
                if (_previewDiagnostics.Availability is PreviewAvailabilityState.Ready or PreviewAvailabilityState.ReadyWithWarnings &&
                    _previewDiagnostics.Diagnostics.Count == 0 &&
                    _previewPlayback is UnityStoryboardPreviewHost { Performance: { } performance })
                {
                    TxtPreviewState.Text =
                        $"Unity 已就绪 · {performance.FramesPerSecond:F0} FPS · " +
                        $"{performance.AverageFrameMilliseconds:F1} ms · " +
                        $"{performance.EffectiveRenderScale * 100:F0}%";
                }
                TxtPreviewState.Foreground = new SolidColorBrush(
                    _previewDiagnostics.Availability is PreviewAvailabilityState.Ready or PreviewAvailabilityState.ReadyWithWarnings
                        ? Colors.LightGreen
                        : _previewDiagnostics.Availability is PreviewAvailabilityState.Starting or PreviewAvailabilityState.Connecting
                            ? Colors.Gold
                            : Colors.OrangeRed);
                BtnRetryPreview.Visibility = _previewDiagnostics.Availability is
                    PreviewAvailabilityState.RuntimeMissing or
                    PreviewAvailabilityState.InvalidData or
                    PreviewAvailabilityState.Disconnected or
                    PreviewAvailabilityState.Faulted
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                var summary = _previewDiagnostics.Summary;
                BtnPreviewDiagnostics.IsEnabled = true;
                DiagnosticBadge.Visibility = summary.ErrorCount + summary.WarningCount > 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                TxtDiagnosticBadge.Text = (summary.ErrorCount > 0
                    ? summary.ErrorCount
                    : summary.WarningCount).ToString();
                TxtDiagnosticIcon.Foreground = new SolidColorBrush(
                    summary.ErrorCount > 0 ? Colors.OrangeRed :
                    summary.WarningCount > 0 ? Colors.Gold : Colors.Gray);
                BtnPreviewDiagnostics.ToolTip =
                    $"Unity 预览诊断：{summary.ErrorCount} 个错误，{summary.WarningCount} 个警告";
                var phase = _previewDiagnostics.SessionStatus.Phase;
                var hasUsableFrame = _previewDiagnostics.Availability is
                    PreviewAvailabilityState.Ready or PreviewAvailabilityState.ReadyWithWarnings;
                var isInitialLoading = _previewDiagnostics.LastKnownGood is null && phase is
                    PreviewSessionPhase.LaunchingProcess or
                    PreviewSessionPhase.InitializingGraphics or
                    PreviewSessionPhase.ConnectingTransport or
                    PreviewSessionPhase.AuthenticatingHost or
                    PreviewSessionPhase.HostReady or
                    PreviewSessionPhase.ValidatingSnapshot or
                    PreviewSessionPhase.MaterializingVfs or
                    PreviewSessionPhase.LoadingContent;
                var isTerminal = _previewDiagnostics.Availability is
                    PreviewAvailabilityState.RuntimeMissing or
                    PreviewAvailabilityState.InvalidData or
                    PreviewAvailabilityState.Disconnected or
                    PreviewAvailabilityState.Faulted;
                // The HwndHost must be visible until its HWND exists. Afterwards it is
                // hidden for initial loading/terminal states so WPF can cover the viewport.
                _unityHost.Visibility = CanvasTabControl.SelectedIndex == 0 &&
                    (_unityHost.HostHandle == IntPtr.Zero || (!isInitialLoading && !isTerminal))
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                LoadingOverlay.IsLoading = isInitialLoading;
                PreviewFallbackText.Visibility = hasUsableFrame
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                BtnRepairResources.Visibility = Context is not null &&
                    AppServices.GetService<IProjectReadinessService>()
                        .Evaluate(Context).NeedsRepair
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }));
        }

        public bool IsJsonTabActive
        {
            get
            {
                if (JsonEditor == null) return false;
                var parent = VisualTreeHelper.GetParent(this);
                while (parent != null && !(parent is TabControl)) parent = VisualTreeHelper.GetParent(parent);
                if (parent is TabControl tc) return tc.SelectedIndex == 1;
                return false;
            }
        }

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source is TabControl tabControl && tabControl.SelectedItem is TabItem)
            {
                if (tabControl.SelectedIndex == 1 && !HasUnappliedChanges)
                {
                    RefreshJsonView();
                }
                else if (tabControl.SelectedIndex == 2)
                {
                    RefreshRuntimeJsonPreview();
                }
                if (_unityHost.HostHandle != IntPtr.Zero)
                {
                    var (width, height) = GetPreviewPixelSize();
                    _ = _previewSession.ResizeAsync(
                        width,
                        height,
                        tabControl.SelectedIndex == 0);
                }
                _unityHost.Visibility = tabControl.SelectedIndex == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        public void RefreshJsonView()
        {
            if (JsonEditor == null) return;
            var currentModel = Context?.Storyboard;
            if (currentModel == null) return;
            // 🛡️ 核心追加看门狗防线：如果当前根本没看代码页，直接拦截，拒绝在后台偷电序列化！
            if (CanvasTabControl == null || CanvasTabControl.SelectedIndex != 1) return;

            try
            {
                _isRefreshing = true;
                if (_isGlobalPreviewMode)
                {
                    NoSelectionHint.Visibility = Visibility.Collapsed;
                    JsonEditor.Visibility = Visibility.Visible;
                    JsonEditor.Text = StoryboardWriter.Write(currentModel);
                }
                else if (_lastSelectedObject == null)
                {
                    NoSelectionHint.Visibility = Visibility.Visible;
                    JsonEditor.Visibility = Visibility.Collapsed;
                    JsonEditor.Text = "";
                }
                else
                {
                    NoSelectionHint.Visibility = Visibility.Collapsed;
                    JsonEditor.Visibility = Visibility.Visible;
                    JsonEditor.Text = StoryboardWriter.WriteNode(_lastSelectedObject);
                }
                HasUnappliedChanges = false;
                TxtJsonStatus.Text = "✅ 代码已刷新为最新状态。";
                TxtJsonStatus.Foreground = new SolidColorBrush(Colors.LightGreen);
                if (_lastSelectedObject != null && !_isGlobalPreviewMode)
                    ExecuteRadarJump(_lastSelectedObject);
            }
            catch (Exception ex)
            {
                JsonEditor.Visibility = Visibility.Visible;
                NoSelectionHint.Visibility = Visibility.Collapsed;
                JsonEditor.Text = "// 序列化异常: " + ex.Message;
            }
            finally { _isRefreshing = false; }
        }

        private void BtnApplyJson_Click(object sender, RoutedEventArgs e)
        {
            if (OnBeforeActionCheckConflict != null && !OnBeforeActionCheckConflict()) return;
            ForceApplyJson();
        }

        public bool ForceApplyJson()
        {
            try
            {
                var root = Context?.Storyboard;
                if (_isGlobalPreviewMode)
                {
                    var newRoot = StoryboardReader.Read(JsonEditor.Text);
                    ThrowIfInvalid(StoryboardValidatorService.Validate(newRoot));
                    OnApplyJsonSuccess?.Invoke(newRoot);
                }
                else if (_lastSelectedObject is IStoryboardEntity currentEntity)
                {
                    var replacement = StoryboardReader.ReadEntity(JsonEditor.Text, currentEntity.GetType());
                    ThrowIfInvalid(StoryboardValidatorService.ValidateEntity(replacement));
                    ReplaceEntityContents(currentEntity, replacement);
                    OnApplyJsonSuccess?.Invoke(root);
                }

                HasUnappliedChanges = false;
                TxtJsonStatus.Text = "🎉 应用成功！事件列表与属性面板已同步。";
                TxtJsonStatus.Foreground = new SolidColorBrush(Colors.LightGreen);
                return true;
            }
            catch (Exception ex)
            {
                TxtJsonStatus.Text = "❌ 语法错误，应用失败: " + ex.Message;
                TxtJsonStatus.Foreground = new SolidColorBrush(Colors.OrangeRed);
                return false;
            }
        }

        private static void ReplaceEntityContents(IStoryboardEntity target, IStoryboardEntity source)
        {
            target.Id = source.Id;
            target.IsIdSynthetic = source.IsIdSynthetic;
            target.TargetId = source.TargetId;
            target.ParentId = source.ParentId;
            target.UnknownProperties.Clear();
            foreach (var property in source.UnknownProperties)
                target.UnknownProperties[property.Key] = property.Value.DeepClone();

            var targetType = target.GetType();
            targetType.GetProperty("BaseState")?.SetValue(target, source.GetBaseState());
            var targetStates = target.GetKeyframes();
            targetStates.Clear();
            foreach (var state in source.GetKeyframes()) targetStates.Add(state);
        }

        private static void ThrowIfInvalid(IReadOnlyList<StoryboardDiagnostic> diagnostics)
        {
            var errors = diagnostics.Where(item => item.Severity == StoryboardDiagnosticSeverity.Error).ToArray();
            if (errors.Length > 0)
                throw new JsonException(string.Join(Environment.NewLine,
                    errors.Select(item => $"{item.Path}: {item.Message}")));
        }

        private void JsonEditor_TextChanged(object sender, EventArgs e)
        {
            if (_isRefreshing) return;
            HasUnappliedChanges = true;
            TxtJsonStatus.Text = "⚠️ 源代码已修改，尚未应用到内存！(冲突保护中)";
            TxtJsonStatus.Foreground = new SolidColorBrush(Colors.Orange);
        }

        public void TrackSelectedObject(object obj)
        {
            _lastSelectedObject = obj;
            _isGlobalPreviewMode = false;

            // 只有当打谱师真正切换到 JSON 源码标签页（Index == 1）时，才激活序列化与雷达！
            // 在常规可视化设计模式下，不执行任何后台大文本计算，单点延迟直接归零，双击判定瞬间复活！
            if (CanvasTabControl != null && CanvasTabControl.SelectedIndex == 1)
            {
                RefreshJsonView();
            }
        }

        // ==========================================
        // 🔍 全频段星际智能雷达跃迁系统
        // ==========================================
        private void ExecuteRadarJump(object obj)
        {
            if (JsonEditor == null || string.IsNullOrEmpty(JsonEditor.Text)) return;
            // 🧠 核心提速：将巨型文本一次性锁死在局部内存变量里，防止每次调用 .Text 都触发底层的段树大字符串组装！
            string editorText = JsonEditor.Text;

            string searchKey = null;
            int searchStartIndex = 0;

            // ✨ 终极重写：雷达追踪机制全线接入 C2 分离架构
            if (obj is IStoryboardEntity sbObj && !string.IsNullOrEmpty(sbObj.Id))
            {
                searchKey = $"\"id\": \"{sbObj.Id}\"";
            }
            else if (obj is C2Sprite sprite && !string.IsNullOrEmpty(sprite.BaseState?.Path))
            {
                searchKey = $"\"path\": \"{sprite.BaseState.Path}\"";
                searchStartIndex = Math.Max(0, JsonEditor.Text.IndexOf("\"sprites\":"));
            }
            else if (obj is C2Video video && !string.IsNullOrEmpty(video.BaseState?.Path))
            {
                searchKey = $"\"path\": \"{video.BaseState.Path}\"";
                searchStartIndex = Math.Max(0, JsonEditor.Text.IndexOf("\"videos\":"));
            }
            else if (obj is C2NoteController noteCtrl && noteCtrl.BaseState?.NoteTarget != null)
            {
                var target = noteCtrl.BaseState.NoteTarget;
                searchKey = target is Newtonsoft.Json.Linq.JObject ? "\"note\": {" : $"\"note\": {target}";
                searchStartIndex = Math.Max(0, JsonEditor.Text.IndexOf("\"note_controllers\":"));
            }
            else if (obj is C2SceneController ctrl)
            {
                // 场景控制器嗅探：优先找动画首帧时间，无则默认为 0 帧
                string targetTime = ctrl.Keyframes?.Count > 0 ? ctrl.Keyframes[0].Time?.ToString() : "0";
                searchKey = $"\"time\": {targetTime}";
                searchStartIndex = Math.Max(0, JsonEditor.Text.IndexOf("\"controllers\":"));
            }

            if (!string.IsNullOrEmpty(searchKey))
            {
                int index = JsonEditor.Text.IndexOf(searchKey, searchStartIndex);
                if (index < 0 && searchStartIndex > 0) index = JsonEditor.Text.IndexOf(searchKey);

                if (index >= 0)
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (JsonEditor.Document == null || index >= JsonEditor.Document.TextLength) return;

                        JsonEditor.Focus();
                        var documentLine = JsonEditor.Document.GetLineByOffset(index);
                        JsonEditor.ScrollToLine(documentLine.LineNumber);
                        JsonEditor.TextArea.Caret.Line = documentLine.LineNumber;
                        JsonEditor.TextArea.Caret.BringCaretToView();
                        JsonEditor.Select(index, searchKey.Length);
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
            }
        }

        // ==========================================
        // 🔍 画布缩放操作（供快捷键系统调用）
        // ==========================================
        private double _canvasZoomLevel = 1.0;
        private const double MinCanvasZoom = 0.25;
        private const double MaxCanvasZoom = 4.0;
        private const double CanvasZoomStep = 0.2;

        /// <summary>
        /// 画布放大。
        /// </summary>
        public void ZoomIn()
        {
            _canvasZoomLevel = Math.Min(MaxCanvasZoom, _canvasZoomLevel + CanvasZoomStep);
            ApplyCanvasZoom();
        }

        /// <summary>
        /// 画布缩小。
        /// </summary>
        public void ZoomOut()
        {
            _canvasZoomLevel = Math.Max(MinCanvasZoom, _canvasZoomLevel - CanvasZoomStep);
            ApplyCanvasZoom();
        }

        /// <summary>
        /// 重置画布缩放至默认大小。
        /// </summary>
        public void ResetZoom()
        {
            _canvasZoomLevel = 1.0;
            ApplyCanvasZoom();
        }

        private void ApplyCanvasZoom()
        {
            // Native HWND content cannot safely receive a WPF LayoutTransform (Airspace).
            // Keep the public shortcut contract and ask Unity to render at the new size.
            if (_unityHost.HostHandle != IntPtr.Zero)
                _ = ResizeNativePreviewForZoomAsync();
        }

        private void RefreshRuntimeJsonPreview()
        {
            if (RuntimeJsonEditor is null || Context is null) return;
            try
            {
                var result = AppServices
                    .GetService<IStoryboardCanonicalBridge>()
                    .Export(Context);
                var errors = result.Issues.Where(issue =>
                    issue.Severity == StoryboardDiagnosticSeverity.Error)
                    .ToArray();
                RuntimeJsonEditor.Text = errors.Length == 0
                    ? result.Json.ToString(Formatting.Indented)
                    : "// 运行导出被阻止\n// " +
                      string.Join("\n// ", errors.Select(issue =>
                          $"{issue.Path}: {issue.Message}"));
            }
            catch (Exception ex)
            {
                RuntimeJsonEditor.Text = "// 运行导出预览失败: " + ex.Message;
            }
        }

        private async Task ResizeNativePreviewForZoomAsync()
        {
            var (width, height) = GetPreviewPixelSize();
            await _previewSession.ResizeAsync(
                Math.Max(1, (int)Math.Round(width * _canvasZoomLevel)),
                Math.Max(1, (int)Math.Round(height * _canvasZoomLevel)),
                CanvasTabControl.SelectedIndex == 0);
        }
    }
}
