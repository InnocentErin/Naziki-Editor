using System;
using System.Collections.Generic;
using System.Linq;
using Naziki_Editor.Core;
using Naziki_Editor.Core.Compilation;
using Naziki_Editor.Core.Serialization;
using Naziki_Editor.Core.Storyboard.Corrections;
using Naziki_Editor.Models;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Naziki_Editor.Tests;

public sealed class StoryboardCompilationTests
{
    [Fact]
    public void VideoTemplateExpansionDoesNotWriteReadOnlyDiagnostics()
    {
        var root = new StoryboardRoot();
        root.templates["Alight"] = new C2Template
        {
            BaseState = new TemplateState { Opacity = .15f },
            Keyframes =
            [
                new TemplateState { RelativeTime = .05f, Opacity = 1 },
                new TemplateState
                {
                    RelativeTime = .05f,
                    Opacity = .15f,
                    Destroy = true
                }
            ]
        };
        root.videos.Add(new C2Video
        {
            BaseState = new VideoState { Time = 0, Path = "video.mp4" },
            Keyframes = [new VideoState { Time = 1, Template = "Alight" }]
        });

        var compiler = Compiler(root);
        var exception = Record.Exception(() => compiler.FlattenStoryboard(root));

        Assert.Null(exception);
        var states = Assert.Single(root.videos).Keyframes;
        Assert.Equal(2, states.Count);
        Assert.Equal(1f, states[0].Opacity);
        Assert.Equal(.15f, states[1].Opacity);
        Assert.True(states[1].Destroy);
        Assert.Empty(root.templates);
    }

    [Fact]
    public void MapperPreservesExplicitZeroAndAppliesUnknownFieldPrecedence()
    {
        var mapper = new StoryboardTemplatePropertyMapper();
        var target = new SpriteState
        {
            X = new UnitFloat { Value = 0, Unit = ReferenceUnit.World },
            UnknownProperties = { ["existing"] = 1 }
        };
        var templateBase = new TemplateState
        {
            X = new UnitFloat { Value = 2, Unit = ReferenceUnit.World },
            UnknownProperties =
            {
                ["existing"] = 2,
                ["future"] = true
            }
        };

        mapper.Apply(target, templateBase,
            Core.Abstractions.StoryboardTemplateApplyMode.FillMissing,
            "$.templates.test");

        Assert.Equal(0, target.X.Value);
        Assert.Equal(1, target.UnknownProperties["existing"]!.Value<int>());
        Assert.True(target.UnknownProperties["future"]!.Value<bool>());

        var keyframe = new TemplateState
        {
            UnknownProperties = { ["existing"] = 3 }
        };
        mapper.Apply(target, keyframe,
            Core.Abstractions.StoryboardTemplateApplyMode.Override,
            "$.templates.test.states[0]");
        Assert.Equal(3, target.UnknownProperties["existing"]!.Value<int>());
    }

    [Fact]
    public void UnsupportedTemplatePropertyWarnsEntityAndTemplateButDoesNotBlock()
    {
        var root = new StoryboardRoot();
        var template = new C2Template
        {
            BaseState = new TemplateState { Fov = 60 }
        };
        root.templates["cameraOnly"] = template;
        var sprite = new C2Sprite
        {
            BaseState = new SpriteState
            {
                Time = 0,
                Path = "sprite.png",
                Template = "cameraOnly"
            }
        };
        root.sprites.Add(sprite);
        var validator = Validator();

        var diagnostics = validator.Validate(root);

        Assert.DoesNotContain(diagnostics,
            item => item.Code == "TEMPLATE_PROPERTY_IGNORED" &&
                    item.Severity == StoryboardDiagnosticSeverity.Error);
        Assert.Contains(sprite.AllDiagnostics(),
            item => item.Code == "TEMPLATE_PROPERTY_IGNORED");
        Assert.Contains(template.AllDiagnostics(),
            item => item.Code == "TEMPLATE_PROPERTY_IGNORED");

        var compiler = Compiler(root);
        compiler.FlattenStoryboard(root);
        Assert.Contains(compiler.CompileWarnings,
            warning => warning.Contains("fov", StringComparison.Ordinal));
        Assert.DoesNotContain(
            Assert.Single(root.sprites).BaseState.UnknownProperties,
            property => property.Key == "fov");
    }

    [Fact]
    public void IncompatibleKnownTemplateValueIsAnExportBlockingError()
    {
        var root = new StoryboardRoot();
        root.templates["badFont"] = new C2Template
        {
            BaseState = new TemplateState { FontStyle = "not-an-integer" }
        };
        root.texts.Add(new C2Text
        {
            BaseState = new TextState
            {
                Time = 0,
                TextContent = "text",
                Template = "badFont"
            }
        });

        var diagnostic = Validator().Validate(root).First(
            item => item.Code == "TEMPLATE_PROPERTY_TYPE_INVALID" &&
                    item.Severity == StoryboardDiagnosticSeverity.Error);

        Assert.Contains("$.templates.badFont.font_style", diagnostic.Path);
        var exception = Assert.Throws<InvalidOperationException>(
            () => Compiler(root).FlattenStoryboard(root));
        Assert.Contains("font_style", exception.Message);
    }

    [Fact]
    public void ControllerMitosisNeverTreatsEditorCollectionsAsSceneProperties()
    {
        var root = new StoryboardRoot
        {
            controllers =
            [
                new C2SceneController
                {
                    BaseState = new ControllerState
                    {
                        Time = 0,
                        Fov = 53.2f,
                        Bloom = true,
                        BloomIntensity = 2
                    }
                }
            ]
        };
        root.controllers[0].BaseState.Diagnostics.Add(
            new StoryboardDiagnostic("TEST", "$", "test",
                StoryboardDiagnosticSeverity.Warning));
        root.controllers[0].BaseState.UnknownProperties["future"] = true;

        var exception = Record.Exception(() => Compiler(root).FlattenStoryboard(root));

        Assert.Null(exception);
        Assert.Equal(2, root.controllers.Count);
    }

    [Fact]
    public void BaseAndKeyframeTemplateReferencesAreBothFlattened()
    {
        var root = new StoryboardRoot();
        root.templates["plus"] = new C2Template
        {
            BaseState = new TemplateState
            {
                Perspective = true,
                Fov = 50
            },
            Keyframes =
            [
                new TemplateState { RelativeTime = .2f, Fov = 60 }
            ]
        };
        root.controllers.Add(new C2SceneController
        {
            BaseState = new ControllerState
            {
                Time = 0,
                Template = "plus"
            },
            Keyframes =
            [
                new ControllerState { Time = 1, Template = "plus" }
            ]
        });

        Compiler(root).FlattenStoryboard(root);

        Assert.Empty(root.templates);
        Assert.Null(root.controllers[0].BaseState.Template);
        Assert.True(root.controllers[0].BaseState.Perspective);
        Assert.Equal(50, root.controllers[0].BaseState.Fov);
        Assert.DoesNotContain(root.controllers.SelectMany(entity => entity.Keyframes),
            state => !string.IsNullOrEmpty(state.Template));
    }

    private static StoryboardCompiler Compiler(StoryboardRoot root)
    {
        var chart = new C2Chart
        {
            time_base = 480,
            note_list = []
        };
        var engine = new ChartTimeEngine(
            [new TempoEvent { tick = 0, value = 500000 }],
            chart.time_base);
        return new StoryboardCompiler(
            chart,
            engine,
            root.templates,
            new StoryboardTemplatePropertyMapper());
    }

    private static StoryboardDocumentValidator Validator()
    {
        var writer = new StoryboardDocumentWriter();
        var analyzer = new StoryboardCorrectionAnalyzer(
            new StoryboardTimeResolver(), writer);
        return new StoryboardDocumentValidator(
            analyzer, new StoryboardTemplatePropertyMapper());
    }
}
