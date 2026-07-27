namespace Naziki_Editor.Core.Input
{
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Input;

    /// <summary>
    /// 鼠标坐标转换辅助工具类。
    /// 提供屏幕坐标 → 时间坐标、屏幕坐标 → 物理坐标等转换方法。
    /// 所有方法均为纯计算，无副作用，可安全用于单元测试。
    /// </summary>
    public static class MouseCoordinateHelper
    {
        /// <summary>
        /// 将鼠标在元素上的 X 坐标转换为时间（秒），考虑滚动偏移。
        /// </summary>
        /// <param name="visualX">鼠标在可视区域内的 X 坐标。</param>
        /// <param name="scrollOffset">水平滚动偏移量。</param>
        /// <param name="pixelsPerSecond">像素/秒的比例。</param>
        /// <returns>转换后的时间（秒），不会小于 0。</returns>
        public static double XToTime(double visualX, double scrollOffset, double pixelsPerSecond)
        {
            if (pixelsPerSecond <= 0) return 0;
            double absoluteX = visualX + scrollOffset;
            return Math.Max(0, absoluteX / pixelsPerSecond);
        }

        /// <summary>
        /// 将时间（秒）转换为像素 X 坐标，考虑滚动偏移。
        /// </summary>
        public static double TimeToX(double timeSeconds, double scrollOffset, double pixelsPerSecond)
        {
            return (timeSeconds * pixelsPerSecond) - scrollOffset;
        }

        /// <summary>
        /// 获取鼠标相对于指定元素的坐标。
        /// </summary>
        public static Point GetPositionRelativeTo(FrameworkElement relativeTo, MouseEventArgs e)
        {
            return e.GetPosition(relativeTo);
        }

        /// <summary>
        /// 检测是否有指定的修饰键按下。
        /// </summary>
        public static bool IsModifierPressed(System.Windows.Input.ModifierKeys modifier)
        {
            return (System.Windows.Input.Keyboard.Modifiers & modifier) == modifier;
        }

        /// <summary>
        /// 检测是否仅按下指定修饰键（无其他修饰键）。
        /// </summary>
        public static bool IsOnlyModifierPressed(System.Windows.Input.ModifierKeys modifier)
        {
            var currentModifiers = System.Windows.Input.Keyboard.Modifiers;
            return currentModifiers == modifier;
        }

        /// <summary>
        /// 判断鼠标事件是否为双击。
        /// </summary>
        public static bool IsDoubleClick(MouseButtonEventArgs e)
        {
            return e.ClickCount == 2;
        }
    }
}