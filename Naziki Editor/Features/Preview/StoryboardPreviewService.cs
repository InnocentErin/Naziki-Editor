using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.State;
using Naziki_Editor.Models;
using Newtonsoft.Json;
using System.IO;
using Naziki_Editor.Features.Project.Resources;

namespace Naziki_Editor.Features.Preview;

public sealed class StoryboardPreviewService :
    IStoryboardPreviewDataSource,
    IStoryboardChangeFeed,
    IStoryboardPreviewPublisher
{
    private readonly IStoryboardCanonicalBridge _bridge;
    private readonly IProjectResourceService _resources;
    private readonly IChartPreviewWireAdapter _chartWire;
    private readonly object _syncRoot = new();
    private readonly List<Action<StoryboardPreviewChangeSet>> _subscribers = [];
    private long _version;
    private string _sessionId = Guid.NewGuid().ToString("N");

    public StoryboardPreviewService(
        IStoryboardCanonicalBridge bridge,
        IProjectResourceService resources,
        IChartPreviewWireAdapter chartWire)
    {
        _bridge = bridge;
        _resources = resources;
        _chartWire = chartWire;
    }

    public StoryboardPreviewService(
        IStoryboardCanonicalBridge bridge,
        IProjectResourceService resources)
        : this(bridge, resources, new ChartPreviewWireAdapter())
    {
    }
    public long CurrentVersion => Interlocked.Read(ref _version);

    public StoryboardPreviewSnapshot GetSnapshot(ProjectDataContext context, double playbackTime = 0)
    {
        ArgumentNullException.ThrowIfNull(context);
        var projectDirectory = string.IsNullOrWhiteSpace(context.ProjectFilePath)
            ? null
            : Path.GetDirectoryName(context.ProjectFilePath);
        var assetRoot = _resources.ResolvePath(context, ProjectResourceKind.Asset) ?? projectDirectory;

        var levelPath = _resources.ResolvePath(context, ProjectResourceKind.Level);
        var chartPath = _resources.ResolvePath(context, ProjectResourceKind.Chart);
        var chartDifficulty = context.ProjectData?.ChartDifficulty;
        if (string.IsNullOrWhiteSpace(chartDifficulty) &&
            !string.IsNullOrWhiteSpace(levelPath) && File.Exists(levelPath) &&
            !string.IsNullOrWhiteSpace(chartPath) && File.Exists(chartPath))
        {
            chartDifficulty = CytoidLevelChartBinding.Resolve(levelPath, chartPath);
            if (context.ProjectData is not null)
                context.ProjectData.ChartDifficulty = chartDifficulty;
        }
        var exported = _bridge.Export(context);
        if (!exported.Success)
            throw new JsonSerializationException(string.Join(
                Environment.NewLine,
                exported.Issues.Where(issue =>
                        issue.Severity == StoryboardDiagnosticSeverity.Error)
                    .Select(issue => $"{issue.Path}: {issue.Message}")));
        var runtimeStoryboardJson = exported.Json.ToString(Formatting.None);

        return new StoryboardPreviewSnapshot(
            _sessionId,
            CurrentVersion,
            context.ProjectFilePath,
            runtimeStoryboardJson,
            _chartWire.Serialize(context.Chart, context.ChartDocument),
            assetRoot,
            playbackTime)
        {
            LevelJson = !string.IsNullOrWhiteSpace(levelPath) && File.Exists(levelPath)
                ? File.ReadAllText(levelPath)
                : null,
            MusicPath = _resources.ResolvePath(context, ProjectResourceKind.Music),
            BackgroundPath = _resources.ResolvePath(context, ProjectResourceKind.Background),
            ChartDifficulty = chartDifficulty,
            ProjectId = context.ProjectData is null
                ? _sessionId
                : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(context.ProjectFilePath ?? context.ProjectData.ProjectName)))
                    .ToLowerInvariant()[..24],
            ProjectName = context.ProjectData?.ProjectName ?? "Naziki Preview"
        };
    }

    public IDisposable Subscribe(Action<StoryboardPreviewChangeSet> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_syncRoot) _subscribers.Add(handler);
        return new Subscription(() =>
        {
            lock (_syncRoot) _subscribers.Remove(handler);
        });
    }

    public long PublishIncremental(
        string source,
        IReadOnlyList<StoryboardEntityChange> changes,
        double? affectedStartTime = null,
        double? affectedEndTime = null)
    {
        var baseVersion = CurrentVersion;
        var targetVersion = Interlocked.Increment(ref _version);
        Publish(new StoryboardPreviewChangeSet(
            _sessionId,
            baseVersion,
            targetVersion,
            StoryboardPreviewChangeKind.Incremental,
            source,
            changes,
            affectedStartTime,
            affectedEndTime));
        return targetVersion;
    }

    public long PublishReset(string source)
    {
        var baseVersion = CurrentVersion;
        var targetVersion = Interlocked.Increment(ref _version);
        Publish(new StoryboardPreviewChangeSet(
            _sessionId, baseVersion, targetVersion,
            StoryboardPreviewChangeKind.Reset, source, []));
        return targetVersion;
    }

    public void EndSession()
    {
        Publish(new StoryboardPreviewChangeSet(
            _sessionId, CurrentVersion, CurrentVersion,
            StoryboardPreviewChangeKind.SessionEnded, "Project.SessionEnded", []));
    }

    public void StartSession()
    {
        _sessionId = Guid.NewGuid().ToString("N");
        Interlocked.Exchange(ref _version, 0);
    }

    private void Publish(StoryboardPreviewChangeSet changes)
    {
        Action<StoryboardPreviewChangeSet>[] handlers;
        lock (_syncRoot) handlers = _subscribers.ToArray();
        foreach (var handler in handlers) handler(changes);
    }

    private sealed class Subscription : IDisposable
    {
        private Action? _dispose;
        public Subscription(Action dispose) => _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
