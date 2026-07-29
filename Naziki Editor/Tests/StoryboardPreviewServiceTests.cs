using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Features.Preview;
using Naziki_Editor.Models;
using Xunit;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Naziki_Editor.Features.Project.Resources;
using Naziki_Editor.State;
using Naziki_Editor.Core.Messaging;
using Newtonsoft.Json.Linq;

namespace Naziki_Editor.Tests;

public sealed class StoryboardPreviewServiceTests
{
    [Fact]
    public void PublishesStrictlyIncreasingVersionsAndSessionEnd()
    {
        var service = new StoryboardPreviewService(new FakeBridge(), new FakeResources());
        var received = new List<StoryboardPreviewChangeSet>();
        using var subscription = service.Subscribe(received.Add);

        var first = service.PublishReset("test");
        var second = service.PublishIncremental("test", []);
        service.EndSession();

        Assert.Equal(1, first);
        Assert.Equal(2, second);
        Assert.Equal(3, received.Count);
        Assert.Equal(StoryboardPreviewChangeKind.SessionEnded, received[^1].Kind);
    }

    [Fact]
    public void DisposedSubscriptionStopsDelivery()
    {
        var service = new StoryboardPreviewService(new FakeBridge(), new FakeResources());
        var count = 0;
        var subscription = service.Subscribe(_ => count++);
        subscription.Dispose();
        service.PublishReset("test");
        Assert.Equal(0, count);
    }

    [Fact]
    public void Snapshot_NormalizesNullableNoteFlagsForProductionChartParser()
    {
        var service = new StoryboardPreviewService(new FakeBridge(), new FakeResources());
        var context = new ProjectDataContext(MessageBroker.Default)
        {
            Storyboard = new StoryboardRoot(),
            Chart = new C2Chart
            {
                time_base = 480,
                tempo_list = [new TempoEvent { tick = 0, value = 500_000 }],
                page_list = [new C2Page { start_tick = 0, end_tick = 1920, scan_line_direction = 1 }],
                note_list =
                [
                    new C2Note
                    {
                        id = 1,
                        page_index = 0,
                        has_sibling = null,
                        is_forward = null,
                        NoteDirection = 1
                    }
                ]
            }
        };

        var snapshot = service.GetSnapshot(context);
        var wire = JObject.Parse(snapshot.ChartJson!);
        var note = (JObject)wire["note_list"]![0]!;

        Assert.Equal(0d, wire.Value<double>("music_offset"));
        Assert.Null(wire["skip_music_on_completion"]);
        Assert.False(note.Value<bool>("has_sibling"));
        Assert.False(note.Value<bool>("is_forward"));
        Assert.Null(note[nameof(C2Note.NoteDirection)]);
        Assert.DoesNotContain(wire.DescendantsAndSelf().OfType<JValue>(),
            value => value.Type == JTokenType.Null);
        Assert.Empty(new ChartPreviewWireAdapter().Validate(
            snapshot.ChartJson));
    }

    [Fact]
    public void ChartWireSerialization_ReportsNullNoteListWithoutNullReference()
    {
        var chart = new C2Chart
        {
            time_base = 480,
            page_list =
            [
                new C2Page
                {
                    start_tick = 0,
                    end_tick = 480,
                    scan_line_direction = 1
                }
            ],
            tempo_list =
            [
                new TempoEvent { tick = 0, value = 500_000 }
            ],
            note_list = null!
        };

        var error = Assert.Throws<Newtonsoft.Json.JsonSerializationException>(
            () => new ChartPreviewWireAdapter().Serialize(chart));

        Assert.Contains("note_list 为 null", error.Message);
    }

    [Fact]
    public void ChartWireValidation_ReportsExactUnityContractPath()
    {
        var issues = new ChartPreviewWireAdapter().Validate("""
        {
          "music_offset": null,
          "time_base": 480,
          "page_list": [],
          "tempo_list": [],
          "note_list": [{
            "has_sibling": null,
            "is_forward": false
          }],
          "event_order_list": []
        }
        """);

        Assert.Contains(issues, issue =>
            issue.Path == "$.music_offset");
        Assert.Contains(issues, issue =>
            issue.Path == "$.note_list[0].has_sibling");
    }

    [Fact]
    public void ExportFailureNeverReusesPreviousStoryboardAsCurrentSnapshot()
    {
        var bridge = new ToggleBridge();
        var service = new StoryboardPreviewService(bridge,
            new FakeResources());
        var context = new ProjectDataContext(MessageBroker.Default);

        service.GetSnapshot(context);
        bridge.Fail = true;
        Assert.Throws<Newtonsoft.Json.JsonSerializationException>(
            () => service.GetSnapshot(context));
        service.StartSession();

        Assert.Throws<Newtonsoft.Json.JsonSerializationException>(
            () => service.GetSnapshot(context));
    }

    private sealed class FakeBridge : IStoryboardCanonicalBridge
    {
        public EditorStoryboardDocument Synchronize(ProjectDataContext context) =>
            context.EditorStoryboard;
        public StoryboardRuntimeExportResult Export(ProjectDataContext context) =>
            new(new JObject(), []);
        public StoryboardRoot CreateLegacyProjection(ProjectDataContext context) =>
            new();
        public string ComputeLegacyProjectionHash(StoryboardRoot storyboard) => "";
    }

    private sealed class ToggleBridge : IStoryboardCanonicalBridge
    {
        public bool Fail { get; set; }
        public EditorStoryboardDocument Synchronize(ProjectDataContext context) =>
            context.EditorStoryboard;
        public StoryboardRuntimeExportResult Export(ProjectDataContext context) =>
            Fail
                ? new(new JObject(),
                [
                    new StoryboardImportIssue("FAILED", "$", "failed",
                        StoryboardDiagnosticSeverity.Error)
                ])
                : new(new JObject { ["sprites"] = new JArray() }, []);
        public StoryboardRoot CreateLegacyProjection(ProjectDataContext context) =>
            new();
        public string ComputeLegacyProjectionHash(StoryboardRoot storyboard) => "";
    }

    private sealed class FakeResources : IProjectResourceService
    {
        public string ResolvePath(string projectFilePath, string configuredPath) => configuredPath;
        public string? ResolvePath(ProjectDataContext context, ProjectResourceKind kind) => null;
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
