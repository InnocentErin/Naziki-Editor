using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Naziki_Editor.Views.Dialogs
{
    /// <summary>
    /// 对话框模式枚举。
    /// </summary>
    public enum ErrorDialogMode
    {
        /// <summary>普通消息提示，仅OK按钮</summary>
        Message,
        /// <summary>错误消息，含忽略/关闭/复制三按钮</summary>
        Error,
        /// <summary>二选一确认，含是/否按钮</summary>
        YesNo,
        /// <summary>三选一确认，含是/否/取消按钮</summary>
        Confirm
    }

    /// <summary>
    /// 对话框返回结果。
    /// </summary>
    public enum ErrorDialogResult
    {
        None,
        OK,
        Yes,
        No,
        Cancel,
        Ignore,
        Close,
        Copy
    }

    /// <summary>
    /// 通用对话框窗口，分上中下结构：
    /// 顶部：标题栏（图标 + 标题 + 关闭按钮）
    /// 中部：消息内容 + 可折叠的错误详情
    /// 底部：操作按钮区域
    ///
    /// 支持四种模式：
    /// - Message：普通消息提示
    /// - Error：报错信息（忽略并继续 / 关闭程序 / 复制错误代码）
    /// - YesNo：二选一确认
    /// - Confirm：三选一确认
    /// </summary>
    public partial class ErrorDialog : Window
    {
        private readonly ErrorDialogMode _mode;
        private ErrorDialogResult _result = ErrorDialogResult.None;
        private bool _isDragging;
        private Point _dragStartPoint;

        public ErrorDialogResult Result => _result;

        /// <summary>
        /// 创建对话框实例。
        /// </summary>
        /// <param name="title">标题文本</param>
        /// <param name="message">消息内容</param>
        /// <param name="mode">对话框模式</param>
        /// <param name="errorDetails">错误详情文本（仅在 Error 模式下有效）</param>
        /// <param name="iconType">图标类型：info / warning / error / question</param>
        public ErrorDialog(string title, string message, ErrorDialogMode mode,
                           string? errorDetails = null, string iconType = "info")
        {
            InitializeComponent();

            _mode = mode;
            ConfigureInitialSize();
            SourceInitialized += (_, _) => ApplyDisplayConstraints();

            // 设置标题和消息
            TitleBlock.Text = string.IsNullOrEmpty(title) ? "Naziki Editor" : title;
            MessageBlock.Text = message ?? string.Empty;

            // 设置图标
            SetIcon(iconType);

            // 设置错误详情
            if (!string.IsNullOrEmpty(errorDetails))
            {
                DetailsTextBox.Text = errorDetails;
                DetailsSection.Visibility = Visibility.Visible;
            }
            else
            {
                DetailsRow.Height = new GridLength(0);
                DetailsRow.MinHeight = 0;
            }

            // 构建按钮
            BuildButtons();
        }

        private void ConfigureInitialSize()
        {
            if (_mode == ErrorDialogMode.Error)
            {
                Width = 820;
                Height = 600;
                MinWidth = 680;
                MinHeight = 360;
                return;
            }

            Width = 560;
            Height = 360;
            MinWidth = 480;
            MinHeight = 280;
        }

        private void ApplyDisplayConstraints()
        {
            var workArea = GetCurrentWorkArea();
            var maxWidth = Math.Max(320, workArea.Width * 0.85);
            var maxHeight = Math.Max(240, workArea.Height * 0.85);

            MinWidth = Math.Min(MinWidth, maxWidth);
            MinHeight = Math.Min(MinHeight, maxHeight);
            MaxWidth = maxWidth;
            MaxHeight = maxHeight;
            Width = Math.Min(Width, maxWidth);
            Height = Math.Min(Height, maxHeight);
        }

        private Rect GetCurrentWorkArea()
        {
            var target = Owner is null
                ? new WindowInteropHelper(this).Handle
                : new WindowInteropHelper(Owner).Handle;
            var monitor = MonitorFromWindow(target, MonitorDefaultToNearest);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };

            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
                return SystemParameters.WorkArea;

            var dpi = VisualTreeHelper.GetDpi(this);
            return new Rect(
                info.WorkArea.Left / dpi.DpiScaleX,
                info.WorkArea.Top / dpi.DpiScaleY,
                (info.WorkArea.Right - info.WorkArea.Left) / dpi.DpiScaleX,
                (info.WorkArea.Bottom - info.WorkArea.Top) / dpi.DpiScaleY);
        }

        private const uint MonitorDefaultToNearest = 2;

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr monitorHandle, ref MonitorInfo monitorInfo);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MonitorInfo
        {
            public int Size;
            public NativeRect Monitor;
            public NativeRect WorkArea;
            public uint Flags;
        }

        /// <summary>
        /// 设置标题栏图标。
        /// </summary>
        private void SetIcon(string iconType)
        {
            (string icon, Color color) = iconType switch
            {
                "error" => ("✕", Color.FromRgb(0xF4, 0x43, 0x36)),
                "warning" => ("⚠", Color.FromRgb(0xFF, 0x98, 0x00)),
                "question" => ("?", Color.FromRgb(0x21, 0x96, 0xF3)),
                _ => ("ℹ", Color.FromRgb(0x21, 0x96, 0xF3))
            };

            IconBlock.Text = icon;
            IconBlock.Foreground = new SolidColorBrush(color);
        }

        /// <summary>
        /// 根据模式动态构建底部按钮。
        /// </summary>
        private void BuildButtons()
        {
            ButtonPanel.Children.Clear();

            switch (_mode)
            {
                case ErrorDialogMode.Message:
                    AddButton("确定", "PrimaryButtonStyle", ErrorDialogResult.OK, isDefault: true);
                    break;

                case ErrorDialogMode.Error:
                    AddButton("忽略并继续运行", "DialogButtonStyle", ErrorDialogResult.Ignore);
                    AddButton("复制错误代码", "PrimaryButtonStyle", ErrorDialogResult.Copy);
                    AddButton("关闭程序", "DangerButtonStyle", ErrorDialogResult.Close);
                    break;

                case ErrorDialogMode.YesNo:
                    AddButton("是", "PrimaryButtonStyle", ErrorDialogResult.Yes, isDefault: true);
                    AddButton("否", "DialogButtonStyle", ErrorDialogResult.No);
                    break;

                case ErrorDialogMode.Confirm:
                    AddButton("是", "PrimaryButtonStyle", ErrorDialogResult.Yes, isDefault: true);
                    AddButton("否", "DialogButtonStyle", ErrorDialogResult.No);
                    AddButton("取消", "DialogButtonStyle", ErrorDialogResult.Cancel);
                    break;
            }
        }

        /// <summary>
        /// 创建按钮并添加到面板。
        /// </summary>
        private void AddButton(string text, string styleKey, ErrorDialogResult result, bool isDefault = false)
        {
            var style = FindResource(styleKey) as Style;

            var button = new Button
            {
                Content = text,
                Style = style,
                Tag = result,
                MinWidth = 80,
                Height = 30,
                Padding = new Thickness(16, 4, 16, 4),
                Margin = new Thickness(4, 0, 4, 0),
                Cursor = Cursors.Hand,
                FontSize = 13
            };

            // 如果找不到样式资源，设置默认外观
            if (style == null)
            {
                Trace.TraceWarning($"[ErrorDialog.AddButton] 警告：样式 '{styleKey}' 未找到，使用默认样式。");
                button.Background = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C));
                button.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                button.BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
                button.BorderThickness = new Thickness(1);
            }

            button.Click += Button_Click;

            if (isDefault)
            {
                button.IsDefault = true;
            }

            ButtonPanel.Children.Add(button);
        }

        #region 事件处理

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ErrorDialogResult result)
            {
                _result = result;

                if (result == ErrorDialogResult.Copy)
                {
                    CopyErrorDetails();
                    // 复制后不关闭窗口，让用户继续操作
                    return;
                }

                if (result == ErrorDialogResult.Close)
                {
                    Application.Current.Shutdown();
                    return;
                }

                DialogResult = true;
                Close();
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            _result = _mode switch
            {
                ErrorDialogMode.Error => ErrorDialogResult.Ignore,
                ErrorDialogMode.Confirm => ErrorDialogResult.Cancel,
                ErrorDialogMode.YesNo => ErrorDialogResult.No,
                _ => ErrorDialogResult.OK
            };

            DialogResult = _result != ErrorDialogResult.None;
            Close();
        }

        /// <summary>
        /// 复制错误详情到剪贴板。
        /// </summary>
        private void CopyErrorDetails()
        {
            try
            {
                var text = $"{TitleBlock.Text}\n\n{MessageBlock.Text}\n\n--- 详细错误信息 ---\n{DetailsTextBox.Text}";
                Clipboard.SetText(text);

                // 视觉反馈：短暂改变按钮文字
                if (ButtonPanel.Children.Count > 0)
                {
                    var copyBtn = ButtonPanel.Children[1] as Button; // Copy button is always second
                    if (copyBtn != null)
                    {
                        var original = copyBtn.Content;
                        copyBtn.Content = "✓ 已复制！";
                        // 使用 Dispatcher 延迟恢复
                        _ = System.Threading.Tasks.Task.Delay(1500).ContinueWith(_ =>
                        {
                            Dispatcher.Invoke(() => copyBtn.Content = original);
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ErrorDialog.CopyErrorDetails] Error: {ex.Message}");
            }
        }

        #endregion

        #region 窗口拖动

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            _dragStartPoint = e.GetPosition(this);
            CaptureMouse();
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                var currentPoint = e.GetPosition(this);
                Left += currentPoint.X - _dragStartPoint.X;
                Top += currentPoint.Y - _dragStartPoint.Y;
            }
        }

        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            ReleaseMouseCapture();
        }

        #endregion

        #region 静态工厂方法

        /// <summary>
        /// 显示普通消息对话框。
        /// </summary>
        public static ErrorDialogResult ShowMessage(string message, string title = "Naziki Editor",
                                                    string iconType = "info")
        {
            return ShowDialog(title, message, ErrorDialogMode.Message, null, iconType);
        }

        /// <summary>
        /// 显示错误对话框（含详细错误信息）。
        /// </summary>
        public static ErrorDialogResult ShowError(string message, string title = "错误",
                                                   string? errorDetails = null)
        {
            return ShowDialog(title, message, ErrorDialogMode.Error, errorDetails, "error");
        }

        /// <summary>
        /// 显示二选一确认对话框。
        /// </summary>
        public static bool ShowYesNo(string message, string title = "确认")
        {
            var result = ShowDialog(title, message, ErrorDialogMode.YesNo, null, "question");
            return result == ErrorDialogResult.Yes;
        }

        /// <summary>
        /// 显示三选一确认对话框。
        /// </summary>
        public static ErrorDialogResult ShowConfirm(string message, string title = "确认",
                                                     string iconType = "question")
        {
            return ShowDialog(title, message, ErrorDialogMode.Confirm, null, iconType);
        }

        /// <summary>
        /// 核心静态方法：创建并显示对话框。
        /// 注意：调用方需确保已在 UI 线程上。
        /// </summary>
        private static ErrorDialogResult ShowDialog(string title, string message, ErrorDialogMode mode,
                                                     string? errorDetails, string iconType)
        {
            ErrorDialogResult result = ErrorDialogResult.None;

            try
            {
                // 确保在 UI 线程上执行
                if (Application.Current?.Dispatcher != null)
                {
                    if (Application.Current.Dispatcher.CheckAccess())
                    {
                        // 已在 UI 线程，直接执行
                        result = ShowDialogInternal(title, message, mode, errorDetails, iconType);
                    }
                    else
                    {
                        // 非 UI 线程，调度到 UI 线程
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            result = ShowDialogInternal(title, message, mode, errorDetails, iconType);
                        });
                    }
                }
                else
                {
                    System.Diagnostics.Trace.WriteLine(
                        $"[ErrorDialog.ShowDialog] 错误：Application.Current 为 null，无法显示对话框");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(
                    $"[ErrorDialog.ShowDialog] 未捕获异常：{ex}");
                // 异常向上抛出，让 WpfDialogService 处理
                throw;
            }

            return result;
        }

        /// <summary>
        /// 在 UI 线程上创建并显示对话框的内部实现。
        /// </summary>
        private static ErrorDialogResult ShowDialogInternal(string title, string message, ErrorDialogMode mode,
                                                             string? errorDetails, string iconType)
        {
            var dialog = new ErrorDialog(title, message, mode, errorDetails, iconType)
            {
                Owner = GetActiveWindow()
            };

            dialog.ShowDialog();
            return dialog.Result;
        }

        /// <summary>
        /// 安全获取当前活动窗口作为对话框的 Owner。
        /// 优先使用 Application.Current.MainWindow（如果有效），
        /// 否则遍历所有窗口查找第一个可见且已加载的窗口。
        /// 避免因 MainWindow 指向已关闭窗口（如 ProjectHubWindow）而导致 InvalidOperationException。
        /// </summary>
        private static Window? GetActiveWindow()
        {
            if (Application.Current == null)
                return null;

            // 优先：MainWindow 如果已加载且可见，直接使用
            if (Application.Current.MainWindow != null &&
                Application.Current.MainWindow.IsLoaded &&
                Application.Current.MainWindow.Visibility == Visibility.Visible)
            {
                return Application.Current.MainWindow;
            }

            // 回退：遍历所有窗口，找到第一个已加载且可见的窗口
            foreach (Window window in Application.Current.Windows)
            {
                if (window.IsLoaded && window.Visibility == Visibility.Visible)
                {
                    return window;
                }
            }

            // 最终回退：如果所有窗口都不可见，尝试使用 MainWindow（至少已构造）
            if (Application.Current.MainWindow != null && Application.Current.MainWindow.IsLoaded)
            {
                return Application.Current.MainWindow;
            }

            return null;
        }

        #endregion
    }
}
