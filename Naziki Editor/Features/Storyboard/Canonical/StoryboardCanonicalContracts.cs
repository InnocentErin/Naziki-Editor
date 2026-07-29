using Naziki_Editor.Models;
using Naziki_Editor.State;
using Newtonsoft.Json.Linq;

namespace Naziki_Editor.Core.Abstractions;

public sealed record StoryboardImportIssue(
    string Code,
    string Path,
    string Message,
    StoryboardDiagnosticSeverity Severity);

public sealed record StoryboardImportResult(
    EditorStoryboardDocument? Document,
    IReadOnlyList<StoryboardImportIssue> Issues)
{
    private static readonly HashSet<string> ReplacementBlockingCodes =
        new(StringComparer.Ordinal)
        {
            "INVALID_JSON", "COLLECTION_NOT_ARRAY", "ENTITY_NOT_OBJECT",
            "TEMPLATES_NOT_OBJECT", "TEMPLATE_NOT_OBJECT",
            "TRIGGERS_NOT_ARRAY", "STATE_NOT_OBJECT", "STATES_NOT_ARRAY",
            "TIME_INVALID", "RELATIVE_TIME_INVALID", "ADD_TIME_INVALID",
            "TIME_OFFSET_INVALID", "NOTE_BINDING_INVALID",
            "NOTE_QUERY_TYPE_INVALID", "NOTE_ARRAY_ITEM_INVALID",
            "MULTIPLE_TIME_ARRAY_FIELDS", "TIME_ARRAY_EMPTY",
            "NOTE_ARRAY_EMPTY"
        };

    public bool CanReplace => Document is not null &&
                              Issues.All(issue =>
                                  !ReplacementBlockingCodes.Contains(
                                      issue.Code));

    public bool Success => Document is not null &&
                           Issues.All(issue =>
                               issue.Severity != StoryboardDiagnosticSeverity.Error);
}

public interface IStoryboardImportService
{
    StoryboardImportResult Import(string json, C2Chart? chart = null,
        StoryboardMeta? legacyMeta = null,
        IReadOnlyDictionary<string, string>? legacyControlBoardIds = null);
}

public interface IEditorStoryboardSerializer
{
    string Serialize(EditorStoryboardDocument document);
    EditorStoryboardDocument Deserialize(string json);
}

public sealed record EditorStoryboardValidationIssue(
    string Code,
    string Path,
    string Message,
    StoryboardDiagnosticSeverity Severity,
    bool BlocksSourceSave,
    bool BlocksRuntimeExport);

public interface IEditorStoryboardValidator
{
    IReadOnlyList<EditorStoryboardValidationIssue> Validate(
        EditorStoryboardDocument document);
}

public interface IStoryboardTimePositionResolver
{
    double? Resolve(StoryboardTimePosition position, C2Chart? chart,
        ITimeEngine? timeEngine, int? currentNoteId = null,
        double? templateStart = null, double? triggerTime = null);
}

public interface INoteQueryService
{
    IReadOnlyList<C2Note> Match(C2Chart? chart, NoteQuery query);
}

public sealed record MaterializedStoryboardFrame(
    string OccurrenceId,
    string FrameId,
    StoryboardTimePosition Time,
    double? EffectiveTime,
    int Sequence,
    JObject EffectiveState,
    string? Easing,
    bool? Destroy,
    string? SourceTemplate,
    int? BoundNoteId);

public sealed record MaterializedStoryboardEntity(
    string OccurrenceId,
    string EditorId,
    EditorStoryboardEntityKind Kind,
    string? RuntimeId,
    string? TargetId,
    string? ParentId,
    StoryboardActivationMode ActivationMode,
    StoryboardTimePosition? ActivationTime,
    double? EffectiveActivationTime,
    int? BoundNoteId,
    JObject BaseState,
    IReadOnlyList<MaterializedStoryboardFrame> Frames);

public sealed record MaterializedStoryboard(
    IReadOnlyList<MaterializedStoryboardEntity> Entities,
    JArray Triggers,
    IReadOnlyList<StoryboardImportIssue> Issues);

public interface IStoryboardMaterializer
{
    MaterializedStoryboard Materialize(EditorStoryboardDocument document,
        C2Chart? chart, ITimeEngine? timeEngine);
}

public sealed record StoryboardRuntimeExportResult(
    JObject Json,
    IReadOnlyList<StoryboardImportIssue> Issues)
{
    public bool Success => Issues.All(issue =>
        issue.Severity != StoryboardDiagnosticSeverity.Error);
}

public interface IStoryboardRuntimeExporter
{
    StoryboardRuntimeExportResult Export(EditorStoryboardDocument document,
        C2Chart? chart, ITimeEngine? timeEngine);
}

public interface IStoryboardSourceStore
{
    string GetDefaultSourcePath(string projectFilePath);
    EditorStoryboardDocument Load(string sourcePath);
    void Save(string sourcePath, EditorStoryboardDocument document);
}

public sealed record StoryboardImportCandidate(
    EditorStoryboardDocument Document,
    JObject RuntimeJson,
    StoryboardRoot LegacyProjection,
    IReadOnlyList<StoryboardImportIssue> Issues,
    string SourceHash,
    string RuntimeHash);

public sealed record StoryboardImportCommitResult(
    EditorStoryboardDocument Document,
    StoryboardRoot LegacyProjection,
    string StoryboardSourcePath,
    string StoryboardRuntimePath,
    string RuntimeHash,
    IReadOnlyList<StoryboardImportIssue> Issues);

/// <summary>
/// Owns the v3 import boundary. External Storyboard JSON is normalized and
/// validated before project memory or any managed project file is replaced.
/// </summary>
public interface IStoryboardImportCoordinator
{
    StoryboardImportCandidate Prepare(
        string json,
        C2Chart? chart = null,
        ITimeEngine? timeEngine = null,
        StoryboardMeta? legacyMeta = null,
        IReadOnlyDictionary<string, string>? controlBoardIds = null);

    Task<StoryboardImportCommitResult> ImportAndCommitAsync(
        ProjectDataContext context,
        string externalStoryboardPath,
        CancellationToken cancellationToken = default);

    Task<StoryboardImportCommitResult> CommitCurrentAsync(
        ProjectDataContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the canonical source, or reconstructs it from the managed runtime
    /// file when a v3 project was left without a usable source.
    /// </summary>
    EditorStoryboardDocument EnsureCanonicalSource(
        ProjectDataContext context);
}

public interface IEditorStoryboardEditService
{
    void MoveFrame(EditorStoryboardDocument document, string frameId,
        double deltaSeconds);

    void ApplyFramePatch(EditorStoryboardDocument document, string frameId,
        JObject patch);

    void ApplyTemplateFrameOverride(EditorStoryboardDocument document,
        string entityId, string? bindingOwnerFrameId, string templateFrameId,
        JObject patch);

    void SetNoteInstanceOverride(EditorStoryboardDocument document,
        string entityId, int noteId, JObject? basePatch = null,
        bool? excluded = null);

    void DetachRootTemplate(EditorStoryboardDocument document, string entityId,
        C2Chart? chart, ITimeEngine? timeEngine);

    EditorStoryboardTemplate AddTemplate(EditorStoryboardDocument document,
        string name);

    void UpdateTemplate(EditorStoryboardDocument document, string templateId,
        EditorStoryboardTemplate replacement);

    void RenameTemplate(EditorStoryboardDocument document, string templateId,
        string newName);

    IReadOnlyList<string> GetTemplateDependents(
        EditorStoryboardDocument document, string templateId);

    void DeleteTemplate(EditorStoryboardDocument document, string templateId);
}

public sealed record CanonicalTemplateEditResult(
    string TemplateId,
    string OriginalName,
    string NewName,
    C2Template WireTemplate);

/// <summary>
/// Adapts canonical templates to the existing official Storyboard editing
/// surface without making the runtime projection an editable source.
/// </summary>
public interface IStoryboardTemplateViewAdapter
{
    C2Template CreateWireView(EditorStoryboardTemplate template);

    EditorStoryboardTemplate ParseWireView(string name,
        C2Template wireTemplate);
}

/// <summary>
/// Temporary adapter while legacy views still edit StoryboardRoot. It detects
/// actual changes to that projection; unchanged views never overwrite the
/// canonical source.
/// </summary>
public interface IStoryboardCanonicalBridge
{
    EditorStoryboardDocument Synchronize(ProjectDataContext context);
    StoryboardRuntimeExportResult Export(ProjectDataContext context);
    StoryboardRoot CreateLegacyProjection(ProjectDataContext context);
    string ComputeLegacyProjectionHash(StoryboardRoot storyboard);
}
