using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Naziki_Editor.Core;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Storyboard.Canonical;
using Naziki_Editor.Core.Storyboard.Corrections;
using Naziki_Editor.Core.Serialization;
using Naziki_Editor.Core.Timeline.Projection;
using Naziki_Editor.Core.Messaging;
using Naziki_Editor.Models;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Naziki_Editor.Tests;

public sealed class StoryboardCanonicalDataLayerTests
{
    private readonly StoryboardImportService _importer = new();
    private readonly EditorStoryboardSerializer _serializer = new();

    [Fact]
    public void Example1_ImportsExpectedResourcesAndExpandsOnlyTimeSugar()
    {
        var result = _importer.Import(ReadFixture("storyboard_example1.json"));

        Assert.NotNull(result.Document);
        var document = result.Document!;
        Assert.Equal(4, Count(document, EditorStoryboardEntityKind.Text));
        Assert.Equal(16, Count(document, EditorStoryboardEntityKind.Sprite));
        Assert.Equal(5, Count(document, EditorStoryboardEntityKind.SceneController));
        Assert.Equal(9, document.Templates.Count);
        Assert.Equal(12,
            document.Metadata.SyntaxStatistics["time_array_groups"]);
        Assert.DoesNotContain(document.Entities,
            entity => entity.ActivationMode == StoryboardActivationMode.Inactive);
        Assert.All(document.Entities.Where(entity =>
                entity.Kind == EditorStoryboardEntityKind.SceneController),
            controller =>
            {
                Assert.Equal(StoryboardActivationMode.GlobalController,
                    controller.ActivationMode);
                Assert.Equal(0, controller.ActivationTime!.Seconds);
            });
        Assert.All(document.Entities.SelectMany(entity => entity.Frames)
                .Concat(document.Templates.Values.SelectMany(template =>
                    template.Frames)),
            frame => Assert.NotEqual(StoryboardTimeAnchorKind.Unresolved,
                frame.Time.Kind));
        Assert.DoesNotContain(result.Issues,
            issue => issue.Code == "ENTITY_NOT_ACTIVATABLE");
        Assert.DoesNotContain(result.Issues,
            issue => issue.Code != "PROPERTY_NAME_NORMALIZED" &&
                     issue.Message.Contains("arcade_inteference_size",
                         StringComparison.Ordinal));
        Assert.Contains(document.Entities
                .Where(entity =>
                    entity.Kind == EditorStoryboardEntityKind.SceneController)
                .SelectMany(entity => entity.Frames),
            frame => frame.Patch.Value<double?>(
                "arcade_interference_size") == 2);
    }

    [Fact]
    public void Example2_PreservesUnitExpressionsAndNoteAnchors()
    {
        var result = _importer.Import(ReadFixture("storyboard_example2.json"));

        Assert.NotNull(result.Document);
        var document = result.Document!;
        Assert.Equal(62, Count(document, EditorStoryboardEntityKind.Sprite));
        Assert.Equal(10, Count(document, EditorStoryboardEntityKind.Text));
        Assert.Equal(1, Count(document, EditorStoryboardEntityKind.Video));
        Assert.Equal(48, Count(document, EditorStoryboardEntityKind.NoteController));
        Assert.Equal(9, Count(document, EditorStoryboardEntityKind.SceneController));

        var tokens = document.Entities
            .SelectMany(entity => entity.BasePatch.DescendantsAndSelf()
                .Concat(entity.Frames.SelectMany(frame =>
                    frame.Patch.DescendantsAndSelf())))
            .ToArray();
        Assert.Equal(260, tokens.OfType<JObject>().Count(value =>
            value.Value<string>("$naziki_type") == "unit_float"));
        var typedTimes = document.Entities
            .Select(entity => entity.ActivationTime)
            .Where(time => time is not null)
            .Select(time => time!)
            .Concat(document.Entities.SelectMany(entity => entity.Frames)
                .Select(frame => frame.Time));
        Assert.Equal(74, typedTimes.Count(time => time.Kind is
                StoryboardTimeAnchorKind.NoteIntro or
                StoryboardTimeAnchorKind.NoteStart or
                StoryboardTimeAnchorKind.NoteEnd or
                StoryboardTimeAnchorKind.NoteAt));
    }

    [Fact]
    public void CanonicalSerializer_RoundTripsStableIdsAndTypedTimes()
    {
        var imported = _importer.Import(ReadFixture("storyboard_example1.json"))
            .Document!;
        var roundTrip = _serializer.Deserialize(_serializer.Serialize(imported));

        Assert.Equal(imported.DocumentId, roundTrip.DocumentId);
        Assert.Equal(imported.Entities.Select(entity => entity.EditorId),
            roundTrip.Entities.Select(entity => entity.EditorId));
        Assert.Equal(imported.Entities.SelectMany(entity => entity.Frames)
                .Select(frame => frame.FrameId),
            roundTrip.Entities.SelectMany(entity => entity.Frames)
                .Select(frame => frame.FrameId));
        Assert.Equal(imported.Templates.Keys, roundTrip.Templates.Keys);
    }

    [Fact]
    public void CanonicalValidator_AllowsSemanticProblemsButStoreRejectsStructuralCorruption()
    {
        var document = _importer.Import("""
        {
          "sprites": [{"id": "inactive", "path": "a.png"}]
        }
        """).Document!;
        document.Entities[0].ActivationMode =
            StoryboardActivationMode.Inactive;
        document.Entities[0].ActivationTime = null;
        var validator = new EditorStoryboardValidator();
        var semantic = validator.Validate(document);
        Assert.Contains(semantic, issue =>
            issue.Code == "ENTITY_NOT_ACTIVATABLE" &&
            !issue.BlocksSourceSave && issue.BlocksRuntimeExport);

        var directory = Path.Combine(Path.GetTempPath(),
            "naziki-canonical-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new StoryboardSourceStore(_serializer, validator);
            var path = Path.Combine(directory, "storyboard.editor.json");
            store.Save(path, document);
            Assert.True(File.Exists(path));

            document.Entities[0].BasePatch["time"] = 1;
            Assert.Throws<InvalidDataException>(() => store.Save(path, document));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Importer_DefaultsUntimedSceneEntitiesToAbsoluteZero()
    {
        var result = _importer.Import("""
        {
          "sprites": [{"id": "sprite", "path": "a.png"}],
          "texts": [{"id": "text", "text": "hello"}],
          "videos": [{"id": "video", "path": "clip.mp4"}],
          "controllers": [{"id": "controller"}]
        }
        """);

        Assert.True(result.CanReplace);
        Assert.All(result.Document!.Entities.Where(entity =>
                entity.Kind is not EditorStoryboardEntityKind.SceneController),
            entity =>
            {
                Assert.Equal(StoryboardActivationMode.Explicit,
                    entity.ActivationMode);
                Assert.Equal(StoryboardTimeAnchorKind.Absolute,
                    entity.ActivationTime!.Kind);
                Assert.Equal(0, entity.ActivationTime.Seconds);
            });
        var controller = Assert.Single(result.Document.Entities.Where(entity =>
            entity.Kind == EditorStoryboardEntityKind.SceneController));
        Assert.Equal(StoryboardActivationMode.GlobalController,
            controller.ActivationMode);
        Assert.Contains(result.Issues, issue =>
            issue.Code == "ENTITY_ACTIVATION_DEFAULTED" &&
            issue.Severity == StoryboardDiagnosticSeverity.Warning);
        Assert.DoesNotContain(result.Issues,
            issue => issue.Code == "ENTITY_NOT_ACTIVATABLE");
        Assert.True(CreateExporter().Export(
            result.Document, null, null).Success);
    }

    [Fact]
    public void Importer_DistinguishesReplaceBlockingStructureFromSaveableSemantics()
    {
        var semantic = _importer.Import("""
        {
          "sprites": [{
            "id": "a",
            "time": 0,
            "template": "missing"
          }]
        }
        """);
        Assert.True(semantic.CanReplace);
        Assert.False(semantic.Success);
        Assert.Contains(semantic.Document!.Metadata.ImportDiagnostics,
            issue => issue.Code == "TEMPLATE_MISSING");
        Assert.False(CreateExporter().Export(semantic.Document, null, null)
            .Success);

        var structural = _importer.Import("""
        {"sprites": [{"id": "a", "time": 0, "states": {}}]}
        """);
        Assert.False(structural.CanReplace);
        Assert.Contains(structural.Issues,
            issue => issue.Code == "STATES_NOT_ARRAY");
    }

    [Fact]
    public void Importer_DistinguishesValueArraysFromReplicationArrays()
    {
        const string json = """
        {
          "lines": [{
            "id": "line",
            "time": 0,
            "pos": [{"x": 0, "y": 0}, {"x": 1, "y": 1}],
            "states": [{
              "time": [1, 2],
              "pos": [{"x": 2, "y": 2}, {"x": 3, "y": 3}]
            }]
          }],
          "controllers": [{
            "note_fill_colors": ["#1", "#2"],
            "states": [{"time": 0, "note_fill_colors": ["#3", "#4"]}]
          }],
          "triggers": [{
            "type": "note_clear",
            "notes": [1, 2],
            "spawn": ["line"],
            "destroy": ["line"]
          }]
        }
        """;

        var document = _importer.Import(json).Document!;
        var line = Assert.Single(document.Entities.Where(entity =>
            entity.Kind == EditorStoryboardEntityKind.Line));
        Assert.Equal(2, line.Frames.Count);
        Assert.Equal(2, ((JArray)line.BasePatch["pos"]!).Count);
        Assert.All(line.Frames,
            frame => Assert.Equal(2, ((JArray)frame.Patch["pos"]!).Count));
        var controller = Assert.Single(document.Entities.Where(entity =>
            entity.Kind == EditorStoryboardEntityKind.SceneController));
        Assert.Equal(2,
            ((JArray)controller.BasePatch["note_fill_colors"]!).Count);
        Assert.Equal(2,
            ((JArray)controller.Frames[0].Patch["note_fill_colors"]!).Count);
        Assert.Equal(2, ((JArray)document.Triggers[0]!["notes"]!).Count);
    }

    [Fact]
    public void Importer_FollowsRuntimeFieldOrderAndRejectsAmbiguousMultipleTimeArrays()
    {
        var imported = _importer.Import("""
        {
          "sprites": [{
            "id": "copy",
            "relative_time": [1, 2],
            "time": [10, 20],
            "note": [3, 4],
            "path": "a.png"
          }]
        }
        """);
        var document = imported.Document!;

        Assert.Equal(8, document.Entities.Count);
        Assert.Equal(new int?[]
            {
                3, 4, 3, 4,
                3, 4, 3, 4
            },
            document.Entities.Select(entity =>
                entity.NoteBinding!.NoteId));
        Assert.Contains(imported.Issues,
            issue => issue.Code == "MULTIPLE_TIME_ARRAY_FIELDS" &&
                     issue.Severity == StoryboardDiagnosticSeverity.Error);
    }

    [Fact]
    public void Materializer_PreservesSameTimeSequenceAndExpandsTemplate()
    {
        const string json = """
        {
          "templates": {
            "pulse": {
              "states": [
                {"relative_time": 0, "opacity": 1},
                {"add_time": 1, "opacity": 0},
                {"relative_time": 0, "opacity": 1}
              ]
            }
          },
          "sprites": [{
            "id": "a",
            "path": "a.png",
            "states": [{"time": 3, "template": "pulse"}]
          }]
        }
        """;
        var document = _importer.Import(json).Document!;
        var materialized = CreateMaterializer().Materialize(document, null, null);
        var entity = Assert.Single(materialized.Entities);
        Assert.Equal(new double?[] { 3d, 3d, 4d, 4d },
            entity.Frames.Select(frame => frame.EffectiveTime).ToArray());
        Assert.Equal(Enumerable.Range(0, 4),
            entity.Frames.Select(frame => frame.Sequence).ToArray());
        Assert.Equal(new double?[] { null, 1d, 0d, 1d },
            entity.Frames.Select(frame =>
                frame.EffectiveState.Value<double?>("opacity")).ToArray());
    }

    [Fact]
    public void Materializer_TemplateFrameOccurrencesAreUniquePerEntity()
    {
        var document = _importer.Import("""
        {
          "templates": {
            "one": {"states": [{"relative_time": 1, "opacity": 1}]}
          },
          "sprites": [
            {"id": "a", "time": 0, "template": "one"},
            {"id": "b", "time": 0, "template": "one"}
          ]
        }
        """).Document!;

        var result = CreateMaterializer().Materialize(document, null, null);

        Assert.Equal(2, result.Entities.Count);
        Assert.NotEqual(result.Entities[0].Frames[0].OccurrenceId,
            result.Entities[1].Frames[0].OccurrenceId);
        Assert.Equal(result.Entities[0].Frames[0].FrameId,
            result.Entities[1].Frames[0].FrameId);
    }

    [Fact]
    public void NoteQuery_RematchesChartAndProducesStableOccurrences()
    {
        const string json = """
        {
          "sprites": [{
            "id": "note_$note",
            "note": {"type": [0], "start": 10, "end": 20},
            "path": "a.png",
            "states": [{"time": "start:$note"}]
          }]
        }
        """;
        var document = _importer.Import(json).Document!;
        var chart = Chart((10, 0), (11, 1), (20, 0));
        var materializer = CreateMaterializer();

        var first = materializer.Materialize(document, chart, Engine(chart));
        Assert.Equal(new[] { "note_10", "note_20" },
            first.Entities.Select(entity => entity.RuntimeId));
        var occurrence10 = first.Entities[0].OccurrenceId;

        chart.note_list.RemoveAll(note => note.id == 20);
        chart.note_list.Add(new C2Note { id = 15, type = 0, tick = 1500 });
        var second = materializer.Materialize(document, chart, Engine(chart));
        Assert.Equal(new[] { "note_10", "note_15" },
            second.Entities.Select(entity => entity.RuntimeId));
        Assert.Equal(occurrence10, second.Entities[0].OccurrenceId);
    }

    [Fact]
    public void RuntimeExporter_EmitsExplicitSyntaxFreeStoryboard()
    {
        var document = _importer.Import(ReadFixture("storyboard_example1.json"))
            .Document!;
        var chart = Chart(Enumerable.Range(0, 3000)
            .Select(id => (id, 0)).ToArray());
        var result = CreateExporter().Export(document, chart, Engine(chart));

        Assert.True(result.Success, string.Join(Environment.NewLine,
            result.Issues.Select(issue => $"{issue.Code}: {issue.Message}")));
        Assert.Null(result.Json["templates"]);
        var forbidden = new HashSet<string>(StringComparer.Ordinal)
        {
            "template", "reset", "relative_time", "add_time"
        };
        Assert.DoesNotContain(result.Json.Descendants().OfType<JProperty>(),
            property => forbidden.Contains(property.Name));
        Assert.DoesNotContain(result.Json.Descendants().OfType<JProperty>()
                .Where(property => property.Name == "time"),
            property => property.Value.Type == JTokenType.Array ||
                        property.Value.Type == JTokenType.String);
        Assert.DoesNotContain(result.Json.Descendants().OfType<JValue>(),
            value => value.Type == JTokenType.String &&
                     (value.Value<string>()?.Contains("$note",
                         StringComparison.Ordinal) ?? false));
        Assert.DoesNotContain(result.Json.Descendants().OfType<JProperty>(),
            property => property.Name.Contains("inteference",
                StringComparison.Ordinal) ||
                        property.Name.Contains("interferance",
                            StringComparison.Ordinal));
        Assert.Contains(result.Json.Descendants().OfType<JProperty>(),
            property =>
                property.Name == "arcade_interference_size" &&
                property.Value.Value<double>() == 2);
    }

    [Fact]
    public void RuntimeExporter_RestoresTypedUnitExpressionsOnlyAtWireBoundary()
    {
        var document = _importer.Import("""
        {
          "sprites": [{
            "id": "unit",
            "time": 0,
            "x": "stageX:0.5",
            "states": [{"time": 1, "y": "noteY:-0.25"}]
          }]
        }
        """).Document!;
        var entity = Assert.Single(document.Entities);
        Assert.Equal("unit_float",
            ((JObject)entity.BasePatch["x"]!).Value<string>("$naziki_type"));
        Assert.Equal("unit_float",
            ((JObject)entity.Frames[0].Patch["y"]!)
                .Value<string>("$naziki_type"));

        var exported = CreateExporter().Export(document, null, null);

        Assert.True(exported.Success);
        Assert.Equal("stageX:0.5",
            exported.Json["sprites"]![0]!["x"]!.Value<string>());
        Assert.Equal("noteY:-0.25",
            exported.Json["sprites"]![0]!["states"]![0]!["y"]!
                .Value<string>());
        Assert.DoesNotContain(exported.Json.Descendants().OfType<JObject>(),
            item => item.Value<string>("$naziki_type") == "unit_float");
    }

    [Fact]
    public void UnityUnitObjects_MapAllExternalEnumValuesSemantically()
    {
        var units = new[]
        {
            "world", "stageX", "stageY", "noteX", "noteY",
            "cameraX", "cameraY"
        };
        var states = new JArray();
        for (var index = 0; index < units.Length; index++)
            states.Add(new JObject
            {
                ["time"] = index + 1,
                ["x"] = new JObject
                {
                    ["Value"] = index + 0.25,
                    ["Unit"] = index,
                    ["ScaleToCanvas"] = true,
                    ["Span"] = false
                }
            });
        var root = new JObject
        {
            ["sprites"] = new JArray(new JObject
            {
                ["id"] = "units",
                ["time"] = 0,
                ["states"] = states
            })
        };

        var imported = _importer.Import(root.ToString());

        Assert.True(imported.CanReplace);
        var entity = Assert.Single(imported.Document!.Entities);
        Assert.Equal(units, entity.Frames.Select(frame =>
            ((JObject)frame.Patch["x"]!).Value<string>("unit")));
        Assert.All(entity.Frames, frame =>
        {
            var typed = (JObject)frame.Patch["x"]!;
            Assert.Null(typed["ScaleToCanvas"]);
            Assert.Null(typed["Span"]);
        });

        var runtime = CreateExporter().Export(
            imported.Document, null, null);
        Assert.True(runtime.Success);
        Assert.Equal(units.Select((unit, index) =>
                $"{unit}:{(index + 0.25).ToString(
                    System.Globalization.CultureInfo.InvariantCulture)}"),
            runtime.Json["sprites"]![0]!["states"]!
                .Select(state => state!["x"]!.Value<string>()));
    }

    [Theory]
    [InlineData("""{"Value":1}""", "UNIT_OBJECT_INVALID")]
    [InlineData("""{"Value":"bad","Unit":1}""", "UNIT_VALUE_INVALID")]
    [InlineData("""{"Value":1,"Unit":99}""", "UNIT_ENUM_UNKNOWN")]
    [InlineData("""{"Value":1,"Unit":1.5}""", "UNIT_ENUM_UNKNOWN")]
    [InlineData("\"unknown:1\"", "UNIT_NAME_UNKNOWN")]
    public void InvalidUnitValues_BlockImportAtSourcePath(
        string unitJson, string expectedCode)
    {
        var result = _importer.Import(
            $$"""{"sprites":[{"id":"s","time":0,"x":{{unitJson}}}]}""");

        Assert.False(result.CanReplace);
        var issue = Assert.Single(result.Issues,
            item => item.Code == expectedCode);
        Assert.Equal("$.sprites[0].x", issue.Path);
    }

    [Fact]
    public void EditorSerializer_MigratesEmptyCanonicalUnitToWorld()
    {
        var document = _serializer.Deserialize("""
        {
          "schema_version": 1,
          "document_id": "document",
          "entities": [{
            "editor_id": "entity",
            "source_group_id": "group",
            "kind": "Sprite",
            "source_order": 0,
            "activation_mode": "Explicit",
            "activation_time": {
              "kind": "Absolute",
              "seconds": 0
            },
            "base_patch": {
              "z": {
                "$naziki_type": "unit_float",
                "unit": "",
                "value": 0
              }
            },
            "frames": [],
            "source": { "path": "$.sprites[0]", "source_order": 0 }
          }],
          "templates": {},
          "triggers": [],
          "root_properties": {},
          "metadata": {}
        }
        """);

        var unit = (JObject)document.Entities[0].BasePatch["z"]!;
        Assert.Equal("world", unit.Value<string>("unit"));
        Assert.Contains(document.Metadata.ImportDiagnostics,
            issue => issue.Code == "CANONICAL_UNIT_WORLD_MIGRATED");
        Assert.Contains("\"unit\": \"world\"",
            _serializer.Serialize(document), StringComparison.Ordinal);
    }

    [Fact]
    public void SourceStore_NormalizesLegacyEmptyUnitBeforeValidationAndWrite()
    {
        var document = _importer.Import("""
        {"sprites":[{"id":"sprite","time":0,"z":0}]}
        """).Document!;
        document.Entities[0].BasePatch["z"] = new JObject
        {
            ["$naziki_type"] = "unit_float",
            ["unit"] = "",
            ["value"] = 0
        };
        var directory = Path.Combine(Path.GetTempPath(),
            "naziki-unit-store-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "storyboard.editor.json");
        try
        {
            var store = new StoryboardSourceStore(_serializer,
                new EditorStoryboardValidator());

            store.Save(path, document);

            var saved = JObject.Parse(File.ReadAllText(path));
            Assert.Equal("world", saved.SelectToken(
                "$.entities[0].base_patch.z.unit")?.Value<string>());
            var reloaded = store.Load(path);
            Assert.Equal("world",
                ((JObject)reloaded.Entities[0].BasePatch["z"]!)
                .Value<string>("unit"));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void RuntimeExporter_PromotesFirstTimedFrameAndPreservesTriggerRelativeTime()
    {
        var document = _importer.Import("""
        {
          "sprites": [
            {
              "id": "first",
              "path": "a.png",
              "opacity": 0.25,
              "states": [
                {"time": 2, "opacity": 0.5},
                {"time": 2, "opacity": 1}
              ]
            },
            {
              "id": "spawned",
              "relative_time": 0.75,
              "path": "b.png"
            }
          ],
          "triggers": [{
            "type": "note_clear",
            "notes": [1],
            "spawn": ["spawned"]
          }]
        }
        """).Document!;

        var result = CreateExporter().Export(document, null, null);

        Assert.True(result.Success, string.Join(Environment.NewLine,
            result.Issues.Select(issue => issue.Message)));
        var first = (JObject)result.Json["sprites"]![0]!;
        Assert.Equal(2, first.Value<double>("time"));
        Assert.Equal(0.5, first.Value<double>("opacity"));
        var remaining = Assert.Single((JArray)first["states"]!);
        Assert.Equal(2, remaining.Value<double>("time"));
        Assert.Equal(1, remaining.Value<double>("opacity"));
        var spawned = (JObject)result.Json["sprites"]![1]!;
        Assert.Null(spawned["time"]);
        Assert.Equal(0.75, spawned.Value<double>("relative_time"));
    }

    [Fact]
    public void RuntimeExporter_CoalescesOnlyFloatTimeEquivalentFrames()
    {
        var document = _importer.Import("""
        {
          "sprites": [{
            "id": "flash",
            "time": 0,
            "opacity": 0,
            "states": [
              {"time": 1.0, "opacity": 0.5},
              {"time": 1.0000000000000002, "opacity": 0.5},
              {"time": 1.0000000000000004, "opacity": 1}
            ]
          }]
        }
        """).Document!;

        var result = CreateExporter().Export(document, null, null);

        Assert.True(result.Success);
        Assert.Equal(3, document.Entities[0].Frames.Count);
        var states = (JArray)result.Json["sprites"]![0]!["states"]!;
        Assert.Equal(2, states.Count);
        Assert.Equal(0.5, states[0]!.Value<double>("opacity"));
        Assert.Equal(1, states[1]!.Value<double>("opacity"));
        Assert.Contains(result.Issues, issue =>
            issue.Code == "RUNTIME_FRAME_DUPLICATE_COALESCED" &&
            issue.Severity == StoryboardDiagnosticSeverity.Info);
    }

    [Fact]
    public void RuntimeExporter_RejectsBrokenTriggerReferencesAndClearsCompiledFlag()
    {
        var document = _importer.Import("""
        {
          "compiled": true,
          "sprites": [{"id": "a", "time": 0}],
          "triggers": [{
            "type": "note_clear",
            "notes": [99],
            "spawn": ["missing"],
            "destroy": ["a"]
          }]
        }
        """).Document!;
        var chart = Chart((1, 0));

        var result = CreateExporter().Export(document, chart, Engine(chart));

        Assert.False(result.Success);
        Assert.Null(result.Json["compiled"]);
        Assert.Contains(result.Issues,
            issue => issue.Code == "TRIGGER_REFERENCE_MISSING");
        Assert.Contains(result.Issues,
            issue => issue.Code == "TRIGGER_NOTE_MISSING");
    }

    [Fact]
    public void CanonicalBridge_PreviewExportDoesNotCommitDiskExportHash()
    {
        var document = _importer.Import("""
        {"sprites": [{"id": "a", "time": 0}]}
        """).Document!;
        var catalog = new StoryboardPropertyCatalogService();
        var reader = new StoryboardDocumentReader(catalog);
        var writer = new StoryboardDocumentWriter();
        var bridge = new StoryboardCanonicalBridge(_importer,
            CreateExporter(), reader, writer);
        var context = new Naziki_Editor.State.ProjectDataContext(
            MessageBroker.Default)
        {
            EditorStoryboard = document,
#pragma warning disable CS0618
            Storyboard = reader.Read("""{"sprites":[{"id":"a","time":0}]}""")
#pragma warning restore CS0618
        };
#pragma warning disable CS0618
        context.LegacyStoryboardProjectionHash =
            bridge.ComputeLegacyProjectionHash(context.Storyboard);
#pragma warning restore CS0618

        var result = bridge.Export(context);

        Assert.True(result.Success);
        Assert.Null(document.Metadata.LastExportHash);
    }

    [Theory]
    [InlineData("storyboard_example1.json")]
    [InlineData("storyboard_example2.json")]
    [InlineData("storyboard_example.json")]
    public void Fixture_ExplicitRuntimeExportIsIdempotentAfterReimport(
        string fixture)
    {
        var source = ReadFixture(fixture);
        var firstImport = _importer.Import(source);
        Assert.NotNull(firstImport.Document);
        Assert.DoesNotContain(firstImport.Issues,
            issue => issue.Severity == StoryboardDiagnosticSeverity.Error);
        var chart = ChartFor(source);
        var engine = Engine(chart);

        var firstExport = CreateExporter().Export(firstImport.Document!, chart,
            engine);
        Assert.True(firstExport.Success, string.Join(Environment.NewLine,
            firstExport.Issues.Where(issue =>
                    issue.Severity == StoryboardDiagnosticSeverity.Error)
                .Take(20).Select(issue =>
                    $"{issue.Code} {issue.Path}: {issue.Message}")));
        var secondImport = _importer.Import(
            firstExport.Json.ToString(Newtonsoft.Json.Formatting.None), chart);
        Assert.NotNull(secondImport.Document);
        Assert.DoesNotContain(secondImport.Issues,
            issue => issue.Severity == StoryboardDiagnosticSeverity.Error);
        var secondExport = CreateExporter().Export(secondImport.Document!,
            chart, engine);

        Assert.True(secondExport.Success, string.Join(Environment.NewLine,
            secondExport.Issues.Where(issue =>
                    issue.Severity == StoryboardDiagnosticSeverity.Error)
                .Take(20).Select(issue =>
                    $"{issue.Code} {issue.Path}: {issue.Message}")));
        Assert.True(JToken.DeepEquals(firstExport.Json, secondExport.Json),
            $"Explicit export changed after reimport for {fixture}: " +
            FirstDifference(firstExport.Json, secondExport.Json));
    }

    [Fact]
    public void CanonicalTimelineProjection_PreservesIdentityAndInstantBoundaries()
    {
        var document = _importer.Import("""
        {
          "sprites": [{
            "id": "instant",
            "time": 0,
            "states": [
              {"time": 1, "opacity": 0},
              {"time": 1, "opacity": 1}
            ]
          }]
        }
        """).Document!;
        var service = new TimelineProjectionService(
            new StoryboardTimeResolver(), CreateMaterializer());

        var projection = Assert.Single(service.BuildCanonicalProjections(
            document, null, null));

        Assert.Equal(document.Entities[0].EditorId, projection.EditorId);
        Assert.Equal(document.Entities[0].Frames.Select(frame => frame.FrameId),
            projection.Frames.Select(frame => frame.FrameId));
        Assert.Equal(new[] { 0, 1 },
            projection.Frames.Select(frame => frame.Sequence));
        Assert.True(projection.Frames[0]
            .IsInstantBoundaryWith(projection.Frames[1]));
        Assert.Equal(0,
            projection.Frames[0].EffectiveState.Value<double>("opacity"));
        Assert.Equal(1,
            projection.Frames[1].EffectiveState.Value<double>("opacity"));
    }

    [Fact]
    public void CanonicalEdit_MovePreservesAnchorAndTemplateOverrideStaysLinked()
    {
        var document = _importer.Import("""
        {
          "templates": {
            "pulse": {"states": [{"relative_time": 1, "opacity": 0.5}]}
          },
          "sprites": [{
            "id": "a",
            "note": 7,
            "time": "start:7",
            "template": "pulse"
          }]
        }
        """).Document!;
        var entity = Assert.Single(document.Entities);
        var templateFrame = document.Templates["pulse"].Frames[0];
        var edit = new EditorStoryboardEditService(CreateMaterializer());

        edit.MoveFrame(document, templateFrame.FrameId, 0.25);
        edit.ApplyTemplateFrameOverride(document, entity.EditorId, null,
            templateFrame.FrameId, JObject.Parse("""{"opacity": 0.9}"""));

        Assert.Equal(StoryboardTimeAnchorKind.TemplateStart,
            templateFrame.Time.Kind);
        Assert.Equal(1.25, templateFrame.Time.OffsetSeconds);
        Assert.Equal(0.9, entity.RootTemplate!.FrameOverrides[
            templateFrame.FrameId].Value<double>("opacity"));
        Assert.Equal(2, document.Revision);
    }

    [Fact]
    public void CanonicalEdit_DetachMaterializesTemplateIntoOrdinaryFrames()
    {
        var document = _importer.Import("""
        {
          "templates": {
            "pulse": {
              "opacity": 0.25,
              "states": [{"relative_time": 1, "opacity": 1}]
            }
          },
          "sprites": [{
            "id": "a",
            "time": 2,
            "template": "pulse",
            "path": "a.png"
          }]
        }
        """).Document!;
        var entity = Assert.Single(document.Entities);
        var edit = new EditorStoryboardEditService(CreateMaterializer());

        edit.DetachRootTemplate(document, entity.EditorId, null, null);

        Assert.Null(entity.RootTemplate);
        Assert.Equal(0.25, entity.BasePatch.Value<double>("opacity"));
        var detached = Assert.Single(entity.Frames);
        Assert.Equal(3, detached.Time.Seconds);
        Assert.Equal(1, detached.Patch.Value<double>("opacity"));
        Assert.Null(detached.Template);
    }

    [Fact]
    public void CanonicalTemplateCommands_RenameBindingsAndProtectDependencies()
    {
        var document = _importer.Import("""
        {
          "templates": {
            "pulse": {
              "states": [{"relative_time": 1, "opacity": 1}]
            }
          },
          "sprites": [{
            "id": "a",
            "time": 0,
            "template": "pulse"
          }]
        }
        """).Document!;
        var edit = new EditorStoryboardEditService(CreateMaterializer());
        var template = document.Templates["pulse"];

        Assert.Single(edit.GetTemplateDependents(document,
            template.TemplateId));
        Assert.Throws<InvalidOperationException>(() =>
            edit.DeleteTemplate(document, template.TemplateId));

        edit.RenameTemplate(document, template.TemplateId, "flash");

        Assert.False(document.Templates.ContainsKey("pulse"));
        Assert.Same(template, document.Templates["flash"]);
        Assert.Equal("flash",
            document.Entities[0].RootTemplate!.TemplateName);
    }

    [Fact]
    public void Example1_TemplateListProjectionContainsAllCanonicalTemplates()
    {
        var document = _importer.Import(
            ReadFixture("storyboard_example1.json")).Document!;
        var edit = new EditorStoryboardEditService(CreateMaterializer());

        var items = StoryboardTemplateListProjection.Build(document, edit);

        Assert.Equal(9, items.Count);
        Assert.All(items, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.TemplateId));
            Assert.Equal(item.Template.Name, item.Name);
            Assert.Equal(item.Template.Frames.Count, item.FrameCount);
        });
    }

    [Fact]
    public void CanonicalTemplateView_RoundTripsThroughOfficialEditorShape()
    {
        var catalog = new StoryboardPropertyCatalogService();
        var adapter = new StoryboardTemplateViewAdapter(
            new StoryboardDocumentReader(catalog),
            new StoryboardDocumentWriter(),
            _importer);
        var template = _importer.Import("""
        {
          "templates": {
            "pulse": {
              "opacity": 0.25,
              "states": [
                {"relative_time": 1, "opacity": 1},
                {"relative_time": 0, "opacity": 0}
              ]
            }
          }
        }
        """).Document!.Templates["pulse"];

        var view = adapter.CreateWireView(template);
        var restored = adapter.ParseWireView("pulse", view);

        Assert.Equal(2, restored.Frames.Count);
        Assert.Equal(template.Frames.Select(frame => frame.FrameId),
            restored.Frames.Select(frame => frame.FrameId));
        Assert.Equal(new[] { 1d, 1d },
            restored.Frames.Select(frame => frame.Time.OffsetSeconds));
        Assert.Equal(new[] { 1d, 0d },
            restored.Frames.Select(frame =>
                frame.Patch.Value<double>("opacity")));
    }

    [Fact]
    public void CorrectionAnalyzer_TreatsFirstTimedStateAndInstantBoundaryAsValid()
    {
        var root = new StoryboardRoot
        {
            sprites =
            [
                new C2Sprite
                {
                    Id = "sprite",
                    BaseState = new SpriteState { Path = "a.png" },
                    Keyframes =
                    [
                        new SpriteState { Time = 1, Opacity = 0 },
                        new SpriteState { Time = 1, Opacity = 1 }
                    ]
                }
            ],
            controllers =
            [
                new C2SceneController
                {
                    BaseState = new ControllerState(),
                    Keyframes =
                    [
                        new ControllerState { Time = 2, UiOpacity = 0 }
                    ]
                }
            ]
        };
        var analyzer = new StoryboardCorrectionAnalyzer(
            new StoryboardTimeResolver(), new StoryboardDocumentWriter());
        var report = analyzer.Scan(root, null);

        Assert.DoesNotContain(report.Issues, issue =>
            issue.Kind == StoryboardCorrectionKind.MissingBaseTime);
        Assert.DoesNotContain(report.Issues, issue =>
            issue.Kind == StoryboardCorrectionKind.SameTimeConflict);
    }

    [Fact]
    public void Import_NormalizesPascalCaseAndPreservesParentReference()
    {
        var result = _importer.Import("""
        {
          "Sprites": [{
            "Id": "child",
            "ParentId": "anchor",
            "Path": "image.png",
            "Time": 0,
            "ScaleToCanvas": true,
            "Width": {
              "Value": 1000,
              "Unit": 1,
              "ScaleToCanvas": true,
              "Span": true
            },
            "States": [{ "RelativeTime": 1, "RotX": 15 }]
          }],
          "NoteControllers": [{
            "Id": "anchor",
            "Note": 7,
            "Time": 0
          }]
        }
        """, Chart((7, 0)));

        Assert.True(result.CanReplace);
        var child = Assert.Single(result.Document!.Entities.Where(entity =>
            entity.Kind == EditorStoryboardEntityKind.Sprite));
        Assert.Equal("anchor", child.ParentId!.Literal);
        Assert.True(child.BasePatch.Value<bool>("scale_to_canvas"));
        Assert.Equal(15, child.Frames[0].Patch.Value<double>("rot_x"));
        Assert.Contains(result.Issues, issue =>
            issue.Code == "PROPERTY_NAME_NORMALIZED");

        var exported = CreateExporter().Export(result.Document,
            Chart((7, 0)), Engine(Chart((7, 0))));
        Assert.Equal("anchor",
            exported.Json["sprites"]![0]!["parent_id"]!.Value<string>());
        Assert.Equal("stageX:1000",
            exported.Json["sprites"]![0]!["width"]!.Value<string>());
    }

    [Fact]
    public void Import_MergesEquivalentCaseDuplicates()
    {
        var result = _importer.Import("""
        { "sprites": [{ "id": "sprite", "time": 0, "Time": 0 }] }
        """);

        Assert.True(result.CanReplace);
        Assert.Contains(result.Issues, issue =>
            issue.Code == "PROPERTY_NAME_DUPLICATE_MERGED");
    }

    [Fact]
    public void Import_BlocksDifferentValuesThatNormalizeToSameProperty()
    {
        var result = _importer.Import("""
        { "sprites": [{ "id": "sprite", "time": 0, "Time": 2 }] }
        """);

        Assert.False(result.CanReplace);
        var conflict = Assert.Single(result.SourceConflicts!);
        Assert.Equal("$.sprites[0]", conflict.Path);
        Assert.Equal("time", conflict.CanonicalName);
        Assert.Contains(result.Issues, issue =>
            issue.Code == "PROPERTY_NAME_CONFLICT");
    }

    [Fact]
    public void Import_PreservesTemplateNamesButNormalizesTemplateProperties()
    {
        var result = _importer.Import("""
        {
          "Templates": {
            "MyPascalTemplate": { "Opacity": 0.5 }
          },
          "Sprites": [{
            "Id": "sprite",
            "Path": "a.png",
            "Time": 0,
            "States": [{ "Template": "MyPascalTemplate", "Time": 1 }]
          }]
        }
        """);

        Assert.True(result.CanReplace);
        Assert.True(result.Document!.Templates.ContainsKey(
            "MyPascalTemplate"));
        Assert.Equal(0.5,
            result.Document.Templates["MyPascalTemplate"]
                .BasePatch.Value<double>("opacity"));
    }

    [Fact]
    public void Import_RejectsInvalidParentSemantics()
    {
        var wrongType = _importer.Import("""
        {
          "note_controllers": [{
            "id": "note",
            "parent_id": "anchor",
            "note": 1,
            "time": 0
          }],
          "sprites": [{ "id": "anchor", "time": 0 }]
        }
        """, Chart((1, 0)));
        Assert.Contains(wrongType.Issues, issue =>
            issue.Code == "PARENT_TYPE_INVALID");

        var cycle = _importer.Import("""
        {
          "sprites": [
            { "id": "a", "parent_id": "b", "time": 0 },
            { "id": "b", "parent_id": "a", "time": 0 }
          ]
        }
        """);
        Assert.Contains(cycle.Issues, issue =>
            issue.Code == "PARENT_CYCLE");
    }

    private static int Count(EditorStoryboardDocument document,
        EditorStoryboardEntityKind kind) =>
        document.Entities.Count(entity => entity.Kind == kind);

    private static string ReadFixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", name));

    private static bool IsUnitExpression(string? value) =>
        value?.StartsWith("noteX:", StringComparison.OrdinalIgnoreCase) == true ||
        value?.StartsWith("noteY:", StringComparison.OrdinalIgnoreCase) == true ||
        value?.StartsWith("stageX:", StringComparison.OrdinalIgnoreCase) == true ||
        value?.StartsWith("stageY:", StringComparison.OrdinalIgnoreCase) == true ||
        value?.StartsWith("cameraX:", StringComparison.OrdinalIgnoreCase) == true ||
        value?.StartsWith("cameraY:", StringComparison.OrdinalIgnoreCase) == true;

    private static StoryboardMaterializer CreateMaterializer() =>
        new(new StoryboardTimePositionResolver(), new NoteQueryService());

    private static StoryboardRuntimeExporter CreateExporter() =>
        new(CreateMaterializer());

    private static C2Chart Chart(params (int id, int type)[] notes) =>
        new()
        {
            time_base = 1000,
            tempo_list = [new TempoEvent { tick = 0, value = 500000 }],
            page_list =
            [
                new C2Page
                {
                    start_tick = 0,
                    end_tick = int.MaxValue,
                    scan_line_direction = 1
                }
            ],
            note_list = notes.Select(item => new C2Note
            {
                id = item.id,
                type = item.type,
                tick = item.id * 100,
                page_index = 0
            }).ToList()
        };

    private static C2Chart ChartFor(string storyboardJson)
    {
        var root = JObject.Parse(storyboardJson);
        var noteTypes = new Dictionary<int, int>();
        void Add(int id, int type = 0)
        {
            if (id >= 0) noteTypes.TryAdd(id, type);
        }

        foreach (var property in root.DescendantsAndSelf()
                     .OfType<JProperty>())
        {
            if (property.Name is "time")
            {
                var timeValues = property.Value is JValue scalarTime
                    ? new[] { scalarTime }
                    : ((JContainer)property.Value).Descendants()
                        .OfType<JValue>();
                foreach (var expression in timeValues
                             .Where(value =>
                                 value.Type == JTokenType.String)
                             .Select(value => value.Value<string>() ?? ""))
                {
                    var parts = expression.Split(':');
                    if (parts.Length >= 2 &&
                        int.TryParse(parts[1], out var id))
                        Add(id);
                }
            }
            if (property.Name is "note" or "notes")
            {
                if (property.Value.Type == JTokenType.Integer)
                    Add(property.Value.Value<int>());
                else if (property.Value is JArray array)
                    foreach (var id in array.Values<int>()) Add(id);
                else if (property.Value is JObject selector)
                {
                    var start = selector.Value<int?>("start") ?? 0;
                    var end = selector.Value<int?>("end") ??
                              checked(start + 8);
                    var types = selector["type"] is JArray typeArray
                        ? typeArray.Values<int>().ToArray()
                        : selector["type"]?.Type == JTokenType.Integer
                            ? [selector.Value<int>("type")]
                            : [0];
                    var candidate = start;
                    foreach (var type in types)
                    {
                        while (candidate <= end &&
                               noteTypes.ContainsKey(candidate))
                            candidate++;
                        if (candidate <= end) Add(candidate++, type);
                    }
                }
            }
        }
        if (noteTypes.Count == 0) Add(0);
        return Chart(noteTypes.OrderBy(item => item.Key)
            .Select(item => (item.Key, item.Value)).ToArray());
    }

    private static ChartTimeEngine Engine(C2Chart chart) =>
        new(chart.tempo_list, chart.time_base);

    private static string FirstDifference(JToken left, JToken right,
        string path = "$")
    {
        if (left.Type != right.Type)
            return $"{path} type {left.Type} != {right.Type}";
        if (left is JObject leftObject && right is JObject rightObject)
        {
            var names = leftObject.Properties().Select(item => item.Name)
                .Union(rightObject.Properties().Select(item => item.Name));
            foreach (var name in names)
            {
                if (leftObject[name] is null || rightObject[name] is null)
                    return $"{path}.{name} missing on " +
                           (leftObject[name] is null ? "first" : "second") +
                           " export";
                if (!JToken.DeepEquals(leftObject[name], rightObject[name]))
                    return FirstDifference(leftObject[name]!,
                        rightObject[name]!, $"{path}.{name}");
            }
        }
        else if (left is JArray leftArray && right is JArray rightArray)
        {
            if (leftArray.Count != rightArray.Count)
                return $"{path} count {leftArray.Count} != {rightArray.Count}";
            for (var index = 0; index < leftArray.Count; index++)
                if (!JToken.DeepEquals(leftArray[index], rightArray[index]))
                    return FirstDifference(leftArray[index]!,
                        rightArray[index]!, $"{path}[{index}]");
        }
        return $"{path}: {left.ToString(Newtonsoft.Json.Formatting.None)} != " +
               right.ToString(Newtonsoft.Json.Formatting.None);
    }
}
