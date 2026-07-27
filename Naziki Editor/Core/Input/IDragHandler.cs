namespace Naziki_Editor.Core.Input
{
    using System.Windows;

    /// <summary>
    /// 通用拖拽处理器接口。
    /// 封装了拖拽操作的完整生命周期：开始 → 移动 → 结束 → 取消。
    /// 所有需要拖拽功能的 View 控件通过此接口统一处理拖拽逻辑。
    /// </summary>
    public interface IDragHandler
    {
        /// <summary>拖拽操作开始。</summary>
        void OnDragStarted(Point startPoint);

        /// <summary>拖拽操作进行中。返回 true 表示已处理。</summary>
        bool OnDragDelta(Point currentPoint, Point delta);

        /// <summary>拖拽操作结束。</summary>
        void OnDragCompleted(Point endPoint);

        /// <summary>拖拽操作取消（如按 Esc 或失去焦点）。</summary>
        void OnDragCancelled();
    }
}