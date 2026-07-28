using System.IO;
using System.Windows.Media.Imaging;
using NAudio.Vorbis;
using NAudio.Wave;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Models;
using Naziki_Editor.State;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Naziki_Editor.Features.Project.Resources;

public enum ProjectResourceKind
{
    Level,
    Chart,
    Storyboard,
    Music,
    Background,
    Asset
}

public sealed record ProjectResourceChanged(
    ProjectResourceKind Kind,
    string RelativePath,
    string AbsolutePath);

public sealed record ProjectCreationRequest(
    string ProjectFilePath,
    string ProjectName,
    string LevelSourcePath,
    string ChartSourcePath,
    string MusicSourcePath,
    string BackgroundSourcePath,
    string? StoryboardSourcePath = null);

public sealed record ProjectCreationResult(
    NazikiProjectModel Project,
    string ProjectFilePath,
    string StoryboardPath);

public interface IProjectResourceService
{
    string ResolvePath(string projectFilePath, string configuredPath);
    string? ResolvePath(ProjectDataContext context, ProjectResourceKind kind);
    string ToProjectRelativePath(string projectFilePath, string absolutePath);
    void ValidateSource(ProjectResourceKind kind, string sourcePath);
    Task<ProjectCreationResult> CreateProjectAsync(
        ProjectCreationRequest request,
        CancellationToken cancellationToken = default);
    Task<string> ImportAsync(
        ProjectDataContext context,
        ProjectResourceKind kind,
        string sourcePath,
        CancellationToken cancellationToken = default);
    Task<string> EnsureStoryboardAsync(
        ProjectDataContext context,
        CancellationToken cancellationToken = default);
}

public enum ProjectReadinessCode
{
    ProjectMissing,
    LevelMissing,
    ChartMissing,
    StoryboardMissing,
    MusicMissing,
    BackgroundMissing
}

public sealed record ProjectReadinessDiagnostic(
    ProjectReadinessCode Code,
    ProjectResourceKind? Resource,
    string Message,
    string? Path);

public sealed record ProjectReadinessState(
    IReadOnlyList<ProjectReadinessDiagnostic> Diagnostics)
{
    public bool HasProject => Diagnostics.All(item => item.Code != ProjectReadinessCode.ProjectMissing);
    public bool HasLevel => HasProject && Diagnostics.All(item => item.Code != ProjectReadinessCode.LevelMissing);
    public bool HasChart => HasProject && Diagnostics.All(item => item.Code != ProjectReadinessCode.ChartMissing);
    public bool HasStoryboard => HasProject && Diagnostics.All(item => item.Code != ProjectReadinessCode.StoryboardMissing);
    public bool HasMusic => HasProject && Diagnostics.All(item => item.Code != ProjectReadinessCode.MusicMissing);
    public bool HasBackground => HasProject && Diagnostics.All(item => item.Code != ProjectReadinessCode.BackgroundMissing);
    public bool CanUseChartFeatures => HasChart;
    public bool CanPlay => HasChart && HasMusic;
    public bool CanPreview => HasLevel && HasChart && HasStoryboard && HasMusic && HasBackground;
    public bool CanExportStoryboard => HasChart && HasStoryboard;
    public bool NeedsRepair => Diagnostics.Count > 0;
}

public interface IProjectReadinessService
{
    ProjectReadinessState Evaluate(ProjectDataContext context);
}

public sealed class ProjectReadinessService : IProjectReadinessService
{
    private readonly IProjectResourceService _resources;

    public ProjectReadinessService(IProjectResourceService resources) => _resources = resources;

    public ProjectReadinessState Evaluate(ProjectDataContext context)
    {
        var diagnostics = new List<ProjectReadinessDiagnostic>();
        if (context.ProjectData is null || string.IsNullOrWhiteSpace(context.ProjectFilePath))
        {
            diagnostics.Add(new ProjectReadinessDiagnostic(
                ProjectReadinessCode.ProjectMissing,
                null,
                "尚未打开或创建工程。",
                context.ProjectFilePath));
            return new ProjectReadinessState(diagnostics);
        }

        Require(ProjectResourceKind.Level, ProjectReadinessCode.LevelMissing, "关卡 level 文件缺失或路径无效。");
        Require(ProjectResourceKind.Chart, ProjectReadinessCode.ChartMissing, "谱面文件缺失或路径无效。");
        Require(ProjectResourceKind.Storyboard, ProjectReadinessCode.StoryboardMissing, "故事板文件缺失或路径无效。");
        Require(ProjectResourceKind.Music, ProjectReadinessCode.MusicMissing, "关卡音乐缺失或路径无效。");
        Require(ProjectResourceKind.Background, ProjectReadinessCode.BackgroundMissing, "背景图片缺失或路径无效。");
        return new ProjectReadinessState(diagnostics);

        void Require(ProjectResourceKind kind, ProjectReadinessCode code, string message)
        {
            string? resolved = null;
            try
            {
                resolved = _resources.ResolvePath(context, kind);
                if (!string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved))
                {
                    _resources.ValidateSource(kind, resolved);
                    return;
                }
            }
            catch (Exception ex)
            {
                message = $"{message} {ex.Message}";
            }

            diagnostics.Add(new ProjectReadinessDiagnostic(code, kind, message, resolved));
        }
    }
}

public sealed class ProjectResourceService : IProjectResourceService
{
    private static readonly HashSet<string> AudioExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wav", ".ogg" };
    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };
    private static readonly HashSet<string> AssetExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp",
            ".mp4", ".webm", ".avi", ".mov", ".ttf", ".otf", ".nem"
        };

    private readonly IStoryboardDocumentReader _storyboardReader;
    private readonly IStoryboardDocumentWriter _storyboardWriter;
    private readonly IMessageBroker _messages;

    public ProjectResourceService(
        IStoryboardDocumentReader storyboardReader,
        IStoryboardDocumentWriter storyboardWriter,
        IMessageBroker messages)
    {
        _storyboardReader = storyboardReader;
        _storyboardWriter = storyboardWriter;
        _messages = messages;
    }

    public string ResolvePath(string projectFilePath, string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath))
            throw new ArgumentException("工程文件路径不能为空。", nameof(projectFilePath));
        if (string.IsNullOrWhiteSpace(configuredPath))
            throw new ArgumentException("资源路径不能为空。", nameof(configuredPath));

        if (Path.IsPathRooted(configuredPath))
            return Path.GetFullPath(configuredPath);

        var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(projectFilePath))
            ?? throw new InvalidOperationException("无法解析工程目录。");
        var resolved = Path.GetFullPath(Path.Combine(
            projectDirectory,
            configuredPath.Replace('/', Path.DirectorySeparatorChar)));
        EnsureInsideProject(projectDirectory, resolved);
        return resolved;
    }

    public string? ResolvePath(ProjectDataContext context, ProjectResourceKind kind)
    {
        if (context.ProjectData is null || string.IsNullOrWhiteSpace(context.ProjectFilePath))
            return null;
        var configured = GetConfiguredPath(context.ProjectData, kind);
        return string.IsNullOrWhiteSpace(configured)
            ? null
            : ResolvePath(context.ProjectFilePath, configured);
    }

    public string ToProjectRelativePath(string projectFilePath, string absolutePath)
    {
        var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(projectFilePath))
            ?? throw new InvalidOperationException("无法解析工程目录。");
        var fullPath = Path.GetFullPath(absolutePath);
        EnsureInsideProject(projectDirectory, fullPath);
        return Path.GetRelativePath(projectDirectory, fullPath).Replace('\\', '/');
    }

    public void ValidateSource(ProjectResourceKind kind, string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            throw new FileNotFoundException("所选资源文件不存在。", sourcePath);

        var extension = Path.GetExtension(sourcePath);
        switch (kind)
        {
            case ProjectResourceKind.Level:
                if (!extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("关卡 level 文件必须是 JSON 文件。");
                ValidateLevel(sourcePath);
                break;
            case ProjectResourceKind.Chart:
                if (!extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("谱面必须是 JSON 文件。");
                ValidateChart(sourcePath);
                break;
            case ProjectResourceKind.Storyboard:
                if (!extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("故事板必须是 JSON 文件。");
                _ = _storyboardReader.Read(File.ReadAllText(sourcePath))
                    ?? throw new InvalidDataException("故事板 JSON 无法解析。");
                break;
            case ProjectResourceKind.Music:
                if (!AudioExtensions.Contains(extension))
                    throw new InvalidDataException($"不支持的音频格式：{extension}");
                ValidateAudio(sourcePath, extension);
                break;
            case ProjectResourceKind.Background:
                if (!ImageExtensions.Contains(extension))
                    throw new InvalidDataException($"不支持的背景图片格式：{extension}");
                ValidateImage(sourcePath);
                break;
            case ProjectResourceKind.Asset:
                if (!AssetExtensions.Contains(extension))
                    throw new InvalidDataException($"不支持的素材格式：{extension}");
                break;
        }
    }

    public async Task<ProjectCreationResult> CreateProjectAsync(
        ProjectCreationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ProjectName))
            throw new InvalidDataException("工程名称不能为空。");

        var projectFile = Path.GetFullPath(request.ProjectFilePath);
        if (!projectFile.EndsWith(".nep", StringComparison.OrdinalIgnoreCase))
            projectFile += ".nep";
        if (File.Exists(projectFile))
            throw new IOException("目标位置已经存在同名 .nep 工程。");

        ValidateSource(ProjectResourceKind.Level, request.LevelSourcePath);
        ValidateSource(ProjectResourceKind.Chart, request.ChartSourcePath);
        ValidateSource(ProjectResourceKind.Music, request.MusicSourcePath);
        ValidateSource(ProjectResourceKind.Background, request.BackgroundSourcePath);
        if (!string.IsNullOrWhiteSpace(request.StoryboardSourcePath))
            ValidateSource(ProjectResourceKind.Storyboard, request.StoryboardSourcePath);

        var projectDirectory = Path.GetDirectoryName(projectFile)
            ?? throw new InvalidOperationException("无法解析工程目录。");
        var createdFiles = new List<string>();
        var createdDirectories = new List<string>();
        if (!Directory.Exists(projectDirectory))
        {
            Directory.CreateDirectory(projectDirectory);
            createdDirectories.Add(projectDirectory);
        }

        try
        {
            var levelDirectory = EnsureDirectory(Path.Combine(projectDirectory, "level"));
            var musicDirectory = EnsureDirectory(Path.Combine(projectDirectory, "music"));
            var assetsDirectory = EnsureDirectory(Path.Combine(projectDirectory, "assets"));
            var backgroundDirectory = EnsureDirectory(Path.Combine(assetsDirectory, "background"));

            var level = await CopyAsNamedUniqueAsync(
                request.LevelSourcePath, levelDirectory, "level", createdFiles, cancellationToken).ConfigureAwait(false);
            var chart = await CopyUniqueAsync(
                request.ChartSourcePath, levelDirectory, createdFiles, cancellationToken).ConfigureAwait(false);
            var music = await CopyUniqueAsync(
                request.MusicSourcePath, musicDirectory, createdFiles, cancellationToken).ConfigureAwait(false);
            var background = await CopyUniqueAsync(
                request.BackgroundSourcePath, backgroundDirectory, createdFiles, cancellationToken).ConfigureAwait(false);
            string storyboard;
            if (!string.IsNullOrWhiteSpace(request.StoryboardSourcePath))
            {
                storyboard = await CopyUniqueAsync(
                    request.StoryboardSourcePath!, levelDirectory, createdFiles, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                storyboard = GetUniqueDestinationPath(
                    levelDirectory,
                    "storyboard",
                    ".json");
                await WriteAtomicAsync(
                    storyboard,
                    _storyboardWriter.Write(new StoryboardRoot()),
                    cancellationToken).ConfigureAwait(false);
                createdFiles.Add(storyboard);
            }

            var now = DateTime.Now;
            var project = new NazikiProjectModel
            {
                ProjectName = request.ProjectName.Trim(),
                CreationTime = now,
                LastModifiedTime = now,
                LevelFilePath = ToProjectRelativePath(projectFile, level),
                ChartFilePath = ToProjectRelativePath(projectFile, chart),
                AudioFilePath = ToProjectRelativePath(projectFile, music),
                BackgroundPath = ToProjectRelativePath(projectFile, background),
                StoryboardExportPath = ToProjectRelativePath(projectFile, storyboard),
                MaterialFolderPath = "assets"
            };
            await WriteAtomicAsync(
                projectFile,
                JsonConvert.SerializeObject(project, Formatting.Indented),
                cancellationToken).ConfigureAwait(false);
            createdFiles.Add(projectFile);
            return new ProjectCreationResult(project, projectFile, storyboard);

            string EnsureDirectory(string path)
            {
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                    createdDirectories.Add(path);
                }
                return path;
            }
        }
        catch
        {
            foreach (var file in createdFiles.AsEnumerable().Reverse())
            {
                try { if (File.Exists(file)) File.Delete(file); }
                catch { }
            }
            foreach (var directory in createdDirectories.OrderByDescending(path => path.Length))
            {
                try
                {
                    if (Directory.Exists(directory) &&
                        !Directory.EnumerateFileSystemEntries(directory).Any())
                        Directory.Delete(directory);
                }
                catch { }
            }
            throw;
        }
    }

    public async Task<string> ImportAsync(
        ProjectDataContext context,
        ProjectResourceKind kind,
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.ProjectData is null || string.IsNullOrWhiteSpace(context.ProjectFilePath))
            throw new InvalidOperationException("必须先打开或创建工程。");
        ValidateSource(kind, sourcePath);

        var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(context.ProjectFilePath))
            ?? throw new InvalidOperationException("无法解析工程目录。");
        var targetDirectory = kind switch
        {
            ProjectResourceKind.Level or ProjectResourceKind.Chart or ProjectResourceKind.Storyboard =>
                Path.Combine(projectDirectory, "level"),
            ProjectResourceKind.Music => Path.Combine(projectDirectory, "music"),
            ProjectResourceKind.Background => Path.Combine(projectDirectory, "assets", "background"),
            _ => Path.Combine(projectDirectory, "assets")
        };
        Directory.CreateDirectory(targetDirectory);
        var destination = await CopyUniqueAsync(
            sourcePath, targetDirectory, null, cancellationToken).ConfigureAwait(false);
        var relative = kind == ProjectResourceKind.Asset
            ? Path.GetRelativePath(targetDirectory, destination).Replace('\\', '/')
            : ToProjectRelativePath(context.ProjectFilePath, destination);
        if (kind != ProjectResourceKind.Asset)
            SetConfiguredPath(context.ProjectData, kind, relative);
        context.ProjectData.LastModifiedTime = DateTime.Now;
        _messages.Publish("ProjectResourcesChanged", new ProjectResourceChanged(kind, relative, destination));
        return relative;
    }

    public async Task<string> EnsureStoryboardAsync(
        ProjectDataContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.ProjectData is null || string.IsNullOrWhiteSpace(context.ProjectFilePath))
            throw new InvalidOperationException("必须先打开或创建工程。");
        var existing = ResolvePath(context, ProjectResourceKind.Storyboard);
        if (!string.IsNullOrWhiteSpace(existing) && File.Exists(existing))
            return context.ProjectData.StoryboardExportPath!;

        var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(context.ProjectFilePath))
            ?? throw new InvalidOperationException("无法解析工程目录。");
        var destination = Path.Combine(projectDirectory, "level", "storyboard.json");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await WriteAtomicAsync(
            destination,
            _storyboardWriter.Write(context.Storyboard ?? new StoryboardRoot()),
            cancellationToken).ConfigureAwait(false);
        var relative = ToProjectRelativePath(context.ProjectFilePath, destination);
        context.ProjectData.StoryboardExportPath = relative;
        context.StoryboardPath = destination;
        _messages.Publish(
            "ProjectResourcesChanged",
            new ProjectResourceChanged(ProjectResourceKind.Storyboard, relative, destination));
        return relative;
    }

    private static string? GetConfiguredPath(NazikiProjectModel project, ProjectResourceKind kind) =>
        kind switch
        {
            ProjectResourceKind.Level => project.LevelFilePath,
            ProjectResourceKind.Chart => project.ChartFilePath,
            ProjectResourceKind.Storyboard => project.StoryboardExportPath,
            ProjectResourceKind.Music => project.AudioFilePath,
            ProjectResourceKind.Background => project.BackgroundPath,
            ProjectResourceKind.Asset => project.MaterialFolderPath,
            _ => null
        };

    private static void SetConfiguredPath(
        NazikiProjectModel project,
        ProjectResourceKind kind,
        string value)
    {
        switch (kind)
        {
            case ProjectResourceKind.Level: project.LevelFilePath = value; break;
            case ProjectResourceKind.Chart: project.ChartFilePath = value; break;
            case ProjectResourceKind.Storyboard: project.StoryboardExportPath = value; break;
            case ProjectResourceKind.Music: project.AudioFilePath = value; break;
            case ProjectResourceKind.Background: project.BackgroundPath = value; break;
            case ProjectResourceKind.Asset: project.MaterialFolderPath = value; break;
        }
    }

    private static async Task<string> CopyUniqueAsync(
        string sourcePath,
        string destinationDirectory,
        ICollection<string>? createdFiles,
        CancellationToken cancellationToken)
    {
        var source = Path.GetFullPath(sourcePath);
        var destination = Path.Combine(destinationDirectory, Path.GetFileName(source));
        if (string.Equals(source, Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            return destination;

        var baseName = Path.GetFileNameWithoutExtension(source);
        var extension = Path.GetExtension(source);
        destination = GetUniqueDestinationPath(destinationDirectory, baseName, extension);

        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        createdFiles?.Add(destination);
        return destination;
    }

    private static async Task<string> CopyAsNamedUniqueAsync(
        string sourcePath,
        string destinationDirectory,
        string baseName,
        ICollection<string>? createdFiles,
        CancellationToken cancellationToken)
    {
        var source = Path.GetFullPath(sourcePath);
        var destination = GetUniqueDestinationPath(
            destinationDirectory,
            baseName,
            Path.GetExtension(source));
        if (string.Equals(source, Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            return destination;

        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        createdFiles?.Add(destination);
        return destination;
    }

    private static string GetUniqueDestinationPath(
        string destinationDirectory,
        string baseName,
        string extension)
    {
        var destination = Path.Combine(destinationDirectory, baseName + extension);
        var suffix = 1;
        while (File.Exists(destination))
            destination = Path.Combine(destinationDirectory, $"{baseName}_{suffix++}{extension}");
        return destination;
    }

    private static async Task WriteAtomicAsync(
        string destination,
        string contents,
        CancellationToken cancellationToken)
    {
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporary,
                contents,
                new System.Text.UTF8Encoding(false),
                cancellationToken).ConfigureAwait(false);
            File.Move(temporary, destination, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static void ValidateChart(string path)
    {
        var root = JObject.Parse(File.ReadAllText(path));
        if ((root.Value<int?>("time_base") ?? 0) <= 0)
            throw new InvalidDataException("谱面的 time_base 必须大于 0。");
        if (root["page_list"] is not JArray pages || pages.Count == 0)
            throw new InvalidDataException("谱面必须至少包含一个 page。");
        if (root["tempo_list"] is not JArray tempos || tempos.Count == 0)
            throw new InvalidDataException("谱面必须至少包含一个 tempo。");
    }

    private static void ValidateLevel(string path)
    {
        var root = JObject.Parse(File.ReadAllText(path));
        if (string.IsNullOrWhiteSpace(root.Value<string>("id")))
            throw new InvalidDataException("关卡 level 文件缺少有效的 id。");
        if (root["charts"] is not JArray charts || charts.Count == 0)
            throw new InvalidDataException("关卡 level 文件必须至少包含一个 charts 项。");
        if (!charts.OfType<JObject>().Any(chart =>
                chart.Value<string>("type") is "easy" or "hard" or "extreme" &&
                !string.IsNullOrWhiteSpace(chart.Value<string>("path"))))
            throw new InvalidDataException("关卡 level 文件没有可用的 easy、hard 或 extreme 谱面配置。");
        if (root["music"] is not JObject music ||
            string.IsNullOrWhiteSpace(music.Value<string>("path")))
            throw new InvalidDataException("关卡 level 文件缺少 music.path。");
    }

    private static void ValidateAudio(string path, string extension)
    {
        using WaveStream reader = extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase)
            ? new VorbisWaveReader(path)
            : new AudioFileReader(path);
        if (reader.TotalTime <= TimeSpan.Zero)
            throw new InvalidDataException("音频文件没有可播放内容。");
    }

    private static void ValidateImage(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0 || decoder.Frames[0].PixelWidth <= 0 || decoder.Frames[0].PixelHeight <= 0)
            throw new InvalidDataException("图片文件无法解码。");
    }

    private static void EnsureInsideProject(string projectDirectory, string path)
    {
        var root = Path.GetFullPath(projectDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(path);
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("工程托管资源路径不能越出工程目录。");
    }
}
