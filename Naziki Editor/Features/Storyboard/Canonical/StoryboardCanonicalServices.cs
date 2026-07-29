using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Naziki_Editor.Core.Storyboard.Canonical;

public sealed class EditorStoryboardSerializer : IEditorStoryboardSerializer
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
        Culture = CultureInfo.InvariantCulture,
        ContractResolver = new DefaultContractResolver
        {
            NamingStrategy = new SnakeCaseNamingStrategy()
        }
    };

    public string Serialize(EditorStoryboardDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.SchemaVersion != 1)
            throw new JsonSerializationException(
                $"Unsupported editor storyboard schema {document.SchemaVersion}.");
        return JsonConvert.SerializeObject(document, Settings);
    }

    public EditorStoryboardDocument Deserialize(string json)
    {
        var document = JsonConvert.DeserializeObject<EditorStoryboardDocument>(json, Settings)
                       ?? throw new JsonSerializationException(
                           "Editor storyboard source is empty.");
        if (document.SchemaVersion != 1)
            throw new JsonSerializationException(
                $"Unsupported editor storyboard schema {document.SchemaVersion}.");
        return document;
    }
}

public sealed class StoryboardTimePositionResolver : IStoryboardTimePositionResolver
{
    public double? Resolve(StoryboardTimePosition position, C2Chart? chart,
        ITimeEngine? timeEngine, int? currentNoteId = null,
        double? templateStart = null, double? triggerTime = null)
    {
        ArgumentNullException.ThrowIfNull(position);
        if (position.Kind == StoryboardTimeAnchorKind.Absolute)
            return position.Seconds;
        if (position.Kind == StoryboardTimeAnchorKind.TemplateStart)
            return templateStart + position.OffsetSeconds;
        if (position.Kind == StoryboardTimeAnchorKind.TriggerSpawn)
            return triggerTime + position.OffsetSeconds;
        if (position.Kind == StoryboardTimeAnchorKind.Unresolved ||
            chart?.note_list is null || timeEngine is null)
            return null;

        var noteId = position.Kind is
            StoryboardTimeAnchorKind.CurrentNoteIntro or
            StoryboardTimeAnchorKind.CurrentNoteStart or
            StoryboardTimeAnchorKind.CurrentNoteEnd or
            StoryboardTimeAnchorKind.CurrentNoteAt
            ? currentNoteId
            : position.NoteId;
        if (!noteId.HasValue) return null;
        var note = chart.note_list.FirstOrDefault(item => item.id == noteId.Value);
        if (note is null) return null;

        var start = timeEngine.TickToSeconds(note.tick);
        var end = timeEngine.TickToSeconds(note.tick + Math.Max(0, note.hold_tick));
        var value = position.Kind switch
        {
            StoryboardTimeAnchorKind.NoteIntro or
                StoryboardTimeAnchorKind.CurrentNoteIntro => start - 1.5,
            StoryboardTimeAnchorKind.NoteStart or
                StoryboardTimeAnchorKind.CurrentNoteStart => start,
            StoryboardTimeAnchorKind.NoteEnd or
                StoryboardTimeAnchorKind.CurrentNoteEnd => end,
            StoryboardTimeAnchorKind.NoteAt or
                StoryboardTimeAnchorKind.CurrentNoteAt =>
                start + (end - start) * (position.HoldPosition ?? 0),
            _ => double.NaN
        };
        return double.IsNaN(value) ? null : value + position.OffsetSeconds;
    }
}

public sealed class NoteQueryService : INoteQueryService
{
    public IReadOnlyList<C2Note> Match(C2Chart? chart, NoteQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (chart?.note_list is null) return [];
        return chart.note_list.Where(note =>
        {
            if (query.Types.Count > 0 && !query.Types.Contains(note.type)) return false;
            if (query.Start.HasValue && note.id < query.Start.Value) return false;
            if (query.End.HasValue && note.id > query.End.Value) return false;
            if (query.MinX.HasValue && note.x < query.MinX.Value) return false;
            if (query.MaxX.HasValue && note.x > query.MaxX.Value) return false;
            if (!query.Direction.HasValue) return true;
            if (note.page_index < 0 || note.page_index >= chart.page_list.Count) return false;
            return chart.page_list[note.page_index].scan_line_direction ==
                   query.Direction.Value;
        }).OrderBy(note => note.id).ToArray();
    }
}

internal static class StoryboardStableId
{
    public static string Create(params object?[] parts)
    {
        var text = string.Join("\u001f", parts.Select(part =>
            Convert.ToString(part, CultureInfo.InvariantCulture) ?? ""));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash).ToLowerInvariant()[..32];
    }

    public static string HashText(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))
            .ToLowerInvariant();
}

public sealed class StoryboardImportService : IStoryboardImportService
{
    private static readonly string[] Collections =
    [
        "sprites", "texts", "lines", "videos", "controllers", "note_controllers"
    ];

    private static readonly Dictionary<string, string> Aliases =
        new(StringComparer.Ordinal)
        {
            ["arcade_inteference_size"] = "arcade_interference_size",
            ["arcade_inteference_speed"] = "arcade_interference_speed",
            ["arcade_interferance_size"] = "arcade_interference_size",
            ["arcade_interferance_speed"] = "arcade_interference_speed",
            ["x_offset"] = "dx",
            ["y_offset"] = "dy"
        };

    public StoryboardImportResult Import(string json, C2Chart? chart = null,
        StoryboardMeta? legacyMeta = null,
        IReadOnlyDictionary<string, string>? legacyControlBoardIds = null)
    {
        var issues = new List<StoryboardImportIssue>();
        JObject root;
        try
        {
            using var textReader = new StringReader(json);
            using var reader = new JsonTextReader(textReader)
            {
                DateParseHandling = DateParseHandling.None,
                FloatParseHandling = FloatParseHandling.Double
            };
            root = JToken.ReadFrom(reader) as JObject
                   ?? throw new JsonSerializationException(
                       "Storyboard root must be an object.");
        }
        catch (JsonException ex)
        {
            return new StoryboardImportResult(null,
            [
                new StoryboardImportIssue("INVALID_JSON", "$", ex.Message,
                    StoryboardDiagnosticSeverity.Error)
            ]);
        }

        NormalizeAliases(root);
        var normalized = root.ToString(Formatting.None);
        var importHash = StoryboardStableId.HashText(normalized);
        var document = new EditorStoryboardDocument
        {
            DocumentId = StoryboardStableId.Create("storyboard", importHash),
            Metadata =
            {
                ImportHash = importHash,
                LegacyMeta = legacyMeta is null
                    ? new JObject()
                    : JObject.FromObject(legacyMeta),
                ControlBoardIdMaps = legacyControlBoardIds is null
                    ? new Dictionary<string, string>(StringComparer.Ordinal)
                    : new Dictionary<string, string>(legacyControlBoardIds,
                        StringComparer.Ordinal)
            }
        };
        document.Metadata.SyntaxStatistics = CollectSyntaxStatistics(root);

        foreach (var property in root.Properties())
        {
            if (!Collections.Contains(property.Name, StringComparer.Ordinal) &&
                property.Name is not "templates" and not "triggers")
                document.RootProperties[property.Name] = property.Value.DeepClone();
        }

        ParseTemplates(root["templates"], document, importHash, issues);
        var triggerSpawnIds = ParseTriggers(root["triggers"], document, issues);

        var sourceOrder = 0;
        foreach (var collection in Collections)
        {
            if (root[collection] is null) continue;
            if (root[collection] is not JArray array)
            {
                issues.Add(new StoryboardImportIssue(
                    "COLLECTION_NOT_ARRAY", $"$.{collection}",
                    $"'{collection}' must be an array.",
                    StoryboardDiagnosticSeverity.Error));
                continue;
            }

            for (var index = 0; index < array.Count; index++)
            {
                if (array[index] is not JObject source)
                {
                    issues.Add(new StoryboardImportIssue(
                        "ENTITY_NOT_OBJECT", $"$.{collection}[{index}]",
                        "Storyboard entity must be an object.",
                        StoryboardDiagnosticSeverity.Error));
                    continue;
                }

                var groupId = StoryboardStableId.Create(importHash, collection, index);
                var timeVariants = ExpandTimingVariants(source,
                    $"$.{collection}[{index}]", issues);
                var variantIndex = 0;
                foreach (var timeVariant in timeVariants)
                {
                    foreach (var noteVariant in ExpandNoteVariants(timeVariant,
                                 $"$.{collection}[{index}]", issues))
                    {
                        var path = $"$.{collection}[{index}]";
                        var entity = ParseEntity(noteVariant, collection, path,
                            sourceOrder++, variantIndex++, groupId, importHash,
                            document.Templates, triggerSpawnIds, issues);
                        document.Entities.Add(entity);
                    }
                }
            }
        }

        ValidateTemplateReferences(document, issues);
        document.Metadata.ImportDiagnostics = issues.Select(issue =>
            new EditorStoryboardStoredDiagnostic
            {
                Code = issue.Code,
                Path = issue.Path,
                Message = issue.Message,
                Severity = issue.Severity.ToString()
            }).ToList();
        return new StoryboardImportResult(document, issues);
    }

    private static void ParseTemplates(JToken? token,
        EditorStoryboardDocument document, string importHash,
        List<StoryboardImportIssue> issues)
    {
        if (token is null) return;
        if (token is not JObject templates)
        {
            issues.Add(new StoryboardImportIssue(
                "TEMPLATES_NOT_OBJECT", "$.templates",
                "'templates' must be an object.",
                StoryboardDiagnosticSeverity.Error));
            return;
        }

        var order = 0;
        var definitions = new List<(JObject Source,
            EditorStoryboardTemplate Template, string Path)>();
        foreach (var property in templates.Properties())
        {
            if (property.Value is not JObject source)
            {
                issues.Add(new StoryboardImportIssue(
                    "TEMPLATE_NOT_OBJECT", $"$.templates.{property.Name}",
                    "Template must be an object.",
                    StoryboardDiagnosticSeverity.Error));
                continue;
            }

            var path = $"$.templates.{Escape(property.Name)}";
            var template = new EditorStoryboardTemplate
            {
                TemplateId = StoryboardStableId.Create(
                    importHash, "template", property.Name),
                Name = property.Name,
                BasePatch = ExtractPatch(source, null),
                RootTemplate = ParseTemplateBinding(source, ExtractPatch(source, null)),
                DefaultRelativeSeconds = ReadScalarDouble(
                    source["relative_time"], $"{path}.relative_time", issues),
                DefaultAddSeconds = ReadScalarDouble(
                    source["add_time"], $"{path}.add_time", issues),
                Source = new EditorSourceInfo
                {
                    Path = path,
                    ImportHash = importHash,
                    SourceOrder = order++
                }
            };
            document.Templates[property.Name] = template;
            definitions.Add((source, template, path));
        }

        // Parse frames only after every template shell and default time is
        // known. Forward references therefore behave exactly like backward
        // references and declaration order remains irrelevant.
        foreach (var (source, template, path) in definitions)
        {
            var sequence = 0;
            if (source["states"] is JArray states)
            {
                ParseStateScope(states, template.Frames, document.Templates,
                    importHash, template.TemplateId, path, ref sequence, null,
                    StoryboardTimePosition.TemplateStart(),
                    StoryboardTimePosition.TemplateStart(), issues);
            }
            else if (source["states"] is not null)
            {
                issues.Add(new StoryboardImportIssue(
                    "STATES_NOT_ARRAY", $"{path}.states",
                    "'states' must be an array.",
                    StoryboardDiagnosticSeverity.Error));
            }
            ApplyTemplateDefaultTimes(template.Frames, document.Templates, issues);
        }
    }

    private static HashSet<string> ParseTriggers(JToken? token,
        EditorStoryboardDocument document, List<StoryboardImportIssue> issues)
    {
        var spawnIds = new HashSet<string>(StringComparer.Ordinal);
        if (token is null) return spawnIds;
        if (token is not JArray triggers)
        {
            issues.Add(new StoryboardImportIssue(
                "TRIGGERS_NOT_ARRAY", "$.triggers",
                "'triggers' must be an array.",
                StoryboardDiagnosticSeverity.Error));
            return spawnIds;
        }

        document.Triggers = (JArray)triggers.DeepClone();
        foreach (var trigger in triggers.OfType<JObject>())
        {
            if (trigger["spawn"] is JArray spawn)
                foreach (var id in spawn.Values<string>().Where(id =>
                             !string.IsNullOrWhiteSpace(id)))
                    spawnIds.Add(id!);
        }
        return spawnIds;
    }

    private static EditorStoryboardEntity ParseEntity(JObject source,
        string collection, string path, int sourceOrder, int variantIndex,
        string groupId, string importHash,
        IReadOnlyDictionary<string, EditorStoryboardTemplate> templates,
        ISet<string> triggerSpawnIds, List<StoryboardImportIssue> issues)
    {
        var kind = collection switch
        {
            "sprites" => EditorStoryboardEntityKind.Sprite,
            "texts" => EditorStoryboardEntityKind.Text,
            "lines" => EditorStoryboardEntityKind.Line,
            "videos" => EditorStoryboardEntityKind.Video,
            "controllers" => EditorStoryboardEntityKind.SceneController,
            "note_controllers" => EditorStoryboardEntityKind.NoteController,
            _ => throw new ArgumentOutOfRangeException(nameof(collection))
        };
        var runtimeId = source.Value<string>("id");
        var binding = ParseNoteBinding(source["note"], $"{path}.note", issues);
        var editorId = StoryboardStableId.Create(groupId, variantIndex);
        var entity = new EditorStoryboardEntity
        {
            EditorId = editorId,
            SourceGroupId = groupId,
            Kind = kind,
            SourceOrder = sourceOrder,
            RuntimeId = EditorInterpolatedString.FromWire(runtimeId),
            TargetId = EditorInterpolatedString.FromWire(
                source.Value<string>("target_id")),
            ParentId = EditorInterpolatedString.FromWire(
                source.Value<string>("parent_id")),
            NoteBinding = binding,
            BasePatch = ExtractPatch(source, kind),
            RootTemplate = ParseTemplateBinding(source, ExtractPatch(source, kind)),
            Source = new EditorSourceInfo
            {
                Path = path,
                ImportHash = importHash,
                SourceOrder = sourceOrder
            }
        };

        StoryboardTimePosition? activation = null;
        if (source["time"] is not null)
        {
            if (StoryboardTimePosition.TryParse(source["time"], out var parsed,
                    out var error))
                activation = parsed;
            else if (error is not null)
                issues.Add(new StoryboardImportIssue(
                    "TIME_INVALID", $"{path}.time", error,
                    StoryboardDiagnosticSeverity.Error));
        }
        else if (!string.IsNullOrWhiteSpace(runtimeId) &&
                 triggerSpawnIds.Contains(runtimeId) &&
                 TryReadDouble(source["relative_time"], out var relative))
            activation = StoryboardTimePosition.TriggerSpawn(relative);
        else if (!string.IsNullOrWhiteSpace(runtimeId) &&
                 triggerSpawnIds.Contains(runtimeId) &&
                 TryReadDouble(source["add_time"], out var add))
            activation = StoryboardTimePosition.TriggerSpawn(add);

        var controller = kind is EditorStoryboardEntityKind.SceneController or
            EditorStoryboardEntityKind.NoteController;
        if (activation is null && !controller &&
            !string.IsNullOrWhiteSpace(runtimeId) &&
            triggerSpawnIds.Contains(runtimeId))
            activation = StoryboardTimePosition.TriggerSpawn();

        entity.ActivationTime = activation;
        var sequence = 0;
        if (source["states"] is JArray states)
        {
            ParseStateScope(states, entity.Frames, templates, importHash,
                editorId, path, ref sequence, null,
                activation ?? StoryboardTimePosition.Unresolved(),
                activation ?? StoryboardTimePosition.Unresolved(), issues);
        }
        else if (source["states"] is not null)
        {
            issues.Add(new StoryboardImportIssue(
                "STATES_NOT_ARRAY", $"{path}.states",
                "'states' must be an array.",
                StoryboardDiagnosticSeverity.Error));
        }
        ApplyTemplateDefaultTimes(entity.Frames, templates, issues);

        if (activation is not null)
            entity.ActivationMode =
                activation.Kind == StoryboardTimeAnchorKind.TriggerSpawn
                    ? StoryboardActivationMode.TriggerSpawn
                    : StoryboardActivationMode.Explicit;
        else if (controller)
        {
            entity.ActivationMode = StoryboardActivationMode.GlobalController;
            entity.ActivationTime = StoryboardTimePosition.Absolute(0);
        }
        else if (entity.Frames.FirstOrDefault(frame =>
                     frame.Time.Kind != StoryboardTimeAnchorKind.Unresolved) is
                 { } firstFrame)
        {
            entity.ActivationMode = StoryboardActivationMode.FirstFrame;
            entity.ActivationTime = firstFrame.Time;
        }
        else if (!string.IsNullOrWhiteSpace(runtimeId) &&
                 triggerSpawnIds.Contains(runtimeId))
        {
            entity.ActivationMode = StoryboardActivationMode.TriggerSpawn;
            entity.ActivationTime = StoryboardTimePosition.TriggerSpawn();
        }
        else
        {
            entity.ActivationMode = StoryboardActivationMode.Inactive;
            issues.Add(new StoryboardImportIssue(
                "ENTITY_NOT_ACTIVATABLE", path,
                "Scene entity has no object time, timed state, or trigger spawn reference.",
                StoryboardDiagnosticSeverity.Warning));
        }
        return entity;
    }

    private static void ParseStateScope(JArray states,
        List<EditorStoryboardFrame> destination,
        IReadOnlyDictionary<string, EditorStoryboardTemplate> templates,
        string importHash, string idScope, string ownerPath, ref int sequence,
        string? inheritedFrameId, StoryboardTimePosition inheritedTime,
        StoryboardTimePosition scopeBaseTime,
        List<StoryboardImportIssue> issues)
    {
        var previousFrameId = inheritedFrameId;
        var previousTime = scopeBaseTime;
        for (var stateIndex = 0; stateIndex < states.Count; stateIndex++)
        {
            if (states[stateIndex] is not JObject rawState)
            {
                issues.Add(new StoryboardImportIssue(
                    "STATE_NOT_OBJECT", $"{ownerPath}.states[{stateIndex}]",
                    "State must be an object.",
                    StoryboardDiagnosticSeverity.Error));
                continue;
            }

            var statePath = $"{ownerPath}.states[{stateIndex}]";
            foreach (var timingState in ExpandTimingVariants(rawState, statePath,
                         issues))
            {
                foreach (var state in ExpandNoteVariants(timingState, statePath,
                             issues))
                {
                    var templateName = state.Value<string>("template");
                    var effectiveState = (JObject)state.DeepClone();
                    if (!string.IsNullOrWhiteSpace(templateName) &&
                        templates.TryGetValue(templateName, out var template))
                    {
                        if (effectiveState["relative_time"] is null &&
                            template.DefaultRelativeSeconds.HasValue)
                            effectiveState["relative_time"] =
                                template.DefaultRelativeSeconds.Value;
                        if (effectiveState["add_time"] is null &&
                            template.DefaultAddSeconds.HasValue)
                            effectiveState["add_time"] =
                                template.DefaultAddSeconds.Value;
                    }

                    var firstInScope = string.Equals(previousFrameId,
                        inheritedFrameId, StringComparison.Ordinal);
                    var baseForRelative = firstInScope
                        ? inheritedTime
                        : previousTime.Kind ==
                          StoryboardTimeAnchorKind.Unresolved
                            ? inheritedTime
                            : previousTime;
                    var frameTime = ParseFrameTime(effectiveState,
                        baseForRelative, previousTime, statePath, issues);
                    var patch = ExtractPatch(effectiveState, null);
                    var frameId = StoryboardStableId.Create(
                        importHash, idScope, sequence, statePath,
                        destination.Count);
                    var frame = new EditorStoryboardFrame
                    {
                        FrameId = frameId,
                        Sequence = sequence++,
                        Time = frameTime,
                        Patch = patch,
                        Easing = effectiveState.Value<string>("easing"),
                        Destroy = effectiveState.Value<bool?>("destroy"),
                        Reset = effectiveState.Value<bool?>("reset") == true,
                        InheritFromFrameId = previousFrameId,
                        Template = ParseTemplateBinding(effectiveState, patch),
                        NoteBinding = ParseNoteBinding(effectiveState["note"],
                            $"{statePath}.note", issues),
                        HasInlineChildren = effectiveState["states"] is JArray,
                        Source = new EditorSourceInfo
                        {
                            Path = statePath,
                            ImportHash = importHash,
                            SourceOrder = sequence - 1
                        }
                    };
                    destination.Add(frame);

                    if (effectiveState["states"] is JArray children)
                    {
                        ParseStateScope(children, destination, templates,
                            importHash, idScope, statePath, ref sequence, frameId,
                            frameTime, scopeBaseTime, issues);
                    }
                    else if (effectiveState["states"] is not null)
                    {
                        issues.Add(new StoryboardImportIssue(
                            "STATES_NOT_ARRAY", $"{statePath}.states",
                            "'states' must be an array.",
                            StoryboardDiagnosticSeverity.Error));
                    }

                    // Nested states are an independent scope in the player;
                    // the next sibling still inherits this parent frame.
                    previousFrameId = frameId;
                    previousTime = frameTime;
                }
            }
        }
    }

    private static StoryboardTimePosition ParseFrameTime(JObject state,
        StoryboardTimePosition inheritedTime,
        StoryboardTimePosition previousTime, string path,
        List<StoryboardImportIssue> issues)
    {
        var time = inheritedTime;
        if (state["time"] is not null)
        {
            if (StoryboardTimePosition.TryParse(state["time"], out var parsed,
                    out var error))
                time = parsed;
            else if (error is not null)
                issues.Add(new StoryboardImportIssue(
                    "TIME_INVALID", $"{path}.time", error,
                    StoryboardDiagnosticSeverity.Error));
        }

        if (state["relative_time"] is not null)
        {
            if (TryReadDouble(state["relative_time"], out var relative))
                time = time.Shift(relative);
            else
                issues.Add(new StoryboardImportIssue(
                    "RELATIVE_TIME_INVALID", $"{path}.relative_time",
                    "relative_time must be a scalar number after expansion.",
                    StoryboardDiagnosticSeverity.Error));
        }

        if (state["add_time"] is not null)
        {
            if (TryReadDouble(state["add_time"], out var add))
                time = previousTime.Shift(add);
            else
                issues.Add(new StoryboardImportIssue(
                    "ADD_TIME_INVALID", $"{path}.add_time",
                    "add_time must be a scalar number after expansion.",
                    StoryboardDiagnosticSeverity.Error));
        }
        return time;
    }

    private static void ApplyTemplateDefaultTimes(
        IEnumerable<EditorStoryboardFrame> frames,
        IReadOnlyDictionary<string, EditorStoryboardTemplate> templates,
        List<StoryboardImportIssue> issues)
    {
        foreach (var frame in frames)
        {
            if (frame.Template is null) continue;
            if (!templates.TryGetValue(frame.Template.TemplateName, out _))
                issues.Add(new StoryboardImportIssue(
                    "TEMPLATE_MISSING", frame.Source.Path,
                    $"Template '{frame.Template.TemplateName}' does not exist.",
                    StoryboardDiagnosticSeverity.Error));
        }
    }

    private static EditorTemplateBinding? ParseTemplateBinding(JObject source,
        JObject patch)
    {
        var name = source.Value<string>("template");
        return string.IsNullOrWhiteSpace(name)
            ? null
            : new EditorTemplateBinding
            {
                TemplateName = name,
                // Explicit wire properties remain in BasePatch/Frame.Patch.
                // This separate map is reserved for editor-created overrides
                // on generated template frames.
                Overrides = new JObject()
            };
    }

    private static EditorNoteBinding? ParseNoteBinding(JToken? token,
        string path, List<StoryboardImportIssue> issues)
    {
        if (token is null) return null;
        if (token.Type == JTokenType.Integer)
            return new EditorNoteBinding { NoteId = token.Value<int>() };
        if (token is not JObject selector)
        {
            issues.Add(new StoryboardImportIssue(
                "NOTE_BINDING_INVALID", path,
                "note must be an integer, integer array, or selector object.",
                StoryboardDiagnosticSeverity.Error));
            return null;
        }

        var query = new NoteQuery
        {
            Start = selector.Value<int?>("start"),
            End = selector.Value<int?>("end"),
            Direction = selector.Value<int?>("direction"),
            MinX = selector.Value<double?>("min_x"),
            MaxX = selector.Value<double?>("max_x")
        };
        if (selector["type"] is JArray typeArray)
            query.Types.AddRange(typeArray.Values<int>());
        else if (selector["type"]?.Type == JTokenType.Integer)
            query.Types.Add(selector.Value<int>("type"));
        else if (selector["type"] is not null)
            issues.Add(new StoryboardImportIssue(
                "NOTE_QUERY_TYPE_INVALID", $"{path}.type",
                "selector type must be an integer or integer array.",
                StoryboardDiagnosticSeverity.Error));
        foreach (var property in selector.Properties())
            if (property.Name is not ("type" or "start" or "end" or
                "direction" or "min_x" or "max_x"))
                query.UnknownProperties[property.Name] = property.Value.DeepClone();
        return new EditorNoteBinding { Query = query };
    }

    private static IReadOnlyList<JObject> ExpandTimingVariants(JObject source,
        string path, List<StoryboardImportIssue> issues)
    {
        var result = new List<JObject>();
        var arrayCount = 0;
        // Matches Storyboard.PopulateJObjects exactly: the player appends the
        // variants of each field in this order. It does not form a Cartesian
        // product between different time fields.
        foreach (var field in new[] { "relative_time", "add_time", "time" })
        {
            if (source[field] is not JArray values) continue;
            arrayCount++;
            if (values.Count == 0)
            {
                issues.Add(new StoryboardImportIssue(
                    "TIME_ARRAY_EMPTY", $"{path}.{field}",
                    $"{field} array must not be empty.",
                    StoryboardDiagnosticSeverity.Error));
                continue;
            }
            foreach (var value in values)
            {
                var clone = (JObject)source.DeepClone();
                clone[field] = value.DeepClone();
                result.Add(clone);
            }
        }
        if (arrayCount > 1)
            issues.Add(new StoryboardImportIssue(
                "MULTIPLE_TIME_ARRAY_FIELDS", path,
                "The Unity player concatenates multiple time-array fields while leaving the other arrays unresolved; this ambiguous construct cannot be normalized safely.",
                StoryboardDiagnosticSeverity.Error));
        return result.Count == 0 ? [source] : result;
    }

    private static IReadOnlyList<JObject> ExpandNoteVariants(JObject source,
        string path, List<StoryboardImportIssue> issues)
    {
        if (source["note"] is not JArray notes) return [source];
        if (notes.Count == 0)
        {
            issues.Add(new StoryboardImportIssue(
                "NOTE_ARRAY_EMPTY", $"{path}.note",
                "note array must not be empty.",
                StoryboardDiagnosticSeverity.Error));
            return [];
        }
        var result = new List<JObject>(notes.Count);
        foreach (var note in notes)
        {
            if (note.Type != JTokenType.Integer)
            {
                issues.Add(new StoryboardImportIssue(
                    "NOTE_ARRAY_ITEM_INVALID", $"{path}.note",
                    "note arrays may contain only integer note IDs.",
                    StoryboardDiagnosticSeverity.Error));
                continue;
            }
            var clone = (JObject)source.DeepClone();
            clone["note"] = note.DeepClone();
            result.Add(clone);
        }
        return result;
    }

    private static JObject ExtractPatch(JObject source,
        EditorStoryboardEntityKind? kind)
    {
        var patch = (JObject)source.DeepClone();
        foreach (var field in new[]
                 {
                     "id", "target_id", "parent_id", "states", "time",
                     "relative_time", "add_time", "template", "reset", "note",
                     "easing", "destroy"
                 })
            patch.Remove(field);

        if (patch.TryGetValue("scale", out var scale))
        {
            patch["scale_x"] = scale.DeepClone();
            patch["scale_y"] = scale.DeepClone();
            patch.Remove("scale");
        }
        StoryboardCanonicalValues.NormalizeUnits(patch);

        // dx/dy are actual Note Controller properties. For stage objects they
        // remain as relative operations until materialization can apply them
        // against the inherited effective state.
        return patch;
    }

    private static double? ReadScalarDouble(JToken? token, string path,
        List<StoryboardImportIssue> issues)
    {
        if (token is null) return null;
        if (TryReadDouble(token, out var value)) return value;
        issues.Add(new StoryboardImportIssue(
            "TIME_OFFSET_INVALID", path,
            "Template time offset must be a scalar number.",
            StoryboardDiagnosticSeverity.Error));
        return null;
    }

    private static bool TryReadDouble(JToken? token, out double value)
    {
        if (token?.Type is JTokenType.Integer or JTokenType.Float)
        {
            value = token.Value<double>();
            return true;
        }
        value = 0;
        return false;
    }

    private static void NormalizeAliases(JToken token)
    {
        if (token is JObject obj)
        {
            foreach (var property in obj.Properties().ToArray())
            {
                if (Aliases.TryGetValue(property.Name, out var canonical) &&
                    obj[canonical] is null)
                    property.Replace(new JProperty(canonical, property.Value));
                NormalizeAliases(property.Value);
            }
        }
        else if (token is JArray array)
        {
            foreach (var item in array) NormalizeAliases(item);
        }
    }

    private static void ValidateTemplateReferences(EditorStoryboardDocument document,
        List<StoryboardImportIssue> issues)
    {
        var references = document.Entities.Select(entity =>
                (entity.RootTemplate, entity.Source.Path))
            .Concat(document.Entities.SelectMany(entity => entity.Frames)
                .Select(frame => (frame.Template, frame.Source.Path)))
            .Concat(document.Templates.Values.Select(template =>
                (template.RootTemplate, template.Source.Path)))
            .Concat(document.Templates.Values.SelectMany(template => template.Frames)
                .Select(frame => (frame.Template, frame.Source.Path)));
        foreach (var (binding, path) in references)
            if (binding is not null &&
                !document.Templates.ContainsKey(binding.TemplateName))
                issues.Add(new StoryboardImportIssue(
                    "TEMPLATE_MISSING", path,
                    $"Template '{binding.TemplateName}' does not exist.",
                    StoryboardDiagnosticSeverity.Error));
    }

    private static Dictionary<string, int> CollectSyntaxStatistics(JObject root)
    {
        var properties = root.DescendantsAndSelf().OfType<JProperty>().ToArray();
        return new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["time_array_groups"] = properties.Count(property =>
                property.Name is "time" or "relative_time" or "add_time" &&
                property.Value.Type == JTokenType.Array),
            ["note_array_groups"] = properties.Count(property =>
                property.Name == "note" &&
                property.Value.Type == JTokenType.Array),
            ["note_selector_groups"] = properties.Count(property =>
                property.Name == "note" &&
                property.Value.Type == JTokenType.Object),
            ["template_references"] = properties.Count(property =>
                property.Name == "template"),
            ["unit_expressions"] = root.DescendantsAndSelf().OfType<JValue>()
                .Count(value => value.Type == JTokenType.String &&
                                IsUnitExpression(value.Value<string>()))
        };
    }

    private static bool IsUnitExpression(string? value) =>
        value?.StartsWith("noteX:", StringComparison.OrdinalIgnoreCase) == true ||
        value?.StartsWith("noteY:", StringComparison.OrdinalIgnoreCase) == true ||
        value?.StartsWith("stageX:", StringComparison.OrdinalIgnoreCase) == true ||
        value?.StartsWith("stageY:", StringComparison.OrdinalIgnoreCase) == true ||
        value?.StartsWith("cameraX:", StringComparison.OrdinalIgnoreCase) == true ||
        value?.StartsWith("cameraY:", StringComparison.OrdinalIgnoreCase) == true;

    private static string Escape(string value) =>
        value.All(character => char.IsLetterOrDigit(character) ||
                               character == '_')
            ? value
            : $"['{value.Replace("'", "\\'", StringComparison.Ordinal)}']";
}
