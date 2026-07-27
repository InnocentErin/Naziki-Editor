using Naziki_Editor.Core;
using Naziki_Editor.Core.Messaging;
using Naziki_Editor.Core.Timeline.Projection;
using Naziki_Editor.Models;
using Naziki_Editor.State;
using Xunit;
using System.Linq;
using Naziki_Editor.Core.Timeline.Models;
using System.Threading;

namespace Naziki_Editor.Tests;

public sealed class TimelineProjectionServiceTests
{
    [Fact]
    public void UsesBaseTimeAndMaximumExpandedStateTimeWithoutDestroy()
    {
        var context = Context();
        var entity = new C2Sprite
        {
            Id = "sprite",
            BaseState = new SpriteState { Time = 2f },
            Keyframes =
            [
                new SpriteState { RelativeTime = 1f },
                new SpriteState { AddTime = 2f }
            ]
        };

        var result = new TimelineProjectionService().BuildEntityProjection(entity, context);

        Assert.Equal(2, result.BaseStateTime);
        Assert.Equal(5, result.LastStateTime);
        Assert.Equal(3, result.Duration);
    }

    [Fact]
    public void RecursivelyExpandsNestedTemplates()
    {
        var context = Context();
        context.Storyboard.templates["child"] = new C2Template
        {
            Keyframes = [new TemplateState { RelativeTime = 2f }]
        };
        context.Storyboard.templates["parent"] = new C2Template
        {
            Keyframes =
            [
                new TemplateState { RelativeTime = 1f },
                new TemplateState { AddTime = 1f, Template = "child" }
            ]
        };
        var entity = new C2Text
        {
            BaseState = new TextState { Time = 10f },
            Keyframes = [new TextState { RelativeTime = 1f, Template = "parent" }]
        };

        var result = new TimelineProjectionService().BuildEntityProjection(entity, context);

        Assert.Equal(15, result.LastStateTime);
        Assert.Contains(result.States, s =>
            s.IsTemplateExpanded && s.TemplateSourcePath.SequenceEqual(["parent", "child"]));
    }

    [Fact]
    public void ResolvesTimeArraysAndHoldAnchors()
    {
        var context = Context();
        context.Chart.note_list.Add(new C2Note { id = 7, tick = 1000, hold_tick = 1000 });
        var entity = new C2Sprite
        {
            BaseState = new SpriteState { Time = "start:7" },
            Keyframes =
            [
                new SpriteState
                {
                    Time = new Newtonsoft.Json.Linq.JArray("start:7:1", "end:7")
                }
            ]
        };

        var result = new TimelineProjectionService().BuildEntityProjection(entity, context);

        Assert.Equal(1, result.BaseStateTime, 6);
        Assert.Equal(2, result.LastStateTime, 6);
    }

    [Fact]
    public void ReportsMissingAndCircularTemplatesWithoutCrashing()
    {
        var context = Context();
        context.Storyboard.templates["loop"] = new C2Template
        {
            Keyframes = [new TemplateState { RelativeTime = 1, Template = "loop" }]
        };
        var entity = new C2Sprite
        {
            BaseState = new SpriteState { Time = 0 },
            Keyframes =
            [
                new SpriteState { RelativeTime = 1, Template = "missing" },
                new SpriteState { RelativeTime = 1, Template = "loop" }
            ]
        };

        var result = new TimelineProjectionService().BuildEntityProjection(entity, context);

        Assert.Contains(result.Diagnostics, d => d.Code == "TIMELINE_TEMPLATE_MISSING");
        Assert.Contains(result.Diagnostics, d => d.Code == "TIMELINE_TEMPLATE_CYCLE");
    }

    [Fact]
    public void MicroSessionBuildsAllTracksFromOneEntityProjection()
    {
        var context = Context();
        var entity = new C2SceneController
        {
            BaseState = new ControllerState
            {
                Time = 2,
                Bloom = true,
                BloomIntensity = .25f
            },
            Keyframes =
            [
                new ControllerState { RelativeTime = 1, BloomIntensity = .75f }
            ]
        };
        var factory = new MicroTimelineSessionFactory(
            new TimelineProjectionService(),
            new PropertyMetadataCatalog());

        var session = factory.Build(
            new MicroEditorContext
            {
                Entity = entity,
                DisplayName = "controller",
                MacroStartTime = 2,
                MacroEndTime = 3,
                InitialPixelsPerSecond = 100
            },
            context,
            CancellationToken.None);

        var intensity = Assert.Single(
            session.Tracks,
            track => track.Descriptor.PropertyName == "BloomIntensity");
        Assert.Equal(.25f, intensity.BaseValue);
        Assert.Contains(intensity.Keyframes, frame => (float)frame.Value == .75f);
        Assert.True(session.ContentEndTime >= 8);
    }

    private static ProjectDataContext Context()
    {
        var chart = new C2Chart
        {
            time_base = 1000,
            tempo_list = [new TempoEvent { tick = 0, value = 1_000_000 }]
        };
        return new ProjectDataContext(MessageBroker.Default)
        {
            Storyboard = new StoryboardRoot(),
            Chart = chart,
            TimeEngine = new ChartTimeEngine(chart.tempo_list, chart.time_base)
        };
    }
}
