using System.Collections;
using System.Globalization;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Models;
using Naziki_Editor.State;
using Newtonsoft.Json.Linq;

namespace Naziki_Editor.Core.Storyboard.Corrections;

public sealed class StoryboardTimeResolver : IStoryboardTimeResolver
{
    public EntityTimeResolution ResolveEntity(
        IStoryboardEntity entity,
        ProjectDataContext? context,
        string path)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var occurrences = new List<StoryboardTimeOccurrence>();
        var problems = new List<StoryboardTimeProblem>();
        var baseState = entity.GetBaseState() as ObjectState;
        var noteId = ResolveBoundNoteId(entity);
        var baseValues = ResolveRawTimes(baseState?.Time, context, noteId, $"{path}.time", problems);
        var inferred = false;

        if (baseValues.Count == 0 && baseState?.Time is null)
        {
            var first = (entity.GetKeyframes() ?? Array.Empty<object>())
                .Cast<object>().OfType<ObjectState>().FirstOrDefault();
            if (first?.Time is not null)
            {
                var firstValues = ResolveRawTimes(
                    first.Time, context, noteId, $"{path}.states[0].time", problems,
                    reportFailure: false);
                if (firstValues.Count > 0)
                {
                    baseValues = [firstValues[0]];
                    inferred = true;
                }
            }
        }

        var hasBase = baseValues.Count > 0;
        var baseTime = hasBase ? baseValues[0].Value : 0d;
        if (baseState is not null && hasBase)
        {
            occurrences.Add(new StoryboardTimeOccurrence(
                baseState, -1, true, baseValues[0].ArrayIndex,
                baseValues[0].Raw, baseTime, path));
        }

        var previousTime = baseTime;
        var states = entity.GetKeyframes();
        for (var index = 0; index < states.Count; index++)
        {
            if (states[index] is not ObjectState state) continue;
            var statePath = $"{path}.states[{index}]";
            var values = ResolveStateTimes(
                state, baseTime, previousTime, context, noteId, statePath, problems);
            foreach (var value in values)
            {
                occurrences.Add(new StoryboardTimeOccurrence(
                    state, index, false, value.ArrayIndex, value.Raw, value.Value, statePath));
            }
            if (values.Count > 0) previousTime = values[^1].Value;
        }

        return new EntityTimeResolution
        {
            Entity = entity,
            Path = path,
            HasValidBaseTime = hasBase,
            BaseTimeWasInferred = inferred,
            BaseTime = baseTime,
            Occurrences = occurrences,
            Problems = problems
        };
    }

    private static List<ResolvedValue> ResolveStateTimes(
        ObjectState state,
        double baseTime,
        double previousTime,
        ProjectDataContext? context,
        string? noteId,
        string path,
        List<StoryboardTimeProblem> problems)
    {
        if (state.AddTime.HasValue)
            return [new ResolvedValue(state.AddTime.Value, previousTime + state.AddTime.Value, null)];

        if (state.RelativeTime.HasValue)
        {
            if (state.Time is not null)
            {
                return ResolveRawTimes(state.Time, context, noteId, $"{path}.time", problems)
                    .Select(value => value with { Value = value.Value + state.RelativeTime.Value })
                    .ToList();
            }
            return [new ResolvedValue(state.RelativeTime.Value, previousTime + state.RelativeTime.Value, null)];
        }

        if (state.Time is not null)
            return ResolveRawTimes(state.Time, context, noteId, $"{path}.time", problems);

        if (double.IsFinite(baseTime))
            return [new ResolvedValue(null, baseTime, null)];

        problems.Add(new("TIME_MISSING", path, "关键帧缺少可用的时间定义。"));
        return [];
    }

    private static List<ResolvedValue> ResolveRawTimes(
        object? value,
        ProjectDataContext? context,
        string? noteId,
        string path,
        List<StoryboardTimeProblem> problems,
        bool reportFailure = true)
    {
        var result = new List<ResolvedValue>();
        if (value is null) return result;

        if (value is IEnumerable enumerable and not string and not JValue)
        {
            var arrayIndex = 0;
            foreach (var item in enumerable)
            {
                var itemValues = ResolveRawTimes(
                    item, context, noteId, $"{path}[{arrayIndex}]", problems, reportFailure);
                result.AddRange(itemValues.Select(itemValue =>
                    itemValue with { ArrayIndex = arrayIndex }));
                arrayIndex++;
            }
            return result;
        }

        var raw = value is JValue jValue ? jValue.Value : value;
        var text = raw?.ToString()?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(noteId))
            text = text.Replace("$note", noteId, StringComparison.Ordinal);

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric) ||
            double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out numeric))
        {
            result.Add(new(raw, numeric, null));
            return result;
        }

        var resolved = context?.TimeEngine?.ParseCytoidTimeExpression(
            text, context.Chart?.note_list) ?? double.NaN;
        if (double.IsFinite(resolved))
        {
            result.Add(new(raw, resolved, null));
            return result;
        }

        if (reportFailure)
            problems.Add(new("TIME_UNRESOLVED", path, $"无法解析时间表达式“{text}”。"));
        return result;
    }

    private static string? ResolveBoundNoteId(IStoryboardEntity entity)
    {
        if (FastReflectionHelper.TryGetValue(entity, "Note", out var note) && note is not null)
            return note.ToString()?.Trim();
        if (FastReflectionHelper.TryGetValue(entity.GetBaseState(), "NoteTarget", out note) && note is not null)
            return note.ToString()?.Trim();
        return null;
    }

    private sealed record ResolvedValue(object? Raw, double Value, int? ArrayIndex);
}
