using System.Security.Cryptography;
using System.Text;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Models;
using Naziki_Editor.State;
using Newtonsoft.Json.Linq;

namespace Naziki_Editor.Core.Storyboard.Corrections;

public sealed class StoryboardCorrectionAnalyzer : IStoryboardCorrectionAnalyzer
{
    public const double SameTimeTolerance = 1e-6;

    private readonly IStoryboardTimeResolver _timeResolver;
    private readonly IStoryboardDocumentWriter _writer;
    private readonly bool _legacyConflictDiagnostics;

    public StoryboardCorrectionAnalyzer(
        IStoryboardTimeResolver timeResolver,
        IStoryboardDocumentWriter writer,
        bool legacyConflictDiagnostics = false)
    {
        _timeResolver = timeResolver;
        _writer = writer;
        _legacyConflictDiagnostics = legacyConflictDiagnostics;
    }

    public StoryboardCorrectionReport Scan(
        StoryboardRoot document,
        ProjectDataContext? context)
    {
        ArgumentNullException.ThrowIfNull(document);
        var issues = new List<StoryboardCorrectionIssue>();
        var triggerSpawnIds = document.triggers
            .SelectMany(trigger => trigger.Spawn)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var entry in EnumerateEntities(document))
            ScanEntity(entry.Entity, entry.Collection, entry.Path, triggerSpawnIds, context, issues);

        return new StoryboardCorrectionReport
        {
            DocumentFingerprint = Fingerprint(_writer.Write(document)),
            Issues = issues
        };
    }

    private void ScanEntity(
        IStoryboardEntity entity,
        string collection,
        string path,
        IReadOnlySet<string> triggerSpawnIds,
        ProjectDataContext? context,
        List<StoryboardCorrectionIssue> output)
    {
        var resolution = _timeResolver.ResolveEntity(entity, context, path);
        var baseState = entity.GetBaseState() as ObjectState;
        var triggerOnly = baseState?.Time is null &&
                          !string.IsNullOrWhiteSpace(entity.Id) &&
                          triggerSpawnIds.Contains(entity.Id);
        var controller = entity is C2SceneController or C2NoteController;
        var firstState = entity.GetKeyframes().Cast<object>()
            .OfType<ObjectState>().FirstOrDefault();
        var firstStateActivates = firstState?.Time is not null ||
                                  firstState?.RelativeTime is not null ||
                                  firstState?.AddTime is not null;

        if (baseState?.Time is null && !triggerOnly &&
            (_legacyConflictDiagnostics ||
             (!controller && !firstStateActivates)))
        {
            var first = firstState;
            var repairable = first?.Time is not null &&
                             resolution.BaseTimeWasInferred &&
                             !resolution.Problems.Any(problem =>
                                 problem.Path.StartsWith($"{path}.states[0].time", StringComparison.Ordinal));
            var participants = first is null
                ? Array.Empty<StoryboardCorrectionParticipant>()
                : new[]
                {
                    Participant(first, 0, false, FirstArrayIndex(first.Time),
                        $"{path}.states[0]", 0)
                };
            output.Add(new StoryboardCorrectionIssue
            {
                Id = $"{path}|missing-base",
                Kind = StoryboardCorrectionKind.MissingBaseTime,
                Code = repairable ? "BASE_TIME_MISSING" : "BASE_TIME_MISSING_UNFIXABLE",
                Path = $"{path}.time",
                CollectionName = collection,
                EntityType = entity.GetType().Name,
                Entity = entity,
                EntityId = entity.Id,
                Message = repairable
                    ? "初始状态缺少 time，可从首关键帧提升并合并。"
                    : "初始状态缺少 time，且首关键帧没有可安全复制的绝对时间。",
                CanAutomaticallyRepair = repairable,
                Participants = participants
            });
        }

        foreach (var problem in resolution.Problems)
        {
            // 没有谱面上下文时不把官方表达式误判成坏数据。
            if (context?.TimeEngine is null && problem.Code == "TIME_UNRESOLVED") continue;
            output.Add(new StoryboardCorrectionIssue
            {
                Id = $"{problem.Path}|{problem.Code}",
                Kind = StoryboardCorrectionKind.UnresolvedTime,
                Code = problem.Code,
                Path = problem.Path,
                CollectionName = collection,
                EntityType = entity.GetType().Name,
                Entity = entity,
                EntityId = entity.Id,
                Message = problem.Message,
                CanAutomaticallyRepair = false
            });
        }

        // 缺失基准时间先通过“提升首关键帧”解决，避免同时显示派生的基准冲突。
        if (baseState?.Time is null && _legacyConflictDiagnostics) return;

        var ordered = resolution.Occurrences.OrderBy(item => item.EffectiveTime).ToArray();
        var groupIndex = 0;
        while (groupIndex < ordered.Length)
        {
            var end = groupIndex + 1;
            while (end < ordered.Length &&
                   Math.Abs(ordered[end].EffectiveTime - ordered[groupIndex].EffectiveTime) <= SameTimeTolerance)
                end++;

            if (end - groupIndex > 1)
            {
                var group = ordered[groupIndex..end];
                if (!_legacyConflictDiagnostics)
                {
                    group = group.GroupBy(occurrence =>
                            SemanticKey(occurrence.State))
                        .Where(items => items.Count() > 1)
                        .Select(items => items.ToArray())
                        .FirstOrDefault() ?? [];
                    if (group.Length == 0)
                    {
                        groupIndex = end;
                        continue;
                    }
                }
                var participants = group.Select((occurrence, index) =>
                    Participant(
                        occurrence.State,
                        occurrence.StateIndex,
                        occurrence.IsBaseState,
                        occurrence.ArrayIndex,
                        occurrence.Path,
                        index)).ToArray();
                var effectiveTime = group[0].EffectiveTime;
                output.Add(new StoryboardCorrectionIssue
                {
                    Id = $"{path}|same-time|{effectiveTime:R}",
                    Kind = StoryboardCorrectionKind.SameTimeConflict,
                    Code = _legacyConflictDiagnostics
                        ? "STATE_TIME_CONFLICT"
                        : "STATE_EXACT_DUPLICATE",
                    Path = path,
                    CollectionName = collection,
                    EntityType = entity.GetType().Name,
                    Entity = entity,
                    EntityId = entity.Id,
                    EffectiveTime = effectiveTime,
                    Message = $"同一有效时间 {effectiveTime:0.######} 存在 {group.Length} 个关键帧。",
                    CanAutomaticallyRepair = true,
                    Participants = participants
                });
            }
            groupIndex = end;
        }
    }

    private string SemanticKey(ObjectState state)
    {
        var node = JObject.Parse(_writer.WriteNode(state));
        node.Remove("time");
        node.Remove("relative_time");
        node.Remove("add_time");
        return node.ToString(Newtonsoft.Json.Formatting.None);
    }

    private StoryboardCorrectionParticipant Participant(
        ObjectState state,
        int stateIndex,
        bool isBase,
        int? arrayIndex,
        string path,
        int participantIndex)
    {
        var node = JObject.Parse(_writer.WriteNode(state));
        node.Remove("time");
        node.Remove("relative_time");
        node.Remove("add_time");
        return new StoryboardCorrectionParticipant(
            participantIndex,
            stateIndex,
            isBase,
            arrayIndex,
            path,
            RawAt(state.Time, arrayIndex)?.ToString() ?? TimeModeText(state),
            node.Properties().ToDictionary(
                property => property.Name,
                property => property.Value.DeepClone(),
                StringComparer.Ordinal))
        {
            State = state
        };
    }

    private static object? RawAt(object? time, int? arrayIndex)
    {
        if (!arrayIndex.HasValue) return time;
        if (time is JArray array && arrayIndex.Value < array.Count)
            return array[arrayIndex.Value];
        if (time is System.Collections.IList list && arrayIndex.Value < list.Count)
            return list[arrayIndex.Value];
        return time;
    }

    private static int? FirstArrayIndex(object? value) =>
        value is JArray or System.Collections.IList ? 0 : null;

    private static string TimeModeText(ObjectState state)
    {
        if (state.AddTime.HasValue) return $"add_time: {state.AddTime.Value}";
        if (state.RelativeTime.HasValue) return $"relative_time: {state.RelativeTime.Value}";
        return "(继承基准时间)";
    }

    internal static string Fingerprint(string json)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }

    private static IEnumerable<(IStoryboardEntity Entity, string Collection, string Path)>
        EnumerateEntities(StoryboardRoot document)
    {
        for (var i = 0; i < document.sprites.Count; i++)
            yield return (document.sprites[i], "sprites", $"$.sprites[{i}]");
        for (var i = 0; i < document.texts.Count; i++)
            yield return (document.texts[i], "texts", $"$.texts[{i}]");
        for (var i = 0; i < document.lines.Count; i++)
            yield return (document.lines[i], "lines", $"$.lines[{i}]");
        for (var i = 0; i < document.videos.Count; i++)
            yield return (document.videos[i], "videos", $"$.videos[{i}]");
        for (var i = 0; i < document.controllers.Count; i++)
            yield return (document.controllers[i], "controllers", $"$.controllers[{i}]");
        for (var i = 0; i < document.note_controllers.Count; i++)
            yield return (document.note_controllers[i], "note_controllers", $"$.note_controllers[{i}]");
    }
}
