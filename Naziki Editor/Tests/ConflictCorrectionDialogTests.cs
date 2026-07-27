using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Controls;
using Naziki_Editor.Core.Storyboard.Corrections;
using Naziki_Editor.Models;
using Naziki_Editor.Views.StoryboardCorrections;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Naziki_Editor.Tests;

public sealed class ConflictCorrectionDialogTests
{
    [Fact]
    public void RepeatedKeeperChangesDoNotReuseLogicallyParentedOffsetControls()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var first = new SpriteState { Time = 1, Opacity = .2f };
                var second = new SpriteState { Time = 1, Opacity = .8f, Scale = 2 };
                var entity = new C2Sprite
                {
                    Id = "sprite",
                    BaseState = new SpriteState { Time = 0 },
                    Keyframes = [first, second]
                };
                var issue = new StoryboardCorrectionIssue
                {
                    Id = "conflict",
                    Kind = StoryboardCorrectionKind.SameTimeConflict,
                    Code = "STATE_TIME_CONFLICT",
                    Path = "$.sprites[0]",
                    CollectionName = "sprites",
                    EntityType = nameof(C2Sprite),
                    Entity = entity,
                    EffectiveTime = 1,
                    CanAutomaticallyRepair = true,
                    Participants =
                    [
                        new StoryboardCorrectionParticipant(
                            0, 0, false, null, "$.sprites[0].states[0]", "1",
                            new Dictionary<string, JToken>
                            {
                                ["opacity"] = new JValue(.2)
                            })
                        {
                            State = first
                        },
                        new StoryboardCorrectionParticipant(
                            1, 1, false, null, "$.sprites[0].states[1]", "1",
                            new Dictionary<string, JToken>
                            {
                                ["opacity"] = new JValue(.8),
                                ["scale"] = new JValue(2)
                            })
                        {
                            State = second
                        }
                    ]
                };
                var dialog = new ConflictCorrectionDialog(issue, "fingerprint");
                var field = typeof(ConflictCorrectionDialog).GetField(
                    "_keeperButtons",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                var buttons = (Dictionary<int, RadioButton>)field.GetValue(dialog)!;
                var editorsField = typeof(ConflictCorrectionDialog).GetField(
                    "_loserEditors",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                var firstEditor = ((IEnumerable)editorsField.GetValue(dialog)!)
                    .Cast<object>()
                    .Single();
                var defaultPlan = (StoryboardLoserCorrection)firstEditor.GetType()
                    .GetMethod("Build")!
                    .Invoke(firstEditor, null)!;
                Assert.Equal(
                    StoryboardPropertyMigrationMode.Skip,
                    defaultPlan.PropertyMigrations.Single(item =>
                        item.JsonPropertyName == "opacity").Mode);
                Assert.Equal(
                    StoryboardPropertyMigrationMode.Add,
                    defaultPlan.PropertyMigrations.Single(item =>
                        item.JsonPropertyName == "scale").Mode);

                buttons[1].IsChecked = true;
                buttons[0].IsChecked = true;
                buttons[1].IsChecked = true;
                dialog.Close();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        Assert.True(thread.TrySetApartmentState(ApartmentState.STA));
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
    }
}
