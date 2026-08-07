using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Models;
using Newtonsoft.Json.Linq;

namespace Naziki_Editor.Core.Storyboard.Canonical;

/// <summary>
/// Projects the lossless editor document onto the fields consumed by the
/// official Cytoid storyboard parsers. The source document is never mutated.
/// </summary>
public static class CytoidStoryboardWireCompatibility
{
    private static readonly string[] UnsupportedStageFields =
        ["pivot_x", "pivot_y"];
    private static readonly string[] UnsupportedTextFields =
        ["line_spacing", "font_style"];
    private static readonly string[] UnsupportedVideoFields =
        ["preserve_aspect", "speed", "loop"];
    private static readonly string[] UnsupportedLineFields =
    [
        "x", "y", "z", "dx", "dy", "rot_x", "rot_y", "rot_z",
        "scale", "scale_x", "scale_y", "height", "fill_width"
    ];

    public static void Normalize(
        JObject root,
        ICollection<StoryboardImportIssue> issues)
    {
        NormalizeCollection(root, "sprites", StoryboardEntityKind.Sprite, issues);
        NormalizeCollection(root, "texts", StoryboardEntityKind.Text, issues);
        NormalizeCollection(root, "lines", StoryboardEntityKind.Line, issues);
        NormalizeCollection(root, "videos", StoryboardEntityKind.Video, issues);
    }

    private static void NormalizeCollection(
        JObject root,
        string collection,
        StoryboardEntityKind kind,
        ICollection<StoryboardImportIssue> issues)
    {
        if (root[collection] is not JArray entities) return;
        for (var entityIndex = 0; entityIndex < entities.Count; entityIndex++)
        {
            if (entities[entityIndex] is not JObject entity) continue;
            NormalizeState(entity, kind,
                $"$.{collection}[{entityIndex}]", issues);
            if (entity["states"] is not JArray states) continue;
            for (var stateIndex = 0; stateIndex < states.Count; stateIndex++)
                if (states[stateIndex] is JObject state)
                    NormalizeState(state, kind,
                        $"$.{collection}[{entityIndex}].states[{stateIndex}]",
                        issues);
        }
    }

    private static void NormalizeState(
        JObject state,
        StoryboardEntityKind kind,
        string path,
        ICollection<StoryboardImportIssue> issues)
    {
        foreach (var property in UnsupportedStageFields)
            RemoveUnsupported(state, property, path, issues);

        if (kind is StoryboardEntityKind.Sprite or StoryboardEntityKind.Video)
        {
            MapLegacyDimension(state, "w", "width", path, issues);
            MapLegacyDimension(state, "h", "height", path, issues);
        }

        if (kind == StoryboardEntityKind.Text)
        {
            foreach (var property in UnsupportedTextFields)
                RemoveUnsupported(state, property, path, issues);
            if (state["size"] is JValue { Type: JTokenType.Float })
                issues.Add(new StoryboardImportIssue(
                    "CYTOID_INTEGER_RUNTIME_CONVERSION", $"{path}.size",
                    "Cytoid preserves the JSON number but converts text size to Int32 at runtime using Newtonsoft rounding.",
                    StoryboardDiagnosticSeverity.Warning));
        }

        if (kind == StoryboardEntityKind.Line)
            foreach (var property in UnsupportedLineFields)
                RemoveUnsupported(state, property, path, issues);

        if (kind == StoryboardEntityKind.Video)
            foreach (var property in UnsupportedVideoFields)
                RemoveUnsupported(state, property, path, issues);
    }

    private static void MapLegacyDimension(
        JObject state,
        string legacy,
        string official,
        string path,
        ICollection<StoryboardImportIssue> issues)
    {
        if (state[legacy] is not { } legacyValue) return;
        if (state[official] is null)
        {
            state[official] = legacyValue.DeepClone();
            issues.Add(new StoryboardImportIssue(
                "CYTOID_LEGACY_DIMENSION_MAPPED", $"{path}.{legacy}",
                $"Legacy '{legacy}' was mapped to official '{official}' in runtime output.",
                StoryboardDiagnosticSeverity.Warning));
        }
        else if (!JToken.DeepEquals(state[official], legacyValue))
        {
            issues.Add(new StoryboardImportIssue(
                "CYTOID_LEGACY_DIMENSION_CONFLICT", $"{path}.{legacy}",
                $"Both '{legacy}' and '{official}' are present; official '{official}' takes precedence.",
                StoryboardDiagnosticSeverity.Warning));
        }
        state.Remove(legacy);
    }

    private static void RemoveUnsupported(
        JObject state,
        string property,
        string path,
        ICollection<StoryboardImportIssue> issues)
    {
        if (state[property] is null) return;
        state.Remove(property);
        issues.Add(new StoryboardImportIssue(
            "CYTOID_UNSUPPORTED_FIELD_FILTERED", $"{path}.{property}",
            $"'{property}' is preserved in editor source but omitted from Cytoid runtime output.",
            StoryboardDiagnosticSeverity.Warning));
    }

    private enum StoryboardEntityKind
    {
        Sprite,
        Text,
        Line,
        Video
    }
}
