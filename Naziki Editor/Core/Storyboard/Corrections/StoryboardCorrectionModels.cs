using Naziki_Editor.Models;

namespace Naziki_Editor.Core.Storyboard.Corrections;

public enum StoryboardCorrectionKind
{
    MissingBaseTime,
    SameTimeConflict,
    UnresolvedTime
}

public enum StoryboardDeleteScope
{
    ConflictOccurrence,
    EntireKeyframe
}

public enum StoryboardPropertyMigrationMode
{
    Skip,
    Add,
    Overwrite
}

public sealed record StoryboardTimeProblem(
    string Code,
    string Path,
    string Message);

public sealed record StoryboardTimeOccurrence(
    ObjectState State,
    int StateIndex,
    bool IsBaseState,
    int? ArrayIndex,
    object? RawTime,
    double EffectiveTime,
    string Path)
{
    public string DisplayTime => RawTime?.ToString() ?? "(缺失)";
}

public sealed class EntityTimeResolution
{
    public required IStoryboardEntity Entity { get; init; }
    public required string Path { get; init; }
    public bool HasValidBaseTime { get; init; }
    public bool BaseTimeWasInferred { get; init; }
    public double BaseTime { get; init; }
    public IReadOnlyList<StoryboardTimeOccurrence> Occurrences { get; init; } = [];
    public IReadOnlyList<StoryboardTimeProblem> Problems { get; init; } = [];
}

public sealed record StoryboardCorrectionParticipant(
    int ParticipantIndex,
    int StateIndex,
    bool IsBaseState,
    int? ArrayIndex,
    string Path,
    string RawTime,
    IReadOnlyDictionary<string, Newtonsoft.Json.Linq.JToken> Properties)
{
    public required ObjectState State { get; init; }
}

public sealed class StoryboardCorrectionIssue
{
    public required string Id { get; init; }
    public required StoryboardCorrectionKind Kind { get; init; }
    public required string Code { get; init; }
    public required string Path { get; init; }
    public required string CollectionName { get; init; }
    public required string EntityType { get; init; }
    public required IStoryboardEntity Entity { get; init; }
    public string? EntityId { get; init; }
    public string Message { get; init; } = string.Empty;
    public double? EffectiveTime { get; init; }
    public bool CanAutomaticallyRepair { get; init; }
    public IReadOnlyList<StoryboardCorrectionParticipant> Participants { get; init; } = [];
}

public sealed class StoryboardCorrectionReport
{
    public required string DocumentFingerprint { get; init; }
    public IReadOnlyList<StoryboardCorrectionIssue> Issues { get; init; } = [];
    public int RepairableCount => Issues.Count(issue => issue.CanAutomaticallyRepair);
}

public sealed record StoryboardPropertyMigration(
    string JsonPropertyName,
    StoryboardPropertyMigrationMode Mode);

public sealed class StoryboardLoserCorrection
{
    public required int ParticipantIndex { get; init; }
    public StoryboardDeleteScope DeleteScope { get; init; }
    public IReadOnlyList<StoryboardPropertyMigration> PropertyMigrations { get; init; } = [];
}

public sealed class StoryboardCorrectionPlan
{
    public required string DocumentFingerprint { get; init; }
    public required string IssueId { get; init; }
    public int KeepParticipantIndex { get; init; }
    public IReadOnlyList<StoryboardLoserCorrection> Losers { get; init; } = [];
    public StoryboardTimeOffsetCorrection? TimeOffset { get; init; }
}

public sealed record StoryboardTimeOffsetCorrection(
    int ParticipantIndex,
    double DeltaSeconds);

public sealed class StoryboardCorrectionPreview
{
    public required StoryboardRoot CorrectedDocument { get; init; }
    public required string BeforeJson { get; init; }
    public required string AfterJson { get; init; }
}
