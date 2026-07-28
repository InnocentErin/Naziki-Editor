using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace Naziki_Editor.Models;

/// <summary>
/// Project-owned storyboard representation. This is deliberately independent
/// from the Cytoid wire DTOs in <see cref="StoryboardRoot"/>.
/// </summary>
public sealed class EditorStoryboardDocument
{
    [JsonProperty("schema_version", Order = -100)]
    public int SchemaVersion { get; set; } = 1;

    [JsonProperty("document_id", Order = -90)]
    public string DocumentId { get; set; } = Guid.NewGuid().ToString("N");

    [JsonProperty("revision", Order = -80)]
    public long Revision { get; set; }

    [JsonProperty("entities")]
    public List<EditorStoryboardEntity> Entities { get; set; } = [];

    [JsonProperty("templates")]
    public Dictionary<string, EditorStoryboardTemplate> Templates { get; set; } =
        new(StringComparer.Ordinal);

    [JsonProperty("triggers")]
    public JArray Triggers { get; set; } = [];

    [JsonProperty("root_properties")]
    public JObject RootProperties { get; set; } = new();

    [JsonProperty("metadata")]
    public EditorStoryboardMetadata Metadata { get; set; } = new();

    [JsonIgnore]
    public bool IsEmpty => Entities.Count == 0 && Templates.Count == 0 &&
                           Triggers.Count == 0 && !RootProperties.HasValues;
}

[JsonConverter(typeof(StringEnumConverter))]
public enum EditorStoryboardEntityKind
{
    Sprite,
    Text,
    Line,
    Video,
    SceneController,
    NoteController
}

[JsonConverter(typeof(StringEnumConverter))]
public enum StoryboardActivationMode
{
    Explicit,
    FirstFrame,
    GlobalController,
    TriggerSpawn,
    Inactive
}

public sealed class EditorStoryboardEntity
{
    [JsonProperty("editor_id")]
    public string EditorId { get; set; } = Guid.NewGuid().ToString("N");

    [JsonProperty("source_group_id")]
    public string SourceGroupId { get; set; } = "";

    [JsonProperty("kind")]
    public EditorStoryboardEntityKind Kind { get; set; }

    [JsonProperty("source_order")]
    public int SourceOrder { get; set; }

    [JsonProperty("runtime_id")]
    public EditorInterpolatedString? RuntimeId { get; set; }

    [JsonProperty("target_id")]
    public EditorInterpolatedString? TargetId { get; set; }

    [JsonProperty("parent_id")]
    public EditorInterpolatedString? ParentId { get; set; }

    [JsonProperty("activation_mode")]
    public StoryboardActivationMode ActivationMode { get; set; }

    [JsonProperty("activation_time")]
    public StoryboardTimePosition? ActivationTime { get; set; }

    [JsonProperty("note_binding")]
    public EditorNoteBinding? NoteBinding { get; set; }

    [JsonProperty("base_patch")]
    public JObject BasePatch { get; set; } = new();

    [JsonProperty("root_template")]
    public EditorTemplateBinding? RootTemplate { get; set; }

    [JsonProperty("frames")]
    public List<EditorStoryboardFrame> Frames { get; set; } = [];

    [JsonProperty("instance_overrides")]
    public Dictionary<int, EditorNoteInstanceOverride> InstanceOverrides { get; set; } = [];

    [JsonProperty("excluded_note_ids")]
    public HashSet<int> ExcludedNoteIds { get; set; } = [];

    [JsonProperty("source")]
    public EditorSourceInfo Source { get; set; } = new();
}

public sealed class EditorStoryboardTemplate
{
    [JsonProperty("template_id")]
    public string TemplateId { get; set; } = Guid.NewGuid().ToString("N");

    [JsonProperty("name")]
    public string Name { get; set; } = "";

    [JsonProperty("base_patch")]
    public JObject BasePatch { get; set; } = new();

    [JsonProperty("root_template")]
    public EditorTemplateBinding? RootTemplate { get; set; }

    [JsonProperty("default_relative_seconds")]
    public double? DefaultRelativeSeconds { get; set; }

    [JsonProperty("default_add_seconds")]
    public double? DefaultAddSeconds { get; set; }

    [JsonProperty("frames")]
    public List<EditorStoryboardFrame> Frames { get; set; } = [];

    [JsonProperty("source")]
    public EditorSourceInfo Source { get; set; } = new();
}

public sealed class EditorStoryboardFrame
{
    [JsonProperty("frame_id")]
    public string FrameId { get; set; } = Guid.NewGuid().ToString("N");

    [JsonProperty("sequence")]
    public int Sequence { get; set; }

    [JsonProperty("time")]
    public StoryboardTimePosition Time { get; set; } = StoryboardTimePosition.Unresolved();

    [JsonProperty("patch")]
    public JObject Patch { get; set; } = new();

    [JsonProperty("easing")]
    public string? Easing { get; set; }

    [JsonProperty("destroy")]
    public bool? Destroy { get; set; }

    [JsonProperty("reset")]
    public bool Reset { get; set; }

    /// <summary>
    /// Explicit inheritance edge used after nested wire states are flattened.
    /// Null means the preceding frame in the same source scope.
    /// </summary>
    [JsonProperty("inherit_from_frame_id")]
    public string? InheritFromFrameId { get; set; }

    [JsonProperty("template")]
    public EditorTemplateBinding? Template { get; set; }

    [JsonProperty("note_binding")]
    public EditorNoteBinding? NoteBinding { get; set; }

    [JsonProperty("has_inline_children")]
    public bool HasInlineChildren { get; set; }

    [JsonProperty("source")]
    public EditorSourceInfo Source { get; set; } = new();
}

public sealed class EditorTemplateBinding
{
    [JsonProperty("template_name")]
    public string TemplateName { get; set; } = "";

    [JsonProperty("overrides")]
    public JObject Overrides { get; set; } = new();

    [JsonProperty("frame_overrides")]
    public Dictionary<string, JObject> FrameOverrides { get; set; } = [];

    [JsonProperty("orphaned_overrides")]
    public Dictionary<string, JObject> OrphanedOverrides { get; set; } = [];
}

public sealed class EditorNoteBinding
{
    [JsonProperty("note_id")]
    public int? NoteId { get; set; }

    [JsonProperty("query")]
    public NoteQuery? Query { get; set; }

    [JsonIgnore]
    public bool IsQuery => Query is not null;
}

public sealed class NoteQuery
{
    [JsonProperty("type")]
    public List<int> Types { get; set; } = [];

    [JsonProperty("start")]
    public int? Start { get; set; }

    [JsonProperty("end")]
    public int? End { get; set; }

    [JsonProperty("direction")]
    public int? Direction { get; set; }

    [JsonProperty("min_x")]
    public double? MinX { get; set; }

    [JsonProperty("max_x")]
    public double? MaxX { get; set; }

    [JsonProperty("unknown_properties")]
    public JObject UnknownProperties { get; set; } = new();
}

public sealed class EditorNoteInstanceOverride
{
    [JsonProperty("excluded")]
    public bool Excluded { get; set; }

    [JsonProperty("base_patch")]
    public JObject BasePatch { get; set; } = new();

    [JsonProperty("frame_patches")]
    public Dictionary<string, JObject> FramePatches { get; set; } = [];

    [JsonProperty("dormant")]
    public bool Dormant { get; set; }
}

public sealed class EditorInterpolatedString
{
    [JsonProperty("literal")]
    public string Literal { get; set; } = "";

    [JsonProperty("uses_current_note")]
    public bool UsesCurrentNote { get; set; }

    public static EditorInterpolatedString? FromWire(string? value) =>
        value is null
            ? null
            : new EditorInterpolatedString
            {
                Literal = value,
                UsesCurrentNote = value.Contains("$note", StringComparison.Ordinal)
            };

    public string Resolve(int? noteId)
    {
        if (!UsesCurrentNote) return Literal;
        if (!noteId.HasValue)
            throw new InvalidOperationException(
                $"Runtime reference '{Literal}' requires a bound note.");
        return Literal.Replace("$note", noteId.Value.ToString(
            System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }
}

public sealed class EditorSourceInfo
{
    [JsonProperty("path")]
    public string Path { get; set; } = "$";

    [JsonProperty("import_hash")]
    public string? ImportHash { get; set; }

    [JsonProperty("source_order")]
    public int SourceOrder { get; set; }
}

public sealed class EditorStoryboardMetadata
{
    [JsonProperty("import_hash")]
    public string? ImportHash { get; set; }

    [JsonProperty("last_export_hash")]
    public string? LastExportHash { get; set; }

    [JsonProperty("legacy_meta")]
    public JObject LegacyMeta { get; set; } = new();

    [JsonProperty("control_board_id_maps")]
    public Dictionary<string, string> ControlBoardIdMaps { get; set; } =
        new(StringComparer.Ordinal);

    [JsonProperty("syntax_statistics")]
    public Dictionary<string, int> SyntaxStatistics { get; set; } =
        new(StringComparer.Ordinal);

    [JsonProperty("import_diagnostics")]
    public List<EditorStoryboardStoredDiagnostic> ImportDiagnostics
    {
        get;
        set;
    } = [];
}

public sealed class EditorStoryboardStoredDiagnostic
{
    [JsonProperty("code")] public string Code { get; set; } = "";
    [JsonProperty("path")] public string Path { get; set; } = "$";
    [JsonProperty("message")] public string Message { get; set; } = "";
    [JsonProperty("severity")] public string Severity { get; set; } = "Warning";
}
