using Naziki_Editor.Models;
using System.Collections.Generic;
using System.Linq;

namespace Naziki_Editor.Core
{
    /// <summary>
    /// 小艾的模板大管家：专职负责模板的生命周期、依赖检测与级联更新！
    /// </summary>
    public static class TemplateManager
    {
        // ==========================================
        // 🔍 功能 1：防死循环 (环形依赖检测)
        // ==========================================
        public static bool CheckForCircularDependency(StoryboardRoot root, string templateName, string targetTemplateToInject)
        {
            if (string.IsNullOrEmpty(targetTemplateToInject)) return false;
            if (templateName == targetTemplateToInject) return true; // 自己套娃自己

            if (root.templates != null && root.templates.TryGetValue(targetTemplateToInject, out var nextTemplate))
            {
                // 扫描目标模板里的所有状态，看看有没有绕回来的
                var allStates = GetAllStatesFromTemplate(nextTemplate);
                foreach (var state in allStates)
                {
                    if (state.Template == templateName) return true;
                    // 递归往深处挖
                    if (CheckForCircularDependency(root, templateName, state.Template)) return true;
                }
            }
            return false;
        }

        // ==========================================
        // 🔄 功能 2：级联重命名
        // ==========================================
        public static void RenameTemplateGlobally(StoryboardRoot root, string oldName, string newName)
        {
            if (root == null || string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName)) return;

            // 1. 修改字典里的 Key
            if (root.templates != null && root.templates.ContainsKey(oldName))
            {
                var templateData = root.templates[oldName];
                root.templates.Remove(oldName);
                root.templates[newName] = templateData;
            }

            // 2. 扫荡全宇宙，修改所有引用！
            var allStates = GetAllStatesInStoryboard(root);
            foreach (var state in allStates.Where(s => s.Template == oldName))
            {
                state.Template = newName;
            }
        }

        // ==========================================
        // 🗑️ 功能 3：级联解除绑定 (删除模板时调用)
        // ==========================================
        public static int UnbindTemplateGlobally(StoryboardRoot root, string templateName)
        {
            if (root == null || string.IsNullOrEmpty(templateName)) return 0;

            int affectedCount = 0;
            var allStates = GetAllStatesInStoryboard(root);

            foreach (var state in allStates.Where(s => s.Template == templateName))
            {
                state.Template = null; // ✨ 小艾的温柔一刀：只摘掉牌子，不毁坏原本的心血！
                affectedCount++;
            }

            // 从字典中彻底抹除
            if (root.templates != null && root.templates.ContainsKey(templateName))
            {
                root.templates.Remove(templateName);
            }

            return affectedCount;
        }

        // ==========================================
        // 🛠️ 辅助法术：把故事板里所有的状态全部翻出来！
        // ==========================================
        private static IEnumerable<ObjectState> GetAllStatesInStoryboard(StoryboardRoot root)
        {
            var allStates = new List<ObjectState>();
            if (root == null) return allStates;

            if (root.sprites != null) foreach (var o in root.sprites) ExtractStates(o.States, allStates);
            if (root.texts != null) foreach (var o in root.texts) ExtractStates(o.States, allStates);
            if (root.lines != null) foreach (var o in root.lines) ExtractStates(o.States, allStates);
            if (root.videos != null) foreach (var o in root.videos) ExtractStates(o.States, allStates);
            if (root.controllers != null) foreach (var o in root.controllers) ExtractStates(o.States, allStates);
            if (root.note_controllers != null) foreach (var o in root.note_controllers) ExtractStates(o.States, allStates);

            // 模板自己也会引用别的模板，也要搜查！
            if (root.templates != null)
            {
                foreach (var t in root.templates.Values)
                {
                    allStates.AddRange(GetAllStatesFromTemplate(t));
                }
            }
            return allStates;
        }

        // 🛠️ 辅助法术：完美适应当前 StoryboardTemplate 继承自 StoryboardRoot 的架构
        private static IEnumerable<ObjectState> GetAllStatesFromTemplate(StoryboardTemplate template)
        {
            // 既然模板本身就是一个微型故事板大本营，直接用大本营扫描器把它体内的所有状态剥离出来！
            var allStates = new List<ObjectState>();
            // 注意：模板自己就是个 StageObjectState，同时包含 States 数组
            allStates.Add(template);
            ExtractStates(template.States, allStates);
            return allStates;
        }

        private static void ExtractStates<T>(IEnumerable<T> source, List<ObjectState> target) where T : ObjectState
        {
            if (source == null) return;
            foreach (var state in source)
            {
                target.Add(state);
            }
        }
    }
}