using Naziki_Editor.Core;
using Naziki_Editor.Core.Timeline.Models;
using Naziki_Editor.Core.Timeline.Shared;
using Naziki_Editor.Models;
using Naziki_Editor.State;

namespace Naziki_Editor.Core.Timeline.Projection;

public sealed record MicroTimelineTrackProjection(
    PropertyTrackDescriptor Descriptor,
    object? BaseValue,
    IReadOnlyList<DecodedKeyframeBox> Keyframes);

public sealed class MicroTimelineSession
{
    public required MicroEditorContext EditorContext { get; init; }
    public required EntityTimelineProjection EntityProjection { get; init; }
    public required IReadOnlyList<MicroTimelineTrackProjection> Tracks { get; init; }
    public required IReadOnlyList<PropertyDependencyGroup> DependencyGroups { get; init; }
    public double ContentEndTime { get; init; }
}

public interface IMicroTimelineSessionFactory
{
    MicroTimelineSession Build(
        MicroEditorContext editorContext,
        ProjectDataContext projectContext,
        CancellationToken cancellationToken);
}

public sealed class MicroTimelineSessionFactory : IMicroTimelineSessionFactory
{
    private readonly ITimelineProjectionService _projectionService;
    private readonly IPropertyMetadataCatalog _propertyCatalog;

    public MicroTimelineSessionFactory(
        ITimelineProjectionService projectionService,
        IPropertyMetadataCatalog propertyCatalog)
    {
        _projectionService = projectionService;
        _propertyCatalog = propertyCatalog;
    }

    public MicroTimelineSession Build(
        MicroEditorContext editorContext,
        ProjectDataContext projectContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(editorContext);
        ArgumentNullException.ThrowIfNull(editorContext.Entity);
        ArgumentNullException.ThrowIfNull(projectContext);

        cancellationToken.ThrowIfCancellationRequested();
        var projection = _projectionService.BuildEntityProjection(editorContext.Entity, projectContext);
        var descriptors = _propertyCatalog.Discover(editorContext.Entity);
        var tracks = new List<MicroTimelineTrackProjection>(descriptors.Count);
        var baseState = editorContext.Entity.GetBaseState();

        foreach (var descriptor in descriptors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FastReflectionHelper.TryGetValue(baseState, descriptor.PropertyName, out var baseValue);
            var frames = new List<DecodedKeyframeBox>();
            foreach (var projectedState in projection.States)
            {
                if (!FastReflectionHelper.TryGetValue(
                        projectedState.SourceState, descriptor.PropertyName, out var value) ||
                    value == null)
                    continue;
                frames.Add(new DecodedKeyframeBox
                {
                    State = projectedState.SourceState,
                    VisualRelTime = projectedState.AbsoluteTime - projection.BaseStateTime,
                    Value = value,
                    IsTemplateExpanded = projectedState.IsTemplateExpanded,
                    TemplateSourcePath = projectedState.TemplateSourcePath
                });
            }
            tracks.Add(new(descriptor, baseValue, frames));
        }

        var chartEnd = 10d;
        if (projectContext.Chart?.note_list is { Count: > 0 } notes)
            chartEnd = projectContext.TimeEngine.TickToSeconds(notes[^1].tick) + 5;
        var contentEnd = Math.Max(chartEnd, projection.LastStateTime + 5);
        // Guard malformed charts without creating multi-million-pixel WPF surfaces.
        if (!double.IsFinite(contentEnd) || contentEnd < 0 || contentEnd > 86_400)
            contentEnd = Math.Max(10, projection.LastStateTime + 5);

        return new MicroTimelineSession
        {
            EditorContext = editorContext,
            EntityProjection = projection,
            Tracks = tracks,
            DependencyGroups = _propertyCatalog.DependencyGroups,
            ContentEndTime = contentEnd
        };
    }
}
