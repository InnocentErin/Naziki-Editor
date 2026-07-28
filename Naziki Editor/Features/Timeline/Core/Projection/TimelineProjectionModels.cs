using Naziki_Editor.Models;

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
