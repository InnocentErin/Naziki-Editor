using System;
using Naziki_Editor.Core.Common;
using Naziki_Editor.Core.Messaging;
using Naziki_Editor.Core.Serialization;
using Naziki_Editor.Core.Storyboard.Corrections;
using Naziki_Editor.Models;
using Naziki_Editor.State;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Naziki_Editor.Tests;

public sealed class StoryboardCorrectionTests
{
    [Fact]
    public void MissingBaseTimePromotesFirstArrayOccurrenceAndMergesProperties()
    {
        var root = new StoryboardRoot
        {
            sprites =
            [
                new C2Sprite
                {
                    Id = "sprite",
                    BaseState = new SpriteState { Path = "a.png", Opacity = .1f },
                    Keyframes =
                    [
                        new SpriteState
                        {
                            Time = new JArray(3, 4),
                            Opacity = .8f,
                            UnknownProperties = { ["future"] = true }
                        }
                    ]
                }
            ]
        };
        var services = Services();
        var report = services.Analyzer.Scan(root, Context(root));
        var issue = Assert.Single(report.Issues,
            item => item.Kind == StoryboardCorrectionKind.MissingBaseTime);

        var corrected = services.Service.Apply(root, Context(root),
            new StoryboardCorrectionPlan
            {
                DocumentFingerprint = report.DocumentFingerprint,
                IssueId = issue.Id,
                KeepParticipantIndex = 0
            });

        var sprite = Assert.Single(corrected.sprites);
        Assert.Equal("3", sprite.BaseState.Time.ToString());
        Assert.Equal(.8f, sprite.BaseState.Opacity);
        Assert.True((bool)sprite.BaseState.UnknownProperties["future"]);
        var remaining = Assert.IsType<JArray>(Assert.Single(sprite.Keyframes).Time);
        Assert.Equal("4", Assert.Single(remaining).ToString());
    }

    [Fact]
    public void TriggerOnlyEntityIsAValidMissingTimeException()
    {
        var root = new StoryboardRoot
        {
            texts =
            [
                new C2Text
                {
                    Id = "spawned",
                    Keyframes = [new TextState { RelativeTime = .5f, Opacity = 0 }]
                }
            ],
            triggers = [new C2Trigger { Type = "noteClear", Spawn = ["spawned"] }]
        };
        var report = Services().Analyzer.Scan(root, Context(root));

        Assert.DoesNotContain(report.Issues,
            item => item.Kind == StoryboardCorrectionKind.MissingBaseTime);
    }

    [Fact]
    public void GroupsThreeStatesAtTheSameEffectiveTime()
    {
        var sprite = new C2Sprite
        {
            Id = "s",
            BaseState = new SpriteState { Time = 0 },
            Keyframes =
            [
                new SpriteState { Time = 1, Opacity = .2f },
                new SpriteState { Time = 1, Scale = 2 },
                new SpriteState { Time = new JArray(1, 2), RotZ = 3 }
            ]
        };
        var root = new StoryboardRoot { sprites = [sprite] };

        var issue = Assert.Single(Services().Analyzer.Scan(root, Context(root)).Issues,
            item => item.Kind == StoryboardCorrectionKind.SameTimeConflict);
        Assert.Equal(3, issue.Participants.Count);
    }

    [Fact]
    public void RemovesOnlyConflictingArrayOccurrenceAndMigratesSelectedProperty()
    {
        var root = ConflictRoot();
        var services = Services();
        var context = Context(root);
        var report = services.Analyzer.Scan(root, context);
        var issue = Assert.Single(report.Issues,
            item => item.Kind == StoryboardCorrectionKind.SameTimeConflict);
        var array = Assert.Single(issue.Participants, item => item.ArrayIndex == 0);
        var scalar = Assert.Single(issue.Participants, item => item.ArrayIndex is null);

        var corrected = services.Service.Apply(root, context,
            new StoryboardCorrectionPlan
            {
                DocumentFingerprint = report.DocumentFingerprint,
                IssueId = issue.Id,
                KeepParticipantIndex = scalar.ParticipantIndex,
                Losers =
                [
                    new StoryboardLoserCorrection
                    {
                        ParticipantIndex = array.ParticipantIndex,
                        DeleteScope = StoryboardDeleteScope.ConflictOccurrence,
                        PropertyMigrations =
                        [
                            new("opacity", StoryboardPropertyMigrationMode.Add)
                        ]
                    }
                ]
            });

        var sprite = Assert.Single(corrected.sprites);
        var arrayState = Assert.Single(sprite.Keyframes, state => state.Time is JArray);
        Assert.Equal("2", Assert.Single((JArray)arrayState.Time).ToString());
        var scalarState = Assert.Single(sprite.Keyframes, state => state.Time is not JArray);
        Assert.Equal(.2f, scalarState.Opacity);
        Assert.Equal(3, scalarState.Scale);
    }

    [Fact]
    public void SplitsArrayKeeperBeforeMigratingProperties()
    {
        var root = ConflictRoot();
        var services = Services();
        var context = Context(root);
        var report = services.Analyzer.Scan(root, context);
        var issue = Assert.Single(report.Issues,
            item => item.Kind == StoryboardCorrectionKind.SameTimeConflict);
        var array = Assert.Single(issue.Participants, item => item.ArrayIndex == 0);
        var scalar = Assert.Single(issue.Participants, item => item.ArrayIndex is null);

        var corrected = services.Service.Apply(root, context,
            new StoryboardCorrectionPlan
            {
                DocumentFingerprint = report.DocumentFingerprint,
                IssueId = issue.Id,
                KeepParticipantIndex = array.ParticipantIndex,
                Losers =
                [
                    new StoryboardLoserCorrection
                    {
                        ParticipantIndex = scalar.ParticipantIndex,
                        DeleteScope = StoryboardDeleteScope.EntireKeyframe,
                        PropertyMigrations =
                        [
                            new("scale", StoryboardPropertyMigrationMode.Add)
                        ]
                    }
                ]
            });

        var sprite = Assert.Single(corrected.sprites);
        Assert.Equal(2, sprite.Keyframes.Count);
        var remainingArray = Assert.Single(sprite.Keyframes, state => state.Time is JArray);
        Assert.Equal("2", Assert.Single((JArray)remainingArray.Time).ToString());
        var split = Assert.Single(sprite.Keyframes, state => state.Time is not JArray);
        Assert.Equal("1", split.Time.ToString());
        Assert.Equal(.2f, split.Opacity);
        Assert.Equal(3, split.Scale);
    }

    [Fact]
    public void EntireArrayFrameCanBeDeletedAndConflictingValueOverwritten()
    {
        var root = ConflictRoot();
        root.sprites[0].Keyframes[1].Opacity = .9f;
        var services = Services();
        var context = Context(root);
        var report = services.Analyzer.Scan(root, context);
        var issue = Assert.Single(report.Issues,
            item => item.Kind == StoryboardCorrectionKind.SameTimeConflict);
        var array = Assert.Single(issue.Participants, item => item.ArrayIndex == 0);
        var scalar = Assert.Single(issue.Participants, item => item.ArrayIndex is null);

        var corrected = services.Service.Apply(root, context,
            new StoryboardCorrectionPlan
            {
                DocumentFingerprint = report.DocumentFingerprint,
                IssueId = issue.Id,
                KeepParticipantIndex = scalar.ParticipantIndex,
                Losers =
                [
                    new StoryboardLoserCorrection
                    {
                        ParticipantIndex = array.ParticipantIndex,
                        DeleteScope = StoryboardDeleteScope.EntireKeyframe,
                        PropertyMigrations =
                        [
                            new("opacity", StoryboardPropertyMigrationMode.Overwrite)
                        ]
                    }
                ]
            });

        var kept = Assert.Single(Assert.Single(corrected.sprites).Keyframes);
        Assert.Equal(.2f, kept.Opacity);
        Assert.Equal("1", kept.Time.ToString());
    }

    [Fact]
    public void SafeMergeKeepsRichestFrameAndAddsOnlyMissingProperties()
    {
        var root = new StoryboardRoot
        {
            sprites =
            [
                new C2Sprite
                {
                    Id = "safe",
                    BaseState = new SpriteState { Time = 0 },
                    Keyframes =
                    [
                        new SpriteState { Time = 1, Opacity = .5f },
                        new SpriteState
                        {
                            Time = 1,
                            Opacity = .5f,
                            Scale = 2,
                            UnknownProperties = { ["future"] = "kept" }
                        },
                        new SpriteState { Time = 1, RotZ = 45 }
                    ]
                }
            ]
        };
        var services = Services();
        var context = Context(root);
        var report = services.Analyzer.Scan(root, context);
        var issue = Assert.Single(report.Issues,
            item => item.Kind == StoryboardCorrectionKind.SameTimeConflict);

        Assert.True(StoryboardCorrectionPolicy.CanSafelyMerge(issue));
        var corrected = services.Service.Apply(
            root,
            context,
            StoryboardCorrectionPolicy.BuildSafeMergePlan(report, issue));

        var kept = Assert.Single(Assert.Single(corrected.sprites).Keyframes);
        Assert.Equal(.5f, kept.Opacity);
        Assert.Equal(2, kept.Scale);
        Assert.Equal(45, kept.RotZ);
        Assert.Equal("kept", kept.UnknownProperties["future"]!.Value<string>());
    }

    [Fact]
    public void SafeMergeIsRejectedWhenSamePropertyValuesDiffer()
    {
        var root = ConflictRoot();
        root.sprites[0].Keyframes[1].Opacity = .9f;
        var services = Services();
        var report = services.Analyzer.Scan(root, Context(root));
        var issue = Assert.Single(report.Issues,
            item => item.Kind == StoryboardCorrectionKind.SameTimeConflict);

        Assert.False(StoryboardCorrectionPolicy.CanSafelyMerge(issue));
        Assert.Throws<InvalidOperationException>(() =>
            StoryboardCorrectionPolicy.BuildSafeMergePlan(report, issue));
    }

    [Theory]
    [InlineData(0.2, 1.2)]
    [InlineData(-0.25, 0.75)]
    public void OffsetsSelectedScalarKeyframe(double delta, double expected)
    {
        var root = ConflictRoot();
        var services = Services();
        var context = Context(root);
        var report = services.Analyzer.Scan(root, context);
        var issue = Assert.Single(report.Issues,
            item => item.Kind == StoryboardCorrectionKind.SameTimeConflict);
        var scalar = Assert.Single(issue.Participants, item => item.ArrayIndex is null);

        var corrected = services.Service.Apply(root, context,
            new StoryboardCorrectionPlan
            {
                DocumentFingerprint = report.DocumentFingerprint,
                IssueId = issue.Id,
                TimeOffset = new StoryboardTimeOffsetCorrection(
                    scalar.ParticipantIndex, delta)
            });

        var moved = Assert.Single(
            Assert.Single(corrected.sprites).Keyframes,
            state => state.Time is not JArray);
        Assert.Equal(expected, Convert.ToDouble(moved.Time), 6);
        Assert.DoesNotContain(
            services.Analyzer.Scan(corrected, Context(corrected)).Issues,
            item => item.Kind == StoryboardCorrectionKind.SameTimeConflict);
    }

    [Fact]
    public void OffsettingArrayOccurrenceSplitsOnlyThatOccurrence()
    {
        var root = ConflictRoot();
        var services = Services();
        var context = Context(root);
        var report = services.Analyzer.Scan(root, context);
        var issue = Assert.Single(report.Issues,
            item => item.Kind == StoryboardCorrectionKind.SameTimeConflict);
        var arrayOccurrence = Assert.Single(issue.Participants, item => item.ArrayIndex == 0);

        var corrected = services.Service.Apply(root, context,
            new StoryboardCorrectionPlan
            {
                DocumentFingerprint = report.DocumentFingerprint,
                IssueId = issue.Id,
                TimeOffset = new StoryboardTimeOffsetCorrection(
                    arrayOccurrence.ParticipantIndex, .2)
            });

        var sprite = Assert.Single(corrected.sprites);
        var remainingArray = Assert.Single(sprite.Keyframes, state => state.Time is JArray);
        Assert.Equal("2", Assert.Single((JArray)remainingArray.Time).ToString());
        var moved = Assert.Single(sprite.Keyframes,
            state => state.Time is not JArray && Math.Abs(Convert.ToDouble(state.Time) - 1.2) < 1e-6);
        Assert.Equal(.2f, moved.Opacity);
    }

    [Fact]
    public void RejectsCorrectionPlanAfterDocumentChanges()
    {
        var root = ConflictRoot();
        var services = Services();
        var context = Context(root);
        var report = services.Analyzer.Scan(root, context);
        var issue = Assert.Single(report.Issues,
            item => item.Kind == StoryboardCorrectionKind.SameTimeConflict);
        root.sprites[0].BaseState.Path = "changed.png";

        Assert.Throws<InvalidOperationException>(() =>
            services.Service.Apply(root, context,
                new StoryboardCorrectionPlan
                {
                    DocumentFingerprint = report.DocumentFingerprint,
                    IssueId = issue.Id,
                    KeepParticipantIndex = issue.Participants[0].ParticipantIndex
                }));
    }

    [Fact]
    public void PropertyEditorRejectsReadOnlyAndInternalProperties()
    {
        var service = new PropertyEditorService();
        var diagnostics = typeof(SpriteState).GetProperty(nameof(SpriteState.Diagnostics))!;
        var opacity = typeof(SpriteState).GetProperty(nameof(SpriteState.Opacity))!;

        Assert.False(service.IsEditableProperty(diagnostics));
        Assert.True(service.IsEditableProperty(opacity));
    }

    private static StoryboardRoot ConflictRoot() => new()
    {
        sprites =
        [
            new C2Sprite
            {
                Id = "s",
                BaseState = new SpriteState { Time = 0 },
                Keyframes =
                [
                    new SpriteState { Time = new JArray(1, 2), Opacity = .2f },
                    new SpriteState { Time = 1, Scale = 3 }
                ]
            }
        ]
    };

    private static (StoryboardCorrectionAnalyzer Analyzer, StoryboardCorrectionService Service)
        Services()
    {
        var writer = new StoryboardDocumentWriter();
        var analyzer = new StoryboardCorrectionAnalyzer(new StoryboardTimeResolver(), writer);
        var service = new StoryboardCorrectionService(
            analyzer, writer, new EditorSnapshotSerializer());
        return (analyzer, service);
    }

    private static ProjectDataContext Context(StoryboardRoot root) =>
        new(MessageBroker.Default) { Storyboard = root };
}
