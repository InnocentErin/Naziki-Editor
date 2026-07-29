using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Charting;
using Naziki_Editor.Features.EditorShell;
using Naziki_Editor.Features.Project.Loading;
using Naziki_Editor.Features.Project.Resources;
using Naziki_Editor.Models;
using Naziki_Editor.State;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Naziki_Editor.Tests;

public sealed class ProjectOpenPreparationServiceTests
{
    [Fact]
    public async Task PrepareAsync_LoadsDataOffCallerThreadAndReportsStages()
    {
        var project = new FakeProjectService();
        var resources = new FakeProjectResourceService();
        var service = new ProjectOpenPreparationService(project, resources);
        var progress = new RecordingProgress();
        var result = await service.PrepareAsync(
            @"C:\projects\sample\sample.nep",
            progress,
            CancellationToken.None);

        Assert.True(project.LoadTaskId.HasValue);
        Assert.Same(project.Context, result.Context);
        Assert.Contains(progress.Values,
            item => item.Stage == ProjectLoadStage.ProjectConfiguration);
        Assert.Contains(progress.Values,
            item => item.Stage == ProjectLoadStage.Assets);
        Assert.Contains(progress.Values,
            item => item.Stage == ProjectLoadStage.EditorSurface);
        Assert.Equal(
            ProjectLoadPipeline.DataPreparationComplete,
            progress.Values[^1].CompletedSteps);
        Assert.Equal(
            ProjectLoadPipeline.TotalSteps,
            progress.Values[^1].TotalSteps);
        Assert.True(progress.Values
            .Zip(progress.Values.Skip(1),
                (previous, current) =>
                    previous.Percentage <= current.Percentage)
            .All(isMonotonic => isMonotonic));
    }

    [Fact]
    public async Task PrepareAsync_PreCanceled_DoesNotReadProject()
    {
        var project = new FakeProjectService();
        var service = new ProjectOpenPreparationService(
            project, new FakeProjectResourceService());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.PrepareAsync(
                @"C:\projects\sample\sample.nep",
                null,
                cancellation.Token));

        Assert.Equal(0, project.LoadThreadId);
    }

    private sealed class RecordingProgress : IProgress<ProjectLoadProgress>
    {
        public List<ProjectLoadProgress> Values { get; } = [];
        public void Report(ProjectLoadProgress value) => Values.Add(value);
    }

    private sealed class FakeProjectService : IProjectService
    {
        public int LoadThreadId { get; private set; }
        public int? LoadTaskId { get; private set; }
        public ProjectDataContext Context { get; } = new(null!)
        {
            ProjectFilePath = @"C:\projects\sample\sample.nep",
            ProjectData = new NazikiProjectModel
            {
                ProjectName = "Sample",
                FormatVersion = 3
            }
        };

        public ProjectDataContext? LoadProjectData(string filePath)
        {
            LoadThreadId = Environment.CurrentManagedThreadId;
            LoadTaskId = Task.CurrentId;
            return Context;
        }

        public Task<ProjectDataContext?> LoadProjectAsync(string filePath) =>
            Task.FromResult<ProjectDataContext?>(LoadProjectData(filePath));
        public Task SaveProjectAsync(ProjectDataContext context, string filePath) =>
            Task.CompletedTask;
        public Task ExportCytoidStoryboardAsync(
            StoryboardRoot storyboard, string outputPath,
            ProjectDataContext? context = null) => Task.CompletedTask;
        public Task ExportCytoidStoryboardJsonAsync(
            string runtimeJson, string outputPath) => Task.CompletedTask;
        public Task SaveStoryboardMetaAsync(
            ProjectDataContext context, string storyboardPath) => Task.CompletedTask;
        public Task SaveProjectNepFileAsync(
            ProjectDataContext context, string? filePath = null) => Task.CompletedTask;
        public void SaveProjectNepFile(
            ProjectDataContext context, string? filePath = null) { }
        public string SaveAssetCapsule(
            ProjectDataContext context, IStoryboardEntity entity,
            string materialType) => string.Empty;
        public StoryboardRoot LoadStoryboard(string filePath) => new();
        public StoryboardMeta LoadStoryboardMeta(string storyboardPath) => new();
        public (StoryboardRoot Storyboard, StoryboardMeta Meta) ImportStoryboard(
            string storyboardPath, NazikiProjectModel? projectData) => (new(), new());
        public (StoryboardRoot? Storyboard, StoryboardMeta Meta) LoadProjectStoryboard(
            string storyboardPath, NazikiProjectModel projectData) => (new(), new());
        public C2Chart? SilentImportChart(string chartPath) => null;
        public ChartDocument LoadChartDocument(string chartPath) =>
            throw new NotSupportedException();
    }

    private sealed class FakeProjectResourceService : IProjectResourceService
    {
        public string ResolvePath(
            string projectFilePath, string configuredPath) => configuredPath;
        public string? ResolvePath(
            ProjectDataContext context, ProjectResourceKind kind) => null;
        public string ToProjectRelativePath(
            string projectFilePath, string absolutePath) => absolutePath;
        public void ValidateSource(ProjectResourceKind kind, string sourcePath) { }
        public Task<ProjectCreationResult> CreateProjectAsync(
            ProjectCreationRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<string> ImportAsync(
            ProjectDataContext context, ProjectResourceKind kind,
            string sourcePath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<string> EnsureStoryboardAsync(
            ProjectDataContext context,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
