using Naziki_Editor.Core;
using Naziki_Editor.Models;
using Xunit;

namespace NazikiEditor.Tests;

/// <summary>
/// 解析器测试：ID 标准化和账本同步
/// </summary>
public class StoryboardParserTests
{
    private readonly StoryboardParser _parser;
    private readonly NazikiProjectModel _project;

    public StoryboardParserTests()
    {
        _parser = new StoryboardParser(new NullErrorHandler());
        _project = new NazikiProjectModel();
    }

    [Fact]
    public void EntitiesWithIds_ShouldKeepThem()
    {
        var root = new StoryboardRoot();
        root.sprites.Add(new C2Sprite { Id = "my_sprite", BaseState = new SpriteState { Time = 0f, Path = "test.png" } });
        root.texts.Add(new C2Text { Id = "my_text", BaseState = new TextState { Time = 0f, TextContent = "Hello" } });

        _parser.StandardizeStoryboardIds(root, _project);

        Assert.Equal("my_sprite", root.sprites[0].Id);
        Assert.Equal("my_text", root.texts[0].Id);
    }

    [Fact]
    public void EntitiesWithoutIds_ShouldGenerateIds()
    {
        var root = new StoryboardRoot();
        root.sprites.Add(new C2Sprite { BaseState = new SpriteState { Time = 0f, Path = "no_id.png" } });

        _parser.StandardizeStoryboardIds(root, _project);

        Assert.NotNull(root.sprites[0].Id);
        Assert.StartsWith("sprite_", root.sprites[0].Id);
    }

    [Fact]
    public void ControlBoard_ShouldGenerateTrackableId()
    {
        var root = new StoryboardRoot();
        var cb = new C2Sprite
        {
            TargetId = "main_sprite",
            BaseState = new SpriteState { Time = 0f, X = new UnitFloat { Value = 100f, Unit = ReferenceUnit.StageX } }
        };
        root.sprites.Add(cb);

        _parser.StandardizeStoryboardIds(root, _project);

        Assert.Equal("main_sprite", cb.TargetId);
        Assert.NotNull(cb.Id);
        Assert.NotEmpty(_project.ControlBoardIdMaps);
    }

    [Fact]
    public void MultipleControlBoards_SameTarget_ShouldHaveUniqueIds()
    {
        var root = new StoryboardRoot();
        root.sprites.Add(new C2Sprite { TargetId = "shared", BaseState = new SpriteState { Time = 0f } });
        root.sprites.Add(new C2Sprite { TargetId = "shared", BaseState = new SpriteState { Time = 0f } });

        _parser.StandardizeStoryboardIds(root, _project);

        Assert.NotEqual(root.sprites[0].Id, root.sprites[1].Id);
        Assert.NotNull(root.sprites[0].Id);
        Assert.NotNull(root.sprites[1].Id);
    }

    [Fact]
    public void AllEntityTypes_ShouldGenerateIds()
    {
        var root = new StoryboardRoot();
        root.sprites.Add(new C2Sprite { BaseState = new SpriteState { Time = 0f, Path = "s.png" } });
        root.texts.Add(new C2Text { BaseState = new TextState { Time = 0f, TextContent = "t" } });
        root.lines.Add(new C2Line { BaseState = new LineState { Time = 0f } });
        root.controllers.Add(new C2SceneController { BaseState = new ControllerState { Time = 0f } });
        root.note_controllers.Add(new C2NoteController { BaseState = new NoteControllerState { Time = 0f, NoteTarget = 1 } });

        _parser.StandardizeStoryboardIds(root, _project);

        foreach (var s in root.sprites) Assert.NotNull(s.Id);
        foreach (var t in root.texts) Assert.NotNull(t.Id);
        foreach (var l in root.lines) Assert.NotNull(l.Id);
    }

    [Fact]
    public void SyncControlBoardIdMaps_ShouldUpdateProject()
    {
        var root = new StoryboardRoot();
        root.sprites.Add(new C2Sprite { Id = "cb_001", TargetId = "target_1", BaseState = new SpriteState { Time = 0f } });
        root.sprites.Add(new C2Sprite { Id = "cb_002", TargetId = "target_1", BaseState = new SpriteState { Time = 0f } });

        _parser.SyncControlBoardIdMaps(root, _project);

        Assert.NotEmpty(_project.ControlBoardIdMaps);
        Assert.Equal(2, _project.ControlBoardIdMaps.Count);
    }

    [Fact]
    public void SyncControlBoardIdMaps_ShouldClearOldEntries()
    {
        _project.ControlBoardIdMaps["old_key"] = "old_value";

        var root = new StoryboardRoot();
        root.sprites.Add(new C2Sprite { Id = "new_cb", TargetId = "new_target", BaseState = new SpriteState { Time = 0f } });

        _parser.SyncControlBoardIdMaps(root, _project);

        Assert.False(_project.ControlBoardIdMaps.ContainsKey("old_key"));
        Assert.Single(_project.ControlBoardIdMaps);
    }
}