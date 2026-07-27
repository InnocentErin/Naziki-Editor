using System;

namespace Naziki_Editor.Core.Abstractions
{
    /// <summary>
    /// 主题模式枚举，定义应用支持的主题切换策略。
    /// 使用 AppThemeMode 命名以避免与 System.Windows.ThemeMode 冲突。
    /// </summary>
    public enum AppThemeMode
    {
        /// <summary>跟随操作系统主题自动切换</summary>
        System,

        /// <summary>强制使用深色主题</summary>
        Dark,

        /// <summary>强制使用浅色主题</summary>
        Light
    }

    /// <summary>
    /// 主题管理器接口，作为应用主题切换的唯一入口。
    /// 负责管理主题状态、动态替换资源字典、响应系统主题变化。
    /// </summary>
    public interface IThemeManager
    {
        /// <summary>当前生效的主题模式</summary>
        AppThemeMode CurrentTheme { get; }

        /// <summary>当前实际应用的主题（在 System 模式下返回系统检测到的实际主题）</summary>
        AppThemeMode EffectiveTheme { get; }

        /// <summary>主题变更事件，当应用主题实际切换时触发</summary>
        event EventHandler<ThemeChangedEventArgs>? ThemeChanged;

        /// <summary>
        /// 初始化主题管理器：加载已保存设置、应用对应主题、注册系统事件监听。
        /// 应在 App 启动时调用一次。
        /// </summary>
        void Initialize();

        /// <summary>
        /// 设置主题模式并立即应用。
        /// </summary>
        /// <param name="mode">目标主题模式</param>
        void SetTheme(AppThemeMode mode);

        /// <summary>
        /// 动态更新强调色资源。
        /// </summary>
        /// <param name="accentColorHex">十六进制颜色字符串，如 "#007ACC"</param>
        void UpdateAccentColor(string accentColorHex);

        /// <summary>
        /// 检测当前系统是否为深色模式。
        /// </summary>
        bool IsSystemDarkMode();
    }

    /// <summary>
    /// 主题变更事件参数。
    /// </summary>
    public class ThemeChangedEventArgs : EventArgs
    {
        /// <summary>变更前的主题模式</summary>
        public AppThemeMode OldTheme { get; }

        /// <summary>变更后的主题模式</summary>
        public AppThemeMode NewTheme { get; }

        public ThemeChangedEventArgs(AppThemeMode oldTheme, AppThemeMode newTheme)
        {
            OldTheme = oldTheme;
            NewTheme = newTheme;
        }
    }
}