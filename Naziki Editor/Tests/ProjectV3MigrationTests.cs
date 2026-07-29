using System;
using System.IO;
using System.Linq;
using Naziki_Editor.Core;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.ErrorHandling;
using Naziki_Editor.Core.Messaging;
using Naziki_Editor.Core.Project;
using Naziki_Editor.Core.Serialization;
using Naziki_Editor.Core.Storyboard.Canonical;
using Naziki_Editor.Core.Storyboard.Corrections;
using Naziki_Editor.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Naziki_Editor.Tests;

public sealed class ProjectV3MigrationTests
{
    [Theory]
    [InlineData("""{"ProjectName":"missing"}""")]
    [InlineData("""{"format_version":2,"ProjectName":"v2"}""")]
    [InlineData("""{"format_version":4,"ProjectName":"future"}""")]
    public void LoadRejectsProjectsWithoutExplicitV3(string nep)
    {
        using var directory = new TemporaryDirectory();
        var projectPath = Path.Combine(directory.Path, "invalid.nep");
        File.WriteAllText(projectPath, nep);

        var exception = Assert.Throws<JsonSerializationException>(() =>
            CreateService().LoadProjectData(projectPath));

        Assert.Contains("format_version", exception.Message);
        Assert.False(Directory.Exists(
            Path.Combine(directory.Path, ".naziki")));
    }

    [Fact]
    public void MissingCanonicalSourceIsRebuiltFromRuntime()
    {
        using var directory = new TemporaryDirectory();
        var projectPath = CreateV3Project(directory.Path,
            """{"sprites":[{"id":"sprite","time":0,"path":"a.png"}]}""");

        var context = CreateService().LoadProjectData(projectPath)!;

        var sourcePath = Path.Combine(directory.Path, ".naziki",
            "storyboard.editor.json");
        Assert.True(File.Exists(sourcePath));
        Assert.Single(context.EditorStoryboard.Entities);
        Assert.True(context.ProjectData
            .StoryboardSourceRecoveredDuringLoad);
        Assert.Equal(".naziki/storyboard.editor.json",
            JObject.Parse(File.ReadAllText(projectPath))
                .Value<string>("storyboard_source_path"));
        Assert.Equal("sprite",
            context.EditorStoryboard.Entities[0].RuntimeId?.Literal);
    }

    [Fact]
    public void CorruptCanonicalSourceIsBackedUpAndRebuilt()
    {
        using var directory = new TemporaryDirectory();
        var projectPath = CreateV3Project(directory.Path,
            """{"texts":[{"id":"text","time":0,"text":"ok"}]}""");
        var sourcePath = Path.Combine(directory.Path, ".naziki",
            "storyboard.editor.json");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        File.WriteAllText(sourcePath, "{not valid");

        var context = CreateService().LoadProjectData(projectPath)!;

        Assert.Single(context.EditorStoryboard.Entities);
        Assert.Single(Directory.GetFiles(Path.Combine(directory.Path,
            ".naziki", "recovery"),
            "storyboard.editor.corrupt.*.json"));
        Assert.Equal(1, JObject.Parse(File.ReadAllText(sourcePath))
            .Value<int>("schema_version"));
    }

    [Fact]
    public void MissingSourceAndRuntimeRejectsV3WithoutCreatingData()
    {
        using var directory = new TemporaryDirectory();
        var projectPath = Path.Combine(directory.Path, "broken.nep");
        File.WriteAllText(projectPath,
            JsonConvert.SerializeObject(new NazikiProjectModel
            {
                FormatVersion = 3,
                StoryboardExportPath = "level/storyboard.json",
                StoryboardSourcePath =
                    ".naziki/storyboard.editor.json"
            }));

        Assert.Throws<InvalidDataException>(() =>
            CreateService().LoadProjectData(projectPath));
        Assert.False(File.Exists(Path.Combine(directory.Path,
            ".naziki", "storyboard.editor.json")));
    }

    private static string CreateV3Project(
        string directory, string runtimeJson)
    {
        var level = Path.Combine(directory, "level");
        Directory.CreateDirectory(level);
        File.WriteAllText(Path.Combine(level, "storyboard.json"),
            runtimeJson);
        var projectPath = Path.Combine(directory, "project.nep");
        File.WriteAllText(projectPath,
            JsonConvert.SerializeObject(new NazikiProjectModel
            {
                FormatVersion = 3,
                StoryboardExportPath = "level/storyboard.json",
                StoryboardSourcePath =
                    ".naziki/storyboard.editor.json"
            }, Formatting.Indented));
        return projectPath;
    }

    private static ProjectService CreateService()
    {
        var messages = MessageBroker.Default;
        var errors = new ErrorHandler(messages);
        var catalog = new StoryboardPropertyCatalogService();
        var reader = new StoryboardDocumentReader(catalog);
        var writer = new StoryboardDocumentWriter();
        var analyzer = new StoryboardCorrectionAnalyzer(
            new StoryboardTimeResolver(), writer);
        var validator = new StoryboardDocumentValidator(analyzer,
            new Naziki_Editor.Core.Compilation
                .StoryboardTemplatePropertyMapper());
        var importer = new StoryboardImportService();
        var serializer = new EditorStoryboardSerializer();
        var sourceStore = new StoryboardSourceStore(serializer);
        var materializer = new StoryboardMaterializer(
            new StoryboardTimePositionResolver(),
            new NoteQueryService());
        var exporter = new StoryboardRuntimeExporter(materializer);
        var bridge = new StoryboardCanonicalBridge(importer, exporter,
            reader, writer);
        var coordinator = new StoryboardImportCoordinator(
            importer, exporter, sourceStore, serializer,
            reader, bridge, messages);
        return new ProjectService(messages, errors,
            new StoryboardParser(errors), reader, writer, validator,
            sourceStore, bridge, importer, coordinator,
            new Naziki_Editor.Core.Charting.ChartJsonCodec());
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "naziki-v3-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, true);
        }
    }
}
