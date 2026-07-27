namespace Naziki_Editor.Core.Settings
{
    /// <summary>
    /// 设置项的值类型枚举，用于驱动 UI 层渲染对应的输入控件。
    /// </summary>
    public enum SettingValueType
    {
        /// <summary>布尔值（渲染为 ToggleSwitch / CheckBox）</summary>
        Bool,

        /// <summary>字符串（渲染为 TextBox）</summary>
        String,

        /// <summary>整数（渲染为 NumericUpDown 或 TextBox with validation）</summary>
        Integer,

        /// <summary>浮点数（渲染为 TextBox with validation）</summary>
        Float,

        /// <summary>下拉选项（渲染为 ComboBox）</summary>
        Combo,

        /// <summary>文件路径（渲染为 TextBox + Browse 按钮）</summary>
        FilePath,

        /// <summary>目录路径（渲染为 TextBox + Browse 按钮）</summary>
        DirectoryPath,

        /// <summary>颜色选择（渲染为 ColorPicker）</summary>
        Color,

        /// <summary>快捷键组合（渲染为 KeyBinding 输入框）</summary>
        KeyBinding,

        /// <summary>多行文本（渲染为多行 TextBox）</summary>
        MultiLineText
    }
}