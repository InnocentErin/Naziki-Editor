using Naziki_Editor.Core;
using Naziki_Editor.Models;
using Xunit;

namespace NazikiEditor.Tests;

/// <summary>
/// 验证器测试：时空冲突检测
/// </summary>
public class StoryboardValidatorTests
{
    [Fact]
    public void NoConflict_ShouldPass()
    {
        var sprite = new C2Sprite
        {
            Id = "valid",
            BaseState = new SpriteState { Time = 0f, Path = "test.png", Opacity = 0f },
            Keyframes = new List<SpriteState>
            {
                new() { Time = 1f, Opacity = 0.5f },
                new() { Time = 2f, Opacity = 1f }
            }
        };

        var (isValid, error) = StoryboardValidator.ValidateStateConflicts(sprite);
        Assert.True(isValid);
        Assert.Empty(error);
    }

    [Fact]
    public void SamePropertyAtSameTime_ShouldFail()
    {
        var sprite = new C2Sprite
        {
            Id = "conflict",
            BaseState = new SpriteState { Time = 0f, Path = "test.png", Opacity = 0f },
            Keyframes = new List<SpriteState>
            {
                new() { Time = 1f, Opacity = 0.5f },
                new() { Time = 1f, Opacity = 1f }
            }
        };

        var (isValid, error) = StoryboardValidator.ValidateStateConflicts(sprite);
        Assert.False(isValid);
        Assert.NotEmpty(error);
        Assert.Contains("冲突", error);
    }

    [Fact]
    public void BaseStateConflictWithKeyframe_ShouldFail()
    {
        var sprite = new C2Sprite
        {
            Id = "base_conflict",
            BaseState = new SpriteState { Time = 1f, Path = "test.png", Opacity = 0.5f },
            Keyframes = new List<SpriteState>
            {
                new() { Time = 1f, Opacity = 1f }
            }
        };

        var (isValid, error) = StoryboardValidator.ValidateStateConflicts(sprite);
        Assert.False(isValid);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void EmptyKeyframes_ShouldPass()
    {
        var sprite = new C2Sprite
        {
            Id = "empty",
            BaseState = new SpriteState { Time = 0f, Path = "test.png", Opacity = 1f }
        };

        var (isValid, error) = StoryboardValidator.ValidateStateConflicts(sprite);
        Assert.True(isValid);
        Assert.Empty(error);
    }

    [Fact]
    public void DifferentPropertiesSameTime_ShouldPass()
    {
        var sprite = new C2Sprite
        {
            Id = "diff",
            BaseState = new SpriteState { Time = 0f, Path = "test.png", Opacity = 0f },
            Keyframes = new List<SpriteState>
            {
                new() { Time = 1f, Opacity = 0.5f, RotZ = 45f }
            }
        };

        var (isValid, error) = StoryboardValidator.ValidateStateConflicts(sprite);
        Assert.True(isValid);
        Assert.Empty(error);
    }

    [Fact]
    public void IgnoredProperties_ShouldNotConflict()
    {
        var sprite = new C2Sprite
        {
            Id = "ignored",
            BaseState = new SpriteState { Time = 0f, Path = "test1.png", Opacity = 1f },
            Keyframes = new List<SpriteState>
            {
                new() { Time = 0f, Path = "test2.png" }
            }
        };

        var (isValid, error) = StoryboardValidator.ValidateStateConflicts(sprite);
        Assert.True(isValid); // Path 在忽略列表中
    }

    [Fact]
    public void ControllerConflict_ShouldDetect()
    {
        var ctrl = new C2SceneController
        {
            Id = "ctrl",
            BaseState = new ControllerState { Time = 0f, Fov = 53.2f },
            Keyframes = new List<ControllerState>
            {
                new() { Time = 1f, Fov = 60f },
                new() { Time = 1f, Fov = 70f }
            }
        };

        var (isValid, error) = StoryboardValidator.ValidateStateConflicts(ctrl);
        Assert.False(isValid);
        Assert.Contains("Fov", error);
    }

    [Fact]
    public void ComplexValidScenario_ShouldPass()
    {
        var ctrl = new C2SceneController
        {
            Id = "complex",
            BaseState = new ControllerState { Time = 0f, Fov = 53.2f, Perspective = true },
            Keyframes = new List<ControllerState>
            {
                new() { Time = 1f, Fov = 60f },
                new() { Time = 2f, Bloom = true, BloomIntensity = 2f },
                new() { Time = 3f, Noise = true },
                new() { Time = 4f, Fov = 53.2f, Bloom = false },
                new() { Time = 5f, StoryboardOpacity = 0f }
            }
        };

        var (isValid, error) = StoryboardValidator.ValidateStateConflicts(ctrl);
        Assert.True(isValid);
        Assert.Empty(error);
    }
}