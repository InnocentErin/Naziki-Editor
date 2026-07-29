using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Naziki_Editor.Core;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Messaging;
using Naziki_Editor.Core.Serialization;
using Naziki_Editor.Core.Storyboard.Canonical;
using Naziki_Editor.Models;
using Naziki_Editor.State;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Naziki_Editor.Tests;

public sealed class StoryboardImportCoordinatorTests
{
    [Fact]
    public async Task SuccessfulImportCommitsSourceRuntimeAndNepTogether()
    {
        using var directory = new TemporaryDirectory();
        var external = Path.Combine(directory.Path, "external.json");
        const string input = """
            {
              "templates": {
                "pulse": {
                  "states": [
                    {"time": 0, "opacity": 0},
                    {"time": 1, "opacity": 1}
                  ]
                }
              },
              "sprites": [{
                "path": "a.png",
                "time": [0, 2],
                "template": "pulse"
              }]
            }
            """;
        await File.WriteAllTextAsync(external, input);
        var context = CreateContext(directory.Path);
        var coordinator = CreateCoordinator();

        var result = await coordinator.ImportAndCommitAsync(
            context, external);

        Assert.Equal(input, await File.ReadAllTextAsync(external));
        Assert.True(File.Exists(result.StoryboardSourcePath));
        Assert.True(File.Exists(result.StoryboardRuntimePath));
        Assert.True(File.Exists(context.ProjectFilePath));
        var nep = JObject.Parse(
            await File.ReadAllTextAsync(context.ProjectFilePath));
        Assert.Equal(3, nep.Value<int>("format_version"));
        Assert.Equal(".naziki/storyboard.editor.json",
            nep.Value<string>("storyboard_source_path"));
        Assert.Equal("level/storyboard.json",
            nep.Value<string>("StoryboardExportPath"));
        Assert.Equal(result.RuntimeHash,
            nep.Value<string>("storyboard_export_hash"));
        Assert.False(string.IsNullOrWhiteSpace(
            nep.Value<string>("storyboard_source_hash")));
        var runtime = JObject.Parse(
            await File.ReadAllTextAsync(result.StoryboardRuntimePath));
        Assert.Null(runtime["templates"]);
        Assert.DoesNotContain(runtime.Descendants().OfType<JProperty>(),
            property => property.Name is "template" or "reset");
        Assert.All(runtime.Descendants().OfType<JProperty>()
                .Where(property => property.Name == "time"),
            property => Assert.NotEqual(JTokenType.Array,
                property.Value.Type));
        Assert.Equal(result.RuntimeHash,
            context.EditorStoryboard.Metadata.LastExportHash);
    }

    [Fact]
    public async Task InvalidImportKeepsDiskAndMemoryUnchanged()
    {
        using var directory = new TemporaryDirectory();
        var external = Path.Combine(directory.Path, "invalid.json");
        await File.WriteAllTextAsync(external,
            """{"sprites":{"not":"an array"}}""");
        var context = CreateContext(directory.Path);
        var oldDocument = context.EditorStoryboard;
        Directory.CreateDirectory(Path.Combine(directory.Path, "level"));
        var runtime = Path.Combine(directory.Path, "level",
            "storyboard.json");
        await File.WriteAllTextAsync(runtime, """{"sprites":[]}""");
        await File.WriteAllTextAsync(context.ProjectFilePath,
            JsonConvert.SerializeObject(context.ProjectData));
        var oldNep = await File.ReadAllTextAsync(
            context.ProjectFilePath);
        var oldRuntime = await File.ReadAllTextAsync(runtime);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            CreateCoordinator().ImportAndCommitAsync(
                context, external));

        Assert.Same(oldDocument, context.EditorStoryboard);
        Assert.Equal(oldNep, await File.ReadAllTextAsync(
            context.ProjectFilePath));
        Assert.Equal(oldRuntime,
            await File.ReadAllTextAsync(runtime));
        Assert.False(File.Exists(Path.Combine(directory.Path,
            ".naziki", "storyboard.editor.json")));
    }

    [Fact]
    public async Task RepeatedImportUsesSingleManagedRuntimePath()
    {
        using var directory = new TemporaryDirectory();
        var external = Path.Combine(directory.Path, "storyboard.json");
        await File.WriteAllTextAsync(external,
            """{"texts":[{"id":"a","time":0,"text":"first"}]}""");
        var context = CreateContext(directory.Path);
        var coordinator = CreateCoordinator();
        await coordinator.ImportAndCommitAsync(context, external);
        await File.WriteAllTextAsync(external,
            """{"texts":[{"id":"b","time":1,"text":"second"}]}""");

        await coordinator.ImportAndCommitAsync(context, external);

        Assert.Single(Directory.GetFiles(
            Path.Combine(directory.Path, "level"),
            "storyboard*.json"));
        Assert.Equal("b", JObject.Parse(File.ReadAllText(
                Path.Combine(directory.Path, "level",
                    "storyboard.json")))["texts"]?[0]?["id"]);
    }

    private static ProjectDataContext CreateContext(string directory)
    {
        var projectPath = Path.Combine(directory, "project.nep");
        return new ProjectDataContext(MessageBroker.Default)
        {
            ProjectFilePath = projectPath,
            ProjectData = new NazikiProjectModel
            {
                FormatVersion = 3,
                ProjectName = "test",
                StoryboardSourcePath =
                    ".naziki/storyboard.editor.json",
                StoryboardExportPath =
                    "level/storyboard.json"
            },
            EditorStoryboard = new EditorStoryboardDocument()
        };
    }

    private static StoryboardImportCoordinator CreateCoordinator()
    {
        var importer = new StoryboardImportService();
        var serializer = new EditorStoryboardSerializer();
        var store = new StoryboardSourceStore(serializer);
        var reader = new StoryboardDocumentReader(
            new StoryboardPropertyCatalogService());
        var writer = new StoryboardDocumentWriter();
        var exporter = new StoryboardRuntimeExporter(
            new StoryboardMaterializer(
                new StoryboardTimePositionResolver(),
                new NoteQueryService()));
        var bridge = new StoryboardCanonicalBridge(importer,
            exporter, reader, writer);
        return new StoryboardImportCoordinator(importer, exporter,
            store, serializer, reader, bridge,
            MessageBroker.Default);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "naziki-import-" + Guid.NewGuid().ToString("N"));
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
