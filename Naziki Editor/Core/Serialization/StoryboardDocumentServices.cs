using System.Collections;
using System.Globalization;
using System.IO;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Compilation;
using Naziki_Editor.Core.Storyboard.Corrections;
using Naziki_Editor.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Naziki_Editor.Core.Serialization;

public sealed class StoryboardPropertyCatalogService : IStoryboardPropertyCatalog
{
    public StoryboardPropertyCatalog Catalog { get; }

    public StoryboardPropertyCatalogService()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Core", "Serialization", "storyboard-properties.json");
        if (!File.Exists(path))
            throw new InvalidOperationException($"Storyboard property catalog was not found: {path}");
        Catalog = JsonConvert.DeserializeObject<StoryboardPropertyCatalog>(File.ReadAllText(path))
            ?? throw new InvalidOperationException($"Storyboard property catalog is empty: {path}");
        if (Catalog.CatalogVersion <= 0 || Catalog.RootCollections.Count == 0)
            throw new InvalidOperationException($"Storyboard property catalog is invalid: {path}");
        var duplicates = Catalog.Properties.GroupBy(p => p.JsonName, StringComparer.Ordinal)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        if (duplicates.Length > 0)
            throw new InvalidOperationException($"Duplicate storyboard catalog properties: {string.Join(", ", duplicates)}");
    }
}

internal static class StoryboardJsonSettings
{
    public static JsonSerializerSettings Create(bool snapshot = false) => new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.Indented,
        Culture = CultureInfo.InvariantCulture,
        ContractResolver = new DefaultContractResolver
        {
            NamingStrategy = new SnakeCaseNamingStrategy()
        },
        Converters =
        {
            snapshot ? new EditorSnapshotEntityJsonConverter() : new StoryboardEntityJsonConverter(),
            new StoryboardUnitValueJsonConverter()
        }
    };
}

public sealed class StoryboardDocumentReader : IStoryboardDocumentReader
{
    private readonly IStoryboardPropertyCatalog _catalog;
    private readonly JsonSerializerSettings _settings = StoryboardJsonSettings.Create();

    public StoryboardDocumentReader(IStoryboardPropertyCatalog catalog) => _catalog = catalog;

    public StoryboardRoot Read(string json)
    {
        var token = ParseAndNormalize(json);
        if (token is not JObject)
            throw new JsonSerializationException("Storyboard root must be a JSON object.");
        return token.ToObject<StoryboardRoot>(JsonSerializer.Create(_settings))
            ?? throw new JsonSerializationException("Storyboard result is empty.");
    }

    public IStoryboardEntity ReadEntity(string json, Type entityType)
    {
        if (!typeof(IStoryboardEntity).IsAssignableFrom(entityType))
            throw new ArgumentException("Type is not a storyboard entity.", nameof(entityType));
        var token = ParseAndNormalize(json);
        return (IStoryboardEntity)(token.ToObject(entityType, JsonSerializer.Create(_settings))
            ?? throw new JsonSerializationException("Storyboard entity result is empty."));
    }

    private JToken ParseAndNormalize(string json)
    {
        using var textReader = new StringReader(json);
        using var reader = new JsonTextReader(textReader)
        {
            DateParseHandling = DateParseHandling.None,
            FloatParseHandling = FloatParseHandling.Double
        };
        var token = JToken.ReadFrom(reader);
        NormalizeAliases(token);
        return token;
    }

    private void NormalizeAliases(JToken token)
    {
        if (token is JObject obj)
        {
            foreach (var property in obj.Properties().ToArray())
            {
                if (_catalog.Catalog.Aliases.TryGetValue(property.Name, out var canonical) &&
                    obj[canonical] is null)
                {
                    property.Replace(new JProperty(canonical, property.Value));
                }
                NormalizeAliases(property.Value);
            }
        }
        else if (token is JArray array)
        {
            foreach (var item in array) NormalizeAliases(item);
        }
    }
}

public sealed class StoryboardDocumentWriter : IStoryboardDocumentWriter
{
    private readonly JsonSerializer _serializer = JsonSerializer.Create(StoryboardJsonSettings.Create());

    public string Write(StoryboardRoot document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var root = new JObject();
        Add(root, "templates", document.templates);
        Add(root, "sprites", document.sprites);
        Add(root, "texts", document.texts);
        Add(root, "lines", document.lines);
        Add(root, "videos", document.videos);
        Add(root, "note_controllers", document.note_controllers);
        Add(root, "controllers", document.controllers);
        Add(root, "triggers", document.triggers);
        foreach (var property in document.UnknownProperties)
            if (root[property.Key] is null) root[property.Key] = property.Value.DeepClone();
        return root.ToString(Formatting.Indented);
    }

    public string WriteNode(object node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return JToken.FromObject(node, _serializer).ToString(Formatting.Indented);
    }

    private void Add(JObject root, string name, object? value)
    {
        if (value is IDictionary dictionary && dictionary.Count == 0) return;
        if (value is ICollection collection && collection.Count == 0) return;
        if (value is not null) root[name] = JToken.FromObject(value, _serializer);
    }
}

public sealed class StoryboardDocumentValidator : IStoryboardDocumentValidator
{
    private readonly IStoryboardCorrectionAnalyzer _correctionAnalyzer;
    private readonly IStoryboardTemplatePropertyMapper _templatePropertyMapper;

    public StoryboardDocumentValidator()
        : this(new StoryboardCorrectionAnalyzer(
            new StoryboardTimeResolver(),
            new StoryboardDocumentWriter()),
            new StoryboardTemplatePropertyMapper())
    {
    }

    public StoryboardDocumentValidator(IStoryboardCorrectionAnalyzer correctionAnalyzer)
        : this(correctionAnalyzer, new StoryboardTemplatePropertyMapper())
    {
    }

    public StoryboardDocumentValidator(
        IStoryboardCorrectionAnalyzer correctionAnalyzer,
        IStoryboardTemplatePropertyMapper templatePropertyMapper)
    {
        _correctionAnalyzer = correctionAnalyzer;
        _templatePropertyMapper = templatePropertyMapper;
    }

    public IReadOnlyList<StoryboardDiagnostic> Validate(StoryboardRoot document)
        => Validate(document, null);

    public IReadOnlyList<StoryboardDiagnostic> Validate(
        StoryboardRoot document,
        Naziki_Editor.State.ProjectDataContext? context)
    {
        ArgumentNullException.ThrowIfNull(document);
        Clear(document);
        var result = new List<StoryboardDiagnostic>();
        InspectUnknown(document, "$", result);
        InspectEntities(document.sprites, "$.sprites", result);
        InspectEntities(document.texts, "$.texts", result);
        InspectEntities(document.lines, "$.lines", result);
        InspectEntities(document.videos, "$.videos", result);
        InspectEntities(document.controllers, "$.controllers", result);
        InspectEntities(document.note_controllers, "$.note_controllers", result);
        foreach (var template in document.templates)
            InspectEntity(template.Value, $"$.templates.{Escape(template.Key)}", result);
        for (var index = 0; index < document.triggers.Count; index++)
        {
            var trigger = document.triggers[index];
            InspectUnknown(trigger, $"$.triggers[{index}]", result);
            if (string.IsNullOrWhiteSpace(trigger.Type))
                Add(trigger, result, "TRIGGER_TYPE_MISSING", $"$.triggers[{index}].type",
                    "Trigger type is required.", StoryboardDiagnosticSeverity.Error);
        }
        ValidateReferences(document, result);
        ValidateTemplateCompatibility(document, result);
        AddCorrectionDiagnostics(document, context, result);
        return result;
    }

    public IReadOnlyList<StoryboardDiagnostic> ValidateEntity(IStoryboardEntity entity, string path = "$")
    {
        Clear(entity);
        var result = new List<StoryboardDiagnostic>();
        InspectEntity(entity, path, result);
        return result;
    }

    private static void InspectEntities<T>(IReadOnlyList<T> entities, string path,
        List<StoryboardDiagnostic> output) where T : IStoryboardEntity
    {
        for (var index = 0; index < entities.Count; index++)
            InspectEntity(entities[index], $"{path}[{index}]", output);
    }

    private static void InspectEntity(IStoryboardEntity entity, string path,
        List<StoryboardDiagnostic> output)
    {
        InspectUnknown(entity, path, output);
        if (entity.GetBaseState() is ObjectState baseState)
            InspectState(baseState, path, output);
        var states = entity.GetKeyframes();
        for (var index = 0; index < states.Count; index++)
            if (states[index] is ObjectState state) InspectState(state, $"{path}.states[{index}]", output);
    }

    private static void InspectState(ObjectState state, string path, List<StoryboardDiagnostic> output)
    {
        InspectUnknown(state, path, output);
        if (state.Time is JArray array && array.Count == 0)
            Add(state, output, "TIME_ARRAY_EMPTY", $"{path}.time",
                "Time array must contain at least one value.", StoryboardDiagnosticSeverity.Error);
        if (state.RelativeTime.HasValue && state.AddTime.HasValue)
            Add(state, output, "TIME_MODE_CONFLICT", path,
                "relative_time and add_time cannot be used together.", StoryboardDiagnosticSeverity.Error);
    }

    private static void InspectUnknown(IExtensibleStoryboardNode node, string path,
        List<StoryboardDiagnostic> output)
    {
        foreach (var property in node.UnknownProperties)
            Add(node, output, "UNKNOWN_PROPERTY", $"{path}.{Escape(property.Key)}",
                $"Unknown storyboard property '{property.Key}' was preserved.",
                StoryboardDiagnosticSeverity.Warning);
    }

    private static void ValidateReferences(StoryboardRoot document, List<StoryboardDiagnostic> output)
    {
        var entities = new[]
        {
            document.sprites.Cast<IStoryboardEntity>(), document.texts, document.lines, document.videos,
            document.controllers, document.note_controllers
        }.SelectMany(items => items).ToArray();
        var sourceIds = entities.Where(e => !e.IsIdSynthetic && !string.IsNullOrWhiteSpace(e.Id))
            .Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var duplicate in entities.Where(e => !e.IsIdSynthetic && !string.IsNullOrWhiteSpace(e.Id))
                     .GroupBy(e => e.Id, StringComparer.Ordinal).Where(group => group.Count() > 1))
            foreach (var entity in duplicate)
                Add(entity, output, "ID_DUPLICATE", "$",
                    $"id '{duplicate.Key}' is not unique.", StoryboardDiagnosticSeverity.Error);
        foreach (var entity in entities)
        {
            if (!string.IsNullOrWhiteSpace(entity.TargetId) && !sourceIds.Contains(entity.TargetId) &&
                !entity.TargetId.Contains("$note", StringComparison.Ordinal))
                Add(entity, output, "TARGET_NOT_FOUND", "$",
                    $"target_id '{entity.TargetId}' does not resolve to an exported object.",
                    StoryboardDiagnosticSeverity.Error);
            if (!string.IsNullOrWhiteSpace(entity.ParentId) &&
                !sourceIds.Contains(entity.ParentId) && !entity.ParentId.Contains("$note", StringComparison.Ordinal))
                Add(entity, output, "PARENT_NOT_FOUND", "$",
                    $"parent_id '{entity.ParentId}' does not resolve to an exported object.",
                    StoryboardDiagnosticSeverity.Error);
            ValidateTemplates(entity, document.templates, output);
        }
        foreach (var template in document.templates.Values)
            ValidateTemplates(template, document.templates, output);
        foreach (var trigger in document.triggers)
            foreach (var reference in trigger.Spawn.Concat(trigger.Destroy))
                if (!sourceIds.Contains(reference))
                    Add(trigger, output, "TRIGGER_REFERENCE_NOT_FOUND", "$",
                        $"Trigger reference '{reference}' does not resolve to an exported object.",
                        StoryboardDiagnosticSeverity.Error);
    }

    private static void ValidateTemplates(IStoryboardEntity entity,
        IReadOnlyDictionary<string, C2Template> templates, List<StoryboardDiagnostic> output)
    {
        IEnumerable<ObjectState> states = entity.GetKeyframes().Cast<object>()
            .OfType<ObjectState>();
        if (entity.GetBaseState() is ObjectState baseState) states = states.Prepend(baseState);
        foreach (var state in states)
            if (!string.IsNullOrWhiteSpace(state.Template) && !templates.ContainsKey(state.Template))
                Add(state, output, "TEMPLATE_NOT_FOUND", "$",
                    $"Template '{state.Template}' does not exist.", StoryboardDiagnosticSeverity.Error);
    }

    private void AddCorrectionDiagnostics(
        StoryboardRoot document,
        Naziki_Editor.State.ProjectDataContext? context,
        List<StoryboardDiagnostic> output)
    {
        var report = _correctionAnalyzer.Scan(document, context);
        foreach (var issue in report.Issues)
        {
            IExtensibleStoryboardNode node = issue.Kind switch
            {
                StoryboardCorrectionKind.SameTimeConflict when issue.Participants.Count > 0
                    => issue.Participants[0].State,
                StoryboardCorrectionKind.MissingBaseTime
                    => issue.Entity.GetBaseState() as IExtensibleStoryboardNode ?? issue.Entity,
                _ => issue.Entity
            };
            Add(node, output, issue.Code, issue.Path, issue.Message,
                    StoryboardDiagnosticSeverity.Error);
        }
    }

    private void ValidateTemplateCompatibility(
        StoryboardRoot document,
        List<StoryboardDiagnostic> output)
    {
        var aggregates = new Dictionary<TemplateCompatibilityKey, TemplateCompatibilityAggregate>();
        foreach (var (entity, path) in EnumerateEntities(document))
        {
            var stateType = entity.GetBaseState().GetType();
            if (entity.GetBaseState() is ObjectState baseState)
                InspectTemplateReference(document, entity, stateType, baseState,
                    path, aggregates, new HashSet<string>(StringComparer.Ordinal));
            var states = entity.GetKeyframes();
            for (var index = 0; index < states.Count; index++)
                if (states[index] is ObjectState state)
                    InspectTemplateReference(document, entity, stateType, state,
                        $"{path}.states[{index}]", aggregates,
                        new HashSet<string>(StringComparer.Ordinal));
        }

        foreach (var aggregate in aggregates.Values)
        {
            var pathSummary = string.Join(", ", aggregate.ReferencePaths.Take(5));
            if (aggregate.ReferencePaths.Count > 5)
                pathSummary += $", 另有 {aggregate.ReferencePaths.Count - 5} 处";
            var message = aggregate.Code switch
            {
                "TEMPLATE_CYCLE" =>
                    $"Template cycle involving '{aggregate.TemplateName}' was detected at {pathSummary}.",
                "TEMPLATE_PROPERTY_IGNORED" =>
                    $"Template '{aggregate.TemplateName}' property " +
                    $"'{aggregate.JsonPropertyName}' is not applicable to " +
                    $"{aggregate.TargetStateType.Name} and will be ignored; " +
                    $"referenced at {pathSummary}.",
                _ => $"{aggregate.Message} Referenced at {pathSummary}."
            };
            Add(aggregate.Owner, output, aggregate.Code,
                aggregate.PrimaryPath, message, aggregate.Severity);
            Add(aggregate.Template, output, aggregate.Code,
                aggregate.PrimaryPath, message, aggregate.Severity);
        }
    }

    private void InspectTemplateReference(
        StoryboardRoot document,
        IStoryboardEntity owner,
        Type targetStateType,
        ObjectState referenceState,
        string referencePath,
        Dictionary<TemplateCompatibilityKey, TemplateCompatibilityAggregate> aggregates,
        HashSet<string> visiting)
    {
        if (string.IsNullOrWhiteSpace(referenceState.Template) ||
            !document.templates.TryGetValue(referenceState.Template, out var template))
            return;
        var templateName = referenceState.Template;
        if (!visiting.Add(templateName))
        {
            AddCompatibilityIssue(
                owner, template, templateName, targetStateType,
                "TEMPLATE_CYCLE", "template",
                $"$.templates.{Escape(templateName)}",
                referencePath,
                "Nested template references form a cycle.",
                StoryboardDiagnosticSeverity.Error,
                aggregates);
            return;
        }

        InspectTemplateState(document, owner, targetStateType, template,
            templateName, template.BaseState,
            $"$.templates.{Escape(templateName)}",
            referencePath, aggregates, visiting);
        for (var index = 0; index < template.Keyframes.Count; index++)
            InspectTemplateState(document, owner, targetStateType, template,
                templateName, template.Keyframes[index],
                $"$.templates.{Escape(templateName)}.states[{index}]",
                referencePath, aggregates, visiting);
        visiting.Remove(templateName);
    }

    private void InspectTemplateState(
        StoryboardRoot document,
        IStoryboardEntity owner,
        Type targetStateType,
        C2Template template,
        string templateName,
        TemplateState state,
        string statePath,
        string referencePath,
        Dictionary<TemplateCompatibilityKey, TemplateCompatibilityAggregate> aggregates,
        HashSet<string> visiting)
    {
        foreach (var issue in _templatePropertyMapper.Analyze(
                     targetStateType, state, statePath))
            AddCompatibilityIssue(
                owner, template, templateName, targetStateType,
                issue.Code, issue.JsonPropertyName, issue.SourcePath,
                referencePath, issue.Message, issue.Severity, aggregates);

        if (!string.IsNullOrWhiteSpace(state.Template))
            InspectTemplateReference(document, owner, targetStateType, state,
                statePath, aggregates, visiting);
    }

    private static void AddCompatibilityIssue(
        IStoryboardEntity owner,
        C2Template template,
        string templateName,
        Type targetStateType,
        string code,
        string jsonPropertyName,
        string primaryPath,
        string referencePath,
        string message,
        StoryboardDiagnosticSeverity severity,
        Dictionary<TemplateCompatibilityKey, TemplateCompatibilityAggregate> aggregates)
    {
        var key = new TemplateCompatibilityKey(
            owner, template, code, jsonPropertyName, targetStateType);
        if (!aggregates.TryGetValue(key, out var aggregate))
        {
            aggregate = new TemplateCompatibilityAggregate
            {
                Owner = owner,
                Template = template,
                TemplateName = templateName,
                TargetStateType = targetStateType,
                Code = code,
                JsonPropertyName = jsonPropertyName,
                PrimaryPath = primaryPath,
                Message = message,
                Severity = severity
            };
            aggregates[key] = aggregate;
        }
        aggregate.ReferencePaths.Add(referencePath);
    }

    private static IEnumerable<(IStoryboardEntity Entity, string Path)>
        EnumerateEntities(StoryboardRoot document)
    {
        for (var index = 0; index < document.sprites.Count; index++)
            yield return (document.sprites[index], $"$.sprites[{index}]");
        for (var index = 0; index < document.texts.Count; index++)
            yield return (document.texts[index], $"$.texts[{index}]");
        for (var index = 0; index < document.lines.Count; index++)
            yield return (document.lines[index], $"$.lines[{index}]");
        for (var index = 0; index < document.videos.Count; index++)
            yield return (document.videos[index], $"$.videos[{index}]");
        for (var index = 0; index < document.controllers.Count; index++)
            yield return (document.controllers[index], $"$.controllers[{index}]");
        for (var index = 0; index < document.note_controllers.Count; index++)
            yield return (document.note_controllers[index], $"$.note_controllers[{index}]");
    }

    private sealed record TemplateCompatibilityKey(
        IStoryboardEntity Owner,
        C2Template Template,
        string Code,
        string JsonPropertyName,
        Type TargetStateType);

    private sealed class TemplateCompatibilityAggregate
    {
        public required IStoryboardEntity Owner { get; init; }
        public required C2Template Template { get; init; }
        public required string TemplateName { get; init; }
        public required Type TargetStateType { get; init; }
        public required string Code { get; init; }
        public required string JsonPropertyName { get; init; }
        public required string PrimaryPath { get; init; }
        public required string Message { get; init; }
        public required StoryboardDiagnosticSeverity Severity { get; init; }
        public HashSet<string> ReferencePaths { get; } = new(StringComparer.Ordinal);
    }
    private static void Add(IExtensibleStoryboardNode node, List<StoryboardDiagnostic> output,
        string code, string path, string message, StoryboardDiagnosticSeverity severity)
    {
        var diagnostic = new StoryboardDiagnostic(code, path, message, severity, node);
        node.Diagnostics.Add(diagnostic);
        output.Add(diagnostic);
    }

    private static void Clear(IExtensibleStoryboardNode node)
    {
        node.Diagnostics.Clear();
        if (node is StoryboardRoot root)
        {
            foreach (var entity in root.sprites.Cast<IStoryboardEntity>().Concat(root.texts)
                         .Concat(root.lines).Concat(root.videos).Concat(root.controllers)
                         .Concat(root.note_controllers).Concat(root.templates.Values))
                Clear(entity);
            foreach (var trigger in root.triggers) Clear(trigger);
        }
        else if (node is IStoryboardEntity entity)
        {
            if (entity.GetBaseState() is IExtensibleStoryboardNode state) Clear(state);
            foreach (var item in entity.GetKeyframes())
                if (item is IExtensibleStoryboardNode child) Clear(child);
        }
    }

    private static string Escape(string value) => value.Replace(".", "\\.", StringComparison.Ordinal);
}

public sealed class EditorSnapshotSerializer : IEditorSnapshotSerializer
{
    private readonly JsonSerializerSettings _settings = StoryboardJsonSettings.Create(snapshot: true);
    public string Serialize(object value) => JsonConvert.SerializeObject(value, _settings);
    public T? Deserialize<T>(string json) where T : class =>
        JsonConvert.DeserializeObject<T>(json, _settings);
    public object? Deserialize(string json, Type type) =>
        JsonConvert.DeserializeObject(json, type, _settings);
}

internal sealed class EditorSnapshotEntityJsonConverter : JsonConverter
{
    public override bool CanConvert(Type objectType) => typeof(IStoryboardEntity).IsAssignableFrom(objectType);
    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        var entity = (IStoryboardEntity)value!;
        var json = JObject.FromObject(entity.GetBaseState(), serializer);
        json["_editor_id"] = entity.Id;
        json["_editor_id_synthetic"] = entity.IsIdSynthetic;
        if (!string.IsNullOrWhiteSpace(entity.TargetId)) json["target_id"] = entity.TargetId;
        if (!string.IsNullOrWhiteSpace(entity.ParentId)) json["parent_id"] = entity.ParentId;
        if (entity.GetKeyframes().Count > 0) json["states"] = JArray.FromObject(entity.GetKeyframes(), serializer);
        foreach (var property in entity.UnknownProperties)
            if (json[property.Key] is null) json[property.Key] = property.Value.DeepClone();
        json.WriteTo(writer);
    }
    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        var json = JObject.Load(reader);
        var entity = (IStoryboardEntity)(existingValue ?? Activator.CreateInstance(objectType)!);
        entity.Id = json.Value<string>("_editor_id");
        entity.IsIdSynthetic = json.Value<bool?>("_editor_id_synthetic") ?? false;
        entity.TargetId = json.Value<string>("target_id");
        entity.ParentId = json.Value<string>("parent_id");
        var stateJson = (JObject)json.DeepClone();
        foreach (var key in new[] { "_editor_id", "_editor_id_synthetic", "target_id", "parent_id", "states" })
            stateJson.Remove(key);
        serializer.Populate(stateJson.CreateReader(), entity.GetBaseState());
        if (json["states"] is JArray states)
        {
            var list = entity.GetKeyframes();
            list.Clear();
            foreach (var stateToken in states)
            {
                var state = stateToken.ToObject(entity.GetBaseState().GetType(), serializer);
                if (state is not null) list.Add(state);
            }
        }
        return entity;
    }
}
