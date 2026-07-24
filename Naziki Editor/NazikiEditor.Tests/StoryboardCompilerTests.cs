using Naziki_Editor.Core;
using Naziki_Editor.Core.Compilation;
using Naziki_Editor.Models;
using Xunit;

namespace NazikiEditor.Tests;

/// <summary>
/// 故事板编译器测试：展平、模板展开、细胞分裂
/// </summary>
public class StoryboardCompilerTests
{
    [Fact]
    public void Flatten_EmptyRoot_ShouldNotThrow()
    {
        var root = new StoryboardRoot();
        var compiler = CreateCompiler();
        var ex = Record.Exception(() => compiler.FlattenStoryboard(root));
        Assert.Null(ex);
    }

    [Fact]
    public void Flatten_SimpleKeyframes_ShouldKeepThem()
    {
        var root = new StoryboardRoot();
        root.sprites.Add(new C2Sprite
        {
            Id = "test",
            BaseState = new SpriteState { Time = 0f, Path = "test.png", Opacity = 0f },
            Keyframes = new List<SpriteState>
            {
                new() { Time = 1f, Opacity = 0.5f },
                new() { Time = 2f, Opacity = 1f }
            }
        });

        var compiler = CreateCompiler();
        compiler.FlattenStoryboard(root);

        Assert.Equal(2, root.sprites[0].Keyframes.Count);
        Assert.Equal(1f, (float)root.sprites[0].Keyframes[0].Time);
        Assert.Equal(2f, (float)root.sprites[0].Keyframes[1].Time);
    }

    [Fact]
    public void Flatten_WithRelativeTime_ShouldComputeAbsolute()
    {
        var root = new StoryboardRoot();
        root.sprites.Add(new C2Sprite
        {
            Id = "rel",
            BaseState = new SpriteState { Time = 5f, Path = "rel.png", Opacity = 0f },
            Keyframes = new List<SpriteState>
            {
                new() { RelativeTime = 2.5f, Opacity = 0.5f },
                new() { RelativeTime = 3f, Opacity = 1f }
            }
        });

        var compiler = CreateCompiler();
        compiler.FlattenStoryboard(root);

        var kf = root.sprites[0].Keyframes;
        Assert.Equal(7.5f, (float)kf[0].Time);
        Assert.Equal(10.5f, (float)kf[1].Time);
        Assert.Null(kf[0].RelativeTime);
    }

    [Fact]
    public void Flatten_WithAddTime_ShouldComputeCorrectly()
    {
        var root = new StoryboardRoot();
        root.sprites.Add(new C2Sprite
        {
            Id = "add",
            BaseState = new SpriteState { Time = 5f, Path = "add.png", Opacity = 0f },
            Keyframes = new List<SpriteState>
            {
                new() { RelativeTime = 2.5f, Opacity = 0.5f },
                new() { AddTime = 3f, Opacity = 1f }
            }
        });

        var compiler = CreateCompiler();
        compiler.FlattenStoryboard(root);

        var kf = root.sprites[0].Keyframes;
        Assert.Equal(7.5f, (float)kf[0].Time);
        Assert.Equal(10.5f, (float)kf[1].Time);
    }

    [Fact]
    public void Flatten_WithTemplate_ShouldExpand()
    {
        var root = new StoryboardRoot();
        var templates = new Dictionary<string, C2Template>();
        templates["fadeIn"] = new C2Template
        {
            BaseState = new TemplateState { Opacity = 0f },
            Keyframes = new List<TemplateState>
            {
                new() { RelativeTime = 0.5f, Opacity = 1f },
                new() { RelativeTime = 0.5f, Opacity = 0f, Destroy = true }
            }
        };
        root.templates = templates;

        root.sprites.Add(new C2Sprite
        {
            Id = "templated",
            BaseState = new SpriteState { Time = 0f, Path = "template.png", Template = "fadeIn" }
        });

        var compiler = CreateCompiler(templates);
        compiler.FlattenStoryboard(root);

        var kf = root.sprites[0].Keyframes;
        Assert.Equal(2, kf.Count);
        Assert.Equal(0.5f, (float)kf[0].Time);
        Assert.Equal(1f, kf[0].Opacity);
        Assert.Equal(1f, (float)kf[1].Time);
        Assert.Equal(0f, kf[1].Opacity);
        Assert.True(kf[1].Destroy);
    }

    [Fact]
    public void Flatten_TimeArray_ShouldExpand()
    {
        var root = new StoryboardRoot();
        root.sprites.Add(new C2Sprite
        {
            Id = "array",
            BaseState = new SpriteState { Time = 0f, Path = "array.png", Opacity = 0f },
            Keyframes = new List<SpriteState>
            {
                new() { Time = new List<object> { 1f, 2f, 3f }, Opacity = 1f }
            }
        });

        var compiler = CreateCompiler();
        compiler.FlattenStoryboard(root);

        var kf = root.sprites[0].Keyframes;
        Assert.Equal(3, kf.Count);
        Assert.Equal(1f, (float)kf[0].Time);
        Assert.Equal(2f, (float)kf[1].Time);
        Assert.Equal(3f, (float)kf[2].Time);
    }

    [Fact]
    public void Mitosis_MixedController_ShouldSplit()
    {
        var root = new StoryboardRoot();
        root.controllers.Add(new C2SceneController
        {
            Id = "mixed",
            BaseState = new ControllerState
            {
                Time = 0f, Fov = 53.2f, Bloom = true, BloomIntensity = 2f
            }
        });

        var compiler = CreateCompiler();
        compiler.FlattenStoryboard(root);

        Assert.Equal(2, root.controllers.Count);
        Assert.Contains(root.controllers, c => c.EditorMode == "Camera");
        Assert.Contains(root.controllers, c => c.EditorMode == "Effects");
    }

    [Fact]
    public void Mitosis_PureCameraController_ShouldNotSplit()
    {
        var root = new StoryboardRoot();
        root.controllers.Add(new C2SceneController
        {
            Id = "pure",
            BaseState = new ControllerState { Time = 0f, Fov = 53.2f, Perspective = true }
        });

        var compiler = CreateCompiler();
        compiler.FlattenStoryboard(root);

        Assert.Single(root.controllers);
        Assert.Equal("Camera", root.controllers[0].EditorMode);
    }

    [Fact]
    public void CircularTemplate_ShouldNotInfiniteLoop()
    {
        var root = new StoryboardRoot();
        var templates = new Dictionary<string, C2Template>();
        templates["loop_a"] = new C2Template
        {
            BaseState = new TemplateState { Opacity = 0f },
            Keyframes = new List<TemplateState> { new() { Template = "loop_b", RelativeTime = 0.5f } }
        };
        templates["loop_b"] = new C2Template
        {
            BaseState = new TemplateState { Opacity = 1f },
            Keyframes = new List<TemplateState> { new() { Template = "loop_a", RelativeTime = 0.5f } }
        };
        root.templates = templates;

        root.sprites.Add(new C2Sprite
        {
            Id = "loop",
            BaseState = new SpriteState { Time = 0f, Path = "loop.png", Template = "loop_a" }
        });

        var compiler = CreateCompiler(templates);
        var ex = Record.Exception(() => compiler.FlattenStoryboard(root));
        Assert.Null(ex); // 不应抛出异常
        Assert.NotEmpty(compiler.CompileWarnings); // 应有警告
    }

    private static StoryboardCompiler CreateCompiler(Dictionary<string, C2Template>? templates = null)
    {
        var chart = new C2Chart
        {
            time_base = 480,
            note_list = new List<C2Note>
            {
                new() { id = 1, tick = 0 },
                new() { id = 2, tick = 480 }
            }
        };

        var tempoList = new List<TempoEvent> { new() { tick = 0, value = 500000 } };
        var engine = new ChartTimeEngine(tempoList, 480);
        var compilerTemplates = templates ?? new Dictionary<string, C2Template>();

        return new StoryboardCompiler(chart, engine, compilerTemplates);
    }
}