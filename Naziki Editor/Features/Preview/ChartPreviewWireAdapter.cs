using Naziki_Editor.Core.Charting;
using Naziki_Editor.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Naziki_Editor.Features.Preview;

public sealed record ChartPreviewWireIssue(
    string Path,
    string Message);

/// <summary>
/// Produces a standard Cytoid chart payload for the bundled player.
/// The player remains an unchanged consumer of the generated files.
/// </summary>
public interface IChartPreviewWireAdapter
{
    string? Serialize(C2Chart? chart, ChartDocument? document = null);
    IReadOnlyList<ChartDiagnostic> Diagnose(string? json);
    IReadOnlyList<ChartDiagnostic> Diagnose(ChartDocument document);
    IReadOnlyList<ChartPreviewWireIssue> Validate(string? json);
}

public sealed class ChartPreviewWireAdapter : IChartPreviewWireAdapter
{
    private readonly IChartJsonCodec _codec;

    public ChartPreviewWireAdapter()
        : this(new ChartJsonCodec())
    {
    }

    public ChartPreviewWireAdapter(IChartJsonCodec codec)
    {
        _codec = codec;
    }

    public string? Serialize(C2Chart? chart, ChartDocument? document = null)
    {
        if (chart is null)
            return null;

        var json = document is not null &&
                   ReferenceEquals(document.Projection, chart)
            ? _codec.EncodeWire(document, ChartRuntimeProfile.Cytoid)
            : _codec.EncodeWire(chart, ChartRuntimeProfile.Cytoid);
        var root = JObject.Parse(json);
        var wireNoteCount = (root["note_list"] as JArray)?.Count ?? -1;
        var modelNoteCount = chart.note_list?.Count;
        if (!modelNoteCount.HasValue)
            throw new JsonSerializationException(
                "谱面模型的 note_list 为 null，无法生成 Cytoid 预览数据。");
        if (wireNoteCount != modelNoteCount.Value)
            throw new JsonSerializationException(
                $"谱面预览序列化前后音符数量不一致：内存 {modelNoteCount.Value}，输出 {wireNoteCount}。");
        return json;
    }

    public IReadOnlyList<ChartDiagnostic> Diagnose(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return
            [
                new ChartDiagnostic(
                    "CHART_JSON_MISSING",
                    "$.chart",
                    "谱面 JSON 为空。",
                    ChartDiagnosticSeverity.Error,
                    ChartRuntimeProfile.Cytoid)
            ];
        }

        return _codec.Decode(json, ChartRuntimeProfile.Cytoid)
            .Diagnostics;
    }

    public IReadOnlyList<ChartDiagnostic> Diagnose(
        ChartDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return _codec.Validate(
            document.Source,
            ChartRuntimeProfile.Cytoid);
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
        RequireArray(root["page_list"], "$.page_list", issues);
        RequireArray(root["tempo_list"], "$.tempo_list", issues);
        RequireArray(root["note_list"], "$.note_list", issues);
        RequireArray(root["event_order_list"], "$.event_order_list", issues);

        if (root["note_list"] is JArray notes)
        {
            if (notes.Count == 0)
                issues.Add(new("$.note_list",
                    "正式 Unity 播放器要求谱面至少包含一个音符。"));
            for (var index = 0; index < notes.Count; index++)
            {
                if (notes[index] is not JObject note)
                {
                    issues.Add(new($"$.note_list[{index}]",
                        "Unity 要求每个音符都是 JSON 对象。"));
                    continue;
                }

            }
        }
        if (root["page_list"] is JArray { Count: 0 })
            issues.Add(new("$.page_list", "正式 Unity 播放器要求至少一个扫描页。"));
        if (root["tempo_list"] is JArray { Count: 0 })
            issues.Add(new("$.tempo_list", "正式 Unity 播放器要求至少一个 BPM 段。"));

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

    private static void RequireInteger(
        JToken? token,
        string path,
        ICollection<ChartPreviewWireIssue> issues)
    {
        if (token?.Type != JTokenType.Integer)
            issues.Add(new(path, "Unity 要求该字段为整数。"));
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
