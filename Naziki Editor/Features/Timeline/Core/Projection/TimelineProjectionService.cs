using System.Collections;
using System.Globalization;
using Naziki_Editor.Models;
using Naziki_Editor.State;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Storyboard.Corrections;
using Naziki_Editor.Core.Storyboard.Canonical;

namespace Naziki_Editor.Core.Timeline.Projection;

/// <summary>
/// Builds the non-destructive, expanded timeline view used by both timeline editors.
/// The source storyboard remains template-based; expansion only exists in this projection.
/// </summary>
public sealed class TimelineProjectionService : ITimelineProjectionService
{
    private readonly IStoryboardTimeResolver _timeResolver;
    private readonly IStoryboardMaterializer _materializer;

    public TimelineProjectionService()
        : this(new StoryboardTimeResolver(),
            new StoryboardMaterializer(
                new StoryboardTimePositionResolver(),
                new NoteQueryService()))
    {
    }

    public TimelineProjectionService(IStoryboardTimeResolver timeResolver)
        : this(timeResolver,
            new StoryboardMaterializer(
                new StoryboardTimePositionResolver(),
                new NoteQueryService()))
    {
    }

    public TimelineProjectionService(IStoryboardTimeResolver timeResolver,
        IStoryboardMaterializer materializer)
    {
        _timeResolver = timeResolver;
        _materializer = materializer;
    }

    public IReadOnlyList<CanonicalEntityTimelineProjection>
        BuildCanonicalProjections(EditorStoryboardDocument document,
            C2Chart? chart, ITimeEngine? timeEngine)
    {
        ArgumentNullException.ThrowIfNull(document);
        var materialized = _materializer.Materialize(document, chart, timeEngine);
        var sharedDiagnostics = materialized.Issues
            .Where(issue => issue.Path == "$" ||
                            !issue.Path.StartsWith("editor:",
                                StringComparison.Ordinal))
            .Select(ToTimelineDiagnostic)
            .ToArray();

        return materialized.Entities.Select(entity =>
        {
            var prefix = $"editor:{entity.EditorId}";
            var diagnostics = materialized.Issues
                .Where(issue => issue.Path.StartsWith(prefix,
                    StringComparison.Ordinal))
                .Select(ToTimelineDiagnostic)
                .Concat(sharedDiagnostics)
                .ToArray();
            var frames = entity.Frames
                .OrderBy(frame => frame.EffectiveTime ??
                                  double.PositiveInfinity)
                .ThenBy(frame => frame.Sequence)
                .Select(frame => new CanonicalProjectedTimelineFrame(
                    frame.OccurrenceId,
                    frame.FrameId,
                    frame.EffectiveTime,
                    frame.Sequence,
                    (Newtonsoft.Json.Linq.JObject)
                        frame.EffectiveState.DeepClone(),
                    frame.Time,
                    frame.SourceTemplate,
                    frame.BoundNoteId))
                .ToArray();
            return new CanonicalEntityTimelineProjection(
                entity.OccurrenceId,
                entity.EditorId,
                entity.Kind,
                entity.RuntimeId,
                entity.BoundNoteId,
                entity.EffectiveActivationTime,
                entity.ActivationMode,
                (Newtonsoft.Json.Linq.JObject)entity.BaseState.DeepClone(),
                frames,
                diagnostics);
        }).ToArray();
    }

    private static TimelineDiagnostic ToTimelineDiagnostic(
        StoryboardImportIssue issue) => new(
        issue.Code,
        $"{issue.Path}: {issue.Message}",
        issue.Severity == StoryboardDiagnosticSeverity.Error
            ? TimelineDiagnosticSeverity.Error
            : TimelineDiagnosticSeverity.Warning);

    public EntityTimelineProjection BuildEntityProjection(
        IStoryboardEntity entity,
        ProjectDataContext? context)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var states = new List<ProjectedTimelineState>();
        var diagnostics = new List<TimelineDiagnostic>();
        var baseState = entity.GetBaseState() as ObjectState;
        var noteId = ResolveBoundNoteId(entity);
        var resolution = _timeResolver.ResolveEntity(entity, context, "$");
        var hasValidBase = resolution.HasValidBaseTime;
        var baseTime = resolution.BaseTime;

        if (!hasValidBase)
        {
            diagnostics.Add(new(
                "TIMELINE_BASE_TIME_INVALID",
                $"事件“{entity.Id ?? entity.GetType().Name}”的基准时间无法解析。",
                TimelineDiagnosticSeverity.Error));
        }
        else if (resolution.BaseTimeWasInferred)
        {
            diagnostics.Add(new(
                "TIMELINE_BASE_TIME_INFERRED",
                $"事件“{entity.Id ?? entity.GetType().Name}”暂时使用首关键帧时间作为基准；导出前必须修正。",
                TimelineDiagnosticSeverity.Warning));
        }
        foreach (var problem in resolution.Problems)
            diagnostics.Add(new(problem.Code, $"{problem.Path}: {problem.Message}",
                TimelineDiagnosticSeverity.Error));

        if (baseState != null)
        {
            states.Add(new(baseState, baseTime, baseState.Template, [], false));
            if (!string.IsNullOrWhiteSpace(baseState.Template))
            {
                ExpandTemplate(
                    baseState.Template,
                    baseTime,
                    context,
                    noteId,
                    states,
                    diagnostics,
                    [],
                    new HashSet<string>(StringComparer.Ordinal));
            }
        }

        foreach (var item in entity.GetKeyframes() ?? Array.Empty<object>())
        {
            if (item is not ObjectState state)
                continue;

            var triggerTimes = resolution.Occurrences
                .Where(occurrence => !occurrence.IsBaseState &&
                                     ReferenceEquals(occurrence.State, state))
                .Select(occurrence => occurrence.EffectiveTime)
                .ToList();

            foreach (var triggerTime in triggerTimes)
            {
                states.Add(new(state, triggerTime, state.Template, [], false));

                if (!string.IsNullOrWhiteSpace(state.Template))
                {
                    ExpandTemplate(
                        state.Template,
                        triggerTime,
                        context,
                        noteId,
                        states,
                        diagnostics,
                        [],
                        new HashSet<string>(StringComparer.Ordinal));
                }
            }
        }

        var lastTime = states.Count == 0
            ? baseTime
            : Math.Max(baseTime, states.Max(s => s.AbsoluteTime));

        return new EntityTimelineProjection
        {
            Entity = entity,
            BaseStateTime = baseTime,
            LastStateTime = lastTime,
            HasValidBaseTime = hasValidBase,
            States = states.OrderBy(s => s.AbsoluteTime).ToArray(),
            Diagnostics = diagnostics.ToArray()
        };
    }

    private static void ExpandTemplate(
        string templateName,
        double triggerTime,
        ProjectDataContext? context,
        string? noteId,
        List<ProjectedTimelineState> output,
        List<TimelineDiagnostic> diagnostics,
        IReadOnlyList<string> parentPath,
        HashSet<string> visiting)
    {
        if (!visiting.Add(templateName))
        {
            diagnostics.Add(new(
                "TIMELINE_TEMPLATE_CYCLE",
                $"模板循环引用已在“{templateName}”处截断。",
                TimelineDiagnosticSeverity.Error));
            return;
        }

        if (context?.Storyboard?.templates == null ||
            !context.Storyboard.templates.TryGetValue(templateName, out var template))
        {
            diagnostics.Add(new(
                "TIMELINE_TEMPLATE_MISSING",
                $"找不到模板“{templateName}”。",
                TimelineDiagnosticSeverity.Error));
            visiting.Remove(templateName);
            return;
        }

        var path = parentPath.Concat([templateName]).ToArray();
        var templateBase = template.BaseState;
        if (templateBase != null)
        {
            output.Add(new(templateBase, triggerTime, templateName, path, true));
            if (!string.IsNullOrWhiteSpace(templateBase.Template))
            {
                ExpandTemplate(
                    templateBase.Template,
                    triggerTime,
                    context,
                    noteId,
                    output,
                    diagnostics,
                    path,
                    visiting);
            }
        }

        var previousTime = triggerTime;
        foreach (var state in template.Keyframes ?? [])
        {
            var times = ResolveStateTimes(
                state, triggerTime, previousTime, context, noteId, diagnostics);

            foreach (var time in times)
            {
                output.Add(new(state, time, templateName, path, true));
                if (!string.IsNullOrWhiteSpace(state.Template))
                {
                    ExpandTemplate(
                        state.Template,
                        time,
                        context,
                        noteId,
                        output,
                        diagnostics,
                        path,
                        visiting);
                }
            }

            if (times.Count > 0)
                previousTime = times[^1];
        }

        visiting.Remove(templateName);
    }

    private static List<double> ResolveStateTimes(
        ObjectState state,
        double parentTime,
        double previousTime,
        ProjectDataContext? context,
        string? noteId,
        List<TimelineDiagnostic> diagnostics)
    {
        if (state.AddTime.HasValue)
            return [previousTime + state.AddTime.Value];

        if (state.RelativeTime.HasValue)
        {
            if (state.Time != null)
            {
                var explicitTimes = ResolveTimes(
                    state.Time, context, noteId, diagnostics, "state-time");
                return explicitTimes.Select(t => t + state.RelativeTime.Value).ToList();
            }

            return [previousTime + state.RelativeTime.Value];
        }

        if (state.Time != null)
            return ResolveTimes(state.Time, context, noteId, diagnostics, "state-time");

        // A state without a time is evaluated at its parent/trigger time.
        return [parentTime];
    }

    private static List<double> ResolveTimes(
        object? value,
        ProjectDataContext? context,
        string? noteId,
        List<TimelineDiagnostic> diagnostics,
        string location)
    {
        var result = new List<double>();
        if (value is null)
            return result;

        if (value is IEnumerable enumerable and not string &&
            value is not Newtonsoft.Json.Linq.JValue)
        {
            foreach (var item in enumerable)
                result.AddRange(ResolveTimes(item, context, noteId, diagnostics, location));
            return result;
        }

        var text = value.ToString()?.Trim() ?? string.Empty;
        if (!string.IsNullOrEmpty(noteId))
            text = text.Replace("$note", noteId, StringComparison.Ordinal);

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric) ||
            double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out numeric))
        {
            result.Add(numeric);
            return result;
        }

        var resolved = context?.TimeEngine?.ParseCytoidTimeExpression(
            text, context.Chart?.note_list) ?? double.NaN;
        if (!double.IsNaN(resolved) && !double.IsInfinity(resolved))
        {
            result.Add(resolved);
            return result;
        }

        diagnostics.Add(new(
            "TIMELINE_TIME_UNRESOLVED",
            $"无法解析 {location} 时间表达式“{text}”。",
            TimelineDiagnosticSeverity.Error));
        return result;
    }

    private static string? ResolveBoundNoteId(IStoryboardEntity entity)
    {
        if (FastReflectionHelper.TryGetValue(entity, "Note", out var note) && note != null)
            return note.ToString()?.Trim();
        if (FastReflectionHelper.TryGetValue(entity, "NoteTarget", out note) && note != null)
            return note.ToString()?.Trim();
        return null;
    }
}
