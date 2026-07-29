using Naziki_Editor.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Naziki_Editor.Features.Preview;

public sealed record ChartPreviewWireIssue(
    string Path,
    string Message);

/// <summary>
/// Defines the exact chart JSON contract accepted by the bundled Unity player.
/// Editor-only nullable/metadata fields must not leak across this boundary.
/// </summary>
public interface IChartPreviewWireAdapter
{
    string? Serialize(C2Chart? chart);
    IReadOnlyList<ChartPreviewWireIssue> Validate(string? json);
}

public sealed class ChartPreviewWireAdapter : IChartPreviewWireAdapter
{
    public string? Serialize(C2Chart? chart)
    {
        if (chart is null)
            return null;

        var root = JObject.FromObject(chart);

        // Unity's ChartModel uses a non-nullable double.
        if (root["music_offset"] is null ||
            root["music_offset"]!.Type == JTokenType.Null)
        {
            root["music_offset"] = 0d;
        }

        if (root["note_list"] is JArray notes)
        {
            foreach (var note in notes.OfType<JObject>())
            {
                // Unity's Note model uses non-nullable booleans.
                if (note["has_sibling"] is null ||
                    note["has_sibling"]!.Type == JTokenType.Null)
                {
                    note["has_sibling"] = false;
                }

                if (note["is_forward"] is null ||
                    note["is_forward"]!.Type == JTokenType.Null)
                {
                    note["is_forward"] = false;
                }

                // This property exists only in the editor model.
                note.Remove(nameof(C2Note.NoteDirection));
            }
        }

        RemoveNullOptionalValues(root);
        return root.ToString(Formatting.None);
    }

    public IReadOnlyList<ChartPreviewWireIssue> Validate(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [new("$.chart", "谱面 JSON 为空。")];

        JObject root;
        try
        {
            root = JObject.Parse(json);
        }
        catch (Exception ex)
        {
            return [new("$", $"谱面 JSON 无法解析：{ex.Message}")];
        }

        var issues = new List<ChartPreviewWireIssue>();
        RequireNumber(root["music_offset"], "$.music_offset", issues);
        RequireInteger(root["time_base"], "$.time_base", issues);
        RequireArray(root["page_list"], "$.page_list", issues);
        RequireArray(root["tempo_list"], "$.tempo_list", issues);
        RequireArray(root["note_list"], "$.note_list", issues);
        RequireArray(root["event_order_list"], "$.event_order_list", issues);

        if (root["note_list"] is JArray notes)
        {
            for (var index = 0; index < notes.Count; index++)
            {
                if (notes[index] is not JObject note)
                {
                    issues.Add(new($"$.note_list[{index}]",
                        "Unity 要求每个音符都是 JSON 对象。"));
                    continue;
                }

                RequireBoolean(note["has_sibling"],
                    $"$.note_list[{index}].has_sibling", issues);
                RequireBoolean(note["is_forward"],
                    $"$.note_list[{index}].is_forward", issues);
                if (note.Property(nameof(C2Note.NoteDirection)) is not null)
                {
                    issues.Add(new($"$.note_list[{index}].{nameof(C2Note.NoteDirection)}",
                        "编辑器字段 NoteDirection 不应发送给 Unity。"));
                }
            }
        }

        foreach (var value in Traverse(root).OfType<JValue>())
        {
            if (value.Type == JTokenType.Null)
                issues.Add(new(ToJsonPath(value.Path),
                    "Unity wire JSON 不应包含 null 可选字段。"));
            else if (value.Type == JTokenType.Float &&
                     value.Value is double number &&
                     !double.IsFinite(number))
            {
                issues.Add(new(ToJsonPath(value.Path),
                    "Unity wire JSON 中的数值必须是有限数。"));
            }
        }

        return issues;
    }

    private static void RemoveNullOptionalValues(JToken token)
    {
        if (token is JObject obj)
        {
            foreach (var property in obj.Properties().ToArray())
            {
                if (property.Value.Type == JTokenType.Null)
                    property.Remove();
                else
                    RemoveNullOptionalValues(property.Value);
            }
        }
        else if (token is JArray array)
        {
            foreach (var child in array)
                RemoveNullOptionalValues(child);
        }
    }

    private static void RequireNumber(
        JToken? token,
        string path,
        ICollection<ChartPreviewWireIssue> issues)
    {
        if (token?.Type is not (JTokenType.Integer or JTokenType.Float))
            issues.Add(new(path, "Unity 要求该字段为非空数值。"));
    }

    private static void RequireInteger(
        JToken? token,
        string path,
        ICollection<ChartPreviewWireIssue> issues)
    {
        if (token?.Type != JTokenType.Integer)
            issues.Add(new(path, "Unity 要求该字段为整数。"));
    }

    private static void RequireBoolean(
        JToken? token,
        string path,
        ICollection<ChartPreviewWireIssue> issues)
    {
        if (token?.Type != JTokenType.Boolean)
            issues.Add(new(path, "Unity 要求该字段为非空布尔值。"));
    }

    private static void RequireArray(
        JToken? token,
        string path,
        ICollection<ChartPreviewWireIssue> issues)
    {
        if (token?.Type != JTokenType.Array)
            issues.Add(new(path, "Unity 要求该字段为数组。"));
    }

    private static IEnumerable<JToken> Traverse(JToken token)
    {
        yield return token;
        if (token is not JContainer container)
            yield break;
        foreach (var child in container.Children())
        foreach (var nested in Traverse(child))
            yield return nested;
    }

    private static string ToJsonPath(string path) =>
        string.IsNullOrWhiteSpace(path) ? "$" : $"$.{path}";
}
