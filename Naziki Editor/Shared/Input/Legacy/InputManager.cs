namespace Naziki_Editor.Core.Input
{
    using System;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Input;

    /// <summary>
    /// 统一输入管理器。
    /// 管理拖拽会话生命周期，提供鼠标/键盘状态查询。
    /// 通过 DI 注入供各 View 控件使用，避免重复的输入处理代码。
    /// 
    /// 使用方式：
    /// 1. 在 View 的 MouseDown 中调用 BeginDrag
    /// 2. 在 View 的 MouseMove 中调用 UpdateDrag
    /// 3. 在 View 的 MouseUp 中调用 EndDrag
    /// </summary>
    public class InputManager
    {
        private readonly object _lock = new();
        private IDragHandler? _activeDragHandler;
        private UIElement? _capturedElement;
        private DragState _currentDragState;
        private bool _isDragging;

        /// <summary>
        /// 当前是否有活跃的拖拽操作。
        /// </summary>
        public bool IsDragging
        {
            get { lock (_lock) { return _isDragging; } }
        }

        /// <summary>
        /// 开始拖拽操作。
        /// </summary>
        /// <param name="handler">拖拽处理器。</param>
        /// <param name="capturedElement">需要捕获鼠标的元素。</param>
        /// <param name="startPoint">拖拽起始点。</param>
        public void BeginDrag(IDragHandler handler, UIElement capturedElement, Point startPoint)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (capturedElement == null) throw new ArgumentNullException(nameof(capturedElement));

            lock (_lock)
            {
                // 如果已有活跃拖拽，先取消
                if (_isDragging && _activeDragHandler != null)
                {
                    _activeDragHandler.OnDragCancelled();
                }

                _activeDragHandler = handler;
                _capturedElement = capturedElement;
                _currentDragState = DragState.CreateInitial(startPoint);
                _isDragging = true;
            }

            capturedElement.CaptureMouse();
            handler.OnDragStarted(startPoint);
        }

        /// <summary>
        /// 更新拖拽位置。
        /// </summary>
        /// <param name="currentPoint">当前鼠标位置。</param>
        /// <returns>true 表示拖拽已被处理。</returns>
        public bool UpdateDrag(Point currentPoint)
        {
            IDragHandler? handler;
            DragState state;

            lock (_lock)
            {
                if (!_isDragging || _activeDragHandler == null)
                    return false;

                handler = _activeDragHandler;
                _currentDragState = _currentDragState.Update(currentPoint);
                state = _currentDragState;
            }

            if (state.HasStarted)
            {
                return handler.OnDragDelta(currentPoint, new Point(
                    currentPoint.X - state.StartPoint.X,
                    currentPoint.Y - state.StartPoint.Y));
            }

            return false;
        }

        /// <summary>
        /// 结束拖拽操作。
        /// </summary>
        /// <param name="endPoint">拖拽结束点。</param>
        public void EndDrag(Point endPoint)
        {
            IDragHandler? handler;
            UIElement? capturedElement;

            lock (_lock)
            {
                if (!_isDragging || _activeDragHandler == null)
                    return;

                handler = _activeDragHandler;
                capturedElement = _capturedElement;
                _activeDragHandler = null;
                _capturedElement = null;
                _isDragging = false;
            }

            capturedElement?.ReleaseMouseCapture();
            handler.OnDragCompleted(endPoint);
        }

        /// <summary>
        /// 取消拖拽操作（如按 Esc 键或失去焦点）。
        /// </summary>
        public void CancelDrag()
        {
            IDragHandler? handler;
            UIElement? capturedElement;

            lock (_lock)
            {
                if (!_isDragging || _activeDragHandler == null)
                    return;

                handler = _activeDragHandler;
                capturedElement = _capturedElement;
                _activeDragHandler = null;
                _capturedElement = null;
                _isDragging = false;
            }

            capturedElement?.ReleaseMouseCapture();
            handler.OnDragCancelled();
        }
    }
}