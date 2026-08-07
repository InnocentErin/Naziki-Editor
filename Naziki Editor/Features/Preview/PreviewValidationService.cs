using System.IO;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Charting;
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
    private readonly IChartPreviewWireAdapter _chartWire;

    public PreviewValidationService(IStoryboardDocumentValidator storyboardValidator)
    {
        _storyboardValidator = storyboardValidator;
        _chartWire = new ChartPreviewWireAdapter();
    }

    public PreviewValidationService(
        IStoryboardDocumentValidator storyboardValidator,
        IProjectReadinessService readiness)
        : this(storyboardValidator, readiness, new ChartPreviewWireAdapter())
    {
    }

    public PreviewValidationService(
        IStoryboardDocumentValidator storyboardValidator,
        IProjectReadinessService readiness,
        IChartPreviewWireAdapter chartWire)
    {
        _storyboardValidator = storyboardValidator;
        _readiness = readiness;
        _chartWire = chartWire;
    }

    public PreviewValidationResult Validate(ProjectDataContext context, StoryboardPreviewSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(snapshot);
        var diagnostics = new List<PreviewDiagnostic>(snapshot.CaptureDiagnostics);

        if (_readiness is not null)
        {
            foreach (var item in _readiness.Evaluate(context).Diagnostics)
            {
                var source = item.Resource switch
                {
                    ProjectResourceKind.Level => PreviewDiagnosticSource.Level,
                    ProjectResourceKind.Chart => PreviewDiagnosticSource.Chart,
                    ProjectResourceKind.Storyboard => PreviewDiagnosticSource.Storyboard,
                    _ => PreviewDiagnosticSource.Asset
                };
                diagnostics.Add(new PreviewDiagnostic(
                    $"PROJECT_{item.Code.ToString().ToUpperInvariant()}",
                    item.Message,
                    PreviewDiagnosticSeverity.Error,
                    source,
                    item.Path,
                    Suggestion: "使用工程资源修复向导补全或更换该文件。")
                {
                    Impact = source == PreviewDiagnosticSource.Storyboard
                        ? PreviewDiagnosticImpact.StoryboardOnly
                        : PreviewDiagnosticImpact.PreviewBlocking,
                    Stage = "readiness"
                });
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
                item.Node is IStoryboardEntity entity ? entity.Id : null)
            {
                Impact = item.Severity == StoryboardDiagnosticSeverity.Error
                    ? PreviewDiagnosticImpact.StoryboardOnly
                    : PreviewDiagnosticImpact.Advisory,
                Stage = "validate"
            });
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
            ValidateChartCollections(snapshot.ChartJson, diagnostics);
            var chartDiagnostics = context.ChartDocument is null
                ? _chartWire.Diagnose(snapshot.ChartJson)
                : _chartWire.Diagnose(context.ChartDocument);
            foreach (var item in chartDiagnostics)
            {
                diagnostics.Add(new PreviewDiagnostic(
                    item.Code,
                    item.Message,
                    item.Severity switch
                    {
                        ChartDiagnosticSeverity.Error =>
                            PreviewDiagnosticSeverity.Error,
                        ChartDiagnosticSeverity.Warning =>
                            PreviewDiagnosticSeverity.Warning,
                        _ => PreviewDiagnosticSeverity.Information
                    },
                    PreviewDiagnosticSource.Chart,
                    item.Path)
                {
                    Impact = item.Severity == ChartDiagnosticSeverity.Error
                        ? PreviewDiagnosticImpact.PreviewBlocking
                        : PreviewDiagnosticImpact.Advisory,
                    Stage = "validate"
                });
            }
            foreach (var issue in _chartWire.Validate(snapshot.ChartJson))
            {
                diagnostics.Add(Error(
                    "PREVIEW_CHART_WIRE_INVALID",
                    issue.Message,
                    PreviewDiagnosticSource.Chart,
                    issue.Path,
                    "请重新导入或修复该谱面字段后再启动 Unity 预览。"));
            }
        }
        else
            diagnostics.Add(Error("PREVIEW_CHART_MISSING", "当前项目没有可供原生播放器读取的谱面。", PreviewDiagnosticSource.Chart, "$.chart"));

        ValidateFiniteNumbers(snapshot.StoryboardJson, PreviewDiagnosticSource.Storyboard, diagnostics);
        ValidateRuntimeStoryboardBoundary(snapshot.StoryboardJson, diagnostics);
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
        WarnPlatformDependentAsset(snapshot.BackgroundPath, diagnostics,
            "Background image decoding is not guaranteed by the official Cytoid runtime for this format.");
        ValidateAssetReferences(snapshot, diagnostics);

        return new PreviewValidationResult(snapshot.Version, MergeDiagnostics(diagnostics));
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

    private static void ValidateChartCollections(
        string json,
        ICollection<PreviewDiagnostic> diagnostics)
    {
        JObject root;
        try { root = JObject.Parse(json); }
        catch { return; }

        RequireNonEmptyArray(root, "note_list", "PREVIEW_CHART_EMPTY",
            "谱面没有任何音符，Unity 正式播放器无法计算关卡时长。", diagnostics);
        RequireNonEmptyArray(root, "page_list", "PREVIEW_CHART_PAGES_EMPTY",
            "谱面没有扫描页 page_list。", diagnostics);
        RequireNonEmptyArray(root, "tempo_list", "PREVIEW_CHART_TEMPO_EMPTY",
            "谱面没有 BPM 段 tempo_list。", diagnostics);
    }

    private static void RequireNonEmptyArray(
        JObject root,
        string property,
        string code,
        string message,
        ICollection<PreviewDiagnostic> diagnostics)
    {
        if (root[property] is JArray { Count: > 0 })
            return;
        diagnostics.Add(Error(code, message, PreviewDiagnosticSource.Chart,
            $"$.{property}", "请重新导入或修复正式谱面文件。"));
    }

    private static void ValidateRuntimeStoryboardBoundary(
        string json,
        ICollection<PreviewDiagnostic> diagnostics)
    {
        JToken root;
        try { root = JToken.Parse(json); }
        catch { return; }

        var forbidden = Traverse(root).OfType<JProperty>().FirstOrDefault(property =>
            property.Name is "editor_id" or "document_id" or "source_group_id" or
                "activation_mode" or "base_patch" or "instance_overrides" or
                "import_diagnostics" ||
            property.Name.StartsWith("$naziki_editor_", StringComparison.Ordinal));
        if (forbidden is null)
            return;

        diagnostics.Add(Error(
            "PREVIEW_STORYBOARD_EDITOR_FORMAT",
            $"故事板仍包含编辑器专用字段“{forbidden.Name}”，不能发送给正式 Unity 播放器。",
            PreviewDiagnosticSource.Storyboard,
            forbidden.Path,
            "请先通过故事板正式运行时导出器生成 wire JSON。"));
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
                diagnostics.Add(StoryboardAssetError("PREVIEW_ASSET_ROOT_MISSING",
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
                    diagnostics.Add(StoryboardAssetError("PREVIEW_ASSET_PATH_ESCAPE",
                        $"素材路径“{reference}”越过项目素材目录。",
                        PreviewDiagnosticSource.Asset,
                        property.Path));
                    continue;
                }
            }
            catch (Exception ex)
            {
                diagnostics.Add(StoryboardAssetError("PREVIEW_ASSET_PATH_INVALID",
                    $"素材路径“{reference}”无效：{ex.Message}",
                    PreviewDiagnosticSource.Asset,
                    property.Path));
                continue;
            }

            var extension = Path.GetExtension(fullPath);
            if (!SupportedAssetExtensions.Contains(extension))
            {
                diagnostics.Add(StoryboardAssetError("PREVIEW_ASSET_UNSUPPORTED",
                    $"原生预览不支持素材类型“{extension}”。",
                    PreviewDiagnosticSource.Asset,
                    property.Path));
            }
            else if (!File.Exists(fullPath))
            {
                diagnostics.Add(StoryboardAssetError("PREVIEW_ASSET_NOT_FOUND",
                    $"找不到素材“{reference}”。",
                    PreviewDiagnosticSource.Asset,
                    property.Path));
            }
            else
            {
                WarnPlatformDependentAsset(fullPath, diagnostics,
                    "Asset decoding depends on the official Unity runtime and target platform.",
                    property.Path);
            }
        }
    }

    private static void WarnPlatformDependentAsset(
        string? path,
        ICollection<PreviewDiagnostic> diagnostics,
        string message,
        string? jsonPath = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".bmp" or ".gif" or ".webp" or
            ".mp4" or ".webm" or ".mov" or ".avi")) return;
        diagnostics.Add(new PreviewDiagnostic(
            "PREVIEW_ASSET_PLATFORM_DEPENDENT",
            $"{message} ({extension})",
            PreviewDiagnosticSeverity.Warning,
            PreviewDiagnosticSource.Asset,
            jsonPath ?? path)
        {
            Impact = PreviewDiagnosticImpact.Advisory,
            Stage = "validate"
        });
    }

    private static PreviewDiagnostic StoryboardAssetError(
        string code,
        string message,
        PreviewDiagnosticSource source,
        string? path = null,
        string? suggestion = null) =>
        Error(code, message, source, path, suggestion,
            PreviewDiagnosticImpact.StoryboardOnly) with
        {
            Stage = "resolve-assets"
        };

    private static IReadOnlyList<PreviewDiagnostic> MergeDiagnostics(
        IEnumerable<PreviewDiagnostic> diagnostics) =>
        diagnostics
            .GroupBy(item => new
            {
                item.Code,
                item.Source,
                item.Path,
                item.EntityId,
                item.Stage,
                item.Impact
            })
            .Select(group =>
            {
                var primary = group
                    .OrderByDescending(item => item.Severity)
                    .ThenBy(item => item.Timestamp)
                    .First();
                return primary with
                {
                    RepeatCount = group.Sum(item => Math.Max(1, item.RepeatCount))
                };
            })
            .ToArray();

    private static PreviewDiagnostic Error(
        string code,
        string message,
        PreviewDiagnosticSource source,
        string? path = null,
        string? suggestion = null,
        PreviewDiagnosticImpact? impact = null) =>
        new(code, message, PreviewDiagnosticSeverity.Error, source, path, Suggestion: suggestion)
        {
            Impact = impact ?? (source == PreviewDiagnosticSource.Storyboard
                ? PreviewDiagnosticImpact.StoryboardOnly
                : PreviewDiagnosticImpact.PreviewBlocking),
            Stage = "validate"
        };

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
