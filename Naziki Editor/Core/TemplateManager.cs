using Naziki_Editor.Models;
using System.Collections.Generic;

namespace Naziki_Editor.Core
{
    public static class TemplateManager
    {
        public static bool CheckForCircularDependency(StoryboardRoot root, string templateName, string targetTemplateToInject)
        {
            if (string.IsNullOrEmpty(targetTemplateToInject)) return false;
            if (templateName == targetTemplateToInject) return true;

            if (root.templates != null && root.templates.TryGetValue(targetTemplateToInject, out var nextTemplate))
            {
                var allStates = GetAllStatesFromTemplate(nextTemplate);
                foreach (var state in allStates)
                {
                    if (state.Template == templateName) return true;
                    if (CheckForCircularDependency(root, templateName, state.Template)) return true;
                }
            }
            return false;
        }

        public static void RenameTemplateGlobally(StoryboardRoot root, string oldName, string newName)
        {
            var allStates = GetAllStatesInStoryboard(root);
            foreach (var state in allStates)
            {
                if (state.Template == oldName)
                {
                    state.Template = newName;
                }
            }
        }

        public static List<ObjectState> GetAllStatesInStoryboard(StoryboardRoot root)
        {
            var allStates = new List<ObjectState>();
            if (root == null) return allStates;

            if (root.sprites != null) foreach (var o in root.sprites) ExtractStates(o, allStates);
            if (root.texts != null) foreach (var o in root.texts) ExtractStates(o, allStates);
            if (root.lines != null) foreach (var o in root.lines) ExtractStates(o, allStates);
            if (root.videos != null) foreach (var o in root.videos) ExtractStates(o, allStates);
            if (root.controllers != null) foreach (var o in root.controllers) ExtractStates(o, allStates);
            if (root.note_controllers != null) foreach (var o in root.note_controllers) ExtractStates(o, allStates);

            if (root.templates != null)
            {
                foreach (var t in root.templates.Values)
                {
                    allStates.AddRange(GetAllStatesFromTemplate(t));
                }
            }
            return allStates;
        }

        private static IEnumerable<ObjectState> GetAllStatesFromTemplate(C2Template template)
        {
            var allStates = new List<ObjectState>();
            if (template.BaseState != null) allStates.Add(template.BaseState);
            if (template.Keyframes != null) allStates.AddRange(template.Keyframes);
            return allStates;
        }

        private static void ExtractStates(IStoryboardEntity entity, List<ObjectState> target)
        {
            if (entity.GetBaseState() is ObjectState bs) target.Add(bs);
            if (entity.GetKeyframes() != null)
            {
                foreach (var frame in entity.GetKeyframes())
                {
                    if (frame is ObjectState os) target.Add(os);
                }
            }
        }
    }
}