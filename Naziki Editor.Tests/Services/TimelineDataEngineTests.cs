using Naziki_Editor.Core;
using Naziki_Editor.Models;
using Naziki_Editor.State;
using Naziki_Editor.UI.Services;
using Naziki_Editor.UI.ViewModels;
using System.Collections.Generic;
using Xunit;

namespace Naziki_Editor.Tests.Services
{
    public class TimelineDataEngineTests
    {
        [Fact]
        public void BuildMacroTimeline_ShouldGenerateGroups_WhenStoryboardExists()
        {
            // Arrange
            var context = new ProjectDataContext(new Naziki_Editor.Core.Messaging.MessageBroker());
            context.Storyboard = new StoryboardRoot();
            context.Storyboard.sprites = new List<C2Sprite> { new C2Sprite { Id = "sprite_1", BaseState = new SpriteState() } };

            var engine = new TimelineDataEngine();

            // Act
            var groups = engine.BuildMacroTimeline(context);

            // Assert
            Assert.NotNull(groups);
            Assert.True(groups.Count > 0); // 至少存在默认图层和画面图层
            Assert.Contains(groups, g => g.GroupName == "📦 物理图层 - Layer 0 (背景)");
        }
    }
}