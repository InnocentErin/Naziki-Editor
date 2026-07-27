namespace Naziki_Editor.Core.Input
{
    using System.Windows;

    /// <summary>
    /// 拖拽操作的不可变状态快照。
    /// 用于在拖拽过程中传递起始位置和当前位置信息。
    /// </summary>
    public readonly struct DragState
    {
        /// <summary>拖拽起始点（相对于被拖拽元素的父容器）。</summary>
        public Point StartPoint { get; }

        /// <summary>当前鼠标位置（相对于被拖拽元素的父容器）。</summary>
        public Point CurrentPoint { get; }

        /// <summary>从起始点到当前点的位移向量。</summary>
        public Vector Delta => CurrentPoint - StartPoint;

        /// <summary>拖拽是否已开始（鼠标移动超过死区阈值）。</summary>
        public bool HasStarted { get; }

        public DragState(Point startPoint, Point currentPoint, bool hasStarted)
        {
            StartPoint = startPoint;
            CurrentPoint = currentPoint;
            HasStarted = hasStarted;
        }

        /// <summary>
        /// 创建初始状态（未开始拖拽）。
        /// </summary>
        public static DragState CreateInitial(Point startPoint)
            => new DragState(startPoint, startPoint, false);

        /// <summary>
        /// 根据当前鼠标位置更新状态。
        /// 如果位移超过死区阈值（3px），自动标记为已开始。
        /// </summary>
        public DragState Update(Point currentPoint, double deadZone = 3.0)
            => new DragState(StartPoint, currentPoint, HasStarted || Delta.Length > deadZone);
    }
}