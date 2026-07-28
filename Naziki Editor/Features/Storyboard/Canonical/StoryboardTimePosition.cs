using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace Naziki_Editor.Models;

[JsonConverter(typeof(StringEnumConverter))]
public enum StoryboardTimeAnchorKind
{
    Absolute,
    NoteIntro,
    NoteStart,
    NoteEnd,
    NoteAt,
    CurrentNoteIntro,
    CurrentNoteStart,
    CurrentNoteEnd,
    CurrentNoteAt,
    TemplateStart,
    TriggerSpawn,
    Unresolved
}

public sealed class StoryboardTimePosition
{
    [JsonProperty("kind")]
    public StoryboardTimeAnchorKind Kind { get; set; }

    [JsonProperty("seconds")]
    public double? Seconds { get; set; }

    [JsonProperty("note_id")]
    public int? NoteId { get; set; }

    [JsonProperty("offset_seconds")]
    public double OffsetSeconds { get; set; }

    [JsonProperty("hold_position")]
    public double? HoldPosition { get; set; }

    [JsonProperty("source_expression")]
    public string? SourceExpression { get; set; }

    public static StoryboardTimePosition Absolute(double seconds) =>
        new() { Kind = StoryboardTimeAnchorKind.Absolute, Seconds = seconds };

    public static StoryboardTimePosition TemplateStart(double offset = 0) =>
        new() { Kind = StoryboardTimeAnchorKind.TemplateStart, OffsetSeconds = offset };

    public static StoryboardTimePosition TriggerSpawn(double offset = 0) =>
        new() { Kind = StoryboardTimeAnchorKind.TriggerSpawn, OffsetSeconds = offset };

    public static StoryboardTimePosition Unresolved(string? source = null) =>
        new() { Kind = StoryboardTimeAnchorKind.Unresolved, SourceExpression = source };

    public StoryboardTimePosition Shift(double seconds)
    {
        var clone = new StoryboardTimePosition
        {
            Kind = Kind,
            Seconds = Seconds,
            NoteId = NoteId,
            OffsetSeconds = OffsetSeconds,
            HoldPosition = HoldPosition,
            SourceExpression = SourceExpression
        };
        if (clone.Kind == StoryboardTimeAnchorKind.Absolute)
            clone.Seconds = (clone.Seconds ?? 0) + seconds;
        else
            clone.OffsetSeconds += seconds;
        return clone;
    }

    public StoryboardTimePosition RebaseTemplate(StoryboardTimePosition anchor) =>
        Kind == StoryboardTimeAnchorKind.TemplateStart
            ? anchor.Shift(OffsetSeconds)
            : this;

    public static bool TryParse(JToken? token, out StoryboardTimePosition position,
        out string? error)
    {
        error = null;
        if (token is null || token.Type is JTokenType.Null or JTokenType.Undefined)
        {
            position = Unresolved();
            return false;
        }

        if (token.Type is JTokenType.Integer or JTokenType.Float)
        {
            position = Absolute(token.Value<double>());
            return true;
        }

        if (token.Type != JTokenType.String)
        {
            position = Unresolved(token.ToString(Formatting.None));
            error = $"Time must be a number or an anchor expression, not {token.Type}.";
            return false;
        }

        var raw = token.Value<string>()?.Trim() ?? "";
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture,
                out var numeric))
        {
            position = Absolute(numeric);
            position.SourceExpression = raw;
            return true;
        }

        var parts = raw.Split(':');
        if (parts.Length < 2)
        {
            position = Unresolved(raw);
            error = $"Invalid time expression '{raw}'.";
            return false;
        }

        var anchor = parts[0].ToLowerInvariant();
        var currentNote = parts[1] == "$note";
        int? noteId = null;
        if (!currentNote)
        {
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out var parsedId))
            {
                position = Unresolved(raw);
                error = $"Invalid note id in time expression '{raw}'.";
                return false;
            }
            noteId = parsedId;
        }

        double third = 0;
        if (parts.Length >= 3 &&
            !double.TryParse(parts[2], NumberStyles.Float,
                CultureInfo.InvariantCulture, out third))
        {
            position = Unresolved(raw);
            error = $"Invalid time argument in expression '{raw}'.";
            return false;
        }

        var kind = (anchor, currentNote) switch
        {
            ("intro", false) => StoryboardTimeAnchorKind.NoteIntro,
            ("start", false) => StoryboardTimeAnchorKind.NoteStart,
            ("end", false) => StoryboardTimeAnchorKind.NoteEnd,
            ("at", false) => StoryboardTimeAnchorKind.NoteAt,
            ("intro", true) => StoryboardTimeAnchorKind.CurrentNoteIntro,
            ("start", true) => StoryboardTimeAnchorKind.CurrentNoteStart,
            ("end", true) => StoryboardTimeAnchorKind.CurrentNoteEnd,
            ("at", true) => StoryboardTimeAnchorKind.CurrentNoteAt,
            _ => StoryboardTimeAnchorKind.Unresolved
        };
        if (kind == StoryboardTimeAnchorKind.Unresolved)
        {
            position = Unresolved(raw);
            error = $"Unknown time anchor '{anchor}'.";
            return false;
        }

        position = new StoryboardTimePosition
        {
            Kind = kind,
            NoteId = noteId,
            OffsetSeconds = anchor == "at" ? 0 : third,
            HoldPosition = anchor == "at" ? third : null,
            SourceExpression = raw
        };
        return true;
    }
}

