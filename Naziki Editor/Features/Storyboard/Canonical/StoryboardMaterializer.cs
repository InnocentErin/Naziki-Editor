using System.Globalization;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Models;
using Newtonsoft.Json.Linq;

namespace Naziki_Editor.Core.Storyboard.Canonical;

public sealed class StoryboardMaterializer : IStoryboardMaterializer
{
    private readonly IStoryboardTimePositionResolver _timeResolver;
    private readonly INoteQueryService _noteQueries;

    public StoryboardMaterializer(
        IStoryboardTimePositionResolver timeResolver,
        INoteQueryService noteQueries)
    {
        _timeResolver = timeResolver;
        _noteQueries = noteQueries;
    }

    public MaterializedStoryboard Materialize(EditorStoryboardDocument document,
        C2Chart? chart, ITimeEngine? timeEngine)
    {
        ArgumentNullException.ThrowIfNull(document);
        var entities = new List<MaterializedStoryboardEntity>();
        var issues = new List<StoryboardImportIssue>();
        foreach (var entity in document.Entities.OrderBy(item => item.SourceOrder))
        {
            var noteIds = ResolveEntityNotes(entity, chart, issues);
            if (noteIds.Count == 0) continue;
            foreach (var noteId in noteIds)
            {
                if (noteId.HasValue &&
                    (entity.ExcludedNoteIds.Contains(noteId.Value) ||
                     entity.InstanceOverrides.TryGetValue(noteId.Value,
                         out var excludedOverride) && excludedOverride.Excluded))
                    continue;
                entities.Add(MaterializeEntity(document, entity, noteId,
                    chart, timeEngine, issues));
            }
        }

        DiagnoseDormantOverrides(document, chart, issues);
        return new MaterializedStoryboard(entities,
            (JArray)document.Triggers.DeepClone(), issues);
    }

    private IReadOnlyList<int?> ResolveEntityNotes(EditorStoryboardEntity entity,
        C2Chart? chart, List<StoryboardImportIssue> issues)
    {
        if (entity.NoteBinding?.Query is { } query)
        {
            if (chart is null)
            {
                issues.Add(new StoryboardImportIssue(
                    "NOTE_QUERY_CHART_MISSING", entity.Source.Path,
                    "A chart is required to materialize this note query.",
                    StoryboardDiagnosticSeverity.Warning));
                return [];
            }
            return _noteQueries.Match(chart, query)
                .Select(note => (int?)note.id).ToArray();
        }
        return [entity.NoteBinding?.NoteId];
    }

    private MaterializedStoryboardEntity MaterializeEntity(
        EditorStoryboardDocument document, EditorStoryboardEntity entity,
        int? noteId, C2Chart? chart, ITimeEngine? timeEngine,
        List<StoryboardImportIssue> issues)
    {
        var occurrenceId = StoryboardStableId.Create(entity.EditorId,
            noteId?.ToString(CultureInfo.InvariantCulture) ?? "single");
        string? runtimeId = ResolveReference(entity.RuntimeId, noteId,
            entity.Source.Path, "id", issues);
        string? targetId = ResolveReference(entity.TargetId, noteId,
            entity.Source.Path, "target_id", issues);
        string? parentId = ResolveReference(entity.ParentId, noteId,
            entity.Source.Path, "parent_id", issues);

        var activationTime = entity.ActivationTime;
        var activationSeconds = activationTime is null
            ? null
            : _timeResolver.Resolve(activationTime, chart, timeEngine, noteId);
        var baseState = new JObject();
        if (entity.RootTemplate is not null)
            baseState = ApplyTemplateBase(document, entity.RootTemplate,
                baseState, entity.Kind, issues, []);
        baseState = ApplyPatch(baseState, entity.BasePatch, entity.Kind, baseState);
        if (noteId.HasValue &&
            entity.InstanceOverrides.TryGetValue(noteId.Value, out var instance))
            baseState = ApplyPatch(baseState, instance.BasePatch, entity.Kind,
                baseState);
        if (entity.Kind == EditorStoryboardEntityKind.NoteController &&
            entity.NoteBinding is not null && noteId.HasValue)
            baseState["note"] = noteId.Value;

        var output = new List<MaterializedStoryboardFrame>();
        var outputSequence = 0;
        if (entity.RootTemplate is not null &&
            document.Templates.TryGetValue(entity.RootTemplate.TemplateName,
                out var rootTemplate))
        {
            ExpandTemplateFrames(document, rootTemplate,
                entity.RootTemplate, activationTime ??
                                     StoryboardTimePosition.Unresolved(),
                baseState, entity.Kind, noteId, chart, timeEngine, output,
                ref outputSequence, issues, occurrenceId,
                new HashSet<string>(StringComparer.Ordinal)
                {
                    rootTemplate.Name
                });
        }

        var stateByFrameId = new Dictionary<string, JObject>(StringComparer.Ordinal);
        var lastSourceState = baseState;
        foreach (var frame in entity.Frames.OrderBy(item => item.Sequence))
        {
            var inherited = frame.Reset
                ? new JObject()
                : frame.InheritFromFrameId is { } inheritedId &&
                  stateByFrameId.TryGetValue(inheritedId, out var inheritedState)
                    ? inheritedState
                    : lastSourceState;
            var frameNotes = ResolveFrameNotes(frame, noteId, chart, issues);
            if (frameNotes.Count == 0) continue;

            JObject lastVariantState = inherited;
            foreach (var frameNote in frameNotes)
            {
                var effective = inherited;
                if (frame.Template is not null)
                    effective = ApplyTemplateBase(document, frame.Template,
                        effective, entity.Kind, issues, []);
                effective = ApplyPatch(effective, frame.Patch, entity.Kind,
                    inherited);
                if (frameNote.HasValue &&
                    entity.Kind == EditorStoryboardEntityKind.NoteController &&
                    frame.NoteBinding is not null)
                    effective["note"] = frameNote.Value;
                if (noteId.HasValue &&
                    entity.InstanceOverrides.TryGetValue(noteId.Value,
                        out var instanceOverride) &&
                    instanceOverride.FramePatches.TryGetValue(frame.FrameId,
                        out var overridePatch))
                    effective = ApplyPatch(effective, overridePatch, entity.Kind,
                        effective);

                var time = frame.Time;
                var effectiveSeconds = _timeResolver.Resolve(time, chart,
                    timeEngine, frameNote ?? noteId);
                output.Add(new MaterializedStoryboardFrame(
                    StoryboardStableId.Create(occurrenceId, frame.FrameId,
                        frameNote?.ToString(CultureInfo.InvariantCulture) ?? ""),
                    frame.FrameId,
                    time,
                    effectiveSeconds,
                    outputSequence++,
                    (JObject)effective.DeepClone(),
                    frame.Easing,
                    frame.Destroy,
                    frame.Template?.TemplateName,
                    frameNote ?? noteId));

                if (frame.Template is not null && !frame.HasInlineChildren &&
                    document.Templates.TryGetValue(frame.Template.TemplateName,
                        out var template))
                {
                    ExpandTemplateFrames(document, template, frame.Template,
                        time, effective, entity.Kind, frameNote ?? noteId,
                        chart, timeEngine, output, ref outputSequence, issues,
                        occurrenceId,
                        new HashSet<string>(StringComparer.Ordinal)
                        {
                            template.Name
                        });
                }
                lastVariantState = effective;
                // Player expansion treats each note selector result as the
                // inherited base for the following populated state.
                inherited = effective;
            }
            stateByFrameId[frame.FrameId] = lastVariantState;
            lastSourceState = lastVariantState;
        }

        var completeOutput = CompleteEffectiveStates(baseState, output);
        return new MaterializedStoryboardEntity(
            occurrenceId,
            entity.EditorId,
            entity.Kind,
            runtimeId,
            targetId,
            parentId,
            entity.ActivationMode,
            activationTime,
            activationSeconds,
            noteId,
            baseState,
            completeOutput);
    }

    private static IReadOnlyList<MaterializedStoryboardFrame>
        CompleteEffectiveStates(JObject baseState,
            IReadOnlyList<MaterializedStoryboardFrame> frames)
    {
        var effective = (JObject)baseState.DeepClone();
        var completed = new List<MaterializedStoryboardFrame>(frames.Count);
        foreach (var frame in frames
                     .OrderBy(frame => frame.EffectiveTime ??
                         (frame.Time.Kind ==
                          StoryboardTimeAnchorKind.TriggerSpawn
                             ? frame.Time.OffsetSeconds
                             : double.PositiveInfinity))
                     .ThenBy(frame => frame.Sequence))
        {
            // A template branch or reset changes parser inheritance, but a
            // missing nullable runtime property emits no visual instruction.
            // Overlaying onto the prior projected state captures the complete
            // visual state without carrying reset/template sugar to the wire.
            foreach (var property in frame.EffectiveState.Properties())
                effective[property.Name] = property.Value.DeepClone();
            completed.Add(frame with
            {
                EffectiveState = (JObject)effective.DeepClone()
            });
        }
        return completed;
    }

    private void ExpandTemplateFrames(EditorStoryboardDocument document,
        EditorStoryboardTemplate template, EditorTemplateBinding binding,
        StoryboardTimePosition anchor, JObject initialState,
        EditorStoryboardEntityKind kind, int? noteId, C2Chart? chart,
        ITimeEngine? timeEngine, List<MaterializedStoryboardFrame> output,
        ref int outputSequence, List<StoryboardImportIssue> issues,
        string occurrenceScope,
        HashSet<string> templateStack)
    {
        var stateByFrameId = new Dictionary<string, JObject>(StringComparer.Ordinal);
        var lastState = initialState;
        foreach (var frame in template.Frames.OrderBy(item => item.Sequence))
        {
            var inherited = frame.Reset
                ? new JObject()
                : frame.InheritFromFrameId is { } inheritedId &&
                  stateByFrameId.TryGetValue(inheritedId, out var inheritedState)
                    ? inheritedState
                    : lastState;
            var effective = inherited;
            if (frame.Template is not null)
            {
                if (!templateStack.Add(frame.Template.TemplateName))
                {
                    issues.Add(new StoryboardImportIssue(
                        "TEMPLATE_CYCLE", frame.Source.Path,
                        $"Template cycle includes '{frame.Template.TemplateName}'.",
                        StoryboardDiagnosticSeverity.Error));
                    continue;
                }
                effective = ApplyTemplateBase(document, frame.Template,
                    effective, kind, issues, templateStack);
            }
            effective = ApplyPatch(effective, frame.Patch, kind, inherited);
            if (binding.FrameOverrides.TryGetValue(frame.FrameId,
                    out var bindingOverride))
                effective = ApplyPatch(effective, bindingOverride, kind, effective);

            var time = frame.Time.RebaseTemplate(anchor);
            var seconds = _timeResolver.Resolve(time, chart, timeEngine, noteId);
            output.Add(new MaterializedStoryboardFrame(
                StoryboardStableId.Create(occurrenceScope,
                    binding.TemplateName, frame.FrameId, outputSequence,
                    noteId),
                frame.FrameId,
                time,
                seconds,
                outputSequence++,
                (JObject)effective.DeepClone(),
                frame.Easing,
                frame.Destroy,
                template.Name,
                noteId));

            if (frame.Template is not null && !frame.HasInlineChildren &&
                document.Templates.TryGetValue(frame.Template.TemplateName,
                    out var nested))
                ExpandTemplateFrames(document, nested, frame.Template, time,
                    effective, kind, noteId, chart, timeEngine, output,
                    ref outputSequence, issues, occurrenceScope,
                    templateStack);
            if (frame.Template is not null)
                templateStack.Remove(frame.Template.TemplateName);

            stateByFrameId[frame.FrameId] = effective;
            lastState = effective;
        }

        var templateFrameIds = template.Frames.Select(frame => frame.FrameId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var orphan in binding.FrameOverrides.Keys.Where(key =>
                     !templateFrameIds.Contains(key)))
            issues.Add(new StoryboardImportIssue(
                "TEMPLATE_OVERRIDE_ORPHANED", template.Source.Path,
                $"Override for removed template frame '{orphan}' was preserved.",
                StoryboardDiagnosticSeverity.Warning));
    }

    private static JObject ApplyTemplateBase(EditorStoryboardDocument document,
        EditorTemplateBinding binding, JObject inherited,
        EditorStoryboardEntityKind kind,
        List<StoryboardImportIssue> issues, HashSet<string> stack)
    {
        if (!document.Templates.TryGetValue(binding.TemplateName, out var template))
        {
            issues.Add(new StoryboardImportIssue(
                "TEMPLATE_MISSING", "$.templates",
                $"Template '{binding.TemplateName}' does not exist.",
                StoryboardDiagnosticSeverity.Error));
            return inherited;
        }
        var result = ApplyPatch(inherited, template.BasePatch, kind, inherited);
        return ApplyPatch(result, binding.Overrides, kind, inherited);
    }

    private IReadOnlyList<int?> ResolveFrameNotes(EditorStoryboardFrame frame,
        int? entityNoteId, C2Chart? chart, List<StoryboardImportIssue> issues)
    {
        if (frame.NoteBinding?.Query is { } query)
        {
            if (chart is null)
            {
                issues.Add(new StoryboardImportIssue(
                    "NOTE_QUERY_CHART_MISSING", frame.Source.Path,
                    "A chart is required to materialize this state note query.",
                    StoryboardDiagnosticSeverity.Warning));
                return [];
            }
            return _noteQueries.Match(chart, query)
                .Select(note => (int?)note.id).ToArray();
        }
        return [frame.NoteBinding?.NoteId ?? entityNoteId];
    }

    private static JObject ApplyPatch(JObject state, JObject patch,
        EditorStoryboardEntityKind kind, JObject relativeBase)
    {
        var result = (JObject)state.DeepClone();
        var normalized = (JObject)patch.DeepClone();
        if (normalized.TryGetValue("scale", out var scale))
        {
            normalized["scale_x"] = scale.DeepClone();
            normalized["scale_y"] = scale.DeepClone();
            normalized.Remove("scale");
        }
        if (kind is EditorStoryboardEntityKind.Sprite or
            EditorStoryboardEntityKind.Text or
            EditorStoryboardEntityKind.Line or
            EditorStoryboardEntityKind.Video)
        {
            ApplyRelativeCoordinate(normalized, result, relativeBase, "dx", "x");
            ApplyRelativeCoordinate(normalized, result, relativeBase, "dy", "y");
        }
        foreach (var property in normalized.Properties())
            result[property.Name] = property.Value.DeepClone();
        return result;
    }

    private static void ApplyRelativeCoordinate(JObject patch, JObject result,
        JObject relativeBase, string deltaName, string targetName)
    {
        if (!patch.TryGetValue(deltaName, out var delta)) return;
        patch.Remove(deltaName);
        if (!StoryboardCanonicalValues.TryReadUnit(delta, out var deltaValue,
                out var unitPrefix)) return;
        var baseValue = StoryboardCanonicalValues.TryReadUnit(
            relativeBase[targetName], out var parsedBase,
            out _) ? parsedBase : 0;
        var sum = baseValue + deltaValue;
        result[targetName] = unitPrefix is null
            ? JToken.FromObject(sum)
            : StoryboardCanonicalValues.Unit(sum, unitPrefix);
    }

    private static string? ResolveReference(EditorInterpolatedString? reference,
        int? noteId, string path, string property,
        List<StoryboardImportIssue> issues)
    {
        if (reference is null) return null;
        try
        {
            return reference.Resolve(noteId);
        }
        catch (InvalidOperationException ex)
        {
            issues.Add(new StoryboardImportIssue(
                "NOTE_INTERPOLATION_UNBOUND", $"{path}.{property}", ex.Message,
                StoryboardDiagnosticSeverity.Error));
            return reference.Literal;
        }
    }

    private void DiagnoseDormantOverrides(EditorStoryboardDocument document,
        C2Chart? chart, List<StoryboardImportIssue> issues)
    {
        foreach (var entity in document.Entities.Where(item =>
                     item.NoteBinding?.Query is not null))
        {
            var matched = chart is null
                ? new HashSet<int>()
                : _noteQueries.Match(chart, entity.NoteBinding!.Query!)
                    .Select(note => note.id).ToHashSet();
            foreach (var noteId in entity.InstanceOverrides.Keys.Where(id =>
                         !matched.Contains(id)))
                issues.Add(new StoryboardImportIssue(
                    "NOTE_OVERRIDE_DORMANT", entity.Source.Path,
                    $"Override for note {noteId} is dormant because the query no longer matches it.",
                    StoryboardDiagnosticSeverity.Warning));
        }
    }
}

public sealed class StoryboardRuntimeExporter : IStoryboardRuntimeExporter
{
    private readonly IStoryboardMaterializer _materializer;
    private readonly IEditorStoryboardValidator _validator;

    public StoryboardRuntimeExporter(IStoryboardMaterializer materializer) =>
        (_materializer, _validator) =
        (materializer, new EditorStoryboardValidator());

    public StoryboardRuntimeExporter(IStoryboardMaterializer materializer,
        IEditorStoryboardValidator validator) =>
        (_materializer, _validator) = (materializer, validator);

    public StoryboardRuntimeExportResult Export(EditorStoryboardDocument document,
        C2Chart? chart, ITimeEngine? timeEngine)
    {
        var materialized = _materializer.Materialize(document, chart, timeEngine);
        var issues = materialized.Issues.ToList();
        issues.AddRange(document.Metadata.ImportDiagnostics
            .Where(issue => string.Equals(issue.Severity, "Error",
                StringComparison.OrdinalIgnoreCase))
            .Select(issue => new StoryboardImportIssue(issue.Code, issue.Path,
                issue.Message, StoryboardDiagnosticSeverity.Error)));
        issues.AddRange(_validator.Validate(document)
            .Where(issue => issue.BlocksRuntimeExport)
            .Select(issue => new StoryboardImportIssue(issue.Code, issue.Path,
                issue.Message, issue.Severity)));
        var root = (JObject)document.RootProperties.DeepClone();
        // Canonical export uses the normal wire parser with fully explicit
        // states. Carrying an imported compiled=true flag would route the
        // player through its incompatible direct-deserialization branch.
        root.Remove("compiled");
        var collections = new Dictionary<EditorStoryboardEntityKind, JArray>
        {
            [EditorStoryboardEntityKind.Sprite] = [],
            [EditorStoryboardEntityKind.Text] = [],
            [EditorStoryboardEntityKind.Line] = [],
            [EditorStoryboardEntityKind.Video] = [],
            [EditorStoryboardEntityKind.SceneController] = [],
            [EditorStoryboardEntityKind.NoteController] = []
        };

        foreach (var entity in materialized.Entities)
        {
            if (entity.ActivationMode == StoryboardActivationMode.Inactive)
            {
                issues.Add(new StoryboardImportIssue(
                    "ENTITY_NOT_ACTIVATABLE", $"editor:{entity.EditorId}",
                    "Inactive scene entities cannot be exported.",
                    StoryboardDiagnosticSeverity.Error));
                continue;
            }
            MaterializedStoryboardFrame? promotedFrame = null;
            var exportBaseState = entity.BaseState;
            if (entity.ActivationMode == StoryboardActivationMode.FirstFrame)
            {
                promotedFrame = entity.Frames
                    .Where(frame => frame.EffectiveTime.HasValue)
                    .OrderBy(frame => frame.EffectiveTime)
                    .ThenBy(frame => frame.Sequence)
                    .FirstOrDefault();
                if (promotedFrame is not null)
                    exportBaseState = promotedFrame.EffectiveState;
            }
            var json = StoryboardCanonicalValues.ToWireObject(exportBaseState);
            if (!string.IsNullOrWhiteSpace(entity.RuntimeId))
                json["id"] = entity.RuntimeId;
            if (!string.IsNullOrWhiteSpace(entity.TargetId))
                json["target_id"] = entity.TargetId;
            if (!string.IsNullOrWhiteSpace(entity.ParentId))
                json["parent_id"] = entity.ParentId;
            if (entity.Kind == EditorStoryboardEntityKind.NoteController &&
                entity.BoundNoteId.HasValue)
                json["note"] = entity.BoundNoteId.Value;

            if (entity.ActivationMode == StoryboardActivationMode.TriggerSpawn)
            {
                json["relative_time"] =
                    entity.ActivationTime?.OffsetSeconds ?? 0;
            }
            else
            {
                if (entity.EffectiveActivationTime.HasValue)
                    json["time"] = entity.EffectiveActivationTime.Value;
                else
                    issues.Add(new StoryboardImportIssue(
                        "ACTIVATION_TIME_UNRESOLVED",
                        $"editor:{entity.EditorId}",
                        "Entity activation time could not be resolved.",
                        StoryboardDiagnosticSeverity.Error));
            }

            var states = new JArray();
            foreach (var frame in entity.Frames
                         .OrderBy(frame => frame.EffectiveTime ??
                                           double.PositiveInfinity)
                         .ThenBy(frame => frame.Sequence))
            {
                if (ReferenceEquals(frame, promotedFrame))
                    continue;
                var state = StoryboardCanonicalValues.ToWireObject(
                    frame.EffectiveState);
                if (frame.Time.Kind == StoryboardTimeAnchorKind.TriggerSpawn)
                    state["relative_time"] = frame.Time.OffsetSeconds;
                else if (frame.EffectiveTime.HasValue)
                    state["time"] = frame.EffectiveTime.Value;
                else
                {
                    issues.Add(new StoryboardImportIssue(
                        "FRAME_TIME_UNRESOLVED",
                        $"editor:{entity.EditorId}/frame:{frame.FrameId}",
                        "Frame time could not be resolved.",
                        StoryboardDiagnosticSeverity.Error));
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(frame.Easing))
                    state["easing"] = frame.Easing;
                if (frame.Destroy.HasValue) state["destroy"] = frame.Destroy.Value;
                states.Add(state);
            }
            if (states.Count > 0) json["states"] = states;
            collections[entity.Kind].Add(json);
        }

        AddCollection(root, "sprites",
            collections[EditorStoryboardEntityKind.Sprite]);
        AddCollection(root, "texts",
            collections[EditorStoryboardEntityKind.Text]);
        AddCollection(root, "lines",
            collections[EditorStoryboardEntityKind.Line]);
        AddCollection(root, "videos",
            collections[EditorStoryboardEntityKind.Video]);
        AddCollection(root, "note_controllers",
            collections[EditorStoryboardEntityKind.NoteController]);
        AddCollection(root, "controllers",
            collections[EditorStoryboardEntityKind.SceneController]);
        if (materialized.Triggers.Count > 0)
            root["triggers"] = materialized.Triggers.DeepClone();

        ValidateReferences(root, chart, issues);
        return new StoryboardRuntimeExportResult(root, issues);
    }

    private static void AddCollection(JObject root, string name, JArray items)
    {
        if (items.Count > 0) root[name] = items;
    }

    private static void ValidateReferences(JObject root, C2Chart? chart,
        List<StoryboardImportIssue> issues)
    {
        var entities = new[]
            {
                "sprites", "texts", "lines", "videos", "note_controllers",
                "controllers"
            }
            .SelectMany(collection => root[collection] is JArray array
                ? array.OfType<JObject>()
                : [])
            .ToArray();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in entities)
        {
            var id = entity.Value<string>("id");
            if (!string.IsNullOrWhiteSpace(id) && !ids.Add(id))
                issues.Add(new StoryboardImportIssue(
                    "RUNTIME_ID_DUPLICATE", "$",
                    $"Runtime id '{id}' is duplicated.",
                    StoryboardDiagnosticSeverity.Error));
            if (entity["id"] is not null && entity["target_id"] is not null)
                issues.Add(new StoryboardImportIssue(
                    "ID_TARGET_CONFLICT", "$",
                    "A runtime entity cannot contain both id and target_id.",
                    StoryboardDiagnosticSeverity.Error));
            if (entity["target_id"] is not null && entity["parent_id"] is not null)
                issues.Add(new StoryboardImportIssue(
                    "TARGET_PARENT_CONFLICT", "$",
                    "A runtime entity cannot contain both target_id and parent_id.",
                    StoryboardDiagnosticSeverity.Error));
        }
        foreach (var entity in entities)
        {
            foreach (var property in new[] { "target_id", "parent_id" })
            {
                var reference = entity.Value<string>(property);
                if (!string.IsNullOrWhiteSpace(reference) &&
                    !ids.Contains(reference))
                    issues.Add(new StoryboardImportIssue(
                        "RUNTIME_REFERENCE_MISSING", "$",
                        $"{property} references missing id '{reference}'.",
                        StoryboardDiagnosticSeverity.Error));
            }
        }
        if (root["triggers"] is not JArray triggers) return;
        var noteIds = chart?.note_list.Select(note => note.id)
            .ToHashSet() ?? [];
        foreach (var (trigger, index) in triggers.OfType<JObject>()
                     .Select((trigger, index) => (trigger, index)))
        {
            foreach (var property in new[] { "spawn", "destroy" })
            {
                if (trigger[property] is not JArray references) continue;
                foreach (var reference in references.Values<string>())
                    if (!string.IsNullOrWhiteSpace(reference) &&
                        !ids.Contains(reference))
                        issues.Add(new StoryboardImportIssue(
                            "TRIGGER_REFERENCE_MISSING",
                            $"$.triggers[{index}].{property}",
                            $"{property} references missing id '{reference}'.",
                            StoryboardDiagnosticSeverity.Error));
            }
            if (chart is null || trigger["notes"] is not JArray notes) continue;
            foreach (var noteId in notes.Values<int>())
                if (!noteIds.Contains(noteId))
                    issues.Add(new StoryboardImportIssue(
                        "TRIGGER_NOTE_MISSING",
                        $"$.triggers[{index}].notes",
                        $"Trigger references missing note {noteId}.",
                        StoryboardDiagnosticSeverity.Error));
        }
    }
}
