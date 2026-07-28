using System.IO;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Models;
using Naziki_Editor.State;
using Newtonsoft.Json.Linq;
using Naziki_Editor.Features.Project.Resources;

namespace Naziki_Editor.Features.Preview;

public sealed class PreviewValidationService : IPreviewValidationService
{
    private static readonly HashSet<string> SupportedAssetExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".gif",
        ".mp4", ".webm", ".mov", ".avi",
        ".mp3", ".ogg", ".wav",
        ".ttf", ".otf"
    };
    private static readonly HashSet<string> SupportedMusicExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".ogg", ".wav" };
    private static readonly HashSet<string> SupportedBackgroundExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };

    private readonly IStoryboardDocumentValidator _storyboardValidator;
    private readonly IProjectReadinessService? _readiness;

    public PreviewValidationService(IStoryboardDocumentValidator storyboardValidator) =>
        _storyboardValidator = storyboardValidator;

    public PreviewValidationService(
        IStoryboardDocumentValidator storyboardValidator,
        IProjectReadinessService readiness)
    {
        _storyboardValidator = storyboardValidator;
        _readiness = readiness;
    }

    public PreviewValidationResult Validate(ProjectDataContext context, StoryboardPreviewSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(snapshot);
        var diagnostics = new List<PreviewDiagnostic>();

        if (_readiness is not null)
        {
            foreach (var item in _readiness.Evaluate(context).Diagnostics)
            {
                diagnostics.Add(new PreviewDiagnostic(
                    $"PROJECT_{item.Code.ToString().ToUpperInvariant()}",
                    item.Message,
                    PreviewDiagnosticSeverity.Error,
                    item.Resource switch
                    {
                        ProjectResourceKind.Level => PreviewDiagnosticSource.Level,
                        ProjectResourceKind.Chart => PreviewDiagnosticSource.Chart,
                        _ => PreviewDiagnosticSource.Asset
                    },
                    item.Path,
                    Suggestion: "使用工程资源修复向导补全或更换该文件。"));
            }
        }

        foreach (var item in _storyboardValidator.Validate(context.Storyboard, context))
        {
            diagnostics.Add(new PreviewDiagnostic(
                item.Code,
                item.Message,
                item.Severity switch
                {
                    StoryboardDiagnosticSeverity.Error => PreviewDiagnosticSeverity.Error,
                    StoryboardDiagnosticSeverity.Warning => PreviewDiagnosticSeverity.Warning,
                    _ => PreviewDiagnosticSeverity.Information
                },
                PreviewDiagnosticSource.Storyboard,
                item.Path,
                item.Node is IStoryboardEntity entity ? entity.Id : null));
        }

        if (!string.IsNullOrWhiteSpace(snapshot.LevelJson))
            ValidateJson(snapshot.LevelJson, PreviewDiagnosticSource.Level, diagnostics);
        else
            diagnostics.Add(Error(
                "PREVIEW_LEVEL_MISSING",
                "当前工程没有可供原生播放器读取的关卡 level 文件。",
                PreviewDiagnosticSource.Level,
                "$.level"));

        ValidateJson(snapshot.StoryboardJson, PreviewDiagnosticSource.Storyboard, diagnostics);
        if (!string.IsNullOrWhiteSpace(snapshot.ChartJson))
        {
            ValidateJson(snapshot.ChartJson, PreviewDiagnosticSource.Chart, diagnostics);
            ValidateChart(snapshot.ChartJson, diagnostics);
        }
        else
            diagnostics.Add(Error("PREVIEW_CHART_MISSING", "当前项目没有可供原生播放器读取的谱面。", PreviewDiagnosticSource.Chart, "$.chart"));

        ValidateFiniteNumbers(snapshot.StoryboardJson, PreviewDiagnosticSource.Storyboard, diagnostics);
        if (!string.IsNullOrWhiteSpace(snapshot.ChartJson))
            ValidateFiniteNumbers(snapshot.ChartJson, PreviewDiagnosticSource.Chart, diagnostics);

        ValidateRequiredFile(
            snapshot.MusicPath,
            "PREVIEW_MUSIC_MISSING",
            "关卡音乐不存在或尚未配置。",
            SupportedMusicExtensions,
            diagnostics);
        ValidateRequiredFile(
            snapshot.BackgroundPath,
            "PREVIEW_BACKGROUND_MISSING",
            "背景图片不存在或尚未配置。",
            SupportedBackgroundExtensions,
            diagnostics);
        ValidateAssetReferences(snapshot, diagnostics);

        return new PreviewValidationResult(snapshot.Version, diagnostics);
    }

    private static void ValidateJson(
        string json,
        PreviewDiagnosticSource source,
        ICollection<PreviewDiagnostic> diagnostics)
    {
        try { _ = JToken.Parse(json); }
        catch (Exception ex)
        {
            diagnostics.Add(Error(
                "PREVIEW_JSON_INVALID",
                $"JSON 无法解析：{ex.Message}",
                source,
                "$",
                "请先修复 JSON 编辑器中的语法错误再应用。"));
        }
    }

    private static void ValidateFiniteNumbers(
        string json,
        PreviewDiagnosticSource source,
        ICollection<PreviewDiagnostic> diagnostics)
    {
        JToken root;
        try { root = JToken.Parse(json); }
        catch { return; }

        foreach (var value in Traverse(root).OfType<JValue>())
        {
            if (value.Type is not (JTokenType.Float or JTokenType.Integer))
                continue;
            if (value.Value is double number && (!double.IsFinite(number) || Math.Abs(number) > 1e12))
                diagnostics.Add(Error("PREVIEW_NUMBER_OUT_OF_RANGE",
                    "数值不是有限数或超出预览播放器的安全范围。",
                    source,
                    value.Path));
            else if (value.Value is float single && (!float.IsFinite(single) || Math.Abs(single) > 1e12f))
                diagnostics.Add(Error("PREVIEW_NUMBER_OUT_OF_RANGE",
                    "数值不是有限数或超出预览播放器的安全范围。",
                    source,
                    value.Path));
        }
    }

    private static void ValidateRequiredFile(
        string? path,
        string code,
        string message,
        IReadOnlySet<string> supportedExtensions,
        ICollection<PreviewDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            diagnostics.Add(Error(code, message, PreviewDiagnosticSource.Asset, path));
        else if (!supportedExtensions.Contains(Path.GetExtension(path)))
            diagnostics.Add(Error(
                "PREVIEW_ASSET_UNSUPPORTED",
                $"原生预览不支持文件类型“{Path.GetExtension(path)}”。",
                PreviewDiagnosticSource.Asset,
                path));
    }

    private static void ValidateChart(string json, ICollection<PreviewDiagnostic> diagnostics)
    {
        JObject chart;
        try { chart = JObject.Parse(json); }
        catch { return; }

        if ((chart.Value<int?>("time_base") ?? 0) <= 0)
            diagnostics.Add(Error("PREVIEW_CHART_TIME_BASE", "谱面的 time_base 必须大于 0。",
                PreviewDiagnosticSource.Chart, "$.time_base"));

        var pages = chart["page_list"] as JArray;
        if (pages is null || pages.Count == 0)
        {
            diagnostics.Add(Error("PREVIEW_CHART_PAGES", "谱面至少需要一个 page。",
                PreviewDiagnosticSource.Chart, "$.page_list"));
        }
        else
        {
            var previousEnd = int.MinValue;
            for (var index = 0; index < pages.Count; index++)
            {
                var page = pages[index] as JObject;
                var start = page?.Value<int?>("start_tick") ?? int.MinValue;
                var end = page?.Value<int?>("end_tick") ?? int.MinValue;
                if (start >= end || start < previousEnd)
                    diagnostics.Add(Error("PREVIEW_CHART_PAGE_RANGE",
                        "Page 时间范围无效或与前一页重叠。",
                        PreviewDiagnosticSource.Chart,
                        $"$.page_list[{index}]"));
                previousEnd = end;
            }
        }

        var tempos = chart["tempo_list"] as JArray;
        if (tempos is null || tempos.Count == 0)
            diagnostics.Add(Error("PREVIEW_CHART_TEMPO", "谱面至少需要一个 tempo。",
                PreviewDiagnosticSource.Chart, "$.tempo_list"));
        else
        {
            var previousTick = int.MinValue;
            for (var index = 0; index < tempos.Count; index++)
            {
                var tempo = tempos[index] as JObject;
                var tick = tempo?.Value<int?>("tick") ?? int.MinValue;
                var value = tempo?.Value<long?>("value") ?? 0;
                if (tick < previousTick || value <= 0)
                    diagnostics.Add(Error("PREVIEW_CHART_TEMPO_RANGE",
                        "Tempo 必须按 tick 排序且 value 大于 0。",
                        PreviewDiagnosticSource.Chart,
                        $"$.tempo_list[{index}]"));
                previousTick = tick;
            }
        }

        var notes = chart["note_list"] as JArray ?? new JArray();
        var noteIds = notes.OfType<JObject>()
            .Select(note => note.Value<int?>("id"))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToHashSet();
        if (noteIds.Count != notes.Count)
            diagnostics.Add(Error("PREVIEW_CHART_NOTE_ID",
                "音符 ID 缺失或重复。",
                PreviewDiagnosticSource.Chart,
                "$.note_list"));
        for (var index = 0; index < notes.Count; index++)
        {
            var note = notes[index] as JObject;
            var pageIndex = note?.Value<int?>("page_index") ?? -1;
            var nextId = note?.Value<int?>("next_id") ?? -1;
            if (pages is null || pageIndex < 0 || pageIndex >= pages.Count)
                diagnostics.Add(Error("PREVIEW_CHART_NOTE_PAGE",
                    "音符引用了不存在的 page_index。",
                    PreviewDiagnosticSource.Chart,
                    $"$.note_list[{index}].page_index"));
            if (nextId > 0 && !noteIds.Contains(nextId))
                diagnostics.Add(Error("PREVIEW_CHART_NOTE_LINK",
                    "音符 next_id 指向不存在的音符。",
                    PreviewDiagnosticSource.Chart,
                    $"$.note_list[{index}].next_id"));
        }
    }

    private static void ValidateAssetReferences(
        StoryboardPreviewSnapshot snapshot,
        ICollection<PreviewDiagnostic> diagnostics)
    {
        JToken root;
        try { root = JToken.Parse(snapshot.StoryboardJson); }
        catch { return; }

        var assetRoot = snapshot.AssetRoot;
        foreach (var property in Traverse(root).OfType<JProperty>()
                     .Where(item => string.Equals(item.Name, "path", StringComparison.OrdinalIgnoreCase)))
        {
            var reference = property.Value.Value<string>();
            if (string.IsNullOrWhiteSpace(reference) || reference.Contains("://", StringComparison.Ordinal))
                continue;
            if (string.IsNullOrWhiteSpace(assetRoot))
            {
                diagnostics.Add(Error("PREVIEW_ASSET_ROOT_MISSING",
                    $"无法解析素材“{reference}”，项目素材根目录未配置。",
                    PreviewDiagnosticSource.Asset,
                    property.Path));
                continue;
            }

            string fullPath;
            try
            {
                var rootPath = Path.GetFullPath(assetRoot);
                fullPath = Path.GetFullPath(Path.Combine(rootPath, reference.Replace('/', Path.DirectorySeparatorChar)));
                if (!fullPath.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(fullPath, rootPath, StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add(Error("PREVIEW_ASSET_PATH_ESCAPE",
                        $"素材路径“{reference}”越过项目素材目录。",
                        PreviewDiagnosticSource.Asset,
                        property.Path));
                    continue;
                }
            }
            catch (Exception ex)
            {
                diagnostics.Add(Error("PREVIEW_ASSET_PATH_INVALID",
                    $"素材路径“{reference}”无效：{ex.Message}",
                    PreviewDiagnosticSource.Asset,
                    property.Path));
                continue;
            }

            var extension = Path.GetExtension(fullPath);
            if (!SupportedAssetExtensions.Contains(extension))
            {
                diagnostics.Add(Error("PREVIEW_ASSET_UNSUPPORTED",
                    $"原生预览不支持素材类型“{extension}”。",
                    PreviewDiagnosticSource.Asset,
                    property.Path));
            }
            else if (!File.Exists(fullPath))
            {
                diagnostics.Add(Error("PREVIEW_ASSET_NOT_FOUND",
                    $"找不到素材“{reference}”。",
                    PreviewDiagnosticSource.Asset,
                    property.Path));
            }
        }
    }

    private static PreviewDiagnostic Error(
        string code,
        string message,
        PreviewDiagnosticSource source,
        string? path = null,
        string? suggestion = null) =>
        new(code, message, PreviewDiagnosticSeverity.Error, source, path, Suggestion: suggestion);

    private static IEnumerable<JToken> Traverse(JToken token)
    {
        yield return token;
        if (token is not JContainer container)
            yield break;
        foreach (var child in container.Children())
        foreach (var nested in Traverse(child))
            yield return nested;
    }
}
