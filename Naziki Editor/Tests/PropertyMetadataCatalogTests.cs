using System.Linq;
using Naziki_Editor.Core.Timeline.Projection;
using Naziki_Editor.Models;
using Xunit;

namespace Naziki_Editor.Tests;

public sealed class PropertyMetadataCatalogTests
{
    private readonly PropertyMetadataCatalog _catalog = new();

    [Fact]
    public void DiscoversBaseStatePropertiesWhenEntityHasNoKeyframes()
    {
        var entity = new C2Sprite
        {
            BaseState = new SpriteState { Time = 1, Opacity = .5f }
        };

        var tracks = _catalog.Discover(entity);

        Assert.Contains(tracks, track =>
            track.PropertyName == "Opacity" &&
            track.Kind == PropertyTrackKind.ContinuousNumeric);
        Assert.DoesNotContain(tracks, track => track.PropertyName == "Time");
    }

    [Fact]
    public void ClassifiesBooleanSegmentsAndEffectDependencies()
    {
        var entity = new C2SceneController
        {
            BaseState = new ControllerState
            {
                Time = 0,
                Bloom = true,
                BloomIntensity = .8f
            }
        };

        var tracks = _catalog.Discover(entity);

        Assert.Contains(tracks, track =>
            track.PropertyName == "Bloom" &&
            track.Kind == PropertyTrackKind.BooleanSegments &&
            track.IsDependencySwitch);
        Assert.Contains(tracks, track =>
            track.PropertyName == "BloomIntensity" &&
            track.DependencyGroup == "Bloom");
    }

    [Fact]
    public void DiscoversPropertyOnlyPresentInLaterState()
    {
        var entity = new C2Sprite
        {
            BaseState = new SpriteState { Time = 0 },
            Keyframes = [new SpriteState { RelativeTime = 1, RotZ = 30 }]
        };

        var tracks = _catalog.Discover(entity);

        Assert.Contains(tracks, track => track.PropertyName == "RotZ");
    }
}
