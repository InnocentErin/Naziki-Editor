using System.Windows.Input;

namespace Naziki_Editor.Core.Shortcuts
{
    /// <summary>
    /// 默认快捷键注册表。
    /// 定义项目中所有默认快捷键绑定，按照行业标准设计。
    /// 所有快捷键绑定在此集中声明，便于维护和审查。
    /// </summary>
    public static class DefaultShortcuts
    {
        /// <summary>
        /// 获取所有默认快捷键绑定。
        /// 在应用启动时调用，将绑定注册到 IShortcutManager。
        /// </summary>
        public static IEnumerable<ShortcutBinding> GetAll()
        {
            // ==========================================
            // 📂 文件操作
            // ==========================================
            yield return new ShortcutBinding
            {
                Id = "NewProject",
                Description = "新建项目",
                Key = Key.N,
                Modifiers = ModifierKeys.Control | ModifierKeys.Shift,
                CommandName = "NewProject",
                Context = ShortcutContext.Global
            };

            yield return new ShortcutBinding
            {
                Id = "OpenProject",
                Description = "打开项目",
                Key = Key.O,
                Modifiers = ModifierKeys.Control,
                CommandName = "OpenProject",
                Context = ShortcutContext.Global
            };

            yield return new ShortcutBinding
            {
                Id = "SaveProject",
                Description = "保存项目",
                Key = Key.S,
                Modifiers = ModifierKeys.Control,
                CommandName = "SaveProject",
                Context = ShortcutContext.Global
            };

            yield return new ShortcutBinding
            {
                Id = "SaveProjectAs",
                Description = "另存为",
                Key = Key.S,
                Modifiers = ModifierKeys.Control | ModifierKeys.Shift,
                CommandName = "SaveProjectAs",
                Context = ShortcutContext.Global
            };

            yield return new ShortcutBinding
            {
                Id = "ExportStoryboard",
                Description = "导出故事板",
                Key = Key.E,
                Modifiers = ModifierKeys.Control | ModifierKeys.Shift,
                CommandName = "ExportStoryboard",
                Context = ShortcutContext.Global
            };

            // ==========================================
            // ✏️ 编辑操作
            // ==========================================
            yield return new ShortcutBinding
            {
                Id = "Undo",
                Description = "撤销",
                Key = Key.Z,
                Modifiers = ModifierKeys.Control,
                CommandName = "Undo",
                Context = ShortcutContext.Global,
                Priority = 10 // 全局优先级，但文本编辑器的绑定优先级更高
            };

            yield return new ShortcutBinding
            {
                Id = "Redo",
                Description = "重做",
                Key = Key.Y,
                Modifiers = ModifierKeys.Control,
                CommandName = "Redo",
                Context = ShortcutContext.Global,
                Priority = 10
            };

            yield return new ShortcutBinding
            {
                Id = "RedoAlt",
                Description = "重做（备选）",
                Key = Key.Z,
                Modifiers = ModifierKeys.Control | ModifierKeys.Shift,
                CommandName = "Redo",
                Context = ShortcutContext.Global,
                Priority = 10
            };

            yield return new ShortcutBinding
            {
                Id = "SelectAll",
                Description = "全选",
                Key = Key.A,
                Modifiers = ModifierKeys.Control,
                CommandName = "SelectAll",
                Context = ShortcutContext.EventList | ShortcutContext.AssetList | ShortcutContext.NoteList
            };

            yield return new ShortcutBinding
            {
                Id = "DuplicateSelected",
                Description = "复制选中对象",
                Key = Key.D,
                Modifiers = ModifierKeys.Control,
                CommandName = "DuplicateSelected",
                Context = ShortcutContext.EventList | ShortcutContext.Timeline
            };

            yield return new ShortcutBinding
            {
                Id = "DeleteSelected",
                Description = "删除选中对象",
                Key = Key.Delete,
                Modifiers = ModifierKeys.None,
                CommandName = "DeleteSelected",
                Context = ShortcutContext.EventList | ShortcutContext.AssetList | ShortcutContext.Timeline
            };

            yield return new ShortcutBinding
            {
                Id = "CopyAsset",
                Description = "复制素材文件路径",
                Key = Key.C,
                Modifiers = ModifierKeys.Control,
                CommandName = "CopyAsset",
                Context = ShortcutContext.AssetList
            };

            yield return new ShortcutBinding
            {
                Id = "PasteAsset",
                Description = "粘贴素材文件",
                Key = Key.V,
                Modifiers = ModifierKeys.Control,
                CommandName = "PasteAsset",
                Context = ShortcutContext.AssetList
            };

            yield return new ShortcutBinding
            {
                Id = "RenameAsset",
                Description = "重命名素材",
                Key = Key.F2,
                Modifiers = ModifierKeys.None,
                CommandName = "RenameAsset",
                Context = ShortcutContext.AssetList
            };

            // ==========================================
            // 🎬 导入操作
            // ==========================================
            yield return new ShortcutBinding
            {
                Id = "ImportChart",
                Description = "导入谱面",
                Key = Key.I,
                Modifiers = ModifierKeys.Control,
                CommandName = "ImportChart",
                Context = ShortcutContext.Global
            };

            yield return new ShortcutBinding
            {
                Id = "ImportStoryboard",
                Description = "导入故事板",
                Key = Key.I,
                Modifiers = ModifierKeys.Control | ModifierKeys.Shift,
                CommandName = "ImportStoryboard",
                Context = ShortcutContext.Global
            };

            yield return new ShortcutBinding
            {
                Id = "ImportAudio",
                Description = "导入音频",
                Key = Key.M,
                Modifiers = ModifierKeys.Control,
                CommandName = "ImportAudio",
                Context = ShortcutContext.Global
            };

            // ==========================================
            // 🎛️ 工具操作
            // ==========================================
            yield return new ShortcutBinding
            {
                Id = "OpenPropertyEditor",
                Description = "打开属性编辑器",
                Key = Key.E,
                Modifiers = ModifierKeys.Control,
                CommandName = "OpenPropertyEditor",
                Context = ShortcutContext.EventList | ShortcutContext.Timeline | ShortcutContext.PropertyPanel
            };

            yield return new ShortcutBinding
            {
                Id = "AddNewText",
                Description = "新建文本事件",
                Key = Key.T,
                Modifiers = ModifierKeys.Control,
                CommandName = "AddNewText",
                Context = ShortcutContext.EventList
            };

            yield return new ShortcutBinding
            {
                Id = "AddNewLine",
                Description = "新建线条事件",
                Key = Key.L,
                Modifiers = ModifierKeys.Control,
                CommandName = "AddNewLine",
                Context = ShortcutContext.EventList
            };

            yield return new ShortcutBinding
            {
                Id = "AddNewSceneController",
                Description = "新建场景控制器",
                Key = Key.C,
                Modifiers = ModifierKeys.Control | ModifierKeys.Shift,
                CommandName = "AddNewSceneController",
                Context = ShortcutContext.EventList
            };

            // ==========================================
            // ▶️ 播放控制
            // ==========================================
            yield return new ShortcutBinding
            {
                Id = "TimelinePlayPause",
                Description = "播放/暂停",
                Key = Key.Space,
                Modifiers = ModifierKeys.None,
                CommandName = "TimelinePlayPause",
                Context = ShortcutContext.Timeline,
                Priority = 5
            };

            yield return new ShortcutBinding
            {
                Id = "TimelineGoToStart",
                Description = "跳转到开头",
                Key = Key.Home,
                Modifiers = ModifierKeys.None,
                CommandName = "TimelineGoToStart",
                Context = ShortcutContext.Timeline
            };

            yield return new ShortcutBinding
            {
                Id = "TimelineGoToEnd",
                Description = "跳转到结尾",
                Key = Key.End,
                Modifiers = ModifierKeys.None,
                CommandName = "TimelineGoToEnd",
                Context = ShortcutContext.Timeline
            };

            // ==========================================
            // 🧭 缩放控制
            // ==========================================
            yield return new ShortcutBinding
            {
                Id = "TimelineZoomIn",
                Description = "时间轴放大",
                Key = Key.OemPlus,
                Modifiers = ModifierKeys.Control,
                CommandName = "TimelineZoomIn",
                Context = ShortcutContext.Timeline
            };

            yield return new ShortcutBinding
            {
                Id = "TimelineZoomOut",
                Description = "时间轴缩小",
                Key = Key.OemMinus,
                Modifiers = ModifierKeys.Control,
                CommandName = "TimelineZoomOut",
                Context = ShortcutContext.Timeline
            };

            yield return new ShortcutBinding
            {
                Id = "TimelineZoomReset",
                Description = "重置缩放",
                Key = Key.D0,
                Modifiers = ModifierKeys.Control,
                CommandName = "TimelineZoomReset",
                Context = ShortcutContext.Timeline
            };

            yield return new ShortcutBinding { Id = "TimelineFitAll", Description = "缩放到全部内容", Key = Key.F, Modifiers = ModifierKeys.Shift, CommandName = "TimelineFitAll", Context = ShortcutContext.Timeline };
            yield return new ShortcutBinding { Id = "TimelineFocusSelection", Description = "聚焦选中内容", Key = Key.F, Modifiers = ModifierKeys.None, CommandName = "TimelineFocusSelection", Context = ShortcutContext.Timeline };
            yield return new ShortcutBinding { Id = "TimelineOpenMicro", Description = "打开微观时间轴", Key = Key.Enter, Modifiers = ModifierKeys.None, CommandName = "TimelineOpenMicro", Context = ShortcutContext.MainTimeline };
            yield return new ShortcutBinding { Id = "TimelineReturnMain", Description = "返回主时间轴", Key = Key.Escape, Modifiers = ModifierKeys.None, CommandName = "TimelineReturnMain", Context = ShortcutContext.MicroTimeline, Priority = 20 };
            yield return new ShortcutBinding { Id = "TimelineSelectAll", Description = "全选时间轴项目", Key = Key.A, Modifiers = ModifierKeys.Control, CommandName = "TimelineSelectAll", Context = ShortcutContext.Timeline };
            yield return new ShortcutBinding { Id = "TimelineCopy", Description = "复制时间轴项目", Key = Key.C, Modifiers = ModifierKeys.Control, CommandName = "TimelineCopy", Context = ShortcutContext.Timeline };
            yield return new ShortcutBinding { Id = "TimelinePaste", Description = "粘贴时间轴项目", Key = Key.V, Modifiers = ModifierKeys.Control, CommandName = "TimelinePaste", Context = ShortcutContext.Timeline };
            yield return new ShortcutBinding { Id = "TimelineAddKeyframe", Description = "添加关键帧", Key = Key.K, Modifiers = ModifierKeys.Control, CommandName = "TimelineAddKeyframe", Context = ShortcutContext.MicroTimeline };
            yield return new ShortcutBinding { Id = "TimelinePreviousKeyframe", Description = "上一个关键帧", Key = Key.PageUp, Modifiers = ModifierKeys.None, CommandName = "TimelinePreviousKeyframe", Context = ShortcutContext.MicroTimeline };
            yield return new ShortcutBinding { Id = "TimelineNextKeyframe", Description = "下一个关键帧", Key = Key.PageDown, Modifiers = ModifierKeys.None, CommandName = "TimelineNextKeyframe", Context = ShortcutContext.MicroTimeline };
            yield return new ShortcutBinding { Id = "TimelineNudgeLeft", Description = "向左微移", Key = Key.Left, Modifiers = ModifierKeys.None, CommandName = "TimelineNudgeLeft", Context = ShortcutContext.Timeline };
            yield return new ShortcutBinding { Id = "TimelineNudgeRight", Description = "向右微移", Key = Key.Right, Modifiers = ModifierKeys.None, CommandName = "TimelineNudgeRight", Context = ShortcutContext.Timeline };
            yield return new ShortcutBinding { Id = "TimelineNudgeLeftLarge", Description = "向左大步移动", Key = Key.Left, Modifiers = ModifierKeys.Shift, CommandName = "TimelineNudgeLeftLarge", Context = ShortcutContext.Timeline };
            yield return new ShortcutBinding { Id = "TimelineNudgeRightLarge", Description = "向右大步移动", Key = Key.Right, Modifiers = ModifierKeys.Shift, CommandName = "TimelineNudgeRightLarge", Context = ShortcutContext.Timeline };
            yield return new ShortcutBinding { Id = "TimelineToggleSnap", Description = "切换时间轴吸附", Key = Key.S, Modifiers = ModifierKeys.Shift, CommandName = "TimelineToggleSnap", Context = ShortcutContext.Timeline };
            yield return new ShortcutBinding { Id = "TimelineDetachTemplate", Description = "解绑模板实例", Key = Key.U, Modifiers = ModifierKeys.Control | ModifierKeys.Shift, CommandName = "TimelineDetachTemplate", Context = ShortcutContext.MicroTimeline };
            yield return new ShortcutBinding { Id = "TimelineCancelEdit", Description = "取消当前时间轴编辑", Key = Key.Escape, Modifiers = ModifierKeys.None, CommandName = "TimelineCancelEdit", Context = ShortcutContext.MainTimeline, Priority = 20 };

            // ==========================================
            // 🖥️ 视图操作
            // ==========================================
            yield return new ShortcutBinding
            {
                Id = "RefreshView",
                Description = "刷新视图",
                Key = Key.F5,
                Modifiers = ModifierKeys.None,
                CommandName = "RefreshView",
                Context = ShortcutContext.Global
            };

            yield return new ShortcutBinding
            {
                Id = "ToggleFullScreen",
                Description = "切换全屏",
                Key = Key.F11,
                Modifiers = ModifierKeys.None,
                CommandName = "ToggleFullScreen",
                Context = ShortcutContext.Global
            };

            // ==========================================
            // 🔍 搜索与帮助
            // ==========================================
            yield return new ShortcutBinding
            {
                Id = "Find",
                Description = "查找",
                Key = Key.F,
                Modifiers = ModifierKeys.Control,
                CommandName = "Find",
                Context = ShortcutContext.Global
            };

            yield return new ShortcutBinding
            {
                Id = "Help",
                Description = "帮助",
                Key = Key.F1,
                Modifiers = ModifierKeys.None,
                CommandName = "Help",
                Context = ShortcutContext.Global
            };

            // ==========================================
            // 🎨 画布操作（Canvas 上下文）
            // ==========================================
            yield return new ShortcutBinding
            {
                Id = "CanvasZoomIn",
                Description = "画布放大",
                Key = Key.OemPlus,
                Modifiers = ModifierKeys.Control,
                CommandName = "CanvasZoomIn",
                Context = ShortcutContext.Canvas
            };

            yield return new ShortcutBinding
            {
                Id = "CanvasZoomOut",
                Description = "画布缩小",
                Key = Key.OemMinus,
                Modifiers = ModifierKeys.Control,
                CommandName = "CanvasZoomOut",
                Context = ShortcutContext.Canvas
            };

            yield return new ShortcutBinding
            {
                Id = "CanvasZoomReset",
                Description = "画布重置缩放",
                Key = Key.D0,
                Modifiers = ModifierKeys.Control,
                CommandName = "CanvasZoomReset",
                Context = ShortcutContext.Canvas
            };

            // ==========================================
            // 🎵 音符列表操作（NoteList 上下文）
            // ==========================================
            yield return new ShortcutBinding
            {
                Id = "NoteListNavigateUp",
                Description = "上一个音符",
                Key = Key.Up,
                Modifiers = ModifierKeys.None,
                CommandName = "NoteListNavigateUp",
                Context = ShortcutContext.NoteList
            };

            yield return new ShortcutBinding
            {
                Id = "NoteListNavigateDown",
                Description = "下一个音符",
                Key = Key.Down,
                Modifiers = ModifierKeys.None,
                CommandName = "NoteListNavigateDown",
                Context = ShortcutContext.NoteList
            };

            // ==========================================
            // 🚪 退出
            // ==========================================
            yield return new ShortcutBinding
            {
                Id = "Exit",
                Description = "退出应用",
                Key = Key.F4,
                Modifiers = ModifierKeys.Alt,
                CommandName = "Exit",
                Context = ShortcutContext.Global
            };
        }
    }
}
