namespace Naziki_Editor.Core.Shortcuts
{
    /// <summary>
    /// 定义快捷键的上下文范围。
    /// 使用 Flags 枚举支持多上下文同时激活。
    /// 优先级从低到高：Global → 具体上下文 → 控件级覆写。
    /// 
    /// 设计说明：
    /// - Global = 0 是有意为之。在 Flags 枚举中，0 值表示"匹配所有上下文"，
    ///   配合 ShortcutBinding.MatchesContext() 中的特殊处理，确保全局快捷键
    ///   在任何活跃上下文中都能触发。
    /// - Global 不能与其他上下文做位运算组合（因为 0 | X == X），
    ///   这是 Acceptable Trade-off——全局快捷键本身就是"无条件匹配"。
    /// - TextEditor 和 ModalDialog 是为未来扩展预留的上下文值，
    ///   当前版本中未使用，但保留以维持位掩码的连续性。
    /// </summary>
    [Flags]
    public enum ShortcutContext
    {
        /// <summary>
        /// 全局快捷键，无论焦点在哪里都生效。
        /// 注意：值为 0 是设计决策，配合 MatchesContext() 中的特殊判断实现"无条件匹配"。
        /// </summary>
        Global = 0,

        /// <summary>事件列表面板获得焦点</summary>
        EventList = 1 << 0,

        /// <summary>素材库面板获得焦点</summary>
        AssetList = 1 << 1,

        /// <summary>时间轴面板获得焦点</summary>
        Timeline = 1 << 2,

        /// <summary>画布 / JSON 编辑器获得焦点</summary>
        Canvas = 1 << 3,

        /// <summary>属性面板获得焦点</summary>
        PropertyPanel = 1 << 4,

        /// <summary>音符列表获得焦点</summary>
        NoteList = 1 << 5,

        /// <summary>
        /// 文本编辑器内部（JSON 编辑器等），优先级最高。
        /// 预留值，当前版本未使用。
        /// </summary>
        TextEditor = 1 << 6,

        /// <summary>
        /// 模态对话框。
        /// 预留值，当前版本未使用。
        /// </summary>
        ModalDialog = 1 << 7,

        /// <summary>匹配所有上下文（用于冲突检测）</summary>
        Any = ~0
    }
}