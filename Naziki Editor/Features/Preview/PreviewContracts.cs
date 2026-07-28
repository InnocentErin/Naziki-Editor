using Naziki_Editor.State;
using Newtonsoft.Json.Linq;

namespace Naziki_Editor.Features.Preview;

public enum StoryboardPreviewChangeKind
{
    Incremental,
    Reset,
    SessionEnded
}

public enum PreviewPlaybackState
{
    Stopped,
    Playing,
    Paused
}

public enum StoryboardEntityChangeOperation
{
    Add,
    Update,
    Delete
}

public enum PreviewAvailabilityState
{
    Disabled,
    RuntimeMissing,
    Starting,
    Connecting,
    Ready,
    InvalidData,
    Disconnected,
    Faulted
}

public enum PreviewDiagnosticSeverity
{
    Information,
    Warning,
    Error
}

public enum PreviewDiagnosticSource
{
    Editor,
    Level,
    Storyboard,
    Chart,
    Asset,
    Transport,
    Unity
}

public sealed record StoryboardPreviewSnapshot(
    string SessionId,
    long Version,
    string? ProjectPath,
    string StoryboardJson,
    string? ChartJson,
    string? AssetRoot,
    double PlaybackTime)
{
    public string? LevelJson { get; init; }
    public string? MusicPath { get; init; }
    public string? BackgroundPath { get; init; }
    public string? ProjectId { get; init; }
    public string ProjectName { get; init; } = "Naziki Preview";
}

public sealed record StoryboardEntityChange(
    string EntityId,
    string Operation,
    string? EntityJson,
    IReadOnlyList<string> Properties)
{
    public string? EntityType { get; init; }
    public IReadOnlyList<string> DependencyIds { get; init; } = [];
    public IReadOnlyList<string> AssetReferences { get; init; } = [];

    public StoryboardEntityChangeOperation TypedOperation =>
        Enum.TryParse<StoryboardEntityChangeOperation>(Operation, true, out var value)
            ? value
            : StoryboardEntityChangeOperation.Update;
}

public sealed record StoryboardPreviewChangeSet(
    string SessionId,
    long BaseVersion,
    long TargetVersion,
    StoryboardPreviewChangeKind Kind,
    string Source,
    IReadOnlyList<StoryboardEntityChange> EntityChanges,
    double? AffectedStartTime = null,
    double? AffectedEndTime = null);

public sealed record PreviewDiagnostic(
    string Code,
    string Message,
    PreviewDiagnosticSeverity Severity,
    PreviewDiagnosticSource Source,
    string? Path = null,
    string? EntityId = null,
    string? PropertyName = null,
    string? Suggestion = null);

public sealed record PreviewValidationResult(
    long EditorVersion,
    IReadOnlyList<PreviewDiagnostic> Diagnostics)
{
    public bool IsValid => Diagnostics.All(item => item.Severity != PreviewDiagnosticSeverity.Error);
    public int WarningCount => Diagnostics.Count(item => item.Severity == PreviewDiagnosticSeverity.Warning);
    public static PreviewValidationResult Valid(long version) => new(version, []);
}

public sealed record LastKnownGoodPreview(
    StoryboardPreviewSnapshot Snapshot,
    string MaterializedDirectory,
    DateTimeOffset AcceptedAt,
    double PlaybackTime,
    PreviewPlaybackState PlaybackState);

public sealed record PreviewPerformanceSample(
    double FramesPerSecond,
    double AverageFrameMilliseconds,
    int RenderWidth,
    int RenderHeight,
    long CacheBytes,
    double EffectiveRenderScale,
    long SuppressedExceptions,
    long DroppedTelemetryMessages);

public sealed record PreviewSettings(
    string Quality,
    int RenderScalePercent,
    string FrameRate,
    bool AdaptiveQuality,
    int InactiveFrameRate,
    double FrameSkipThresholdMilliseconds,
    int RenderThreads,
    long MaxCacheBytes,
    bool HardwareAcceleration,
    int ExternalClockRate,
    int AdaptiveMinimumScalePercent,
    string AspectRatio);

public sealed record PreviewProtocolMessage(
    string Type,
    string SessionId,
    string RequestId,
    long EditorVersion,
    long BasePreviewVersion,
    long TargetPreviewVersion,
    JObject Payload);

public interface IStoryboardPreviewDataSource
{
    long CurrentVersion { get; }
    StoryboardPreviewSnapshot GetSnapshot(ProjectDataContext context, double playbackTime = 0);
}

public interface IStoryboardChangeFeed
{
    IDisposable Subscribe(Action<StoryboardPreviewChangeSet> handler);
}

public interface IStoryboardPreviewHost
{
    void Attach(IStoryboardPreviewDataSource dataSource, IStoryboardChangeFeed changeFeed);
    void Detach();
    void ApplySnapshot(StoryboardPreviewSnapshot snapshot);
    void ApplyChanges(StoryboardPreviewChangeSet changes);
    void Seek(double seconds);
    void SetPlaybackState(PreviewPlaybackState state);
}

public interface IPreviewValidationService
{
    PreviewValidationResult Validate(ProjectDataContext context, StoryboardPreviewSnapshot snapshot);
}

public interface IPreviewDiagnosticsService
{
    PreviewAvailabilityState Availability { get; }
    IReadOnlyList<PreviewDiagnostic> Diagnostics { get; }
    LastKnownGoodPreview? LastKnownGood { get; }
    event EventHandler? Changed;
}

public interface IStoryboardPreviewPublisher
{
    long PublishIncremental(
        string source,
        IReadOnlyList<StoryboardEntityChange> changes,
        double? affectedStartTime = null,
        double? affectedEndTime = null);
    long PublishReset(string source);
    void EndSession();
    void StartSession();
}
