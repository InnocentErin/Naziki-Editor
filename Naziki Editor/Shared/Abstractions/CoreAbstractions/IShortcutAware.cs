using Naziki_Editor.Core.Shortcuts;

namespace Naziki_Editor.Core.Abstractions
{
    /// <summary>
    /// 实现此接口的控件声明自己所属的快捷键上下文。
    /// 当控件获得焦点时，ShortcutManager 通过此接口识别并切换上下文。
    /// </summary>
    public interface IShortcutAware
    {
        /// <summary>
        /// 该控件对应的快捷键上下文。
        /// 当控件获得焦点时，ShortcutManager 将激活此上下文。
        /// </summary>
        ShortcutContext ShortcutContext { get; }

        /// <summary>
        /// 当该控件获得焦点时调用。
        /// 控件可在此方法中执行上下文切换前的准备工作。
        /// </summary>
        /// <returns>true 表示控件接管了快捷键处理，false 表示不接管。</returns>
        bool OnShortcutFocusGained();

        /// <summary>
        /// 当该控件失去焦点时调用。
        /// 控件可在此方法中执行上下文切换后的清理工作。
        /// </summary>
        void OnShortcutFocusLost();
    }
}