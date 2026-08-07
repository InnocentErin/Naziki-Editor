using Naziki_Editor.Core.Charting;
using Naziki_Editor.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Naziki_Editor.Features.Preview;

public sealed record ChartPreviewWireIssue(
    string Path,
    string Message);

/// <summary>
/// Produces and validates the exact chart payload consumed by the bundled Unity player.
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
    private const ChartRuntimeProfile PreviewProfile = ChartRuntimeProfile.BundledUnity;
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

        var json = document is not null && ReferenceEquals(document.Projection, chart)
            ? _codec.EncodeWire(document, PreviewProfile)
            : _codec.EncodeWire(chart, PreviewProfile);
        var root = JObject.Parse(json);
        var wireNoteCount = (root["note_list"] as JArray)?.Count ?? -1;
        var modelNoteCount = chart.note_list?.Count;
        if (!modelNoteCount.HasValue)
            throw new JsonSerializationException(
                "谱面模型的 note_list 为 null，无法生成 Unity 预览数据。");
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
                    PreviewProfile)
            ];
        }

        return _codec.Decode(json, PreviewProfile).Diagnostics;
    }

    public IReadOnlyList<ChartDiagnostic> Diagnose(ChartDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return _codec.Validate(document.Source, PreviewProfile);
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

        var issues = _codec.Validate(root, PreviewProfile)
            .Where(item => item.Severity == ChartDiagnosticSeverity.Error)
            .Select(item => new ChartPreviewWireIssue(item.Path, item.Message))
            .ToList();

        RequireArray(root["page_list"], "$.page_list", issues);
        RequireArray(root["tempo_list"], "$.tempo_list", issues);
        RequireArray(root["note_list"], "$.note_list", issues);
        RequireArray(root["event_order_list"], "$.event_order_list", issues);

        foreach (var value in Traverse(root).OfType<JValue>())
        {
            if (value.Type == JTokenType.Null)
            {
                issues.Add(new(ToJsonPath(value.Path),
                    "Unity wire JSON 不应包含 null 可选字段。"));
            }
            else if (value.Type is JTokenType.Float or JTokenType.Integer &&
                     !IsFinite(value))
            {
                issues.Add(new(ToJsonPath(value.Path),
                    "Unity wire JSON 中的数值必须是有限数。"));
            }
        }

        return issues
            .DistinctBy(item => (item.Path, item.Message))
            .ToArray();
    }

    private static bool IsFinite(JValue value) =>
        value.Value switch
        {
            double number => double.IsFinite(number),
            float number => float.IsFinite(number),
            decimal => true,
            byte or sbyte or short or ushort or int or uint or long or ulong => true,
            _ => false
        };

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
