using System.Text;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Messaging;
using Naziki_Editor.Features.Project.Resources;
using Naziki_Editor.Models;
using Naziki_Editor.State;
using Xunit;
using Newtonsoft.Json.Linq;

namespace Naziki_Editor.Tests;

public sealed class ProjectResourceServiceTests
{
    [Fact]
    public void ValidateSource_AllowsPlayablePageOverlapEffects()
    {
        var path = Path.Combine(Path.GetTempPath(),
            "naziki-overlap-chart-" + Guid.NewGuid().ToString("N") +
            ".json");
        File.WriteAllText(path, """
        {
          "time_base":480,
          "page_list":[
            {"start_tick":0,"end_tick":960,"scan_line_direction":1},
            {"start_tick":-960,"end_tick":1920,"scan_line_direction":-1}
          ],
          "tempo_list":[{"tick":0,"value":500000}],
          "event_order_list":[],
          "note_list":[]
        }
        """);

        try
        {
            var service = new ProjectResourceService(
                new FakeStoryboardReader(),
                new FakeStoryboardWriter(),
                MessageBroker.Default);

            service.ValidateSource(ProjectResourceKind.Chart, path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CreateProject_CopiesManagedResourcesAndUsesPortablePaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "naziki-project-resources-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source");
        var projectDirectory = Path.Combine(root, "project");
        Directory.CreateDirectory(source);
        var level = Path.Combine(source, "level.json");
        var chart = Path.Combine(source, "chart.json");
        var music = Path.Combine(source, "music.wav");
        var background = Path.Combine(source, "cover.png");
        var initialImage = Path.Combine(source, "initial.png");
        var initialVideo = Path.Combine(source, "intro.mp4");
        var storyboard = Path.Combine(source, "storyboard.json");
        await File.WriteAllTextAsync(level, ValidLevel);
        await File.WriteAllTextAsync(chart, ValidChart);
        await File.WriteAllTextAsync(storyboard, """
            {
              "templates": {
                "fade": {
                  "states": [
                    {"time": 0, "opacity": 0},
                    {"time": 1, "opacity": 1}
                  ]
                }
              },
              "texts": [{
                "text": "hello",
                "time": [0, 2],
                "template": "fade"
              }],
              "sprites": [{
                "id": "untimed",
                "path": "initial.png"
              }]
            }
            """);
        WriteSilentWav(music);
        await File.WriteAllBytesAsync(background, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl2nXsAAAAASUVORK5CYII="));
        await File.WriteAllBytesAsync(initialImage, [1, 2, 3]);
        await File.WriteAllBytesAsync(initialVideo, [4, 5, 6]);

        try
        {
            var service = new ProjectResourceService(new FakeStoryboardReader(), new FakeStoryboardWriter(), MessageBroker.Default);
            var projectFile = Path.Combine(projectDirectory, "portable.nep");
            var result = await service.CreateProjectAsync(new ProjectCreationRequest(
                projectFile, "Portable", level, chart, music,
                background, storyboard,
                [initialImage, initialImage.ToUpperInvariant(), initialVideo]));

            Assert.Equal("assets", result.Project.MaterialFolderPath);
            Assert.Equal("level/level.json", result.Project.LevelFilePath);
            Assert.Equal("level/chart.json", result.Project.ChartFilePath);
            Assert.Equal("music/music.wav", result.Project.AudioFilePath);
            Assert.Equal("assets/background/cover.png", result.Project.BackgroundPath);
            Assert.True(File.Exists(Path.Combine(projectDirectory, "assets", "initial.png")));
            Assert.True(File.Exists(Path.Combine(projectDirectory, "assets", "intro.mp4")));
            Assert.Single(Directory.GetFiles(
                Path.Combine(projectDirectory, "assets"), "initial*"));
            Assert.Equal("level/storyboard.json", result.Project.StoryboardExportPath);
            Assert.Equal(3, result.Project.FormatVersion);
            Assert.Equal(".naziki/storyboard.editor.json",
                result.Project.StoryboardSourcePath);
            var editorSourcePath = Path.Combine(projectDirectory,
                ".naziki", "storyboard.editor.json");
            Assert.True(File.Exists(editorSourcePath));
            Assert.Equal(1, JObject.Parse(
                await File.ReadAllTextAsync(editorSourcePath))
                .Value<int>("schema_version"));
            var runtime = JObject.Parse(await File.ReadAllTextAsync(
                Path.Combine(projectDirectory, "level",
                    "storyboard.json")));
            Assert.Null(runtime["templates"]);
            Assert.DoesNotContain(
                runtime.Descendants().OfType<JProperty>(),
                property => property.Name is "template" or "reset");
            Assert.All(runtime.Descendants().OfType<JProperty>()
                    .Where(property => property.Name == "time"),
                property => Assert.NotEqual(JTokenType.Array,
                    property.Value.Type));
            Assert.Equal(0,
                runtime["sprites"]?[0]?.Value<double>("time"));
            var editorSource = JObject.Parse(
                await File.ReadAllTextAsync(editorSourcePath));
            var untimedSourceEntity = Assert.Single(
                editorSource["entities"]!.Children<JObject>(),
                entity => entity.Value<string>("kind") == "Sprite");
            Assert.Equal("Explicit",
                untimedSourceEntity.Value<string>("activation_mode"));
            Assert.Equal(0, untimedSourceEntity["activation_time"]
                ?.Value<double>("seconds"));
            Assert.All(new[]
            {
                result.Project.ChartFilePath,
                result.Project.LevelFilePath,
                result.Project.AudioFilePath,
                result.Project.BackgroundPath,
                result.Project.StoryboardExportPath
            }, path => Assert.DoesNotContain('\\', path!));

            var context = new ProjectDataContext(MessageBroker.Default)
            {
                ProjectFilePath = result.ProjectFilePath,
                ProjectData = result.Project
            };
            var readiness = new ProjectReadinessService(service).Evaluate(context);
            Assert.True(readiness.CanPreview);
            Assert.True(File.Exists(service.ResolvePath(context, ProjectResourceKind.Music)));

            var second = await service.CreateProjectAsync(new ProjectCreationRequest(
                Path.Combine(projectDirectory, "portable-copy.nep"),
                "Portable Copy",
                level,
                chart,
                music,
                background));
            Assert.Equal("level/level_1.json", second.Project.LevelFilePath);
            Assert.Equal("level/chart_1.json", second.Project.ChartFilePath);
            Assert.Equal("music/music_1.wav", second.Project.AudioFilePath);
            Assert.Equal("assets/background/cover_1.png", second.Project.BackgroundPath);
            Assert.Equal("level/storyboard_1.json", second.Project.StoryboardExportPath);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task CreateProject_InvalidInitialAssetLeavesNoProjectFiles()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "naziki-project-assets-rollback-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source");
        Directory.CreateDirectory(source);
        var level = Path.Combine(source, "level.json");
        var chart = Path.Combine(source, "chart.json");
        var music = Path.Combine(source, "music.wav");
        var background = Path.Combine(source, "cover.png");
        var invalidAsset = Path.Combine(source, "unsupported.txt");
        await File.WriteAllTextAsync(level, ValidLevel);
        await File.WriteAllTextAsync(chart, ValidChart);
        WriteSilentWav(music);
        await File.WriteAllBytesAsync(background, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl2nXsAAAAASUVORK5CYII="));
        await File.WriteAllTextAsync(invalidAsset, "not an asset");
        var projectFile = Path.Combine(root, "project", "invalid.nep");

        try
        {
            var service = new ProjectResourceService(
                new FakeStoryboardReader(), new FakeStoryboardWriter(),
                MessageBroker.Default);
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                service.CreateProjectAsync(new ProjectCreationRequest(
                    projectFile, "Invalid", level, chart, music, background,
                    AssetSourcePaths: [invalidAsset])));

            Assert.False(File.Exists(projectFile));
            Assert.False(Directory.Exists(Path.Combine(root, "project")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ResolvePath_RejectsTraversalAndAbsolutePathsInV3()
    {
        var service = new ProjectResourceService(new FakeStoryboardReader(), new FakeStoryboardWriter(), MessageBroker.Default);
        var project = Path.Combine(Path.GetTempPath(), "project", "test.nep");
        Assert.Throws<InvalidDataException>(() => service.ResolvePath(project, "../outside.wav"));
        var absolute = Path.Combine(Path.GetTempPath(), "legacy.wav");
        Assert.Throws<InvalidDataException>(() =>
            service.ResolvePath(project, absolute));
    }

    [Fact]
    public void Readiness_UsesGranularCapabilityLocks()
    {
        var resources = new FakeMissingResources();
        var context = new ProjectDataContext(MessageBroker.Default)
        {
            ProjectFilePath = Path.Combine(Path.GetTempPath(), "test.nep"),
            ProjectData = new NazikiProjectModel
            {
                LevelFilePath = "level.json",
                ChartFilePath = "chart.json",
                StoryboardExportPath = "storyboard.json",
                AudioFilePath = "music.wav",
                BackgroundPath = "cover.png"
            }
        };
        var state = new ProjectReadinessService(resources).Evaluate(context);
        Assert.False(state.CanPlay);
        Assert.False(state.CanPreview);
        Assert.True(state.HasChart);
        Assert.True(state.HasStoryboard);
        Assert.False(state.HasMusic);
        Assert.False(state.HasBackground);
    }

    [Fact]
    public void Readiness_WithoutProjectLocksEveryCapability()
    {
        var state = new ProjectReadinessService(new FakeMissingResources())
            .Evaluate(new ProjectDataContext(MessageBroker.Default));

        Assert.False(state.HasProject);
        Assert.False(state.CanUseChartFeatures);
        Assert.False(state.CanPlay);
        Assert.False(state.CanPreview);
        Assert.False(state.CanExportStoryboard);
    }

    private const string ValidChart = """
        {
          "format_version": 1,
          "time_base": 480,
          "page_list": [{"start_tick":0,"end_tick":480,"scan_line_direction":1}],
          "tempo_list": [{"tick":0,"value":500000}],
          "note_list": [],
          "event_order_list": []
        }
        """;

    private const string ValidLevel = """
        {
          "schema_version": 2,
          "version": 1,
          "id": "test.level",
          "title": "Test Level",
          "artist": "Test Artist",
          "music": { "path": "music.wav" },
          "background": { "path": "cover.png" },
          "charts": [
            { "type": "hard", "difficulty": 10, "path": "chart.json" }
          ]
        }
        """;

    private static void WriteSilentWav(string path)
    {
        const int sampleRate = 8000;
        const short channels = 1;
        const short bits = 16;
        var data = new byte[sampleRate / 10 * channels * bits / 8];
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + data.Length);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bits / 8);
        writer.Write((short)(channels * bits / 8));
        writer.Write(bits);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(data.Length);
        writer.Write(data);
    }

    private sealed class FakeStoryboardReader : IStoryboardDocumentReader
    {
        public StoryboardRoot Read(string json) => new();
        public IStoryboardEntity ReadEntity(string json, Type entityType) =>
            throw new NotSupportedException();
    }

    private sealed class FakeStoryboardWriter : IStoryboardDocumentWriter
    {
        public string Write(StoryboardRoot document) => "{}";
        public string WriteNode(object node) => "{}";
    }

    private sealed class FakeMissingResources : IProjectResourceService
    {
        public string ResolvePath(string projectFilePath, string configuredPath) => configuredPath;
        public string? ResolvePath(ProjectDataContext context, ProjectResourceKind kind) =>
            kind is ProjectResourceKind.Level or ProjectResourceKind.Chart or ProjectResourceKind.Storyboard
                ? typeof(ProjectResourceServiceTests).Assembly.Location
                : Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public string ToProjectRelativePath(string projectFilePath, string absolutePath) => absolutePath;
        public void ValidateSource(ProjectResourceKind kind, string sourcePath) { }
        public Task<ProjectCreationResult> CreateProjectAsync(ProjectCreationRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<string> ImportAsync(ProjectDataContext context, ProjectResourceKind kind, string sourcePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<string> EnsureStoryboardAsync(ProjectDataContext context, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
