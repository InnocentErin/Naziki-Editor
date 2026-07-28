using System;

namespace Naziki_Editor.Core.Settings
{
    /// <summary>
    /// 设置变更事件参数，携带变更的设置键名、旧值和新值。
    /// 其他模块可通过订阅此事件响应特定设置的变化。
    /// </summary>
    public class SettingsChangedEventArgs : EventArgs
    {
        /// <summary>发生变更的设置键名</summary>
        public string Key { get; }

        /// <summary>变更前的值（可能为 null）</summary>
        public object? OldValue { get; }

        /// <summary>变更后的值（可能为 null）</summary>
        public object? NewValue { get; }

        /// <summary>所属分类键名</summary>
        public string CategoryKey { get; }

        public SettingsChangedEventArgs(string key, object? oldValue, object? newValue, string categoryKey)
        {
            Key = key;
            OldValue = oldValue;
            NewValue = newValue;
            CategoryKey = categoryKey;
        }
    }
}