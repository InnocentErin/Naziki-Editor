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

    [Fact]
    public async Task UntimedEntitiesPersistAndExportAtAbsoluteZero()
    {
        using var directory = new TemporaryDirectory();
        var external = Path.Combine(directory.Path, "untimed.json");
        await File.WriteAllTextAsync(external, """
            {
              "sprites": [{"id":"image","path":"image.png"}],
              "videos": [{"id":"video","path":"video.mp4"}],
              "texts": [{"id":"text","text":"hello"}]
            }
            """);
        var context = CreateContext(directory.Path);

        var result = await CreateCoordinator().ImportAndCommitAsync(
            context, external);

        Assert.All(context.EditorStoryboard.Entities, entity =>
        {
            Assert.Equal(StoryboardActivationMode.Explicit,
                entity.ActivationMode);
            Assert.Equal(0, entity.ActivationTime!.Seconds);
        });
        var source = JObject.Parse(await File.ReadAllTextAsync(
            result.StoryboardSourcePath));
        Assert.All(source["entities"]!.Children<JObject>(), entity =>
        {
            Assert.Equal("Explicit",
                entity.Value<string>("activation_mode"));
            Assert.Equal(0,
                entity["activation_time"]?.Value<double>("seconds"));
        });
        var runtime = JObject.Parse(await File.ReadAllTextAsync(
            result.StoryboardRuntimePath));
        Assert.All(new[] { "sprites", "videos", "texts" }, collection =>
        {
            var entity = Assert.Single(
                runtime[collection]!.Children<JObject>());
            Assert.Equal(0, entity.Value<double>("time"));
        });
    }

    [Fact]
    public void Prepare_PreservesExplicitWorldUnitForLineWidth()
    {
        const string input = """
        {
          "lines": [{
            "id": "line",
            "time": 0,
            "pos": [{
              "x": { "Value": 0, "Unit": 1 },
              "y": { "Value": 0, "Unit": 2 },
              "z": { "Value": 0, "Unit": 0 }
            }],
            "width": {
              "Value": 0.03,
              "Unit": 0,
              "ScaleToCanvas": false,
              "Span": true
            },
            "states": [{
              "relative_time": 1,
              "width": {
                "Value": 0.05,
                "Unit": 0,
                "ScaleToCanvas": false,
                "Span": true
              }
            }]
          }]
        }
        """;

        var candidate = CreateCoordinator().Prepare(input);

        var runtimeLine = Assert.Single(
            candidate.RuntimeJson["lines"]!.Children<JObject>());
        Assert.Equal("world:0.03",
            runtimeLine.Value<string>("width"));
        Assert.Equal("world:0.05",
            runtimeLine["states"]![0]!.Value<string>("width"));
        var legacyLine = Assert.Single(
            candidate.LegacyProjection.lines);
        Assert.Equal(0.03f, legacyLine.BaseState.Width!.Value);
        Assert.True(legacyLine.BaseState.Width.HasExplicitUnit);
        Assert.Equal(ReferenceUnit.World,
            legacyLine.BaseState.Width.Unit);
        Assert.Equal(0.05f,
            legacyLine.Keyframes[0].Width!.Value);
    }

    [Fact]
    public void Prepare_NormalizesExternalPlayerColorObjects()
    {
        const string input = """
        {
          "note_controllers": [{
            "id": "note-color",
            "note": 1,
            "time": 0,
            "override_fill_color": true,
            "fill_color": {
              "R": 0.917647064,
              "G": 0,
              "B": 0.215686277,
              "A": 1
            }
          }]
        }
        """;

        var candidate = CreateCoordinator().Prepare(input);

        var runtime = Assert.Single(candidate.RuntimeJson[
            "note_controllers"]!.Children<JObject>());
        Assert.Equal("#EA0037",
            runtime.Value<string>("fill_color"));
        var legacy = Assert.Single(
            candidate.LegacyProjection.note_controllers);
        Assert.Equal("#EA0037", legacy.BaseState.FillColor);
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
