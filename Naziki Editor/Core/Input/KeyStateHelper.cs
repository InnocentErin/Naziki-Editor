namespace Naziki_Editor.Core.Input
{
    using System.Windows.Input;

    /// <summary>
    /// 键盘状态辅助工具类。
    /// 提供键盘修饰键检测、按键状态查询等纯计算功能。
    /// </summary>
    public static class KeyStateHelper
    {
        /// <summary>
        /// 判断给定的键是否为纯修饰键（Ctrl、Shift、Alt、Win）。
        /// 纯修饰键单独按下时不应触发快捷键。
        /// </summary>
        public static bool IsModifierKey(Key key)
        {
            return key == Key.LeftCtrl || key == Key.RightCtrl ||
                   key == Key.LeftAlt || key == Key.RightAlt ||
                   key == Key.LeftShift || key == Key.RightShift ||
                   key == Key.LWin || key == Key.RWin ||
                   key == Key.System;
        }

        /// <summary>
        /// 规范化修饰键：去除 Windows 键的干扰。
        /// </summary>
        public static ModifierKeys NormalizeModifiers(ModifierKeys modifiers)
        {
            return modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift);
        }

        /// <summary>
        /// 检测 Ctrl 键是否按下。
        /// </summary>
        public static bool IsCtrlPressed => (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

        /// <summary>
        /// 检测 Shift 键是否按下。
        /// </summary>
        public static bool IsShiftPressed => (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

        /// <summary>
        /// 检测 Alt 键是否按下。
        /// </summary>
        public static bool IsAltPressed => (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
    }
}