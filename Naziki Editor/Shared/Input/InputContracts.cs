using Naziki_Editor.Models;
using Naziki_Editor.UI.ViewModels;
using System.Windows;
using System.Windows.Input;

namespace Naziki_Editor.Shared.Input;

public interface IEditorDragPayload
{
    string Kind { get; }
}

public sealed record AssetDragPayload(
    AssetItemViewModel Asset,
    IStoryboardEntity Entity) : IEditorDragPayload
{
    public string Kind => "Asset";
}

public sealed record EntityDragPayload(
    IStoryboardEntity Entity) : IEditorDragPayload
{
    public string Kind => "Entity";
}

public sealed record KeyframeDragPayload(
    string EntityId,
    string PropertyName,
    object Keyframe) : IEditorDragPayload
{
    public string Kind => "Keyframe";
}

public sealed record TimelineDropContext(
    double RawTime,
    double SnappedTime,
    int? TargetTrack,
    ModifierKeys Modifiers,
    Point Position);

public interface IDropTargetHandler<in TPayload> where TPayload : IEditorDragPayload
{
    bool CanDrop(TPayload payload, TimelineDropContext context, out string? rejectionReason);
    void Drop(TPayload payload, TimelineDropContext context);
}

public interface IInputSessionManager
{
    bool IsDragging { get; }
    void BeginDrag(Core.Input.IDragHandler handler, UIElement capturedElement, Point startPoint);
    bool UpdateDrag(Point currentPoint);
    void EndDrag(Point endPoint);
    void CancelDrag();
}
