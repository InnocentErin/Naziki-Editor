using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Features.Preview;
using Naziki_Editor.Models;
using Xunit;
using System.Collections.Generic;
using System;
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
        var service = new StoryboardPreviewService(new FakeWriter(), new FakeResources());
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
        var service = new StoryboardPreviewService(new FakeWriter(), new FakeResources());
        var count = 0;
        var subscription = service.Subscribe(_ => count++);
        subscription.Dispose();
        service.PublishReset("test");
        Assert.Equal(0, count);
    }

    [Fact]
    public void Snapshot_NormalizesNullableNoteFlagsForProductionChartParser()
    {
        var service = new StoryboardPreviewService(new FakeWriter(), new FakeResources());
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
        var note = (JObject)JObject.Parse(snapshot.ChartJson!)["note_list"]![0]!;

        Assert.False(note.Value<bool>("has_sibling"));
        Assert.False(note.Value<bool>("is_forward"));
        Assert.Null(note[nameof(C2Note.NoteDirection)]);
    }

    private sealed class FakeWriter : IStoryboardDocumentWriter
    {
        public string Write(StoryboardRoot document) => "{}";
        public string WriteNode(object node) => "{}";
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
