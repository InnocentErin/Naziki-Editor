using System.Windows.Input;

namespace Naziki_Editor.Core.Shortcuts
{
    /// <summary>
    /// 表示一个快捷键绑定：按键组合 → 命令名 → 上下文。
    /// 封装了快捷键的完整元数据，包括唯一标识、描述、按键组合、
    /// 触发的命令名称、生效上下文和优先级。
    /// </summary>
    public class ShortcutBinding
    {
        /// <summary>
        /// 绑定的唯一标识符（如 "SaveProject"）。
        /// 用于注册、注销和查找。
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 人类可读的描述（如 "保存项目"）。
        /// 用于 UI 提示和文档生成。
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 主键（如 Key.S）。
        /// </summary>
        public Key Key { get; set; }

        /// <summary>
        /// 修饰键组合（如 ModifierKeys.Control）。
        /// 使用 Flags 组合支持多修饰键（如 Ctrl+Shift）。
        /// </summary>
        public ModifierKeys Modifiers { get; set; }

        /// <summary>
        /// 触发的命令名称，对应 ICommandDispatcher 中注册的命令。
        /// </summary>
        public string CommandName { get; set; } = string.Empty;

        /// <summary>
        /// 快捷键生效的上下文。
        /// Global 表示在所有上下文中生效。
        /// </summary>
        public ShortcutContext Context { get; set; } = ShortcutContext.Global;

        /// <summary>
        /// 优先级（数值越大越优先）。
        /// 用于同一按键组合在相同上下文中的冲突解决。
        /// 默认值：0。
        /// </summary>
        public int Priority { get; set; }

        /// <summary>
        /// 是否启用。禁用后快捷键不会触发命令。
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 生成 InputGestureText 格式的显示文本。
        /// 例如："Ctrl+S"、"Ctrl+Shift+E"。
        /// </summary>
        public string ToGestureText()
        {
            var parts = new List<string>();

            if (Modifiers.HasFlag(ModifierKeys.Control))
                parts.Add("Ctrl");
            if (Modifiers.HasFlag(ModifierKeys.Alt))
                parts.Add("Alt");
            if (Modifiers.HasFlag(ModifierKeys.Shift))
                parts.Add("Shift");
            if (Modifiers.HasFlag(ModifierKeys.Windows))
                parts.Add("Win");

            parts.Add(KeyToString(Key));

            return string.Join("+", parts);
        }

        /// <summary>
        /// 生成用于字典查找的按键组合键。
        /// </summary>
        public (Key Key, ModifierKeys Modifiers) ToLookupKey()
        {
            return (Key, Modifiers);
        }

        /// <summary>
        /// 判断此绑定是否匹配给定的上下文。
        /// Global 绑定总是匹配；非 Global 绑定需要上下文标志位匹配。
        /// </summary>
        public bool MatchesContext(ShortcutContext activeContext)
        {
            if (Context == ShortcutContext.Global)
                return true;

            return (Context & activeContext) != 0;
        }

        /// <summary>
        /// 判断两个绑定是否在相同的按键组合和上下文上存在冲突。
        /// </summary>
        public bool ConflictsWith(ShortcutBinding other)
        {
            if (other == null) return false;
            if (ReferenceEquals(this, other)) return false;

            // 按键组合必须相同
            if (Key != other.Key || Modifiers != other.Modifiers)
                return false;

            // 任一方的上下文是 Global，或双方上下文有重叠
            if (Context == ShortcutContext.Global || other.Context == ShortcutContext.Global)
                return true;

            return (Context & other.Context) != 0;
        }

        private static string KeyToString(Key key)
        {
            return key switch
            {
                Key.D0 => "0",
                Key.D1 => "1",
                Key.D2 => "2",
                Key.D3 => "3",
                Key.D4 => "4",
                Key.D5 => "5",
                Key.D6 => "6",
                Key.D7 => "7",
                Key.D8 => "8",
                Key.D9 => "9",
                Key.OemPlus => "=",
                Key.OemMinus => "-",
                Key.OemComma => ",",
                Key.OemPeriod => ".",
                Key.OemQuestion => "/",
                Key.OemSemicolon => ";",
                Key.OemQuotes => "'",
                Key.OemOpenBrackets => "[",
                Key.OemCloseBrackets => "]",
                Key.OemPipe => "\\",
                Key.OemTilde => "`",
                _ => key.ToString()
            };
        }
    }
}