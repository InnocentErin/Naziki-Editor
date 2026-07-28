using Naziki_Editor.Models;
using Newtonsoft.Json.Linq;

namespace Naziki_Editor.Core.Timeline.Projection;

public enum TimelineDiagnosticSeverity
{
    Warning,
    Error
}

public sealed record TimelineDiagnostic(
    string Code,
    string Message,
    TimelineDiagnosticSeverity Severity);

public sealed record ProjectedTimelineState(
    ObjectState SourceState,
    double AbsoluteTime,
    string? TemplateName,
    IReadOnlyList<string> TemplateSourcePath,
    bool IsTemplateExpanded);

public sealed class EntityTimelineProjection
{
    public required IStoryboardEntity Entity { get; init; }
    public double BaseStateTime { get; init; }
    public double LastStateTime { get; init; }
    public double Duration => Math.Max(0, LastStateTime - BaseStateTime);
    public bool HasValidBaseTime { get; init; }
    public bool HasErrors => Diagnostics.Any(d => d.Severity == TimelineDiagnosticSeverity.Error);
    public IReadOnlyList<ProjectedTimelineState> States { get; init; } = [];
    public IReadOnlyList<TimelineDiagnostic> Diagnostics { get; init; } = [];
}

/// <summary>
/// Immutable editor projection. Unlike <see cref="ProjectedTimelineState"/>,
/// it never exposes a mutable wire-format state object to timeline clients.
/// </summary>
public sealed record CanonicalProjectedTimelineFrame(
    string OccurrenceId,
    string FrameId,
    double? AbsoluteTime,
    int Sequence,
    JObject EffectiveState,
    StoryboardTimePosition SourceTime,
    string? SourceTemplate,
    int? BoundNoteId)
{
    public bool IsInstantBoundaryWith(CanonicalProjectedTimelineFrame other) =>
        AbsoluteTime.HasValue &&
        other.AbsoluteTime.HasValue &&
        Math.Abs(AbsoluteTime.Value - other.AbsoluteTime.Value) < 0.0000001 &&
        Sequence != other.Sequence;
}

public sealed record CanonicalEntityTimelineProjection(
    string OccurrenceId,
    string EditorId,
    EditorStoryboardEntityKind Kind,
    string? RuntimeId,
    int? BoundNoteId,
    double? ActivationTime,
    StoryboardActivationMode ActivationMode,
    JObject EffectiveBaseState,
    IReadOnlyList<CanonicalProjectedTimelineFrame> Frames,
    IReadOnlyList<TimelineDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(item =>
        item.Severity == TimelineDiagnosticSeverity.Error);

    public double? LastStateTime => Frames
        .Where(frame => frame.AbsoluteTime.HasValue)
        .Select(frame => frame.AbsoluteTime)
        .DefaultIfEmpty(ActivationTime)
        .Max();
}
