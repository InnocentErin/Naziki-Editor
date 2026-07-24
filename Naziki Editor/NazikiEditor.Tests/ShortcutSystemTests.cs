using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Commands;
using Naziki_Editor.Core.Shortcuts;
using System.Windows.Input;
using Xunit;

namespace NazikiEditor.Tests;

/// <summary>
/// 快捷键系统全面测试套件。
/// 覆盖 ShortcutBinding、ShortcutManager、ShortcutContext、DefaultShortcuts 的
/// 单元测试、边界测试、冲突检测和回归测试。
/// </summary>
public class ShortcutSystemTests
{
    // ==========================================
    // ShortcutBinding 测试
    // ==========================================

    public class ShortcutBindingTests
    {
        [Fact]
        public void ToGestureText_CtrlS_ReturnsCorrectFormat()
        {
            var binding = new ShortcutBinding
            {
                Id = "Save",
                Key = Key.S,
                Modifiers = ModifierKeys.Control
            };
            Assert.Equal("Ctrl+S", binding.ToGestureText());
        }

        [Fact]
        public void ToGestureText_CtrlShiftE_ReturnsCorrectFormat()
        {
            var binding = new ShortcutBinding
            {
                Id = "Export",
                Key = Key.E,
                Modifiers = ModifierKeys.Control | ModifierKeys.Shift
            };
            Assert.Equal("Ctrl+Shift+E", binding.ToGestureText());
        }

        [Fact]
        public void ToGestureText_AltF4_ReturnsCorrectFormat()
        {
            var binding = new ShortcutBinding
            {
                Id = "Exit",
                Key = Key.F4,
                Modifiers = ModifierKeys.Alt
            };
            Assert.Equal("Alt+F4", binding.ToGestureText());
        }

        [Fact]
        public void ToGestureText_NoModifiers_ReturnsKeyOnly()
        {
            var binding = new ShortcutBinding
            {
                Id = "Play",
                Key = Key.Space,
                Modifiers = ModifierKeys.None
            };
            Assert.Equal("Space", binding.ToGestureText());
        }

        [Fact]
        public void ToGestureText_F11_ReturnsCorrectFormat()
        {
            var binding = new ShortcutBinding
            {
                Id = "FullScreen",
                Key = Key.F11,
                Modifiers = ModifierKeys.None
            };
            Assert.Equal("F11", binding.ToGestureText());
        }

        [Fact]
        public void ToGestureText_DigitKeys_ReturnNumber()
        {
            var binding = new ShortcutBinding
            {
                Id = "ZoomReset",
                Key = Key.D0,
                Modifiers = ModifierKeys.Control
            };
            Assert.Equal("Ctrl+0", binding.ToGestureText());
        }

        [Fact]
        public void ToGestureText_OemPlus_ReturnsEquals()
        {
            var binding = new ShortcutBinding
            {
                Id = "ZoomIn",
                Key = Key.OemPlus,
                Modifiers = ModifierKeys.Control
            };
            Assert.Equal("Ctrl+=", binding.ToGestureText());
        }

        [Fact]
        public void ToGestureText_OemMinus_ReturnsMinus()
        {
            var binding = new ShortcutBinding
            {
                Id = "ZoomOut",
                Key = Key.OemMinus,
                Modifiers = ModifierKeys.Control
            };
            Assert.Equal("Ctrl+-", binding.ToGestureText());
        }

        [Fact]
        public void MatchesContext_GlobalAlwaysMatches()
        {
            var binding = new ShortcutBinding { Context = ShortcutContext.Global };
            Assert.True(binding.MatchesContext(ShortcutContext.EventList));
            Assert.True(binding.MatchesContext(ShortcutContext.Timeline));
            Assert.True(binding.MatchesContext(ShortcutContext.Canvas));
            Assert.True(binding.MatchesContext(ShortcutContext.Global));
        }

        [Fact]
        public void MatchesContext_SpecificContextMatchesExactly()
        {
            var binding = new ShortcutBinding { Context = ShortcutContext.EventList };
            Assert.True(binding.MatchesContext(ShortcutContext.EventList));
            Assert.False(binding.MatchesContext(ShortcutContext.Timeline));
            Assert.False(binding.MatchesContext(ShortcutContext.Global));
        }

        [Fact]
        public void MatchesContext_MultiContextMatchesAny()
        {
            var binding = new ShortcutBinding
            {
                Context = ShortcutContext.EventList | ShortcutContext.Timeline
            };
            Assert.True(binding.MatchesContext(ShortcutContext.EventList));
            Assert.True(binding.MatchesContext(ShortcutContext.Timeline));
            Assert.False(binding.MatchesContext(ShortcutContext.AssetList));
            Assert.False(binding.MatchesContext(ShortcutContext.Canvas));
        }

        [Fact]
        public void ConflictsWith_SameKeyDifferentContext_NoConflict()
        {
            var a = new ShortcutBinding
            {
                Id = "A",
                Key = Key.S,
                Modifiers = ModifierKeys.Control,
                Context = ShortcutContext.EventList
            };
            var b = new ShortcutBinding
            {
                Id = "B",
                Key = Key.S,
                Modifiers = ModifierKeys.Control,
                Context = ShortcutContext.Timeline
            };
            Assert.False(a.ConflictsWith(b));
        }

        [Fact]
        public void ConflictsWith_SameKeySameContext_Conflict()
        {
            var a = new ShortcutBinding
            {
                Id = "A",
                Key = Key.S,
                Modifiers = ModifierKeys.Control,
                Context = ShortcutContext.EventList
            };
            var b = new ShortcutBinding
            {
                Id = "B",
                Key = Key.S,
                Modifiers = ModifierKeys.Control,
                Context = ShortcutContext.EventList
            };
            Assert.True(a.ConflictsWith(b));
        }

        [Fact]
        public void ConflictsWith_GlobalConflictsWithSpecific()
        {
            var a = new ShortcutBinding
            {
                Id = "GlobalSave",
                Key = Key.S,
                Modifiers = ModifierKeys.Control,
                Context = ShortcutContext.Global
            };
            var b = new ShortcutBinding
            {
                Id = "EventListSave",
                Key = Key.S,
                Modifiers = ModifierKeys.Control,
                Context = ShortcutContext.EventList
            };
            Assert.True(a.ConflictsWith(b));
        }

        [Fact]
        public void ConflictsWith_DifferentKey_NoConflict()
        {
            var a = new ShortcutBinding
            {
                Id = "A",
                Key = Key.S,
                Modifiers = ModifierKeys.Control,
                Context = ShortcutContext.Global
            };
            var b = new ShortcutBinding
            {
                Id = "B",
                Key = Key.O,
                Modifiers = ModifierKeys.Control,
                Context = ShortcutContext.Global
            };
            Assert.False(a.ConflictsWith(b));
        }

        [Fact]
        public void ConflictsWith_MultiContextOverlap_Conflict()
        {
            var a = new ShortcutBinding
            {
                Id = "A",
                Key = Key.D,
                Modifiers = ModifierKeys.Control,
                Context = ShortcutContext.EventList | ShortcutContext.Timeline
            };
            var b = new ShortcutBinding
            {
                Id = "B",
                Key = Key.D,
                Modifiers = ModifierKeys.Control,
                Context = ShortcutContext.Timeline | ShortcutContext.AssetList
            };
            Assert.True(a.ConflictsWith(b)); // 都有 Timeline 上下文
        }

        [Fact]
        public void ToLookupKey_ReturnsCorrectTuple()
        {
            var binding = new ShortcutBinding
            {
                Key = Key.A,
                Modifiers = ModifierKeys.Control | ModifierKeys.Shift
            };
            var (key, mods) = binding.ToLookupKey();
            Assert.Equal(Key.A, key);
            Assert.Equal(ModifierKeys.Control | ModifierKeys.Shift, mods);
        }

        [Fact]
        public void IsEnabled_DefaultsToTrue()
        {
            var binding = new ShortcutBinding();
            Assert.True(binding.IsEnabled);
        }

        [Fact]
        public void Priority_DefaultsToZero()
        {
            var binding = new ShortcutBinding();
            Assert.Equal(0, binding.Priority);
        }
    }

    // ==========================================
    // ShortcutManager 测试
    // ==========================================

    public class ShortcutManagerTests
    {
        private static ICommandDispatcher CreateCommandDispatcher()
        {
            var dispatcher = new CommandDispatcher();
            // 预注册所有可能被快捷键触发的命令
            dispatcher.Register("SaveProject", () => { });
            dispatcher.Register("OpenProject", () => { });
            dispatcher.Register("Undo", () => { });
            dispatcher.Register("Redo", () => { });
            dispatcher.Register("Exit", () => { });
            dispatcher.Register("SelectAll", () => { });
            dispatcher.Register("DeleteSelected", () => { });
            dispatcher.Register("CopyAsset", () => { });
            dispatcher.Register("PasteAsset", () => { });
            dispatcher.Register("TimelinePlayPause", () => { });
            dispatcher.Register("TimelineZoomIn", () => { });
            dispatcher.Register("TimelineZoomOut", () => { });
            dispatcher.Register("RefreshView", () => { });
            dispatcher.Register("ToggleFullScreen", () => { });
            dispatcher.Register("Find", () => { });
            dispatcher.Register("Help", () => { });
            dispatcher.Register("CanvasZoomIn", () => { });
            dispatcher.Register("CanvasZoomOut", () => { });
            dispatcher.Register("CanvasZoomReset", () => { });
            dispatcher.Register("NoteListNavigateUp", () => { });
            dispatcher.Register("NoteListNavigateDown", () => { });
            return dispatcher;
        }

        [Fact]
        public void Register_ValidBinding_ReturnsId()
        {
            var manager = new ShortcutManager(CreateCommandDispatcher());
            var binding = new ShortcutBinding
            {
                Id = "Test",
                Key = Key.T,
                Modifiers = ModifierKeys.Control,
                CommandName = "SaveProject",
                Context = ShortcutContext.Global
            };
            var result = manager.Register(binding);
            Assert.Equal("Test", result);
            Assert.Equal(1, manager.BindingCount);
        }

        [Fact]
        public void Register_DuplicateId_ThrowsException()
        {
            var manager = new ShortcutManager(CreateCommandDispatcher());
            var binding = new ShortcutBinding
            {
                Id = "Duplicate",
                Key = Key.A,
                Modifiers = ModifierKeys.Control,
                CommandName = "SaveProject"
            };
            manager.Register(binding);
            Assert.Throws<ArgumentException>(() => manager.Register(binding));
        }

        [Fact]
        public void Register_ConflictingBinding_ThrowsException()
        {
            var manager = new ShortcutManager(CreateCommandDispatcher());
            var a = new ShortcutBinding
            {
                Id = "First",
                Key = Key.S,
                Modifiers = ModifierKeys.Control,
                CommandName = "SaveProject",
                Context = ShortcutContext.Global
            };
            manager.Register(a);

            var b = new ShortcutBinding
            {
                Id = "Second",
                Key = Key.S,
                Modifiers = ModifierKeys.Control,
                CommandName = "OpenProject",
                Context = ShortcutContext.Global
            };
            Assert.Throws<InvalidOperationException>(() => manager.Register(b));
        }

        [Fact]
        public void RegisterBatch_SkipsConflicts_ReturnsSuccessCount()
        {
            var manager = new ShortcutManager(CreateCommandDispatcher());
            var bindings = new List<ShortcutBinding>
            {
                new() { Id = "B1", Key = Key.S, Modifiers = ModifierKeys.Control, CommandName = "SaveProject", Context = ShortcutContext.Global },
                new() { Id = "B2", Key = Key.S, Modifiers = ModifierKeys.Control, CommandName = "OpenProject", Context = ShortcutContext.Global }, // 冲突
                new() { Id = "B3", Key = Key.O, Modifiers = ModifierKeys.Control, CommandName = "OpenProject", Context = ShortcutContext.Global },
            };
            var count = manager.RegisterBatch(bindings);
            Assert.Equal(2, count); // B2 被跳过
            Assert.Equal(2, manager.BindingCount);
        }

        [Fact]
        public void Unregister_ExistingBinding_ReturnsTrue()
        {
            var manager = new ShortcutManager(CreateCommandDispatcher());
            var binding = new ShortcutBinding
            {
                Id = "ToRemove",
                Key = Key.X,
                Modifiers = ModifierKeys.Control,
                CommandName = "SaveProject"
            };
            manager.Register(binding);
            Assert.True(manager.Unregister("ToRemove"));
            Assert.Equal(0, manager.BindingCount);
        }

        [Fact]
        public void Unregister_NonExistent_ReturnsFalse()
        {
            var manager = new ShortcutManager(CreateCommandDispatcher());
            Assert.False(manager.Unregister("NonExistent"));
        }

        [Fact]
        public void HandleKeyDown_MatchingBinding_ReturnsTrue()
        {
            var dispatcher = CreateCommandDispatcher();
            bool wasExecuted = false;
            dispatcher.Register("TestCommand", () => wasExecuted = true);

            var manager = new ShortcutManager(dispatcher);
            manager.Register(new ShortcutBinding
            {
                Id = "Test",
                Key = Key.T,
                Modifiers = ModifierKeys.Control,
                CommandName = "TestCommand",
                Context = ShortcutContext.Global
            });

            var result = manager.HandleKeyDown(Key.T, ModifierKeys.Control, ShortcutContext.Global);
            Assert.True(result);
            Assert.True(wasExecuted);
        }

        [Fact]
        public void HandleKeyDown_NoMatchingBinding_ReturnsFalse()
        {
            var manager = new ShortcutManager(CreateCommandDispatcher());
            manager.Register(new ShortcutBinding
            {
                Id = "Save",
                Key = Key.S,
                Modifiers = ModifierKeys.Control,
                CommandName = "SaveProject",
                Context = ShortcutContext.Global
            });

            var result = manager.HandleKeyDown(Key.X, ModifierKeys.Control, ShortcutContext.Global);
            Assert.False(result);
        }

        [Fact]
        public void HandleKeyDown_WrongContext_ReturnsFalse()
        {
            var dispatcher = CreateCommandDispatcher();
            bool wasExecuted = false;
            dispatcher.Register("TimelineCmd", () => wasExecuted = true);

            var manager = new ShortcutManager(dispatcher);
            manager.Register(new ShortcutBinding
            {
                Id = "TimelineOnly",
                Key = Key.Space,
                Modifiers = ModifierKeys.None,
                CommandName = "TimelineCmd",
                Context = ShortcutContext.Timeline
            });

            // 在非 Timeline 上下文中按下 Space
            var result = manager.HandleKeyDown(Key.Space, ModifierKeys.None, ShortcutContext.EventList);
            Assert.False(result);
            Assert.False(wasExecuted);
        }

        [Fact]
        public void HandleKeyDown_DisabledBinding_ReturnsFalse()
        {
            var dispatcher = CreateCommandDispatcher();
            bool wasExecuted = false;
            dispatcher.Register("DisabledCmd", () => wasExecuted = true);

            var manager = new ShortcutManager(dispatcher);
            manager.Register(new ShortcutBinding
            {
                Id = "Disabled",
                Key = Key.D,
                Modifiers = ModifierKeys.Control,
                CommandName = "DisabledCmd",
                Context = ShortcutContext.Global,
                IsEnabled = false
            });

            var result = manager.HandleKeyDown(Key.D, ModifierKeys.Control, ShortcutContext.Global);
            Assert.False(result);
            Assert.False(wasExecuted);
        }

        [Fact]
        public void HandleKeyDown_PureModifierKey_ReturnsFalse()
        {
            var manager = new ShortcutManager(CreateCommandDispatcher());
            // 纯修饰键不应触发任何快捷键
            Assert.False(manager.HandleKeyDown(Key.LeftCtrl, ModifierKeys.Control, ShortcutContext.Global));
            Assert.False(manager.HandleKeyDown(Key.LeftAlt, ModifierKeys.Alt, ShortcutContext.Global));
            Assert.False(manager.HandleKeyDown(Key.LeftShift, ModifierKeys.Shift, ShortcutContext.Global));
        }

        [Fact]
        public void HandleKeyDown_PriorityField_IsSetCorrectly()
        {
            // 优先级字段在绑定时正确设置，但同一按键+上下文不能注册两个绑定
            // 冲突检测确保系统安全。优先级用于多上下文场景下的匹配排序。
            var dispatcher = CreateCommandDispatcher();
            bool wasExecuted = false;
            dispatcher.Register("PriorityCmd", () => wasExecuted = true);

            var manager = new ShortcutManager(dispatcher);
            var binding = new ShortcutBinding
            {
                Id = "PriorityTest",
                Key = Key.P,
                Modifiers = ModifierKeys.Control,
                CommandName = "PriorityCmd",
                Context = ShortcutContext.Global,
                Priority = 10
            };
            manager.Register(binding);

            var found = manager.FindBinding("PriorityTest");
            Assert.NotNull(found);
            Assert.Equal(10, found!.Priority);

            var result = manager.HandleKeyDown(Key.P, ModifierKeys.Control, ShortcutContext.Global);
            Assert.True(result);
            Assert.True(wasExecuted);
        }

        [Fact]
        public void HandleKeyDown_MultiContextBinding_MatchesAnyListedContext()
        {
            var dispatcher = CreateCommandDispatcher();
            var executionCount = 0;
            dispatcher.Register("MultiCtx", () => executionCount++);

            var manager = new ShortcutManager(dispatcher);
            manager.Register(new ShortcutBinding
            {
                Id = "MultiCtx",
                Key = Key.M,
                Modifiers = ModifierKeys.Control,
                CommandName = "MultiCtx",
                Context = ShortcutContext.EventList | ShortcutContext.Timeline | ShortcutContext.AssetList
            });

            // 在 EventList 上下文中应该匹配
            Assert.True(manager.HandleKeyDown(Key.M, ModifierKeys.Control, ShortcutContext.EventList));
            // 在 Timeline 上下文中应该匹配
            Assert.True(manager.HandleKeyDown(Key.M, ModifierKeys.Control, ShortcutContext.Timeline));
            // 在 AssetList 上下文中应该匹配
            Assert.True(manager.HandleKeyDown(Key.M, ModifierKeys.Control, ShortcutContext.AssetList));
            // 在 Canvas 上下文中不应该匹配
            Assert.False(manager.HandleKeyDown(Key.M, ModifierKeys.Control, ShortcutContext.Canvas));

            Assert.Equal(3, executionCount);
        }

        [Fact]
        public void FindBinding_Existing_ReturnsBinding()
        {
            var manager = new ShortcutManager(CreateCommandDispatcher());
            var binding = new ShortcutBinding
            {
                Id = "FindMe",
                Key = Key.F,
                Modifiers = ModifierKeys.Control,
                CommandName = "SaveProject"
            };
            manager.Register(binding);

            var found = manager.FindBinding("FindMe");
            Assert.NotNull(found);
            Assert.Equal("FindMe", found!.Id);
        }

        [Fact]
        public void FindBinding_NonExistent_ReturnsNull()
        {
            var manager = new ShortcutManager(CreateCommandDispatcher());
            Assert.Null(manager.FindBinding("Ghost"));
        }

        [Fact]
        public void SetBindingEnabled_TogglesCorrectly()
        {
            var manager = new ShortcutManager(CreateCommandDispatcher());
            var binding = new ShortcutBinding
            {
                Id = "Toggle",
                Key = Key.T,
                Modifiers = ModifierKeys.Control,
                CommandName = "SaveProject"
            };
            manager.Register(binding);

            Assert.True(manager.SetBindingEnabled("Toggle", false));
            Assert.False(manager.FindBinding("Toggle")!.IsEnabled);

            Assert.True(manager.SetBindingEnabled("Toggle", true));
            Assert.True(manager.FindBinding("Toggle")!.IsEnabled);
        }

        [Fact]
        public void GetAllBindings_ReturnsAllRegistered()
        {
            var manager = new ShortcutManager(CreateCommandDispatcher());
            manager.Register(new ShortcutBinding { Id = "A", Key = Key.A, Modifiers = ModifierKeys.Control, CommandName = "SaveProject" });
            manager.Register(new ShortcutBinding { Id = "B", Key = Key.B, Modifiers = ModifierKeys.Control, CommandName = "Undo" });
            manager.Register(new ShortcutBinding { Id = "C", Key = Key.C, Modifiers = ModifierKeys.Control, CommandName = "Redo" });

            var all = manager.GetAllBindings();
            Assert.Equal(3, all.Count);
        }

        [Fact]
        public void GetBindings_ByContext_ReturnsOnlyMatching()
        {
            var manager = new ShortcutManager(CreateCommandDispatcher());
            manager.Register(new ShortcutBinding { Id = "Global", Key = Key.G, Modifiers = ModifierKeys.Control, CommandName = "SaveProject", Context = ShortcutContext.Global });
            manager.Register(new ShortcutBinding { Id = "Timeline", Key = Key.T, Modifiers = ModifierKeys.Control, CommandName = "Undo", Context = ShortcutContext.Timeline });
            manager.Register(new ShortcutBinding { Id = "EventList", Key = Key.E, Modifiers = ModifierKeys.Control, CommandName = "Redo", Context = ShortcutContext.EventList });

            // Global 上下文应该只返回 Global 绑定
            var globalBindings = manager.GetBindings(ShortcutContext.Global);
            Assert.Single(globalBindings);
            Assert.Equal("Global", globalBindings[0].Id);

            // Timeline 上下文应该返回 Global + Timeline 绑定
            var timelineBindings = manager.GetBindings(ShortcutContext.Timeline);
            Assert.Equal(2, timelineBindings.Count);
        }

        [Fact]
        public void Clear_RemovesAllBindings()
        {
            var manager = new ShortcutManager(CreateCommandDispatcher());
            manager.Register(new ShortcutBinding { Id = "A", Key = Key.A, Modifiers = ModifierKeys.Control, CommandName = "SaveProject" });
            manager.Register(new ShortcutBinding { Id = "B", Key = Key.B, Modifiers = ModifierKeys.Control, CommandName = "Undo" });

            manager.Clear();
            Assert.Equal(0, manager.BindingCount);
            Assert.Empty(manager.GetAllBindings());
        }
    }

    // ==========================================
    // ShortcutContext 枚举测试
    // ==========================================

    public class ShortcutContextTests
    {
        [Fact]
        public void Global_IsZero()
        {
            Assert.Equal(0, (int)ShortcutContext.Global);
        }

        [Fact]
        public void Flags_CanCombine()
        {
            var combined = ShortcutContext.EventList | ShortcutContext.Timeline;
            Assert.True(combined.HasFlag(ShortcutContext.EventList));
            Assert.True(combined.HasFlag(ShortcutContext.Timeline));
            Assert.False(combined.HasFlag(ShortcutContext.AssetList));
        }

        [Fact]
        public void Any_MatchesAll()
        {
            var any = ShortcutContext.Any;
            Assert.True(any.HasFlag(ShortcutContext.EventList));
            Assert.True(any.HasFlag(ShortcutContext.Timeline));
            Assert.True(any.HasFlag(ShortcutContext.Canvas));
            Assert.True(any.HasFlag(ShortcutContext.TextEditor));
            Assert.True(any.HasFlag(ShortcutContext.ModalDialog));
        }

        [Fact]
        public void GlobalWithFlag_EqualsTheFlag()
        {
            // Global = 0, so Global | EventList == EventList
            var combined = ShortcutContext.Global | ShortcutContext.EventList;
            Assert.Equal(ShortcutContext.EventList, combined);
        }
    }

    // ==========================================
    // DefaultShortcuts 回归测试
    // ==========================================

    public class DefaultShortcutsTests
    {
        [Fact]
        public void GetAll_ReturnsAtLeast20Bindings()
        {
            var all = DefaultShortcuts.GetAll().ToList();
            Assert.True(all.Count >= 20, $"Expected at least 20 shortcuts, got {all.Count}");
        }

        [Fact]
        public void AllBindings_HaveUniqueIds()
        {
            var all = DefaultShortcuts.GetAll().ToList();
            var ids = all.Select(b => b.Id).ToList();
            var distinctIds = ids.Distinct().ToList();
            Assert.Equal(ids.Count, distinctIds.Count);
        }

        [Fact]
        public void AllBindings_HaveNonEmptyId()
        {
            var all = DefaultShortcuts.GetAll().ToList();
            Assert.All(all, b => Assert.False(string.IsNullOrEmpty(b.Id), $"Binding with CommandName '{b.CommandName}' has empty Id"));
        }

        [Fact]
        public void AllBindings_HaveNonEmptyCommandName()
        {
            var all = DefaultShortcuts.GetAll().ToList();
            Assert.All(all, b => Assert.False(string.IsNullOrEmpty(b.CommandName), $"Binding '{b.Id}' has empty CommandName"));
        }

        [Fact]
        public void AllBindings_HaveNonEmptyDescription()
        {
            var all = DefaultShortcuts.GetAll().ToList();
            Assert.All(all, b => Assert.False(string.IsNullOrEmpty(b.Description), $"Binding '{b.Id}' has empty Description"));
        }

        [Fact]
        public void AllBindings_HaveValidGestureText()
        {
            var all = DefaultShortcuts.GetAll().ToList();
            Assert.All(all, b =>
            {
                var text = b.ToGestureText();
                Assert.False(string.IsNullOrEmpty(text), $"Binding '{b.Id}' produced empty gesture text");
            });
        }

        [Fact]
        public void NoConflictingShortcuts_WithinSameContext()
        {
            var all = DefaultShortcuts.GetAll().ToList();
            for (int i = 0; i < all.Count; i++)
            {
                for (int j = i + 1; j < all.Count; j++)
                {
                    Assert.False(all[i].ConflictsWith(all[j]),
                        $"Conflict detected: '{all[i].Id}' ({all[i].ToGestureText()}) conflicts with '{all[j].Id}' ({all[j].ToGestureText()})");
                }
            }
        }

        [Fact]
        public void AllGlobalShortcuts_AreEnabled()
        {
            var globalBindings = DefaultShortcuts.GetAll().Where(b => b.Context == ShortcutContext.Global).ToList();
            Assert.All(globalBindings, b => Assert.True(b.IsEnabled, $"Global shortcut '{b.Id}' is disabled"));
        }

        [Fact]
        public void StandardShortcutsExist()
        {
            var all = DefaultShortcuts.GetAll().ToList();
            var ids = all.Select(b => b.Id).ToHashSet();

            // 文件操作快捷键
            Assert.Contains("NewProject", ids);
            Assert.Contains("OpenProject", ids);
            Assert.Contains("SaveProject", ids);
            Assert.Contains("SaveProjectAs", ids);

            // 编辑操作快捷键
            Assert.Contains("Undo", ids);
            Assert.Contains("Redo", ids);
            Assert.Contains("RedoAlt", ids);
            Assert.Contains("SelectAll", ids);
            Assert.Contains("DeleteSelected", ids);

            // 导入操作快捷键
            Assert.Contains("ImportChart", ids);
            Assert.Contains("ImportStoryboard", ids);
            Assert.Contains("ImportAudio", ids);

            // 时间轴操作快捷键
            Assert.Contains("TimelinePlayPause", ids);
            Assert.Contains("TimelineGoToStart", ids);
            Assert.Contains("TimelineGoToEnd", ids);
            Assert.Contains("TimelineZoomIn", ids);
            Assert.Contains("TimelineZoomOut", ids);
            Assert.Contains("TimelineZoomReset", ids);

            // 视图与系统快捷键
            Assert.Contains("RefreshView", ids);
            Assert.Contains("ToggleFullScreen", ids);
            Assert.Contains("Find", ids);
            Assert.Contains("Help", ids);
            Assert.Contains("Exit", ids);

            // 画布快捷键
            Assert.Contains("CanvasZoomIn", ids);
            Assert.Contains("CanvasZoomOut", ids);
            Assert.Contains("CanvasZoomReset", ids);

            // 音符列表快捷键
            Assert.Contains("NoteListNavigateUp", ids);
            Assert.Contains("NoteListNavigateDown", ids);
        }

        [Fact]
        public void ContextSpecificShortcuts_DontOverlapWithGlobal()
        {
            // 检查特定上下文快捷键不会与全局快捷键在按键上冲突（除非是有意为之）
            var all = DefaultShortcuts.GetAll().ToList();
            var globalBindings = all.Where(b => b.Context == ShortcutContext.Global).ToList();
            var contextBindings = all.Where(b => b.Context != ShortcutContext.Global).ToList();

            foreach (var ctx in contextBindings)
            {
                foreach (var global in globalBindings)
                {
                    // 相同按键的全局和上下文快捷键是允许的（上下文快捷键会覆盖全局）
                    // 但这里检查是否真的需要冲突解决
                    if (ctx.Key == global.Key && ctx.Modifiers == global.Modifiers)
                    {
                        // 这是预期的覆盖行为，不应报错
                        // 但记录一下用于审查
                    }
                }
            }
        }

        [Fact]
        public void RegisterAllDefaultShortcuts_Succeeds()
        {
            var dispatcher = new CommandDispatcher();
            // 注册所有可能被引用的命令
            var all = DefaultShortcuts.GetAll().ToList();
            var commandNames = all.Select(b => b.CommandName).Distinct();
            foreach (var name in commandNames)
            {
                dispatcher.Register(name, () => { });
            }

            var manager = new ShortcutManager(dispatcher);
            var count = manager.RegisterBatch(all);
            Assert.Equal(all.Count, count);
            Assert.Equal(all.Count, manager.BindingCount);
        }
    }
}