using Naziki_Editor.Core;
using Naziki_Editor.Core.Compilation;
using Naziki_Editor.Core.Serialization.Converters;
using Naziki_Editor.Models;
using Newtonsoft.Json;
using Xunit;

namespace NazikiEditor.Tests;

/// <summary>
/// 故事板集成测试：完整导入/导出/编译流程
/// </summary>
public class StoryboardIntegrationTests
{
    private readonly JsonSerializerSettings _settings;

    public StoryboardIntegrationTests()
    {
        _settings = StoryboardSerializer.GetSettings();
    }

    // ==========================================
    // 基础往返测试
    // ==========================================

    [Fact]
    public void SimpleSprite_SaveThenLoad_ShouldBeIdentical()
    {
        var original = new StoryboardRoot();
        original.sprites.Add(new C2Sprite
        {
            Id = "bg_sprite",
            BaseState = new SpriteState
            {
                Time = 0f, Path = "background.png",
                X = new UnitFloat { Value = 0.5f, Unit = ReferenceUnit.NoteX },
                Y = new UnitFloat { Value = 0.5f, Unit = ReferenceUnit.NoteY },
                Opacity = 1f, Layer = 0, Order = 0
            }
        });

        var json = StoryboardSerializer.ToJson(original);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        Assert.Single(reloaded!.sprites);
        var sprite = reloaded.sprites[0];
        Assert.Equal("bg_sprite", sprite.Id);
        Assert.Equal("background.png", sprite.BaseState.Path);
        Assert.Equal(1f, sprite.BaseState.Opacity);
        Assert.Equal(0, sprite.BaseState.Layer);
        Assert.Equal(0, sprite.BaseState.Order);
    }

    [Fact]
    public void ComplexSprite_WithKeyframes_ShouldPreserveAllFrames()
    {
        var original = new StoryboardRoot();
        original.sprites.Add(new C2Sprite
        {
            Id = "animated",
            BaseState = new SpriteState
            {
                Time = 0f, Path = "anim.png",
                Opacity = 0f, Easing = "easeOutQuad"
            },
            Keyframes = new List<SpriteState>
            {
                new() { Time = 1f, Opacity = 0.5f },
                new() { Time = 2f, Opacity = 1.0f },
                new() { Time = 3f, Opacity = 0f, Destroy = true }
            }
        });

        var json = StoryboardSerializer.ToJson(original);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        var sprite = reloaded!.sprites[0];
        Assert.Equal("easeOutQuad", sprite.BaseState.Easing);
        Assert.Equal(0f, sprite.BaseState.Opacity);
        Assert.Equal(3, sprite.Keyframes.Count);
        Assert.Equal(0.5f, sprite.Keyframes[0].Opacity);
        Assert.Equal(1.0f, sprite.Keyframes[1].Opacity);
        Assert.True(sprite.Keyframes[2].Destroy);
    }

    [Fact]
    public void Coordinates_Roundtrip_ShouldPreserveAllUnits()
    {
        var original = new StoryboardRoot();
        original.sprites.Add(new C2Sprite
        {
            Id = "coord_test",
            BaseState = new SpriteState
            {
                Time = 0f, Path = "coord.png",
                X = new UnitFloat { Value = 0.1f, Unit = ReferenceUnit.NoteX },
                Y = new UnitFloat { Value = 0.9f, Unit = ReferenceUnit.NoteY },
                W = new UnitFloat { Value = 400f, Unit = ReferenceUnit.StageX },
                H = new UnitFloat { Value = 300f, Unit = ReferenceUnit.StageY }
            },
            Keyframes = new List<SpriteState>
            {
                new() { Time = 1f, X = new UnitFloat { Value = 0.9f, Unit = ReferenceUnit.NoteX } }
            }
        });

        var json = StoryboardSerializer.ToJson(original);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        var sprite = reloaded!.sprites[0];
        Assert.NotNull(sprite.BaseState.X);
        Assert.Equal(0.1f, sprite.BaseState.X!.Value);
        Assert.Equal(ReferenceUnit.NoteX, sprite.BaseState.X.Unit);
        Assert.NotNull(sprite.BaseState.Y);
        Assert.Equal(0.9f, sprite.BaseState.Y!.Value);
        Assert.Equal(ReferenceUnit.NoteY, sprite.BaseState.Y.Unit);
        Assert.NotNull(sprite.BaseState.W);
        Assert.Equal(400f, sprite.BaseState.W!.Value);
        Assert.Equal(ReferenceUnit.StageX, sprite.BaseState.W.Unit);
        Assert.NotNull(sprite.Keyframes[0].X);
        Assert.Equal(0.9f, sprite.Keyframes[0].X!.Value);
        Assert.Equal(ReferenceUnit.NoteX, sprite.Keyframes[0].X.Unit);
    }

    [Fact]
    public void TripleRoundtrip_ShouldNotDegradeData()
    {
        var original = new StoryboardRoot();
        original.sprites.Add(new C2Sprite
        {
            Id = "triple_test",
            BaseState = new SpriteState
            {
                Time = 0f, Path = "triple.png",
                Opacity = 0.8f, Layer = 1, Order = 5,
                RotZ = 45f, ScaleX = 1.5f, ScaleY = 1.5f,
                PivotX = 0.5f, PivotY = 0.5f
            },
            Keyframes = new List<SpriteState>
            {
                new() { Time = 1f, RotZ = 90f },
                new() { Time = 2f, RotZ = 180f, Destroy = true }
            }
        });

        var json1 = StoryboardSerializer.ToJson(original);
        var round1 = JsonConvert.DeserializeObject<StoryboardRoot>(json1, _settings);
        var json2 = StoryboardSerializer.ToJson(round1!);
        var round2 = JsonConvert.DeserializeObject<StoryboardRoot>(json2, _settings);
        var json3 = StoryboardSerializer.ToJson(round2!);
        var round3 = JsonConvert.DeserializeObject<StoryboardRoot>(json3, _settings);

        Assert.NotNull(round3);
        var sprite = round3!.sprites[0];
        Assert.Equal("triple_test", sprite.Id);
        Assert.Equal("triple.png", sprite.BaseState.Path);
        Assert.Equal(0.8f, sprite.BaseState.Opacity);
        Assert.Equal(1, sprite.BaseState.Layer);
        Assert.Equal(5, sprite.BaseState.Order);
        Assert.Equal(45f, sprite.BaseState.RotZ);
        Assert.Equal(1.5f, sprite.BaseState.ScaleX);
        Assert.Equal(2, sprite.Keyframes.Count);
        Assert.Equal(90f, sprite.Keyframes[0].RotZ);
        Assert.Equal(180f, sprite.Keyframes[1].RotZ);
        Assert.True(sprite.Keyframes[1].Destroy);
    }

    // ==========================================
    // NoteController 往返测试
    // ==========================================

    [Fact]
    public void NoteController_SingleNote_ShouldPreserveTarget()
    {
        var original = new StoryboardRoot();
        original.note_controllers.Add(new C2NoteController
        {
            Id = "nc_test",
            BaseState = new NoteControllerState
            {
                Time = 0f, NoteTarget = 42,
                OverrideX = true,
                X = new UnitFloat { Value = 0.5f, Unit = ReferenceUnit.NoteX },
                OverrideY = true,
                Y = new UnitFloat { Value = 0.75f, Unit = ReferenceUnit.NoteY },
                OverrideRotZ = true, RotZ = 45f,
                NoteOpacityMultiplier = 0.5f, NoteSizeMultiplier = 1.2f
            }
        });

        var json = StoryboardSerializer.ToJson(original);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        var nc = reloaded!.note_controllers[0];
        Assert.NotNull(nc.BaseState.NoteTarget);
        Assert.True(nc.BaseState.OverrideX);
        Assert.True(nc.BaseState.OverrideY);
        Assert.True(nc.BaseState.OverrideRotZ);
        Assert.Equal(45f, nc.BaseState.RotZ);
        Assert.Equal(0.5f, nc.BaseState.NoteOpacityMultiplier);
        Assert.Equal(1.2f, nc.BaseState.NoteSizeMultiplier);
    }

    // ==========================================
    // 多实体类型往返测试
    // ==========================================

    [Fact]
    public void AllEntityTypes_Roundtrip_ShouldPreserveCounts()
    {
        var original = new StoryboardRoot();
        original.sprites.Add(new C2Sprite { Id = "s1", BaseState = new SpriteState { Time = 0f, Path = "s1.png", Opacity = 1f } });
        original.sprites.Add(new C2Sprite { Id = "s2", BaseState = new SpriteState { Time = 0f, Path = "s2.png", Opacity = 1f } });
        original.texts.Add(new C2Text { Id = "t1", BaseState = new TextState { Time = 0f, TextContent = "Hello", Size = 30f } });
        original.lines.Add(new C2Line { Id = "l1", BaseState = new LineState { Time = 0f, Opacity = 1f } });
        original.controllers.Add(new C2SceneController { Id = "c1", BaseState = new ControllerState { Time = 0f } });
        original.note_controllers.Add(new C2NoteController { Id = "n1", BaseState = new NoteControllerState { Time = 0f, NoteTarget = 1 } });

        var json = StoryboardSerializer.ToJson(original);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        Assert.Equal(2, reloaded!.sprites.Count);
        Assert.Single(reloaded.texts);
        Assert.Single(reloaded.lines);
        Assert.Single(reloaded.controllers);
        Assert.Single(reloaded.note_controllers);
    }

    // ==========================================
    // Controller 效果属性往返测试
    // ==========================================

    [Fact]
    public void Controller_WithEffects_ShouldPreserveEffects()
    {
        var original = new StoryboardRoot();
        original.controllers.Add(new C2SceneController
        {
            Id = "effects_ctrl",
            BaseState = new ControllerState
            {
                Time = 0f, Bloom = true, BloomIntensity = 3f,
                Chromatical = true, ChromaticalIntensity = 0.7f,
                Noise = true, NoiseIntensity = 0.4f,
                Glitch = true, GlitchIntensity = 0.5f,
                Perspective = true, Fov = 60f,
                ColorFilter = true, ColorFilterColor = "#00ff00"
            }
        });

        var json = StoryboardSerializer.ToJson(original);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        var ctrl = reloaded!.controllers[0];
        Assert.True(ctrl.BaseState.Bloom);
        Assert.Equal(3f, ctrl.BaseState.BloomIntensity);
        Assert.True(ctrl.BaseState.Chromatical);
        Assert.Equal(0.7f, ctrl.BaseState.ChromaticalIntensity);
        Assert.True(ctrl.BaseState.Noise);
        Assert.Equal(0.4f, ctrl.BaseState.NoiseIntensity);
        Assert.True(ctrl.BaseState.Glitch);
        Assert.Equal(0.5f, ctrl.BaseState.GlitchIntensity);
        Assert.True(ctrl.BaseState.Perspective);
        Assert.Equal(60f, ctrl.BaseState.Fov);
        Assert.True(ctrl.BaseState.ColorFilter);
        Assert.Equal("#00ff00", ctrl.BaseState.ColorFilterColor);
    }

    // ==========================================
    // 模板往返测试
    // ==========================================

    [Fact]
    public void Templates_Roundtrip_ShouldPreserveTemplates()
    {
        var original = new StoryboardRoot();
        original.templates["fadeIn"] = new C2Template
        {
            BaseState = new TemplateState { Opacity = 0f, Easing = "easeInQuad" },
            Keyframes = new List<TemplateState>
            {
                new() { RelativeTime = 0.5f, Opacity = 1f },
                new() { RelativeTime = 0.5f, Opacity = 0f, Destroy = true }
            }
        };
        original.sprites.Add(new C2Sprite
        {
            Id = "templated_sprite",
            BaseState = new SpriteState { Time = 0f, Path = "test.png", Template = "fadeIn" }
        });

        var json = StoryboardSerializer.ToJson(original);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        Assert.True(reloaded!.templates.ContainsKey("fadeIn"));
        Assert.Equal("fadeIn", reloaded.sprites[0].BaseState.Template);
    }

    // ==========================================
    // NoteSelector 往返测试
    // ==========================================

    [Fact]
    public void NoteController_WithSelector_ShouldPreserveSelector()
    {
        var original = new StoryboardRoot();
        original.note_controllers.Add(new C2NoteController
        {
            Id = "nc_selector",
            BaseState = new NoteControllerState
            {
                Time = 0f,
                NoteTarget = new NoteSelectorModel
                {
                    Type = new List<int> { 0, 1, 2 },
                    Start = 10, End = 100,
                    Direction = 1, MinX = 0f, MaxX = 0.5f
                },
                OverrideX = true,
                X = new UnitFloat { Value = 0.3f, Unit = ReferenceUnit.NoteX }
            }
        });

        var json = StoryboardSerializer.ToJson(original);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        var reExportedJson = StoryboardSerializer.ToJson(reloaded!);
        Assert.Contains("\"type\"", reExportedJson);
        Assert.Contains("\"start\"", reExportedJson);
        Assert.Contains("\"end\"", reExportedJson);
        Assert.Contains("\"direction\"", reExportedJson);
        Assert.Contains("\"min_x\"", reExportedJson);
        Assert.Contains("\"max_x\"", reExportedJson);
    }

    // ==========================================
    // TargetId 实体测试
    // ==========================================

    [Fact]
    public void TargetId_Entity_ShouldNotSerializeId()
    {
        var root = new StoryboardRoot();
        var controlBoard = new C2Sprite
        {
            Id = "should_be_hidden",
            TargetId = "main_sprite",
            BaseState = new SpriteState { Time = 0f, Path = "child.png" }
        };
        root.sprites.Add(controlBoard);

        var parser = new StoryboardParser(new NullErrorHandler());
        parser.StandardizeStoryboardIds(root, new NazikiProjectModel());

        var json = StoryboardSerializer.ToJson(root);

        Assert.Contains("\"target_id\": \"main_sprite\"", json);
        Assert.DoesNotContain("\"id\": \"should_be_hidden\"", json);
    }

    // ==========================================
    // 大量数据测试
    // ==========================================

    [Fact]
    public void LargeNumberOfSprites_ShouldNotFail()
    {
        var root = new StoryboardRoot();
        for (int i = 0; i < 100; i++)
        {
            root.sprites.Add(new C2Sprite
            {
                Id = $"sprite_{i}",
                BaseState = new SpriteState
                {
                    Time = i * 0.5f, Path = $"sprite_{i}.png",
                    Opacity = (float)(i % 10) / 10f
                }
            });
        }

        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        Assert.Equal(100, reloaded!.sprites.Count);
    }

    [Fact]
    public void LargeNumberOfKeyframes_ShouldNotFail()
    {
        var root = new StoryboardRoot();
        var keyframes = new List<SpriteState>();
        for (int i = 0; i < 500; i++)
        {
            keyframes.Add(new SpriteState { Time = i * 0.1f, Opacity = (float)(i % 10) / 10f });
        }

        root.sprites.Add(new C2Sprite
        {
            Id = "many_keyframes",
            BaseState = new SpriteState { Time = 0f, Path = "anim.png", Opacity = 0f },
            Keyframes = keyframes
        });

        var json = StoryboardSerializer.ToJson(root);
        var reloaded = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);

        Assert.NotNull(reloaded);
        Assert.Equal(500, reloaded!.sprites[0].Keyframes.Count);
    }
}