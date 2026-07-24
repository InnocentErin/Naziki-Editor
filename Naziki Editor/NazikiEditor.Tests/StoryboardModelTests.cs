using Naziki_Editor.Core;
using Naziki_Editor.Models;
using Newtonsoft.Json;
using Xunit;

namespace NazikiEditor.Tests;

/// <summary>
/// 故事板数据模型序列化测试
/// </summary>
public class StoryboardModelTests
{
    private readonly JsonSerializerSettings _settings;

    public StoryboardModelTests()
    {
        _settings = StoryboardSerializer.GetSettings();
    }

    [Fact]
    public void EmptyStoryboard_ShouldSerializeToValidJson()
    {
        var root = new StoryboardRoot();
        var json = StoryboardSerializer.ToJson(root);
        Assert.NotNull(json);
        Assert.Contains("\"sprites\"", json);
        Assert.Contains("\"texts\"", json);
        Assert.Contains("\"controllers\"", json);
        Assert.Contains("\"note_controllers\"", json);
        Assert.Contains("\"templates\"", json);
    }

    [Fact]
    public void Sprite_BasicProperties_SerializeCorrectly()
    {
        var root = new StoryboardRoot();
        root.sprites.Add(new C2Sprite
        {
            Id = "test_sprite",
            BaseState = new SpriteState
            {
                Time = 0f, Path = "sprite.png",
                Opacity = 1.0f, Layer = 1, Order = 0,
                Color = "#ffffff", PreserveAspect = true
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        Assert.Contains("\"id\": \"test_sprite\"", json);
        Assert.Contains("\"path\": \"sprite.png\"", json);
        Assert.Contains("\"opacity\"", json);
        Assert.Contains("\"layer\"", json);
        Assert.Contains("\"preserve_aspect\"", json);
    }

    [Fact]
    public void Sprite_WithKeyframes_ShouldIncludeStatesArray()
    {
        var root = new StoryboardRoot();
        root.sprites.Add(new C2Sprite
        {
            Id = "animated",
            BaseState = new SpriteState { Time = 0f, Path = "anim.png", Opacity = 0f },
            Keyframes = new List<SpriteState>
            {
                new() { Time = 1f, Opacity = 0.5f },
                new() { Time = 2f, Opacity = 1f },
                new() { Time = 3f, Opacity = 0f, Destroy = true }
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        Assert.Contains("\"states\"", json);
        Assert.Contains("\"destroy\"", json);
    }

    [Fact]
    public void Text_BasicProperties_SerializeCorrectly()
    {
        var root = new StoryboardRoot();
        root.texts.Add(new C2Text
        {
            Id = "title",
            BaseState = new TextState
            {
                Time = 0f, TextContent = "Hello World!",
                Size = 40f, Color = "#ff0000", LetterSpacing = 2f
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        Assert.Contains("\"text\": \"Hello World!\"", json);
        Assert.Contains("\"size\"", json);
        Assert.Contains("\"color\": \"#ff0000\"", json);
        Assert.Contains("\"letter_spacing\"", json);
    }

    [Fact]
    public void Line_WithPositions_SerializeCorrectly()
    {
        var root = new StoryboardRoot();
        root.lines.Add(new C2Line
        {
            Id = "scanline",
            BaseState = new LineState
            {
                Time = 0f, Opacity = 1f, Layer = 1, Order = 0,
                Width = 0.05f, Color = "#ffffff",
                Pos = new List<LinePosition>
                {
                    new() { X = new UnitFloat { Value = 0f, Unit = ReferenceUnit.NoteX }, Y = new UnitFloat { Value = 0f, Unit = ReferenceUnit.NoteY } },
                    new() { X = new UnitFloat { Value = 1f, Unit = ReferenceUnit.NoteX }, Y = new UnitFloat { Value = 0f, Unit = ReferenceUnit.NoteY } }
                }
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        Assert.Contains("\"pos\"", json);
        Assert.Contains("\"width\"", json);
        Assert.Contains("\"color\"", json);
    }

    [Fact]
    public void Controller_BasicProperties_SerializeCorrectly()
    {
        var root = new StoryboardRoot();
        root.controllers.Add(new C2SceneController
        {
            Id = "main_ctrl",
            BaseState = new ControllerState
            {
                Time = 0f, StoryboardOpacity = 1f, UiOpacity = 1f,
                Perspective = true, Fov = 53.2f, Size = 5f
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        Assert.Contains("\"storyboard_opacity\"", json);
        Assert.Contains("\"ui_opacity\"", json);
        Assert.Contains("\"perspective\"", json);
        Assert.Contains("\"fov\"", json);
    }

    [Fact]
    public void Controller_WithEffects_SerializeAllEffects()
    {
        var root = new StoryboardRoot();
        root.controllers.Add(new C2SceneController
        {
            Id = "effects",
            BaseState = new ControllerState
            {
                Time = 0f, Bloom = true, BloomIntensity = 2.5f,
                Chromatical = true, ChromaticalIntensity = 0.5f,
                Vignette = true, VignetteIntensity = 0.7f
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        Assert.Contains("\"bloom\"", json);
        Assert.Contains("\"bloom_intensity\"", json);
        Assert.Contains("\"chromatical\"", json);
        Assert.Contains("\"vignette\"", json);
    }

    [Fact]
    public void NoteController_SingleNote_SerializeCorrectly()
    {
        var root = new StoryboardRoot();
        root.note_controllers.Add(new C2NoteController
        {
            Id = "nc",
            BaseState = new NoteControllerState
            {
                Time = 0f, NoteTarget = 100,
                OverrideX = true,
                X = new UnitFloat { Value = 0.5f, Unit = ReferenceUnit.NoteX },
                OverrideY = true,
                Y = new UnitFloat { Value = 0.75f, Unit = ReferenceUnit.NoteY }
            }
        });

        var json = StoryboardSerializer.ToJson(root);
        Assert.Contains("\"note\": 100", json);
        Assert.Contains("\"override_x\": true", json);
        Assert.Contains("\"override_y\": true", json);
    }

    [Fact]
    public void ShouldSerializeId_SceneController_ReturnsFalse()
    {
        var ctrl = new C2SceneController { Id = "hidden", BaseState = new ControllerState { Time = 0f } };
        var method = typeof(StoryboardEntity<ControllerState>).GetMethod("ShouldSerializeId",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        var result = method!.Invoke(ctrl, null);
        Assert.False((bool)result!);
    }

    [Fact]
    public void ShouldSerializeId_NormalSprite_ReturnsTrue()
    {
        var sprite = new C2Sprite { Id = "visible", BaseState = new SpriteState { Time = 0f, Path = "test.png" } };
        var method = typeof(StoryboardEntity<SpriteState>).GetMethod("ShouldSerializeId",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        var result = method!.Invoke(sprite, null);
        Assert.True((bool)result!);
    }

    [Fact]
    public void EmptyJson_Deserialize_ShouldNotThrow()
    {
        var json = "{}";
        var result = JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings);
        Assert.NotNull(result);
    }

    [Fact]
    public void InvalidJson_Deserialize_ShouldThrow()
    {
        var json = "{ this is not valid }";
        Assert.ThrowsAny<JsonException>(() => JsonConvert.DeserializeObject<StoryboardRoot>(json, _settings));
    }
}