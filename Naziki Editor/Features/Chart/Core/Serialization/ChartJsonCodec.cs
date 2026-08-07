using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using Naziki_Editor.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Naziki_Editor.Core.Charting;

public enum ChartRuntimeProfile
{
    Cytus2,
    Cytoid,
    BundledUnity
}

public enum ChartDiagnosticSeverity
{
    Information,
    Warning,
    Error
}

public sealed record ChartDiagnostic(
    string Code,
    string Path,
    string Message,
    ChartDiagnosticSeverity Severity,
    ChartRuntimeProfile Profile);

public sealed class ChartDocument
{
    internal ChartDocument(
        JObject source,
        C2Chart projection,
        IReadOnlyList<ChartDiagnostic> diagnostics,
        string sourceHash)
    {
        Source = source;
        Projection = projection;
        Diagnostics = diagnostics;
        SourceHash = sourceHash;
    }

    public JObject Source { get; }
    public C2Chart Projection { get; }
    public IReadOnlyList<ChartDiagnostic> Diagnostics { get; }
    public string SourceHash { get; }
}

public sealed record ChartDecodeResult(
    ChartDocument? Document,
    IReadOnlyList<ChartDiagnostic> Diagnostics)
{
    public bool Success =>
        Document is not null &&
        Diagnostics.All(item =>
            item.Severity != ChartDiagnosticSeverity.Error);
}

public interface IChartJsonCodec
{
    ChartDecodeResult Decode(string json,
        ChartRuntimeProfile profile = ChartRuntimeProfile.Cytus2);
    string EncodeSource(ChartDocument document);
    string EncodeWire(ChartDocument document, ChartRuntimeProfile profile);
    string EncodeWire(C2Chart chart, ChartRuntimeProfile profile);
    IReadOnlyList<ChartDiagnostic> Validate(
        JObject source,
        ChartRuntimeProfile profile);
}

/// <summary>
/// Lossless chart boundary. Chart and storyboard use separate business codecs;
/// both follow the same source/projection/wire architecture.
/// </summary>
public sealed class ChartJsonCodec : IChartJsonCodec
{
    private static JsonSerializer CreateSerializer() =>
        JsonSerializer.Create(new JsonSerializerSettings
        {
            DateParseHandling = DateParseHandling.None,
            FloatParseHandling = FloatParseHandling.Double,
            NullValueHandling = NullValueHandling.Ignore
        });

    public ChartDecodeResult Decode(string json,
        ChartRuntimeProfile profile = ChartRuntimeProfile.Cytus2)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Failure("CHART_JSON_EMPTY", "$",
                "谱面 JSON 为空。", profile);

        JObject source;
        try
        {
            source = JObject.Parse(json);
        }
        catch (JsonException ex)
        {
            return Failure("CHART_JSON_INVALID", "$",
                $"谱面 JSON 无法解析：{ex.Message}", profile);
        }

        var diagnostics = Validate(source, profile);
        C2Chart projection;
        try
        {
            projection = source.ToObject<C2Chart>(CreateSerializer())
                         ?? throw new JsonSerializationException(
                             "谱面反序列化结果为空。");
        }
        catch (JsonException ex)
        {
            return new ChartDecodeResult(null,
            [
                .. diagnostics,
                new ChartDiagnostic(
                    "CHART_PROJECTION_FAILED", "$",
                    ex.Message, ChartDiagnosticSeverity.Error, profile)
            ]);
        }

        var normalized = source.ToString(Formatting.None);
        var document = new ChartDocument(
            (JObject)source.DeepClone(),
            projection,
            diagnostics,
            Hash(normalized));
        return new ChartDecodeResult(document, diagnostics);
    }

    public string EncodeSource(ChartDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Source.ToString(Formatting.Indented);
    }

    public string EncodeWire(ChartDocument document,
        ChartRuntimeProfile profile)
    {
        ArgumentNullException.ThrowIfNull(document);
        var root = (JObject)document.Source.DeepClone();
        NormalizeWireDefaults(root);
        ApplyProfile(root, profile);
        RemoveNulls(root);
        return root.ToString(Formatting.None);
    }

    public string EncodeWire(C2Chart chart, ChartRuntimeProfile profile)
    {
        ArgumentNullException.ThrowIfNull(chart);
        var root = JObject.FromObject(chart, CreateSerializer());
        NormalizeWireDefaults(root);
        ApplyProfile(root, profile);
        RemoveNulls(root);
        return root.ToString(Formatting.None);
    }

    public IReadOnlyList<ChartDiagnostic> Validate(
        JObject source,
        ChartRuntimeProfile profile)
    {
        ArgumentNullException.ThrowIfNull(source);
        var diagnostics = new List<ChartDiagnostic>();
        var timeBase = source.Value<double?>("time_base");
        if (!IsFinite(timeBase) || timeBase <= 0)
            Add("CHART_TIME_BASE_INVALID", "$.time_base",
                "time_base 必须是大于 0 的有限数值。",
                ChartDiagnosticSeverity.Error);

        var pages = source["page_list"] as JArray;
        if (pages is null || pages.Count == 0)
            Add("CHART_PAGE_LIST_MISSING", "$.page_list",
                "谱面至少需要一个 Page。",
                ChartDiagnosticSeverity.Error);
        else
            ValidatePages(pages);

        var tempos = source["tempo_list"] as JArray;
        if (tempos is null || tempos.Count == 0)
            Add("CHART_TEMPO_LIST_MISSING", "$.tempo_list",
                "谱面至少需要一个 Tempo。",
                ChartDiagnosticSeverity.Error);
        else
            ValidateTempos(tempos);

        var notes = source["note_list"] as JArray;
        if (notes is null)
            Add("CHART_NOTE_LIST_MISSING", "$.note_list",
                "note_list 必须是数组。",
                ChartDiagnosticSeverity.Error);
        else
        {
            if (notes.Count == 0 &&
                profile == ChartRuntimeProfile.BundledUnity)
                Add("CHART_NOTE_LIST_EMPTY", "$.note_list",
                    "内置 Unity 播放器不能预览不含音符的谱面。",
                    ChartDiagnosticSeverity.Error);
            ValidateNotes(notes, pages);
        }

        ValidateProfileCompatibility();
        ValidateFiniteNumbers();
        if (profile == ChartRuntimeProfile.BundledUnity)
        {
            ValidateBundledUnityRuntime(notes);
            for (var index = 0; index < diagnostics.Count; index++)
            {
                if (diagnostics[index].Code == "CHART_NOTE_OUTSIDE_PAGE")
                {
                    diagnostics[index] = diagnostics[index] with
                    {
                        Severity = ChartDiagnosticSeverity.Warning,
                        Message = "音符 Tick 超出其声明页面；内置播放器会保留该演出数据。"
                    };
                }
            }
        }
        return diagnostics;

        void ValidateProfileCompatibility()
        {
            if (profile == ChartRuntimeProfile.Cytus2)
                return;

            WarnIgnored("start_offset_time",
                CompatibilityMessage("start_offset_time"));
            WarnIgnored("end_offset_time",
                CompatibilityMessage("end_offset_time"));
            WarnIgnored("is_start_without_ui",
                CompatibilityMessage("is_start_without_ui"));

            if (pages is not null)
            {
                for (var index = 0; index < pages.Count; index++)
                {
                    if (pages[index]?["PositionFunction"] is not null)
                        Add("CHART_PROFILE_FIELD_IGNORED",
                            $"$.page_list[{index}].PositionFunction",
                            "目标运行时不支持 PositionFunction；源谱面中的页面函数会保留，但该预览/导出目标不会应用它。",
                            ChartDiagnosticSeverity.Warning);
                }
            }

            if (notes is not null)
            {
                for (var index = 0; index < notes.Count; index++)
                {
                    if (notes[index]?[nameof(C2Note.NoteDirection)] is not null)
                        Add("CHART_PROFILE_FIELD_IGNORED",
                            $"$.note_list[{index}].{nameof(C2Note.NoteDirection)}",
                            CompatibilityMessage(nameof(C2Note.NoteDirection)),
                            ChartDiagnosticSeverity.Warning);
                }
            }

            if (source["event_order_list"] is JArray eventOrders)
            {
                for (var index = 0; index < eventOrders.Count; index++)
                {
                    if (eventOrders[index]?["tick"] is JValue { Type: JTokenType.Float })
                        Add("CHART_INTEGER_RUNTIME_CONVERSION",
                            $"$.event_order_list[{index}].tick",
                            "Cytoid preserves the JSON number but converts event tick to Int32 at runtime using Newtonsoft rounding.",
                            ChartDiagnosticSeverity.Warning);
                }
            }

            void WarnIgnored(string propertyName, string message)
            {
                if (source[propertyName] is not null)
                    Add("CHART_PROFILE_FIELD_IGNORED",
                        $"$.{propertyName}",
                        message,
                        ChartDiagnosticSeverity.Warning);
            }

            string CompatibilityMessage(string propertyName) =>
                profile == ChartRuntimeProfile.Cytoid
                    ? $"Cytoid 不使用 {propertyName}；源谱面中的值会保留，但 Cytoid 输出不会包含它。"
                    : $"内置 Unity 播放器不应用 {propertyName}；该字段仍随原始 Cytus II 谱面传输。";
        }

        void ValidatePages(JArray values)
        {
            double? previousEnd = null;
            for (var index = 0; index < values.Count; index++)
            {
                if (values[index] is not JObject page)
                {
                    Add("CHART_PAGE_INVALID", $"$.page_list[{index}]",
                        "Page 必须是 JSON 对象。",
                        ChartDiagnosticSeverity.Error);
                    continue;
                }
                var start = page.Value<double?>("start_tick");
                var end = page.Value<double?>("end_tick");
                var path = $"$.page_list[{index}]";
                if (!IsFinite(start) || !IsFinite(end) ||
                    start >= end)
                {
                    Add("CHART_PAGE_RANGE_INVALID", path,
                        "Page 必须满足有限的 start_tick < end_tick。",
                        ChartDiagnosticSeverity.Error);
                    previousEnd = end;
                    continue;
                }
                if (previousEnd.HasValue && end <= previousEnd)
                    Add("CHART_PAGE_END_ORDER_INVALID",
                        $"{path}.end_tick",
                        "Page 的 end_tick 必须严格递增，播放器依赖该顺序选择当前页。",
                        ChartDiagnosticSeverity.Error);
                if (start < 0)
                    Add("CHART_PAGE_NEGATIVE_START", $"{path}.start_tick",
                        "Page 使用负 start_tick；目标播放器允许该演出技巧。",
                        ChartDiagnosticSeverity.Warning);
                if (previousEnd.HasValue && start < previousEnd)
                    Add("CHART_PAGE_OVERLAP", path,
                        "Page 与前一页重叠；范围保持原样以保留扫描线演出。",
                        ChartDiagnosticSeverity.Warning);
                else if (previousEnd.HasValue && start > previousEnd)
                    Add("CHART_PAGE_GAP", path,
                        "Page 与前一页之间存在时间空隙。",
                        ChartDiagnosticSeverity.Warning);

                var direction =
                    page.Value<int?>("scan_line_direction");
                if (direction is not (1 or -1))
                    Add("CHART_PAGE_DIRECTION_NONSTANDARD",
                        $"{path}.scan_line_direction",
                        "扫描线方向不是 1 或 -1，部分运行目标可能不兼容。",
                        ChartDiagnosticSeverity.Warning);
                previousEnd = end;
            }
        }

        void ValidateTempos(JArray values)
        {
            double? previousTick = null;
            for (var index = 0; index < values.Count; index++)
            {
                var path = $"$.tempo_list[{index}]";
                if (values[index] is not JObject tempo)
                {
                    Add("CHART_TEMPO_INVALID", path,
                        "Tempo 必须是 JSON 对象。",
                        ChartDiagnosticSeverity.Error);
                    continue;
                }
                var tick = tempo.Value<double?>("tick");
                var value = tempo.Value<double?>("value");
                if (!IsFinite(tick) || !IsFinite(value) || value <= 0)
                    Add("CHART_TEMPO_RANGE_INVALID", path,
                        "Tempo tick 必须有限且 value 必须大于 0。",
                        ChartDiagnosticSeverity.Error);
                if (previousTick.HasValue && tick < previousTick)
                    Add("CHART_TEMPO_ORDER_INVALID", $"{path}.tick",
                        "Tempo 必须按 tick 排序。",
                        ChartDiagnosticSeverity.Error);
                previousTick = tick;
            }
        }

        void ValidateNotes(JArray values, JArray? pageValues)
        {
            var ids = new HashSet<int>();
            for (var index = 0; index < values.Count; index++)
            {
                var path = $"$.note_list[{index}]";
                if (values[index] is not JObject note)
                {
                    Add("CHART_NOTE_INVALID", path,
                        "Note 必须是 JSON 对象。",
                        ChartDiagnosticSeverity.Error);
                    continue;
                }
                var id = note.Value<int?>("id");
                if (!id.HasValue || !ids.Add(id.Value))
                    Add("CHART_NOTE_ID_INVALID", $"{path}.id",
                        "音符 ID 缺失或重复。",
                        ChartDiagnosticSeverity.Error);
                var pageIndex = note.Value<int?>("page_index");
                if (!pageIndex.HasValue || pageValues is null ||
                    pageIndex < 0 || pageIndex >= pageValues.Count)
                {
                    Add("CHART_NOTE_PAGE_INVALID",
                        $"{path}.page_index",
                        "音符引用了不存在的 Page。",
                        ChartDiagnosticSeverity.Error);
                }
                else if (pageValues[pageIndex.Value] is JObject page)
                {
                    var tick = note.Value<double?>("tick");
                    var start = page.Value<double?>("start_tick");
                    var end = page.Value<double?>("end_tick");
                    if (!IsFinite(tick) || !IsFinite(start) ||
                        !IsFinite(end) || tick < start || tick > end)
                        Add("CHART_NOTE_OUTSIDE_PAGE", $"{path}.tick",
                            "音符 Tick 不在 page_index 指向的 Page 范围内。",
                            ChartDiagnosticSeverity.Error);
                }
                var next = note.Value<int?>("next_id") ?? -1;
                if (next < -1 || next >= values.Count)
                    Add("CHART_NOTE_NEXT_INDEX_INVALID",
                        $"{path}.next_id",
                        "next_id 是音符列表索引，必须为 -1 或有效索引。",
                        ChartDiagnosticSeverity.Error);
            }
        }

        void ValidateFiniteNumbers()
        {
            foreach (var value in source.DescendantsAndSelf().OfType<JValue>())
            {
                if (value.Type is not (JTokenType.Float or JTokenType.Integer))
                    continue;
                if (TryReadFiniteDouble(value, out _))
                    continue;
                Add("CHART_NUMBER_NONFINITE",
                    string.IsNullOrWhiteSpace(value.Path) ? "$" : $"$.{value.Path}",
                    "谱面中的所有数值都必须是有限数。",
                    ChartDiagnosticSeverity.Error);
            }
        }

        void ValidateBundledUnityRuntime(JArray? noteValues)
        {
            if (noteValues is not null)
            {
                var nextById = new Dictionary<int, int>();
                for (var index = 0; index < noteValues.Count; index++)
                {
                    var path = $"$.note_list[{index}]";
                    if (noteValues[index] is not JObject note)
                        continue;

                    if (!TryReadInt32(note["id"], out var id) || id != index)
                        Add("CHART_NOTE_ID_SEQUENCE_INVALID", $"{path}.id",
                            $"内置 Unity 播放器要求音符 ID 严格等于其数组索引；此处应为 {index}。",
                            ChartDiagnosticSeverity.Error);

                    if (!TryReadInt32(note["type"], out var type) || type is < 0 or > 7)
                        Add("CHART_NOTE_TYPE_INVALID", $"{path}.type",
                            "内置 Unity 播放器仅支持 0..7 的音符类型。",
                            ChartDiagnosticSeverity.Error);

                    if (!TryReadInt32(note["page_index"], out _))
                        Add("CHART_NOTE_PAGE_INDEX_TYPE_INVALID", $"{path}.page_index",
                            "page_index 必须是整数。",
                            ChartDiagnosticSeverity.Error);

                    RequireFiniteNoteNumber(note, "tick", path);
                    RequireFiniteNoteNumber(note, "x", path);
                    RequireFiniteNoteNumber(note, "hold_tick", path, defaultWhenMissing: 0d);

                    foreach (var optionalName in new[] { "size", "opacity", "hitbox" })
                    {
                        if (note[optionalName] is not null && note[optionalName]!.Type != JTokenType.Null)
                            RequireFiniteNoteNumber(note, optionalName, path);
                    }

                    if (note["approach_rate"] is not null && note["approach_rate"]!.Type != JTokenType.Null)
                    {
                        if (!TryReadFiniteDouble(note["approach_rate"], out var approachRate) || approachRate <= 0)
                            Add("CHART_NOTE_APPROACH_RATE_INVALID", $"{path}.approach_rate",
                                "approach_rate 必须是大于 0 的有限数，否则播放器会产生除零或无穷时间。",
                                ChartDiagnosticSeverity.Error);
                    }

                    var next = -1;
                    if (note["next_id"] is not null && note["next_id"]!.Type != JTokenType.Null &&
                        !TryReadInt32(note["next_id"], out next))
                    {
                        Add("CHART_NOTE_NEXT_ID_TYPE_INVALID", $"{path}.next_id",
                            "next_id 必须是整数。",
                            ChartDiagnosticSeverity.Error);
                        continue;
                    }

                    if (next < -1 || next >= noteValues.Count)
                        Add("CHART_NOTE_NEXT_INDEX_INVALID", $"{path}.next_id",
                            "next_id 必须为 -1、0（结束链）或现有音符 ID。",
                            ChartDiagnosticSeverity.Error);
                    else
                        nextById[index] = next;
                }

                ValidateDragCycles(nextById);
            }

            if (source["event_order_list"] is not null &&
                source["event_order_list"] is not JArray)
            {
                Add("CHART_EVENT_ORDER_LIST_INVALID", "$.event_order_list",
                    "event_order_list 必须是数组。",
                    ChartDiagnosticSeverity.Error);
            }
            else if (source["event_order_list"] is JArray eventOrders)
            {
                for (var index = 0; index < eventOrders.Count; index++)
                {
                    var path = $"$.event_order_list[{index}]";
                    if (eventOrders[index] is not JObject order)
                    {
                        Add("CHART_EVENT_ORDER_INVALID", path,
                            "事件组必须是 JSON 对象。",
                            ChartDiagnosticSeverity.Error);
                        continue;
                    }

                    if (!TryReadFiniteDouble(order["tick"], out _))
                        Add("CHART_EVENT_TICK_INVALID", $"{path}.tick",
                            "事件 Tick 必须是有限数。",
                            ChartDiagnosticSeverity.Error);

                    if (order["event_list"] is not JArray { Count: > 0 } events)
                    {
                        Add("CHART_EVENT_LIST_EMPTY", $"{path}.event_list",
                            "内置 Unity 播放器会读取事件组的首个事件，因此 event_list 不能为空。",
                            ChartDiagnosticSeverity.Error);
                        continue;
                    }

                    if (events[0] is not JObject firstEvent ||
                        !TryReadInt32(firstEvent["type"], out _))
                        Add("CHART_EVENT_TYPE_INVALID", $"{path}.event_list[0].type",
                            "事件组首个事件的 type 必须是整数。",
                            ChartDiagnosticSeverity.Error);
                }
            }
        }

        void RequireFiniteNoteNumber(
            JObject note,
            string property,
            string notePath,
            double? defaultWhenMissing = null)
        {
            var token = note[property];
            if ((token is null || token.Type == JTokenType.Null) && defaultWhenMissing.HasValue)
                return;
            if (!TryReadFiniteDouble(token, out _))
                Add("CHART_NOTE_NUMBER_INVALID", $"{notePath}.{property}",
                    $"音符字段 {property} 必须是有限数。",
                    ChartDiagnosticSeverity.Error);
        }

        void ValidateDragCycles(IReadOnlyDictionary<int, int> nextById)
        {
            var resolved = new HashSet<int>();
            var reported = new HashSet<int>();
            foreach (var start in nextById.Keys)
            {
                if (resolved.Contains(start))
                    continue;
                var chain = new List<int>();
                var positions = new Dictionary<int, int>();
                var current = start;
                while (true)
                {
                    if (resolved.Contains(current))
                        break;
                    if (positions.TryGetValue(current, out var cycleStart))
                    {
                        var cycle = chain.Skip(cycleStart).ToArray();
                        var representative = cycle.Min();
                        if (reported.Add(representative))
                            Add("CHART_NOTE_DRAG_CYCLE", $"$.note_list[{current}].next_id",
                                $"拖链形成循环：{string.Join(" -> ", cycle)} -> {current}。",
                                ChartDiagnosticSeverity.Error);
                        break;
                    }

                    positions[current] = chain.Count;
                    chain.Add(current);
                    if (!nextById.TryGetValue(current, out var next) || next <= 0)
                        break;
                    current = next;
                }

                foreach (var id in chain)
                    resolved.Add(id);
            }
        }

        void Add(string code, string path, string message,
            ChartDiagnosticSeverity severity) =>
            diagnostics.Add(new ChartDiagnostic(
                code, path, message, severity, profile));
    }

    private static void NormalizeWireDefaults(JObject root)
    {
        SetDefault(root, "music_offset", 0d);
        SetDefault(root, "event_order_list", new JArray());
        if (root["note_list"] is not JArray notes) return;
        foreach (var note in notes.OfType<JObject>())
        {
            SetDefault(note, "has_sibling", false);
            SetDefault(note, "is_forward", false);
            SetDefault(note, "hold_tick", 0d);
            SetDefault(note, "next_id", -1);
            SetDefault(note, "approach_rate", 1d);
        }
    }

    private static void SetDefault(JObject root, string property, JToken value)
    {
        if (root[property] is null || root[property]!.Type == JTokenType.Null)
            root[property] = value;
    }

    private static void ApplyProfile(
        JObject root,
        ChartRuntimeProfile profile)
    {
        // The bundled Unity player uses Newtonsoft.Json and safely ignores
        // Cytus II fields it does not model. Preserve the original document
        // at this boundary; only Cytoid export requires field projection.
        if (profile != ChartRuntimeProfile.Cytoid) return;
        root.Remove("start_offset_time");
        root.Remove("end_offset_time");
        root.Remove("is_start_without_ui");
        if (root["page_list"] is JArray pages)
            foreach (var page in pages.OfType<JObject>())
                page.Remove("PositionFunction");
        if (root["note_list"] is JArray notes)
            foreach (var note in notes.OfType<JObject>())
                note.Remove(nameof(C2Note.NoteDirection));
    }

    private static void RemoveNulls(JToken token)
    {
        if (token is JObject obj)
        {
            foreach (var property in obj.Properties().ToArray())
            {
                if (property.Value.Type == JTokenType.Null)
                    property.Remove();
                else
                    RemoveNulls(property.Value);
            }
        }
        else if (token is JArray array)
            foreach (var child in array)
                RemoveNulls(child);
    }

    private static bool IsFinite(double? value) =>
        value.HasValue && double.IsFinite(value.Value);

    private static bool TryReadInt32(JToken? token, out int value)
    {
        value = default;
        if (token?.Type != JTokenType.Integer)
            return false;
        return int.TryParse(token.ToString(Formatting.None),
            NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadFiniteDouble(JToken? token, out double value)
    {
        value = default;
        if (token?.Type is not (JTokenType.Integer or JTokenType.Float))
            return false;
        return double.TryParse(token.ToString(Formatting.None),
                   NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
               double.IsFinite(value);
    }

    private static ChartDecodeResult Failure(
        string code,
        string path,
        string message,
        ChartRuntimeProfile profile)
    {
        var diagnostic = new ChartDiagnostic(
            code, path, message,
            ChartDiagnosticSeverity.Error, profile);
        return new ChartDecodeResult(null, [diagnostic]);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}
