using System;
using System.Collections;
using System.Collections.Generic;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Models;

namespace Naziki_Editor.Core.Storyboard
{
    /// <summary>
    /// 故事板仓储实现，集中处理实体在 StoryboardRoot 各集合中的增删改查。
    /// </summary>
    public class StoryboardRepository : IStoryboardRepository
    {
        public void Add(StoryboardRoot root, IStoryboardEntity entity)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var list = GetListByType(root, entity.GetType());
            if (list == null) throw new InvalidOperationException($"不支持的实体类型：{entity.GetType().Name}");

            list.Add(entity);
        }

        public void Remove(StoryboardRoot root, IStoryboardEntity entity)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var list = GetListByType(root, entity.GetType());
            if (list == null) return;

            list.Remove(entity);
        }

        public void Replace(StoryboardRoot root, IStoryboardEntity oldEntity, IStoryboardEntity newEntity)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (oldEntity == null) throw new ArgumentNullException(nameof(oldEntity));
            if (newEntity == null) throw new ArgumentNullException(nameof(newEntity));

            var list = GetListByType(root, oldEntity.GetType());
            if (list == null) return;

            int index = list.IndexOf(oldEntity);
            if (index >= 0) list[index] = newEntity;
        }

        public IList? GetListByType(StoryboardRoot root, Type type)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (type == null) throw new ArgumentNullException(nameof(type));

            if (typeof(C2Sprite).IsAssignableFrom(type)) return root.sprites;
            if (typeof(C2Text).IsAssignableFrom(type)) return root.texts;
            if (typeof(C2Line).IsAssignableFrom(type)) return root.lines;
            if (typeof(C2Video).IsAssignableFrom(type)) return root.videos;
            if (typeof(C2SceneController).IsAssignableFrom(type)) return root.controllers;
            if (typeof(C2NoteController).IsAssignableFrom(type)) return root.note_controllers;

            return null;
        }

        public IEnumerable<IStoryboardEntity> GetAllEntities(StoryboardRoot root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));

            var result = new List<IStoryboardEntity>();
            if (root.sprites != null) result.AddRange(root.sprites);
            if (root.texts != null) result.AddRange(root.texts);
            if (root.lines != null) result.AddRange(root.lines);
            if (root.videos != null) result.AddRange(root.videos);
            if (root.controllers != null) result.AddRange(root.controllers);
            if (root.note_controllers != null) result.AddRange(root.note_controllers);
            return result;
        }

        public void MoveEntityToIndex(StoryboardRoot root, IStoryboardEntity entity, int newIndex)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var list = GetListByType(root, entity.GetType());
            if (list == null) return;

            int currentIndex = list.IndexOf(entity);
            if (currentIndex < 0 || newIndex < 0 || newIndex >= list.Count) return;

            var temp = list[currentIndex];
            if (currentIndex < newIndex)
            {
                for (int i = currentIndex; i < newIndex; i++)
                    list[i] = list[i + 1];
            }
            else
            {
                for (int i = currentIndex; i > newIndex; i--)
                    list[i] = list[i - 1];
            }
            list[newIndex] = temp;
        }

        public void AddTemplate(StoryboardRoot root, string key, C2Template template)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (key == null) throw new ArgumentNullException(nameof(key));

            if (root.templates == null) root.templates = new Dictionary<string, C2Template>();
            root.templates[key] = template;
        }

        public void RemoveTemplate(StoryboardRoot root, string key)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (key == null) return;

            root.templates?.Remove(key);
        }

        public void RenameTemplate(StoryboardRoot root, string oldKey, string newKey)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (oldKey == null || newKey == null || oldKey == newKey) return;
            if (root.templates == null || !root.templates.ContainsKey(oldKey)) return;

            var template = root.templates[oldKey];
            root.templates.Remove(oldKey);
            root.templates[newKey] = template;
        }

        public bool ContainsTemplate(StoryboardRoot root, string key)
        {
            if (root == null || key == null || root.templates == null) return false;
            return root.templates.ContainsKey(key);
        }

        public C2Template? GetTemplate(StoryboardRoot root, string key)
        {
            if (root == null || key == null || root.templates == null) return null;
            root.templates.TryGetValue(key, out var template);
            return template;
        }

        public string? GetTemplateKey(StoryboardRoot root, C2Template template)
        {
            if (root?.templates == null || template == null) return null;
            foreach (var kvp in root.templates)
            {
                if (kvp.Value == template) return kvp.Key;
            }
            return null;
        }
    }
}
