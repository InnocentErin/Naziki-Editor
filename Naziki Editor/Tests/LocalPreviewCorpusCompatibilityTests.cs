using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Naziki_Editor.Core;
using Naziki_Editor.Core.Charting;
using Naziki_Editor.Core.Storyboard.Canonical;
using Naziki_Editor.Features.Project.Resources;
using Naziki_Editor.Features.Preview;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Naziki_Editor.Tests;

/// <summary>
/// Opportunistic local-corpus scan. The repository does not ship the licensed
/// media under .test, so CI simply skips the body when that folder is absent.
/// </summary>
public sealed class LocalPreviewCorpusCompatibilityTests
{
    [Fact]
    public async Task LocalCorpus_AllProjectsProduceBundledUnitySnapshots()
    {
        var workspace = FindWorkspaceWithTestCorpus();
        if (workspace is null)
            return;

        var corpusRoot = Path.Combine(workspace, ".test");
        var requestedCache = Environment.GetEnvironmentVariable(
            "NAZIKI_PREVIEW_CORPUS_CACHE");
        var preserveCache = !string.IsNullOrWhiteSpace(requestedCache);
        var cache = preserveCache
            ? Path.GetFullPath(requestedCache!)
            : Path.Combine(Path.GetTempPath(),
                "naziki-local-corpus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cache);
        try
        {
            var codec = new ChartJsonCodec();
            var serializer = new EditorStoryboardSerializer();
            var exporter = new StoryboardRuntimeExporter(new StoryboardMaterializer(
                new StoryboardTimePositionResolver(), new NoteQueryService()));
            var wire = new ChartPreviewWireAdapter(codec);
            var materializer = new PreviewVfsMaterializer(cache);
            foreach (var expected in new[] { "1-vampire", "2-8bit", "3-cmc" })
            {
                var directory = Path.Combine(corpusRoot, expected);
                var projectPath = Assert.Single(Directory.EnumerateFiles(
                    directory, "*.nep", SearchOption.TopDirectoryOnly));
                var project = JObject.Parse(await File.ReadAllTextAsync(projectPath));
                string Resolve(string property) => Path.GetFullPath(Path.Combine(
                    directory,
                    project.Value<string>(property)
                        ?? throw new InvalidDataException(
                            $"{expected}: project is missing {property}.")));

                var chartPath = Resolve("ChartFilePath");
                var decoded = codec.Decode(await File.ReadAllTextAsync(chartPath),
                    ChartRuntimeProfile.BundledUnity);
                Assert.True(decoded.Success,
                    $"{expected}: {string.Join(Environment.NewLine, decoded.Diagnostics.Select(item => $"{item.Code} {item.Path}: {item.Message}"))}");
                var chart = decoded.Document!.Projection;
                var sourcePath = Resolve("storyboard_source_path");
                var document = serializer.Deserialize(await File.ReadAllTextAsync(sourcePath));
                var runtime = exporter.Export(document, chart,
                    new ChartTimeEngine(chart.tempo_list, chart.time_base));
                Assert.True(runtime.Success,
                    $"{expected}: {string.Join(Environment.NewLine, runtime.Issues.Select(item => $"{item.Code} {item.Path}: {item.Message}"))}");
                Assert.All(runtime.Json["note_controllers"] as JArray ?? new JArray(), controller =>
                    Assert.NotNull(controller!["note"]));

                var chartJson = wire.Serialize(chart, decoded.Document);
                Assert.Empty(wire.Validate(chartJson));
                if (expected == "3-cmc")
                {
                    Assert.Contains(decoded.Diagnostics,
                        item => item.Code == "CHART_PAGE_NEGATIVE_START");
                    Assert.Contains(decoded.Diagnostics,
                        item => item.Code == "CHART_PAGE_OVERLAP");
                }

                var levelPath = Resolve("LevelFilePath");
                var chartDifficulty = project.Value<string>("chart_difficulty");
                if (string.IsNullOrWhiteSpace(chartDifficulty))
                    chartDifficulty = CytoidLevelChartBinding.Resolve(levelPath, chartPath);
                var snapshot = new StoryboardPreviewSnapshot(
                    "corpus-" + expected,
                    1,
                    projectPath,
                    runtime.Json.ToString(Newtonsoft.Json.Formatting.None),
                    chartJson,
                    Resolve("MaterialFolderPath"),
                    0)
                {
                    LevelJson = await File.ReadAllTextAsync(levelPath),
                    MusicPath = Resolve("AudioFilePath"),
                    BackgroundPath = Resolve("BackgroundPath"),
                    ChartDifficulty = chartDifficulty,
                    ProjectName = expected
                };
                var vfs = await materializer.MaterializeAsync(snapshot);
                Assert.True(vfs.StoryboardEnabled,
                    $"{expected}: {string.Join(Environment.NewLine, vfs.Diagnostics.Select(item => item.Message))}");
                Assert.True(File.Exists(vfs.LevelPath));
                Assert.True(File.Exists(vfs.ChartPath));
                Assert.True(File.Exists(vfs.StoryboardPath));
            }
        }
        finally
        {
            if (!preserveCache)
                Directory.Delete(cache, true);
        }
    }

    private static string? FindWorkspaceWithTestCorpus()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".test")))
                return current.FullName;
            current = current.Parent;
        }
        return null;
    }
}
