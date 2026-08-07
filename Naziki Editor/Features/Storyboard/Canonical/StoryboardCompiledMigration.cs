using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Models;
using Newtonsoft.Json.Linq;

namespace Naziki_Editor.Core.Storyboard.Canonical;

/// <summary>
/// Converts the player's direct-deserialization storyboard representation into
/// the normal wire representation used by the editor and preview exporter.
/// </summary>
internal static class StoryboardCompiledMigration
{
    private static readonly string[] Collections =
    [
        "sprites", "texts", "lines", "videos", "controllers",
        "note_controllers"
    ];

    public static IReadOnlyList<StoryboardImportIssue> ExpandWireRoot(
        JObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (root.Value<bool?>("compiled") != true)
            return [];

        var issues = new List<StoryboardImportIssue>();
        var promoted = 0;
        foreach (var collection in Collections)
        {
            if (root[collection] is not JArray entities)
                continue;

            for (var index = 0; index < entities.Count; index++)
            {
                var path = $"$.{collection}[{index}]";
                if (entities[index] is not JObject entity)
                    continue;
                if (entity["states"] is not JArray { Count: > 0 } states ||
                    states[0] is not JObject initialState)
                {
                    issues.Add(new StoryboardImportIssue(
                        "COMPILED_INITIAL_STATE_MISSING",
                        $"{path}.states[0]",
                        "Compiled storyboard entities require a first state.",
                        StoryboardDiagnosticSeverity.Error));
                    continue;
                }

                foreach (var property in initialState.Properties().ToArray())
                {
                    if (entity.TryGetValue(property.Name, out var existing))
                    {
                        if (!JToken.DeepEquals(existing, property.Value))
                            issues.Add(new StoryboardImportIssue(
                                "COMPILED_BASE_STATE_CONFLICT",
                                $"{path}.{property.Name}",
                                $"The compiled root and states[0] contain different values for '{property.Name}'.",
                                StoryboardDiagnosticSeverity.Error));
                        continue;
                    }

                    entity[property.Name] = property.Value.DeepClone();
                }

                states.RemoveAt(0);
                if (states.Count == 0)
                    entity.Remove("states");
                promoted++;
            }
        }

        root.Remove("compiled");
        issues.Add(new StoryboardImportIssue(
            "COMPILED_STORYBOARD_EXPANDED",
            "$",
            $"Expanded {promoted} compiled storyboard initial states into normal wire roots.",
            StoryboardDiagnosticSeverity.Warning));
        return issues;
    }

    public static void MigrateCanonicalDocument(EditorStoryboardDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.RootProperties.Value<bool?>("compiled") != true)
            return;

        var promoted = 0;
        foreach (var entity in document.Entities)
        {
            var first = entity.Frames
                .Select((frame, index) => (frame, index))
                .OrderBy(item => item.frame.Sequence)
                .ThenBy(item => item.index)
                .Select(item => item.frame)
                .FirstOrDefault();
            if (first is null)
            {
                AddStoredDiagnostic(document,
                    "COMPILED_INITIAL_STATE_MISSING",
                    $"{entity.Source.Path}.states[0]",
                    "Compiled storyboard entity has no initial frame to promote.",
                    StoryboardDiagnosticSeverity.Error);
                continue;
            }

            if (first.Reset)
                entity.BasePatch = (JObject)first.Patch.DeepClone();
            else
                foreach (var property in first.Patch.Properties())
                    entity.BasePatch[property.Name] = property.Value.DeepClone();
            if (!string.IsNullOrWhiteSpace(first.Easing))
                entity.RootEasing = first.Easing;
            if (first.Destroy.HasValue)
                entity.RootDestroy = first.Destroy.Value;

            if (first.Template is not null)
            {
                if (entity.RootTemplate is not null &&
                    !Equivalent(entity.RootTemplate, first.Template))
                    AddStoredDiagnostic(document,
                        "COMPILED_TEMPLATE_BINDING_CONFLICT",
                        $"{entity.Source.Path}.states[0].template",
                        "Compiled root and initial state contain different template bindings.",
                        StoryboardDiagnosticSeverity.Error);
                else
                    entity.RootTemplate = first.Template;
            }

            if (first.NoteBinding is not null)
            {
                if (entity.NoteBinding is not null &&
                    !Equivalent(entity.NoteBinding, first.NoteBinding))
                    AddStoredDiagnostic(document,
                        "COMPILED_NOTE_BINDING_CONFLICT",
                        $"{entity.Source.Path}.states[0].note",
                        "Compiled root and initial state contain different note bindings.",
                        StoryboardDiagnosticSeverity.Error);
                else
                    entity.NoteBinding = first.NoteBinding;
            }

            entity.ActivationTime = first.Time;
            entity.ActivationMode = first.Time.Kind ==
                                    StoryboardTimeAnchorKind.TriggerSpawn
                ? StoryboardActivationMode.TriggerSpawn
                : StoryboardActivationMode.Explicit;
            entity.Frames.Remove(first);
            foreach (var frame in entity.Frames.Where(frame =>
                         string.Equals(frame.InheritFromFrameId,
                             first.FrameId, StringComparison.Ordinal)))
                frame.InheritFromFrameId = null;

            foreach (var instance in entity.InstanceOverrides.Values)
            {
                if (!instance.FramePatches.Remove(first.FrameId,
                        out var promotedPatch))
                    continue;
                foreach (var property in promotedPatch.Properties())
                    instance.BasePatch[property.Name] =
                        property.Value.DeepClone();
            }
            promoted++;
        }

        document.RootProperties.Remove("compiled");
        AddStoredDiagnostic(document,
            "COMPILED_CANONICAL_MIGRATED",
            "$.root_properties.compiled",
            $"Promoted {promoted} compiled initial frames into canonical entity roots.",
            StoryboardDiagnosticSeverity.Warning);
    }

    private static bool Equivalent(EditorNoteBinding left,
        EditorNoteBinding right) =>
        left.NoteId == right.NoteId &&
        JToken.DeepEquals(
            left.Query is null ? null : JObject.FromObject(left.Query),
            right.Query is null ? null : JObject.FromObject(right.Query));

    private static bool Equivalent(EditorTemplateBinding left,
        EditorTemplateBinding right) =>
        JToken.DeepEquals(JObject.FromObject(left), JObject.FromObject(right));

    private static void AddStoredDiagnostic(EditorStoryboardDocument document,
        string code, string path, string message,
        StoryboardDiagnosticSeverity severity)
    {
        if (document.Metadata.ImportDiagnostics.Any(item =>
                string.Equals(item.Code, code, StringComparison.Ordinal) &&
                string.Equals(item.Path, path, StringComparison.Ordinal)))
            return;
        document.Metadata.ImportDiagnostics.Add(
            new EditorStoryboardStoredDiagnostic
            {
                Code = code,
                Path = path,
                Message = message,
                Severity = severity.ToString()
            });
    }
}
