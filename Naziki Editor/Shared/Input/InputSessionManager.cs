using Naziki_Editor.Core.Input;
using System.Windows;

namespace Naziki_Editor.Shared.Input;

public sealed class InputSessionManager : IInputSessionManager
{
    private readonly InputManager _inner = new();
    public bool IsDragging => _inner.IsDragging;
    public void BeginDrag(IDragHandler handler, UIElement capturedElement, Point startPoint) =>
        _inner.BeginDrag(handler, capturedElement, startPoint);
    public bool UpdateDrag(Point currentPoint) => _inner.UpdateDrag(currentPoint);
    public void EndDrag(Point endPoint) => _inner.EndDrag(endPoint);
    public void CancelDrag() => _inner.CancelDrag();
}
