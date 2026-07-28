using System;
using System.Collections.Generic;
using Naziki_Editor.Core.Settings;

namespace Naziki_Editor.Core.Abstractions
{
    /// <summary>
    /// 统一的设置存储与加载接口。
    /// 提供键值对式的设置存取能力，并支持设置变更通知。
    /// 默认实现使用 JSON 文件持久化到用户 AppData 目录。
    /// </summary>
    public interface ISettingsStore
    {
        /// <summary>
        /// 设置变更事件，当任意设置项值发生变化时触发。
        /// 其他模块可订阅此事件以响应设置变化。
        /// </summary>
        event EventHandler<SettingsChangedEventArgs>? SettingChanged;

        /// <summary>
        /// 获取指定键的设置值，若不存在则返回默认值。
        /// </summary>
        /// <typeparam name="T">设置值类型</typeparam>
        /// <param name="key">设置键名（建议使用 "Category.SettingName" 格式）</param>
        /// <param name="defaultValue">默认值</param>
        T Get<T>(string key, T defaultValue = default);

        /// <summary>
        /// 设置指定键的值，并触发 SettingChanged 事件。
        /// </summary>
        /// <typeparam name="T">设置值类型</typeparam>
        /// <param name="key">设置键名</param>
        /// <param name="value">设置值</param>
        void Set<T>(string key, T value);

        /// <summary>
        /// 检查指定键是否存在。
        /// </summary>
        bool ContainsKey(string key);

        /// <summary>
        /// 获取所有设置分类的列表。
        /// </summary>
        IReadOnlyList<SettingsCategory> GetCategories();

        /// <summary>
        /// 获取指定分类下的所有设置项。
        /// </summary>
        IReadOnlyList<SettingItem> GetCategoryItems(string categoryKey);

        /// <summary>
        /// 从持久化存储加载所有设置。
        /// </summary>
        void Load();

        /// <summary>
        /// 将所有设置保存到持久化存储。
        /// </summary>
        void Save();

        /// <summary>
        /// 将指定键的设置重置为默认值。
        /// </summary>
        void Reset(string key);

        /// <summary>
        /// 将指定分类的所有设置重置为默认值。
        /// </summary>
        void ResetCategory(string categoryKey);

        /// <summary>
        /// 注册一个设置分类及其包含的设置项定义。
        /// 应在应用程序启动时调用，用于声明可用的设置项。
        /// </summary>
        void RegisterCategory(SettingsCategory category);
    }
}