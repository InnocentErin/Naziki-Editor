using System;
using System.Linq;
using Naziki_Editor.Core.Storyboard.Canonical;
using Naziki_Editor.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Naziki_Editor.Tests;

public sealed class CompiledStoryboardCompatibilityTests
{
    private readonly StoryboardImportService _importer = new();
    private readonly EditorStoryboardSerializer _serializer = new();

    [Fact]
    public void CompiledImport_PromotesInitialNoteControllerStateToRuntimeRoot()
    {
        var chart = Chart(43);
        var imported = _importer.Import("""
        {
          "compiled": true,
          "note_controllers": [{
            "id": "note-42",
            "states": [
              {"time": 3.5, "note": 42, "opacity": 0.25, "path": "marker.png"},
              {"time": 4.0, "opacity": 1.0}
            ]
          }]
        }
        """, chart);

        Assert.True(imported.CanReplace, string.Join(Environment.NewLine,
            imported.Issues.Select(issue => $"{issue.Code}: {issue.Message}")));
        var entity = Assert.Single(imported.Document!.Entities);
        Assert.Equal(EditorStoryboardEntityKind.NoteController, entity.Kind);
        Assert.Equal(42, entity.NoteBinding!.NoteId);
        Assert.Equal(3.5, entity.ActivationTime!.Seconds);
        Assert.Equal(0.25, entity.BasePatch.Value<double>("opacity"));
        Assert.Equal("marker.png", entity.BasePatch.Value<string>("path"));
        Assert.Single(entity.Frames);
        Assert.Null(imported.Document.RootProperties["compiled"]);

        var exported = CreateExporter().Export(imported.Document, chart, null);

        Assert.True(exported.Success, string.Join(Environment.NewLine,
            exported.Issues.Select(issue => $"{issue.Code}: {issue.Message}")));
        var controller = (JObject)exported.Json["note_controllers"]![0]!;
        Assert.Equal(42, controller.Value<int>("note"));
        Assert.Equal(3.5, controller.Value<double>("time"));
        Assert.Equal(0.25, controller.Value<double>("opacity"));
        var remaining = Assert.Single((JArray)controller["states"]!);
        Assert.Equal(4.0, remaining.Value<double>("time"));
        Assert.Null(exported.Json["compiled"]);
    }

    [Fact]
    public void CompiledImport_PreservesAllBindingsAcrossLargeControllerBatch()
    {
        const int controllerCount = 212;
        var controllers = new JArray();
        for (var noteId = 0; noteId < controllerCount; noteId++)
        {
            controllers.Add(new JObject
            {
                ["id"] = $"note-{noteId}",
                ["states"] = new JArray
                {
                    new JObject
                    {
                        ["time"] = noteId / 10d,
                        ["note"] = noteId,
                        ["opacity"] = 1d
                    },
                    new JObject
                    {
                        ["time"] = noteId / 10d + 0.5,
                        ["opacity"] = 0d
                    }
                }
            });
        }
        var source = new JObject
        {
            ["compiled"] = true,
            ["note_controllers"] = controllers
        };
        var chart = Chart(controllerCount);

        var imported = _importer.Import(source.ToString(Formatting.None), chart);
        var exported = CreateExporter().Export(imported.Document!, chart, null);

        Assert.True(imported.CanReplace);
        Assert.True(exported.Success, string.Join(Environment.NewLine,
            exported.Issues.Select(issue => $"{issue.Code}: {issue.Message}")));
        var runtimeControllers = (JArray)exported.Json["note_controllers"]!;
        Assert.Equal(controllerCount, runtimeControllers.Count);
        Assert.Equal(Enumerable.Range(0, controllerCount),
            runtimeControllers.Values<int>("note"));
    }

    [Fact]
    public void CanonicalCompiledMigration_IsIdempotentAndRepairsInheritance()
    {
        var firstFrame = new EditorStoryboardFrame
        {
            FrameId = "first",
            Sequence = 0,
            Time = StoryboardTimePosition.Absolute(2),
            Patch = new JObject { ["opacity"] = 0.4 },
            Easing = "easeInSine",
            Destroy = false,
            Reset = true,
            Template = new EditorTemplateBinding
            {
                TemplateName = "initial-pulse",
                Overrides = new JObject { ["scale"] = 1.25 }
            },
            NoteBinding = new EditorNoteBinding { NoteId = 7 }
        };
        var document = new EditorStoryboardDocument
        {
            DocumentId = "compiled-document",
            RootProperties = new JObject { ["compiled"] = true },
            Entities =
            [
                new EditorStoryboardEntity
                {
                    EditorId = "controller",
                    SourceGroupId = "controller",
                    Kind = EditorStoryboardEntityKind.NoteController,
                    RuntimeId = EditorInterpolatedString.FromWire("controller"),
                    ActivationMode = StoryboardActivationMode.FirstFrame,
                    BasePatch = new JObject { ["legacy"] = true },
                    Source = new EditorSourceInfo { Path = "$.note_controllers[0]" },
                    Frames =
                    [
                        firstFrame,
                        new EditorStoryboardFrame
                        {
                            FrameId = "second",
                            Sequence = 1,
                            Time = StoryboardTimePosition.Absolute(3),
                            Patch = new JObject { ["opacity"] = 1d },
                            InheritFromFrameId = firstFrame.FrameId
                        }
                    ]
                }
            ]
        };

        var migrated = _serializer.Deserialize(_serializer.Serialize(document));
        var migratedAgain = _serializer.Deserialize(_serializer.Serialize(migrated));

        var entity = Assert.Single(migrated.Entities);
        Assert.Null(migrated.RootProperties["compiled"]);
        Assert.Equal(7, entity.NoteBinding!.NoteId);
        Assert.Equal(2, entity.ActivationTime!.Seconds);
        Assert.Equal(0.4, entity.BasePatch.Value<double>("opacity"));
        Assert.Null(entity.BasePatch["legacy"]);
        Assert.Equal("easeInSine", entity.RootEasing);
        Assert.False(entity.RootDestroy);
        Assert.Equal("initial-pulse", entity.RootTemplate!.TemplateName);
        Assert.Equal(1.25,
            entity.RootTemplate.Overrides.Value<double>("scale"));
        var remaining = Assert.Single(entity.Frames);
        Assert.Null(remaining.InheritFromFrameId);
        Assert.Single(migrated.Metadata.ImportDiagnostics, item =>
            item.Code == "COMPILED_CANONICAL_MIGRATED");
        Assert.True(JToken.DeepEquals(
            JToken.Parse(_serializer.Serialize(migrated)),
            JToken.Parse(_serializer.Serialize(migratedAgain))));
    }

    [Fact]
    public void RuntimeExport_BlocksNoteControllerWithoutRootBinding()
    {
        var document = new EditorStoryboardDocument
        {
            Entities =
            [
                new EditorStoryboardEntity
                {
                    EditorId = "missing-note",
                    SourceGroupId = "missing-note",
                    Kind = EditorStoryboardEntityKind.NoteController,
                    RuntimeId = EditorInterpolatedString.FromWire("missing-note"),
                    ActivationMode = StoryboardActivationMode.Explicit,
                    ActivationTime = StoryboardTimePosition.Absolute(0),
                    Source = new EditorSourceInfo { Path = "$.note_controllers[0]" }
                }
            ]
        };

        var result = CreateExporter().Export(document, Chart(1), null);

        Assert.False(result.Success);
        Assert.Contains(result.Issues, issue =>
            issue.Code == "RUNTIME_NOTE_CONTROLLER_BINDING_MISSING" &&
            issue.Path.Contains("note_controllers", StringComparison.Ordinal));
    }

    [Fact]
    public void RuntimeExport_BlocksNoteControllerMissingFromCurrentChart()
    {
        var document = new EditorStoryboardDocument
        {
            Entities =
            [
                new EditorStoryboardEntity
                {
                    EditorId = "missing-chart-note",
                    SourceGroupId = "missing-chart-note",
                    Kind = EditorStoryboardEntityKind.NoteController,
                    RuntimeId = EditorInterpolatedString.FromWire(
                        "missing-chart-note"),
                    ActivationMode = StoryboardActivationMode.Explicit,
                    ActivationTime = StoryboardTimePosition.Absolute(0),
                    NoteBinding = new EditorNoteBinding { NoteId = 4 },
                    Source = new EditorSourceInfo
                    {
                        Path = "$.note_controllers[0]"
                    }
                }
            ]
        };

        var result = CreateExporter().Export(document, Chart(1), null);

        Assert.False(result.Success);
        Assert.Contains(result.Issues, issue =>
            issue.Code == "RUNTIME_NOTE_CONTROLLER_NOTE_MISSING" &&
            issue.Path == "$.note_controllers[0].note");
    }

    private static StoryboardRuntimeExporter CreateExporter() =>
        new(new StoryboardMaterializer(
            new StoryboardTimePositionResolver(),
            new NoteQueryService()));

    private static C2Chart Chart(int noteCount) => new()
    {
        time_base = 480,
        page_list =
        [
            new C2Page
            {
                start_tick = 0,
                end_tick = Math.Max(960, noteCount * 10),
                scan_line_direction = 1
            }
        ],
        tempo_list = [new TempoEvent { tick = 0, value = 500000 }],
        note_list = Enumerable.Range(0, noteCount).Select(id => new C2Note
        {
            id = id,
            page_index = 0,
            type = 0,
            tick = id * 10,
            x = 0.5,
            next_id = -1
        }).ToList()
    };
}
