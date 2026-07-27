using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.ErrorHandling;
using Naziki_Editor.Core.Settings;
using Naziki_Editor.ViewModels.Settings;

namespace Naziki_Editor.Views.Settings
{
    /// <summary>
    /// 设置器窗口控件，提供左右分栏的设置管理界面。
    /// 左侧为分类导航，右侧为动态加载的设置项内容。
    /// 采用 MVVM 模式，通过 SettingsWindowViewModel 驱动。
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly SettingsWindowViewModel _viewModel;
        private readonly IErrorHandler _errorHandler;
        private readonly INotificationService _notificationService;

        /// <summary>
        /// 颜色字符串到画刷的转换器（静态，供 XAML 绑定使用）。
        /// </summary>
        public static readonly IValueConverter ColorStringToBrushConverter = new ColorStringToBrushConverterImpl();

        /// <summary>
        /// 集合计数到 Visibility 的转换器（静态，供 XAML 绑定使用）。
        /// </summary>
        public static readonly IValueConverter CountToVisibilityConverter = new CountToVisibilityConverterImpl();

        /// <summary>当前正在录制的快捷键项</summary>
        private SettingItem? _recordingItem;

        public SettingsWindow(
            SettingsWindowViewModel viewModel,
            IErrorHandler errorHandler,
            INotificationService notificationService)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));

            DataContext = _viewModel;
            InitializeComponent();

            // 订阅 ViewModel 的保存/取消完成事件以关闭窗口
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

            // 窗口关闭时清理资源
            Closed += OnWindowClosed;

            // ⌨️ 全局键盘监听（用于快捷键录制）
            PreviewKeyDown += OnSettingsWindowPreviewKeyDown;
        }

        /// <summary>
        /// 监听 ViewModel 属性变化，在保存完成后关闭窗口。
        /// </summary>
        private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SettingsWindowViewModel.IsDirty) && !_viewModel.IsDirty)
            {
                // 设置在保存或取消后变为非脏状态，通知用户并关闭
                // 注意：这里不自动关闭，让用户手动关闭窗口以便查看结果
            }
        }

        /// <summary>
        /// 窗口关闭时清理事件订阅和 ViewModel 资源。
        /// </summary>
        private void OnWindowClosed(object? sender, EventArgs e)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.Dispose();
        }

        /// <summary>
        /// 安全地显示设置窗口，自动处理异常。
        /// </summary>
        public static void ShowSettings(
            Window owner,
            SettingsWindowViewModel viewModel,
            IErrorHandler errorHandler,
            INotificationService notificationService)
        {
            errorHandler.TryExecute(() =>
            {
                var window = new SettingsWindow(viewModel, errorHandler, notificationService)
                {
                    Owner = owner
                };
                window.ShowDialog();
            }, "SettingsUI", "SettingsWindow.ShowSettings");
        }
    }

    // ==========================================
    // 🎯 设置项值类型 → DataTemplate 选择器
    // ==========================================

    /// <summary>
    /// 根据 SettingItem 的 ValueType 属性选择对应的 DataTemplate。
    /// 支持 Bool / String / Integer / Float / Combo / Color / MultiLineText 等类型。
    /// 未匹配的类型回退到 String 模板。
    /// </summary>
    public class SettingItemTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? BoolTemplate { get; set; }
        public DataTemplate? StringTemplate { get; set; }
        public DataTemplate? IntegerTemplate { get; set; }
        public DataTemplate? FloatTemplate { get; set; }
        public DataTemplate? ComboTemplate { get; set; }
        public DataTemplate? ColorTemplate { get; set; }
        public DataTemplate? MultiLineTemplate { get; set; }
        public DataTemplate? KeyBindingTemplate { get; set; }

        public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        {
            if (item is not SettingItem settingItem)
                return StringTemplate;

            return settingItem.ValueType switch
            {
                SettingValueType.Bool => BoolTemplate ?? StringTemplate,
                SettingValueType.String => StringTemplate,
                SettingValueType.Integer => IntegerTemplate ?? StringTemplate,
                SettingValueType.Float => FloatTemplate ?? StringTemplate,
                SettingValueType.Combo => ComboTemplate ?? StringTemplate,
                SettingValueType.Color => ColorTemplate ?? StringTemplate,
                SettingValueType.MultiLineText => MultiLineTemplate ?? StringTemplate,
                SettingValueType.FilePath => StringTemplate,
                SettingValueType.DirectoryPath => StringTemplate,
                SettingValueType.KeyBinding => KeyBindingTemplate ?? StringTemplate,
                _ => StringTemplate
            };
        }
    }

    // ==========================================
    // 🎨 颜色字符串 → Brush 转换器
    // ==========================================

    /// <summary>
    /// 将十六进制颜色字符串（如 "#007ACC"）转换为 SolidColorBrush。
    /// 用于颜色设置项的预览色块。
    /// </summary>
    internal class ColorStringToBrushConverterImpl : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string colorStr && !string.IsNullOrWhiteSpace(colorStr))
            {
                try
                {
                    var color = (Color)ColorConverter.ConvertFromString(colorStr);
                    return new SolidColorBrush(color);
                }
                catch
                {
                    // 颜色解析失败，返回默认灰色
                }
            }
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SolidColorBrush brush)
                return brush.Color.ToString();
            return "#808080";
        }
    }

    // ==========================================
    // ⌨️ 快捷键录制相关事件处理
    // ==========================================
    public partial class SettingsWindow
    {
        /// <summary>
        /// 点击快捷键手势边框 → 开始录制。
        /// </summary>
        private void KeyBindingBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is SettingItem item)
            {
                StartRecording(item);
            }
        }

        /// <summary>
        /// 点击"录制新快捷键"按钮 → 开始录制。
        /// </summary>
        private void KeyBindingRecord_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SettingItem item)
            {
                StartRecording(item);
            }
        }

        /// <summary>
        /// 点击"清除"按钮 → 清空快捷键绑定。
        /// </summary>
        private void KeyBindingClear_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SettingItem item)
            {
                item.CurrentValue = string.Empty;
                item.ConflictBindings.Clear();
                _notificationService.Show($"已清除「{item.DisplayName}」的快捷键绑定。", NotificationType.Info);
            }
        }

        /// <summary>
        /// 点击"默认"按钮 → 恢复为默认快捷键。
        /// </summary>
        private void KeyBindingReset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SettingItem item)
            {
                item.CurrentValue = item.DefaultKeyGesture;
                item.ConflictBindings.Clear();
                _notificationService.ShowSuccess($"已恢复「{item.DisplayName}」为默认快捷键：{item.DefaultKeyGesture}");
            }
        }

        /// <summary>
        /// 开始录制快捷键。
        /// </summary>
        private void StartRecording(SettingItem item)
        {
            // 如果已有其他项在录制，先取消
            if (_recordingItem != null && _recordingItem != item)
            {
                _recordingItem.IsRecording = false;
            }

            _recordingItem = item;
            item.IsRecording = true;
            item.ConflictBindings.Clear();
            _notificationService.Show($"正在录制「{item.DisplayName}」的快捷键...\n请按下键盘组合键（如 Ctrl+S），按 Esc 取消。", NotificationType.Info);
        }

        /// <summary>
        /// 停止录制（不保存）。
        /// </summary>
        private void StopRecording()
        {
            if (_recordingItem != null)
            {
                _recordingItem.IsRecording = false;
                _recordingItem = null;
            }
        }

        /// <summary>
        /// 全局键盘按下事件处理 —— 快捷键录制模式下的按键捕获。
        /// </summary>
        private void OnSettingsWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_recordingItem == null) return;

            // Esc 取消录制
            if (e.Key == Key.Escape)
            {
                StopRecording();
                _notificationService.Show("已取消快捷键录制。", NotificationType.Info);
                e.Handled = true;
                return;
            }

            // 过滤纯修饰键
            if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl ||
                e.Key == Key.LeftAlt || e.Key == Key.RightAlt ||
                e.Key == Key.LeftShift || e.Key == Key.RightShift ||
                e.Key == Key.LWin || e.Key == Key.RWin ||
                e.Key == Key.System)
            {
                return;
            }

            // 构建手势文本
            var modifiers = Keyboard.Modifiers;
            var gestureText = BuildGestureText(e.Key, modifiers);

            if (string.IsNullOrEmpty(gestureText))
            {
                StopRecording();
                return;
            }

            // 检查冲突
            var conflicts = _viewModel.DetectKeyBindingConflicts(_recordingItem.Key, gestureText);
            _recordingItem.ConflictBindings.Clear();
            if (conflicts.Count > 0)
            {
                foreach (var conflict in conflicts)
                    _recordingItem.ConflictBindings.Add(conflict);
            }

            // 保存快捷键
            _recordingItem.CurrentValue = gestureText;
            _recordingItem.IsRecording = false;

            if (conflicts.Count > 0)
            {
                _notificationService.ShowWarning(
                    $"「{_recordingItem.DisplayName}」快捷键已设置为 {gestureText}，但存在 {conflicts.Count} 个冲突，请检查。");
            }
            else
            {
                _notificationService.ShowSuccess(
                    $"「{_recordingItem.DisplayName}」快捷键已设置为：{gestureText}");
            }

            _recordingItem = null;
            e.Handled = true;
        }

        /// <summary>
        /// 根据按键和修饰键构建手势文本。
        /// </summary>
        private static string BuildGestureText(Key key, ModifierKeys modifiers)
        {
            var parts = new System.Collections.Generic.List<string>();

            if (modifiers.HasFlag(ModifierKeys.Control))
                parts.Add("Ctrl");
            if (modifiers.HasFlag(ModifierKeys.Alt))
                parts.Add("Alt");
            if (modifiers.HasFlag(ModifierKeys.Shift))
                parts.Add("Shift");
            if (modifiers.HasFlag(ModifierKeys.Windows))
                parts.Add("Win");

            parts.Add(KeyToString(key));

            return string.Join("+", parts);
        }

        private static string KeyToString(Key key)
        {
            return key switch
            {
                Key.D0 => "0", Key.D1 => "1", Key.D2 => "2", Key.D3 => "3", Key.D4 => "4",
                Key.D5 => "5", Key.D6 => "6", Key.D7 => "7", Key.D8 => "8", Key.D9 => "9",
                Key.OemPlus => "=", Key.OemMinus => "-",
                Key.OemComma => ",", Key.OemPeriod => ".",
                Key.OemQuestion => "/", Key.OemSemicolon => ";",
                Key.OemQuotes => "'", Key.OemOpenBrackets => "[",
                Key.OemCloseBrackets => "]", Key.OemPipe => "\\",
                Key.OemTilde => "`",
                _ => key.ToString()
            };
        }
    }

    // ==========================================
    // 🔢 Count → Visibility 转换器
    // ==========================================

    /// <summary>
    /// 将整数计数转换为 Visibility。count > 0 → Visible，否则 → Collapsed。
    /// </summary>
    internal class CountToVisibilityConverterImpl : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int count && count > 0)
                return Visibility.Visible;
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    // ==========================================
    // 🎤 录制状态转换器（已弃用，保留用于 XAML 参考）
    // ==========================================

    /// <summary>
    /// 将 IsRecording 布尔值转换为录制提示文本的可见性。
    /// </summary>
    internal class KeyBindingRecordingConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isRecording && isRecording)
                return Visibility.Visible;
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// 布尔值反转转换器：true → Collapsed，false → Visible。
    /// 用于快捷键录制时隐藏正常显示文本。
    /// </summary>
    internal class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue && boolValue)
                return Visibility.Collapsed;
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}