using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.ErrorHandling;
using Naziki_Editor.Core.Settings;

namespace Naziki_Editor.Core.Theming
{
    /// <summary>
    /// 主题管理器实现，负责应用主题的切换、系统主题检测和资源字典的动态替换。
    /// 通过 MergedDictionaries 索引管理实现热切换，无需重启应用。
    /// </summary>
    public class ThemeManager : IThemeManager, IDisposable
    {
        private readonly ISettingsStore _settingsStore;
        private readonly IErrorHandler _errorHandler;
        private readonly IMessageBroker _messageBroker;

        private AppThemeMode _currentTheme = AppThemeMode.System;
        private AppThemeMode _effectiveTheme = AppThemeMode.Dark;
        private bool _isDisposed;
        private bool _isInitialized;

        /// <summary>
        /// MergedDictionaries 中各资源字典的索引位置常量。
        /// 索引 0: 主题颜色 (Dark/Colors.xaml 或 Light/Colors.xaml)
        /// 索引 1: 通知颜色 (Dark/NotificationColors.xaml 或 Light/NotificationColors.xaml)
        /// 索引 2: 时间轴颜色 (Dark/TimelineColors.xaml 或 Light/TimelineColors.xaml)
        /// 索引 3+: Base/ 下的样式文件
        /// </summary>
        private const int ThemeColorIndex = 0;
        private const int NotificationColorIndex = 1;
        private const int TimelineColorIndex = 2;

        public AppThemeMode CurrentTheme => _currentTheme;
        public AppThemeMode EffectiveTheme => _effectiveTheme;

        public event EventHandler<ThemeChangedEventArgs>? ThemeChanged;

        public ThemeManager(
            ISettingsStore settingsStore,
            IErrorHandler errorHandler,
            IMessageBroker messageBroker)
        {
            _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
            _errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
            _messageBroker = messageBroker ?? throw new ArgumentNullException(nameof(messageBroker));
        }

        public void Initialize()
        {
            if (_isInitialized) return;
            _isInitialized = true;

            _errorHandler.TryExecute(() =>
            {
                // 1. 从设置中读取已保存的主题偏好
                var savedTheme = _settingsStore.Get("Appearance.Theme", "跟随系统");
                _currentTheme = ParseThemeMode(savedTheme);

                // 2. 应用主题
                ApplyTheme(_currentTheme);

                // 3. 应用已保存的强调色
                var savedAccentColor = _settingsStore.Get("Appearance.AccentColor", "#007ACC");
                UpdateAccentColor(savedAccentColor);

                // 4. 订阅系统主题变化事件
                SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

                // 5. 订阅设置变更（响应强调色修改）
                _settingsStore.SettingChanged += OnSettingChanged;
            }, "Theming", "ThemeManager.Initialize");
        }

        public void SetTheme(AppThemeMode mode)
        {
            _errorHandler.TryExecute(() =>
            {
                _currentTheme = mode;
                ApplyTheme(mode);

                // 保存到设置存储（避免触发循环）
                _settingsStore.Set("Appearance.Theme", ThemeModeToString(mode));
            }, "Theming", "ThemeManager.SetTheme", $"Mode: {mode}");
        }

        public void UpdateAccentColor(string accentColorHex)
        {
            _errorHandler.TryExecute(() =>
            {
                if (string.IsNullOrWhiteSpace(accentColorHex))
                    return;

                try
                {
                    var color = (Color)ColorConverter.ConvertFromString(accentColorHex);
                    var brush = new SolidColorBrush(color);

                    // 更新应用资源中的强调色
                    var resources = Application.Current.Resources;
                    resources["HighlightBorderColor"] = brush;
                    resources["HighlightColor"] = brush;
                }
                catch (FormatException)
                {
                    _errorHandler.HandleException(
                        new FormatException($"无效的颜色格式: {accentColorHex}"),
                        ErrorSeverity.Warning,
                        "Theming",
                        $"强调色格式无效，已忽略: {accentColorHex}",
                        "ThemeManager.UpdateAccentColor");
                }
            }, "Theming", "ThemeManager.UpdateAccentColor", $"Color: {accentColorHex}");
        }

        public bool IsSystemDarkMode()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key?.GetValue("AppsUseLightTheme") is int value)
                    return value == 0;
            }
            catch
            {
                // 注册表读取失败，默认返回深色模式
            }
            return true; // 默认深色
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _settingsStore.SettingChanged -= OnSettingChanged;
        }

        // ==========================================
        // 🔧 内部方法
        // ==========================================

        /// <summary>
        /// 应用主题：替换 MergedDictionaries 中的颜色资源字典。
        /// </summary>
        private void ApplyTheme(AppThemeMode mode)
        {
            var actualTheme = mode == AppThemeMode.System
                ? (IsSystemDarkMode() ? AppThemeMode.Dark : AppThemeMode.Light)
                : mode;

            var oldEffective = _effectiveTheme;
            if (oldEffective == actualTheme) return;

            _effectiveTheme = actualTheme;

            // 在 UI 线程上执行资源字典替换
            if (Application.Current.Dispatcher.CheckAccess())
                ReplaceThemeDictionaries(actualTheme);
            else
                Application.Current.Dispatcher.Invoke(() => ReplaceThemeDictionaries(actualTheme));

            // 触发主题变更事件
            ThemeChanged?.Invoke(this, new ThemeChangedEventArgs(oldEffective, actualTheme));
            _messageBroker.Publish("Theme.Changed", new ThemeChangedEventArgs(oldEffective, actualTheme));
        }

        /// <summary>
        /// 替换 MergedDictionaries 中的主题颜色资源字典。
        /// </summary>
        private void ReplaceThemeDictionaries(AppThemeMode theme)
        {
            var themeFolder = theme == AppThemeMode.Dark ? "Dark" : "Light";
            var mergedDicts = Application.Current.Resources.MergedDictionaries;

            // 替换三个颜色资源字典（索引 0, 1, 2）
            ReplaceDictionaryAt(mergedDicts, ThemeColorIndex,
                $"Themes/{themeFolder}/Colors.xaml");
            ReplaceDictionaryAt(mergedDicts, NotificationColorIndex,
                $"Themes/{themeFolder}/NotificationColors.xaml");
            ReplaceDictionaryAt(mergedDicts, TimelineColorIndex,
                $"Themes/{themeFolder}/TimelineColors.xaml");
        }

        /// <summary>
        /// 在指定索引位置替换 ResourceDictionary。
        /// </summary>
        private static void ReplaceDictionaryAt(
            IList<ResourceDictionary> collection,
            int index,
            string sourcePath)
        {
            if (index < 0 || index >= collection.Count)
                return;

            var newDict = new ResourceDictionary
            {
                Source = new Uri(sourcePath, UriKind.Relative)
            };
            collection[index] = newDict;
        }

        /// <summary>
        /// 系统主题变化回调。
        /// </summary>
        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            // 仅在 System 模式下响应系统主题变化
            if (_currentTheme != AppThemeMode.System) return;

            // 确保在 UI 线程上执行
            if (Application.Current.Dispatcher.CheckAccess())
                ApplyTheme(AppThemeMode.System);
            else
                Application.Current.Dispatcher.Invoke(() => ApplyTheme(AppThemeMode.System));
        }

        /// <summary>
        /// 设置变更回调：响应强调色和主题设置的外部变更。
        /// </summary>
        private void OnSettingChanged(object? sender, SettingsChangedEventArgs e)
        {
            switch (e.Key)
            {
                case "Appearance.AccentColor":
                    if (e.NewValue is string colorStr)
                        UpdateAccentColor(colorStr);
                    break;

                case "Appearance.Theme":
                    if (e.NewValue is string themeStr)
                    {
                        var newMode = ParseThemeMode(themeStr);
                        if (newMode != _currentTheme)
                            SetTheme(newMode);
                    }
                    break;
            }
        }

        private static AppThemeMode ParseThemeMode(string? value)
        {
            return value?.ToLowerInvariant() switch
            {
                "dark" or "深色" => AppThemeMode.Dark,
                "light" or "浅色" => AppThemeMode.Light,
                _ => AppThemeMode.System
            };
        }

        private static string ThemeModeToString(AppThemeMode mode)
        {
            return mode switch
            {
                AppThemeMode.Dark => "深色",
                AppThemeMode.Light => "浅色",
                _ => "跟随系统"
            };
        }
    }
}