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
    [Fact]
    public void V2OpenIsReadOnlyUntilExplicitSaveThenCreatesV3SourceAndBackup()
    {
        var directory = Path.Combine(Path.GetTempPath(),
            "naziki-v3-migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var projectPath = Path.Combine(directory, "legacy.nep");
            var storyboardPath = Path.Combine(directory, "storyboard.json");
            File.WriteAllText(storyboardPath, """
            {
              "sprites": [{
                "id": "sprite",
                "path": "a.png",
                "states": [{"time": 1, "opacity": 1}]
              }]
            }
            """);
            var project = new NazikiProjectModel
            {
                FormatVersion = 2,
                ProjectName = "legacy",
                StoryboardExportPath = "storyboard.json"
            };
            File.WriteAllText(projectPath,
                JsonConvert.SerializeObject(project, Formatting.Indented));

            var service = CreateService();
            var context = service.LoadProjectData(projectPath)!;

            Assert.True(context.IsLegacyProjectMigrationPending);
            Assert.False(Directory.Exists(
                Path.Combine(directory, ".naziki")));

            var loaded = service.LoadProjectStoryboard(
                storyboardPath, context.ProjectData);
#pragma warning disable CS0618
            context.Storyboard = loaded.Storyboard!;
#pragma warning restore CS0618
            context.StoryboardMeta = loaded.Meta;
            context.StoryboardPath = storyboardPath;
            service.SaveProjectNepFile(context, projectPath);

            var savedProject = JObject.Parse(File.ReadAllText(projectPath));
            Assert.Equal(3, savedProject.Value<int>("format_version"));
            Assert.Equal(".naziki/storyboard.editor.json",
                savedProject.Value<string>("storyboard_source_path"));
            var sourcePath = Path.Combine(directory, ".naziki",
                "storyboard.editor.json");
            Assert.True(File.Exists(sourcePath));
            Assert.Equal(1,
                JObject.Parse(File.ReadAllText(sourcePath))
                    .Value<int>("schema_version"));
            Assert.Single(Directory.GetFiles(Path.Combine(directory,
                ".naziki", "migrations"), "storyboard.v2.*.json"));
            Assert.False(context.IsLegacyProjectMigrationPending);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void FailedV3SourceWriteKeepsNepV2AndRemovesPartialSource()
    {
        var directory = Path.Combine(Path.GetTempPath(),
            "naziki-v3-rollback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var projectPath = Path.Combine(directory, "legacy.nep");
            var storyboardPath = Path.Combine(directory, "storyboard.json");
            File.WriteAllText(storyboardPath,
                """{"sprites":[{"id":"a","time":0}]}""");
            var originalNep = JsonConvert.SerializeObject(
                new NazikiProjectModel
                {
                    FormatVersion = 2,
                    StoryboardExportPath = "storyboard.json"
                }, Formatting.Indented);
            File.WriteAllText(projectPath, originalNep);
            var throwingStore = new ThrowAfterPartialWriteStore();
            var service = CreateService(throwingStore);
            var context = service.LoadProjectData(projectPath)!;
            var loaded = service.LoadProjectStoryboard(storyboardPath,
                context.ProjectData);
#pragma warning disable CS0618
            context.Storyboard = loaded.Storyboard!;
#pragma warning restore CS0618
            context.StoryboardPath = storyboardPath;

            Assert.Throws<IOException>(() =>
                service.SaveProjectNepFile(context, projectPath));

            Assert.True(JToken.DeepEquals(JToken.Parse(originalNep),
                JToken.Parse(File.ReadAllText(projectPath))));
            Assert.False(File.Exists(Path.Combine(directory, ".naziki",
                "storyboard.editor.json")));
            Assert.True(context.IsLegacyProjectMigrationPending);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    private static ProjectService CreateService(
        IStoryboardSourceStore? sourceStoreOverride = null)
    {
        var messages = MessageBroker.Default;
        var errors = new ErrorHandler(messages);
        var catalog = new StoryboardPropertyCatalogService();
        var reader = new StoryboardDocumentReader(catalog);
        var writer = new StoryboardDocumentWriter();
        var analyzer = new StoryboardCorrectionAnalyzer(
            new StoryboardTimeResolver(), writer);
        var validator = new StoryboardDocumentValidator(analyzer,
            new Naziki_Editor.Core.Compilation.StoryboardTemplatePropertyMapper());
        var importer = new StoryboardImportService();
        var serializer = new EditorStoryboardSerializer();
        var sourceStore = sourceStoreOverride ??
                          new StoryboardSourceStore(serializer);
        var materializer = new StoryboardMaterializer(
            new StoryboardTimePositionResolver(), new NoteQueryService());
        var exporter = new StoryboardRuntimeExporter(materializer);
        var bridge = new StoryboardCanonicalBridge(importer, exporter,
            reader, writer);
        return new ProjectService(messages, errors,
            new StoryboardParser(errors), reader, writer, validator,
            sourceStore, bridge, importer);
    }

    private sealed class ThrowAfterPartialWriteStore :
        IStoryboardSourceStore
    {
        public string GetDefaultSourcePath(string projectFilePath) =>
            Path.Combine(Path.GetDirectoryName(projectFilePath)!,
                ".naziki", "storyboard.editor.json");

        public EditorStoryboardDocument Load(string sourcePath) =>
            throw new NotSupportedException();

        public void Save(string sourcePath,
            EditorStoryboardDocument document)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            File.WriteAllText(sourcePath, "partial");
            throw new IOException("simulated source write failure");
        }
    }
}
