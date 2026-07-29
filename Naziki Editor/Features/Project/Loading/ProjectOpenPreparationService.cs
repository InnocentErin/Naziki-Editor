using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Services;
using Naziki_Editor.Features.EditorShell;
using Naziki_Editor.Features.Project.Resources;
using Naziki_Editor.State;
using System.IO;

namespace Naziki_Editor.Features.Project.Loading;

public sealed record PreparedProjectSession(
    ProjectDataContext Context,
    AssetBundle Assets,
    string? MusicPath);

public interface IProjectOpenPreparationService
{
    Task<PreparedProjectSession> PrepareAsync(
        string projectFilePath,
        IProgress<ProjectLoadProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// Performs all project file and asset preparation without constructing WPF
/// editor controls. The resulting session is immutable by convention until it
/// is handed to MainWindow on the UI thread.
/// </summary>
public sealed class ProjectOpenPreparationService : IProjectOpenPreparationService
{
    private readonly IProjectService _projects;
    private readonly IProjectResourceService _resources;

    public ProjectOpenPreparationService(
        IProjectService projects,
        IProjectResourceService resources)
    {
        _projects = projects;
        _resources = resources;
    }

    public async Task<PreparedProjectSession> PrepareAsync(
        string projectFilePath,
        IProgress<ProjectLoadProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFilePath);
        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report(new(ProjectLoadStage.ProjectConfiguration,
            "正在读取工程配置…", 0, ProjectLoadPipeline.TotalSteps));
        ProjectDataContext context;
        try
        {
            context = await Task.Run(
                () => _projects.LoadProjectData(projectFilePath)
                    ?? throw new InvalidDataException("工程文件内容为空。"),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ProjectLoadException(
                ProjectLoadStage.ProjectConfiguration,
                "读取工程、谱面或故事板数据失败。",
                ex,
                projectFilePath);
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new(ProjectLoadStage.Chart,
            context.HasChart ? "谱面数据已准备完成。" : "工程未配置可用谱面。",
            2, ProjectLoadPipeline.TotalSteps));
        progress?.Report(new(ProjectLoadStage.Storyboard,
            context.HasStoryboard ? "故事板数据已准备完成。" : "工程未配置可用故事板。",
            3, ProjectLoadPipeline.TotalSteps));

        progress?.Report(new(ProjectLoadStage.ResourcePaths,
            "正在检查工程资源路径…", 4, ProjectLoadPipeline.TotalSteps));
        string projectDirectory;
        string? assetRoot;
        string? musicPath;
        try
        {
            projectDirectory = Path.GetDirectoryName(
                Path.GetFullPath(projectFilePath))
                ?? throw new InvalidOperationException("无法解析工程目录。");
            assetRoot = _resources.ResolvePath(
                context, ProjectResourceKind.Asset);
            musicPath = _resources.ResolvePath(
                context, ProjectResourceKind.Music);
        }
        catch (Exception ex)
        {
            throw new ProjectLoadException(
                ProjectLoadStage.ResourcePaths,
                "工程资源路径检查失败。",
                ex,
                projectFilePath);
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new(ProjectLoadStage.Assets,
            "正在后台扫描素材库…", 5, ProjectLoadPipeline.TotalSteps));
        AssetBundle assets;
        try
        {
            assets = string.IsNullOrWhiteSpace(assetRoot)
                ? new AssetBundle()
                : await Task.Run(
                    () => AssetScanner.ScanResolvedProjectAssets(
                        projectDirectory, assetRoot),
                    cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ProjectLoadException(
                ProjectLoadStage.Assets,
                "素材库扫描失败。",
                ex,
                assetRoot);
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new(ProjectLoadStage.Audio,
            string.IsNullOrWhiteSpace(musicPath)
                ? "工程未配置音频。"
                : "音频路径检查完成。",
            6, ProjectLoadPipeline.TotalSteps));
        progress?.Report(new(ProjectLoadStage.EditorSurface,
            "工程数据准备完成，正在初始化编辑器控件…",
            ProjectLoadPipeline.DataPreparationComplete,
            ProjectLoadPipeline.TotalSteps));
        return new PreparedProjectSession(context, assets, musicPath);
    }
}
