using System.Collections.Generic;

namespace Naziki_Editor.Core.Settings
{
    /// <summary>
    /// 设置分类数据模型，代表左侧导航栏中的一个分类。
    /// 包含分类键名、显示名称、图标文本和该分类下的所有设置项。
    /// </summary>
    public class SettingsCategory
    {
        /// <summary>分类唯一键名（如 "General", "Appearance", "Editor" 等）</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>分类显示名称（如 "基本设置"）</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>分类图标（使用 Unicode 字符或 Emoji，如 "⚙️"）</summary>
        public string Icon { get; set; } = "⚙️";

        /// <summary>分类描述（Tooltip 提示）</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>排序序号（数值越小越靠前）</summary>
        public int Order { get; set; }

        /// <summary>该分类下的所有设置项</summary>
        public List<SettingItem> Items { get; set; } = new();
    }
}