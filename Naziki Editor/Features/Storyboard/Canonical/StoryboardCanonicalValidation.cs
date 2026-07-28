using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Models;
using Newtonsoft.Json.Linq;

namespace Naziki_Editor.Core.Storyboard.Canonical;

/// <summary>
/// Validates the editor-owned schema independently from wire-format
/// validation. Structural corruption blocks source persistence; recoverable
/// semantic problems remain saveable but block runtime materialization.
/// </summary>
public sealed class EditorStoryboardValidator : IEditorStoryboardValidator
{
    private static readonly HashSet<string> StructuralPatchFields =
        new(StringComparer.Ordinal)
        {
            "time", "relative_time", "add_time", "states", "template",
            "reset", "note", "easing", "destroy"
        };

    public IReadOnlyList<EditorStoryboardValidationIssue> Validate(
        EditorStoryboardDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var issues = new List<EditorStoryboardValidationIssue>();
        if (document.SchemaVersion != 1)
            AddStructural(issues, "CANONICAL_SCHEMA_UNSUPPORTED", "$.schema_version",
                $"Unsupported canonical schema {document.SchemaVersion}.");
        if (string.IsNullOrWhiteSpace(document.DocumentId))
            AddStructural(issues, "CANONICAL_DOCUMENT_ID_MISSING", "$.document_id",
                "document_id is required.");

        var entityIds = new HashSet<string>(StringComparer.Ordinal);
        var allFrameIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in document.Entities ?? [])
        {
            var path = $"$.entities[editor_id='{entity.EditorId}']";
            if (string.IsNullOrWhiteSpace(entity.EditorId))
                AddStructural(issues, "CANONICAL_EDITOR_ID_MISSING", path,
                    "Every entity requires an editor_id.");
            else if (!entityIds.Add(entity.EditorId))
                AddStructural(issues, "CANONICAL_EDITOR_ID_DUPLICATE", path,
                    $"editor_id '{entity.EditorId}' is duplicated.");
            ValidatePatch(entity.BasePatch, $"{path}.base_patch", issues);
            ValidateFrames(entity.Frames, path, allFrameIds, issues);
            ValidateBinding(entity.RootTemplate, $"{path}.root_template",
                document, issues);
            foreach (var frame in entity.Frames ?? [])
                ValidateBinding(frame.Template,
                    $"{path}.frames[frame_id='{frame.FrameId}'].template",
                    document, issues);

            if (entity.ActivationMode == StoryboardActivationMode.Inactive)
                AddSemantic(issues, "ENTITY_NOT_ACTIVATABLE", path,
                    "The entity has no activation time or trigger reference.");
            if (entity.ActivationTime?.Kind ==
                StoryboardTimeAnchorKind.Unresolved)
                AddSemantic(issues, "ACTIVATION_TIME_UNRESOLVED",
                    $"{path}.activation_time",
                    "The activation anchor cannot be resolved.");
        }

        var templateIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, template) in document.Templates ??
                                          new Dictionary<string,
                                              EditorStoryboardTemplate>())
        {
            var path = $"$.templates['{name}']";
            if (!string.Equals(name, template.Name, StringComparison.Ordinal))
                AddStructural(issues, "CANONICAL_TEMPLATE_KEY_MISMATCH", path,
                    "Template dictionary key and template name differ.");
            if (string.IsNullOrWhiteSpace(template.TemplateId) ||
                !templateIds.Add(template.TemplateId))
                AddStructural(issues, "CANONICAL_TEMPLATE_ID_INVALID", path,
                    "Template IDs must be non-empty and unique.");
            ValidatePatch(template.BasePatch, $"{path}.base_patch", issues);
            ValidateFrames(template.Frames, path, allFrameIds, issues);
            ValidateBinding(template.RootTemplate, $"{path}.root_template",
                document, issues);
            foreach (var frame in template.Frames ?? [])
                ValidateBinding(frame.Template,
                    $"{path}.frames[frame_id='{frame.FrameId}'].template",
                    document, issues);
        }
        ValidateTemplateCycles(document, issues);
        return issues;
    }

    private static void ValidateFrames(IEnumerable<EditorStoryboardFrame>? frames,
        string ownerPath, HashSet<string> allFrameIds,
        List<EditorStoryboardValidationIssue> issues)
    {
        var sequences = new HashSet<int>();
        foreach (var frame in frames ?? [])
        {
            var path = $"{ownerPath}.frames[frame_id='{frame.FrameId}']";
            if (string.IsNullOrWhiteSpace(frame.FrameId) ||
                !allFrameIds.Add(frame.FrameId))
                AddStructural(issues, "CANONICAL_FRAME_ID_INVALID", path,
                    "Frame IDs must be non-empty and globally unique.");
            if (!sequences.Add(frame.Sequence))
                AddStructural(issues, "CANONICAL_SEQUENCE_DUPLICATE", path,
                    $"Sequence {frame.Sequence} is duplicated in one owner.");
            if (frame.Time is null)
                AddStructural(issues, "CANONICAL_FRAME_TIME_MISSING",
                    $"{path}.time", "Every frame requires a typed time.");
            else if (frame.Time.Kind == StoryboardTimeAnchorKind.Unresolved)
                AddSemantic(issues, "FRAME_TIME_UNRESOLVED", $"{path}.time",
                    "The frame time anchor cannot be resolved.");
            ValidatePatch(frame.Patch, $"{path}.patch", issues);
        }
    }

    private static void ValidatePatch(JObject? patch, string path,
        List<EditorStoryboardValidationIssue> issues)
    {
        if (patch is null)
        {
            AddStructural(issues, "CANONICAL_PATCH_MISSING", path,
                "Patch objects must not be null.");
            return;
        }
        foreach (var property in patch.Properties().Where(property =>
                     StructuralPatchFields.Contains(property.Name)))
            AddStructural(issues, "CANONICAL_STRUCTURAL_FIELD_IN_PATCH",
                $"{path}.{property.Name}",
                $"'{property.Name}' belongs to the canonical frame structure, not a value patch.");
        foreach (var unit in patch.DescendantsAndSelf().OfType<JObject>()
                     .Where(StoryboardCanonicalValues.IsUnitToken))
        {
            if (unit["value"]?.Type is not
                    (JTokenType.Integer or JTokenType.Float) ||
                string.IsNullOrWhiteSpace(unit.Value<string>("unit")))
                AddStructural(issues, "CANONICAL_UNIT_INVALID", path,
                    "Typed unit values require numeric value and non-empty unit.");
        }
    }

    private static void ValidateBinding(EditorTemplateBinding? binding,
        string path, EditorStoryboardDocument document,
        List<EditorStoryboardValidationIssue> issues)
    {
        if (binding is null) return;
        if (string.IsNullOrWhiteSpace(binding.TemplateName) ||
            !document.Templates.ContainsKey(binding.TemplateName))
            AddSemantic(issues, "TEMPLATE_MISSING", path,
                $"Template '{binding.TemplateName}' does not exist.");
    }

    private static void ValidateTemplateCycles(EditorStoryboardDocument document,
        List<EditorStoryboardValidationIssue> issues)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in document.Templates.Keys)
            Visit(name);

        void Visit(string name)
        {
            if (visited.Contains(name) || !document.Templates.TryGetValue(name,
                    out var template))
                return;
            if (!visiting.Add(name))
            {
                AddSemantic(issues, "TEMPLATE_CYCLE",
                    $"$.templates['{name}']",
                    $"Template cycle includes '{name}'.");
                return;
            }
            var references = new[] { template.RootTemplate }
                .Concat(template.Frames.Select(frame => frame.Template))
                .Where(binding => binding is not null)
                .Select(binding => binding!.TemplateName);
            foreach (var reference in references)
                Visit(reference);
            visiting.Remove(name);
            visited.Add(name);
        }
    }

    private static void AddStructural(
        List<EditorStoryboardValidationIssue> issues, string code, string path,
        string message) => issues.Add(new(code, path, message,
        StoryboardDiagnosticSeverity.Error, true, true));

    private static void AddSemantic(
        List<EditorStoryboardValidationIssue> issues, string code, string path,
        string message) => issues.Add(new(code, path, message,
        StoryboardDiagnosticSeverity.Error, false, true));
}
