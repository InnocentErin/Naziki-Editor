using System.Collections;
using System.Reflection;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Serialization;
using Naziki_Editor.Models;
using Naziki_Editor.State;
using Naziki_Editor.Core.Timeline.Shared;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Naziki_Editor.Core.Storyboard.Corrections;

public sealed class StoryboardCorrectionService : IStoryboardCorrectionService
{
    private readonly IStoryboardCorrectionAnalyzer _analyzer;
    private readonly IStoryboardDocumentWriter _writer;
    private readonly IEditorSnapshotSerializer _snapshotSerializer;
    private readonly JsonSerializer _jsonSerializer =
        JsonSerializer.Create(StoryboardJsonSettings.Create());

    public StoryboardCorrectionService(
        IStoryboardCorrectionAnalyzer analyzer,
        IStoryboardDocumentWriter writer,
        IEditorSnapshotSerializer snapshotSerializer)
    {
        _analyzer = analyzer;
        _writer = writer;
        _snapshotSerializer = snapshotSerializer;
    }

    public StoryboardCorrectionPreview Preview(
        StoryboardRoot document,
        ProjectDataContext? context,
        StoryboardCorrectionPlan plan)
    {
        var before = _writer.Write(document);
        var corrected = Apply(document, context, plan);
        return new StoryboardCorrectionPreview
        {
            CorrectedDocument = corrected,
            BeforeJson = before,
            AfterJson = _writer.Write(corrected)
        };
    }

    public StoryboardRoot Apply(
        StoryboardRoot document,
        ProjectDataContext? context,
        StoryboardCorrectionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(plan);
        var current = _analyzer.Scan(document, context);
        if (!string.Equals(current.DocumentFingerprint, plan.DocumentFingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("故事板已在扫描后发生变化，请重新搜索后再修正。");

        var clone = _snapshotSerializer.Deserialize<StoryboardRoot>(
            _snapshotSerializer.Serialize(document))
            ?? throw new InvalidOperationException("无法创建故事板修正副本。");
        var cloneReport = _analyzer.Scan(clone, context);
        var issue = cloneReport.Issues.FirstOrDefault(item => item.Id == plan.IssueId)
            ?? throw new InvalidOperationException("待修正问题已不存在，请重新搜索。");

        switch (issue.Kind)
        {
            case StoryboardCorrectionKind.MissingBaseTime:
                ApplyMissingBaseTime(issue);
                break;
            case StoryboardCorrectionKind.SameTimeConflict:
                if (plan.TimeOffset is not null)
                    ApplyTimeOffset(issue, plan.TimeOffset);
                else
                    ApplyConflict(issue, plan);
                break;
            default:
                throw new InvalidOperationException("该问题不能自动修正，只能定位后手动编辑。");
        }
        return clone;
    }

    private void ApplyMissingBaseTime(StoryboardCorrectionIssue issue)
    {
        if (!issue.CanAutomaticallyRepair || issue.Participants.Count == 0)
            throw new InvalidOperationException("首关键帧没有可安全提升的绝对时间。");
        if (issue.Entity.GetBaseState() is not ObjectState baseState)
            throw new InvalidOperationException("实体缺少初始状态。");
        var first = issue.Participants[0].State;
        var firstTime = ExtractOccurrenceTime(first.Time, issue.Participants[0].ArrayIndex);
        if (firstTime is null)
            throw new InvalidOperationException("首关键帧时间为空。");

        baseState.Time = CloneValue(firstTime);
        MergeAllNonTimeProperties(first, baseState);
        RemoveOccurrence(issue.Entity, issue.Participants[0], StoryboardDeleteScope.ConflictOccurrence);
    }

    private void ApplyConflict(
        StoryboardCorrectionIssue issue,
        StoryboardCorrectionPlan plan)
    {
        var keep = issue.Participants.FirstOrDefault(
            participant => participant.ParticipantIndex == plan.KeepParticipantIndex)
            ?? throw new InvalidOperationException("没有选择有效的保留关键帧。");
        if (issue.Participants.Any(participant => participant.IsBaseState) && !keep.IsBaseState)
            throw new InvalidOperationException("冲突组包含初始状态时，初始状态必须作为保留项。");

        var configured = plan.Losers.ToDictionary(item => item.ParticipantIndex);
        var losers = issue.Participants
            .Where(participant => participant.ParticipantIndex != keep.ParticipantIndex)
            .ToArray();
        if (losers.Any(loser => !configured.ContainsKey(loser.ParticipantIndex)))
            throw new InvalidOperationException("仍有冲突关键帧没有选择处理方式。");

        var needsMigration = plan.Losers.Any(loser =>
            loser.PropertyMigrations.Any(migration =>
                migration.Mode != StoryboardPropertyMigrationMode.Skip));
        var keeperState = keep.State;
        if (needsMigration && keep.ArrayIndex.HasValue && !keep.IsBaseState)
            keeperState = SplitOccurrence(issue.Entity, keep);

        foreach (var loser in losers)
        {
            var loserPlan = configured[loser.ParticipantIndex];
            ApplyMigrations(loser.State, keeperState, loserPlan.PropertyMigrations);
        }

        // 从靠后的状态开始移除，避免同一 IList 中的索引移动影响其他操作。
        foreach (var loser in losers
                     .OrderByDescending(item => item.StateIndex)
                     .ThenByDescending(item => item.ArrayIndex ?? -1))
        {
            var loserPlan = configured[loser.ParticipantIndex];
            RemoveOccurrence(issue.Entity, loser, loserPlan.DeleteScope);
        }
    }

    private void ApplyTimeOffset(
        StoryboardCorrectionIssue issue,
        StoryboardTimeOffsetCorrection offset)
    {
        if (!double.IsFinite(offset.DeltaSeconds) ||
            Math.Abs(offset.DeltaSeconds) < StoryboardCorrectionAnalyzer.SameTimeTolerance)
            throw new InvalidOperationException("错位时间必须是非零的有限数值。");
        var participant = issue.Participants.FirstOrDefault(item =>
            item.ParticipantIndex == offset.ParticipantIndex)
            ?? throw new InvalidOperationException("没有选择有效的错位关键帧。");
        if (participant.IsBaseState)
            throw new InvalidOperationException("初始状态不能执行单关键帧错位，请选择 states 中的关键帧。");

        var state = participant.ArrayIndex.HasValue
            ? SplitOccurrence(issue.Entity, participant)
            : participant.State;
        if (state.AddTime.HasValue)
        {
            state.AddTime += (float)offset.DeltaSeconds;
            return;
        }
        if (state.RelativeTime.HasValue && state.Time is null)
        {
            state.RelativeTime += (float)offset.DeltaSeconds;
            return;
        }
        if (state.Time is not null)
        {
            state.Time = TimeExpressionUpdater.UpdateTimeExpressionByDelta(
                state.Time, offset.DeltaSeconds);
            return;
        }
        throw new InvalidOperationException("选中的关键帧没有可偏移的时间字段。");
    }

    private ObjectState SplitOccurrence(
        IStoryboardEntity entity,
        StoryboardCorrectionParticipant participant)
    {
        var keyframes = entity.GetKeyframes();
        var standalone = CloneState(participant.State);
        standalone.Time = CloneValue(
            ExtractOccurrenceTime(participant.State.Time, participant.ArrayIndex)
            ?? throw new InvalidOperationException("无法拆分空的时间数组元素。"));
        standalone.RelativeTime = null;
        standalone.AddTime = null;

        RemoveTimeArrayElement(participant.State, participant.ArrayIndex!.Value);
        var insertAt = Math.Clamp(participant.StateIndex + 1, 0, keyframes.Count);
        keyframes.Insert(insertAt, standalone);
        return standalone;
    }

    private void ApplyMigrations(
        ObjectState source,
        ObjectState target,
        IReadOnlyList<StoryboardPropertyMigration> migrations)
    {
        if (migrations.Count == 0) return;
        var sourceJson = JObject.Parse(_writer.WriteNode(source));
        var targetJson = JObject.Parse(_writer.WriteNode(target));
        foreach (var migration in migrations)
        {
            if (migration.Mode == StoryboardPropertyMigrationMode.Skip) continue;
            if (IsTimeProperty(migration.JsonPropertyName)) continue;
            var sourceValue = sourceJson[migration.JsonPropertyName];
            if (sourceValue is null) continue;
            if (migration.Mode == StoryboardPropertyMigrationMode.Add &&
                targetJson[migration.JsonPropertyName] is not null)
                continue;
            targetJson[migration.JsonPropertyName] = sourceValue.DeepClone();
        }
        var merged = targetJson.ToObject(target.GetType(), _jsonSerializer) as ObjectState
            ?? throw new InvalidOperationException("无法生成合并后的关键帧。");
        CopyWritableProperties(merged, target);
    }

    private void MergeAllNonTimeProperties(ObjectState source, ObjectState target)
    {
        var sourceJson = JObject.Parse(_writer.WriteNode(source));
        var targetJson = JObject.Parse(_writer.WriteNode(target));
        foreach (var property in sourceJson.Properties())
        {
            if (IsTimeProperty(property.Name)) continue;
            targetJson[property.Name] = property.Value.DeepClone();
        }
        var merged = targetJson.ToObject(target.GetType(), _jsonSerializer) as ObjectState
            ?? throw new InvalidOperationException("无法合并首关键帧到初始状态。");
        CopyWritableProperties(merged, target);
    }

    private static void CopyWritableProperties(ObjectState source, ObjectState target)
    {
        foreach (var property in target.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length != 0) continue;
            if (property.Name is nameof(ObjectState.Diagnostics) or nameof(ObjectState.UnknownProperties)) continue;
            property.SetValue(target, property.GetValue(source));
        }
        target.UnknownProperties.Clear();
        foreach (var pair in source.UnknownProperties)
            target.UnknownProperties[pair.Key] = pair.Value.DeepClone();
    }

    private static void RemoveOccurrence(
        IStoryboardEntity entity,
        StoryboardCorrectionParticipant participant,
        StoryboardDeleteScope scope)
    {
        if (participant.IsBaseState)
            throw new InvalidOperationException("不能删除实体的初始状态。");
        var keyframes = entity.GetKeyframes();
        if (scope == StoryboardDeleteScope.EntireKeyframe || !participant.ArrayIndex.HasValue)
        {
            keyframes.Remove(participant.State);
            return;
        }
        RemoveTimeArrayElement(participant.State, participant.ArrayIndex.Value);
        if (TimeArrayCount(participant.State.Time) == 0)
            keyframes.Remove(participant.State);
    }

    private static void RemoveTimeArrayElement(ObjectState state, int arrayIndex)
    {
        if (state.Time is JArray array)
        {
            if (arrayIndex >= 0 && arrayIndex < array.Count) array.RemoveAt(arrayIndex);
            return;
        }
        if (state.Time is IList list)
        {
            if (arrayIndex >= 0 && arrayIndex < list.Count) list.RemoveAt(arrayIndex);
            return;
        }
        throw new InvalidOperationException("目标关键帧的 time 不是数组。");
    }

    private static int TimeArrayCount(object? value) => value switch
    {
        JArray array => array.Count,
        IList list => list.Count,
        _ => -1
    };

    private static object? ExtractOccurrenceTime(object? value, int? arrayIndex)
    {
        if (!arrayIndex.HasValue) return value;
        return value switch
        {
            JArray array when arrayIndex.Value < array.Count => array[arrayIndex.Value],
            IList list when arrayIndex.Value < list.Count => list[arrayIndex.Value],
            _ => null
        };
    }

    private ObjectState CloneState(ObjectState state)
    {
        var json = JObject.Parse(_writer.WriteNode(state));
        return json.ToObject(state.GetType(), _jsonSerializer) as ObjectState
               ?? throw new InvalidOperationException("无法复制关键帧。");
    }

    private static object? CloneValue(object? value) =>
        value is JToken token ? token.DeepClone() : value;

    private static bool IsTimeProperty(string name) =>
        name is "time" or "relative_time" or "add_time";
}
