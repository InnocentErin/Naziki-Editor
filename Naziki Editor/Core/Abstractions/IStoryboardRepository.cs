using System;
using System.Collections;
using System.Collections.Generic;
using Naziki_Editor.Models;

namespace Naziki_Editor.Core.Abstractions
{
    /// <summary>
    /// 故事板仓储抽象，负责将各类实体注入到 StoryboardRoot 的对应集合中。
    /// </summary>
    public interface IStoryboardRepository
    {
        void Add(StoryboardRoot root, IStoryboardEntity entity);
        void Remove(StoryboardRoot root, IStoryboardEntity entity);
        void Replace(StoryboardRoot root, IStoryboardEntity oldEntity, IStoryboardEntity newEntity);
        IList? GetListByType(StoryboardRoot root, Type type);
        IEnumerable<IStoryboardEntity> GetAllEntities(StoryboardRoot root);

        /// <summary>
        /// 将实体在所属集合中移动到指定索引位置（仅支持基于 List 的实体集合）。
        /// </summary>
        void MoveEntityToIndex(StoryboardRoot root, IStoryboardEntity entity, int newIndex);

        // 模板存储在 Dictionary<string, C2Template> 中，需要独立的 CRUD 接口
        void AddTemplate(StoryboardRoot root, string key, C2Template template);
        void RemoveTemplate(StoryboardRoot root, string key);
        void RenameTemplate(StoryboardRoot root, string oldKey, string newKey);
        bool ContainsTemplate(StoryboardRoot root, string key);
        C2Template? GetTemplate(StoryboardRoot root, string key);

        /// <summary>
        /// 根据模板实例反查其在字典中的 Key；找不到返回 null。
        /// </summary>
        string? GetTemplateKey(StoryboardRoot root, C2Template template);
    }
}
