using System;
using System.IO;
using System.Text.RegularExpressions;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Models;

namespace Naziki_Editor.Core.Common
{
    /// <summary>
    /// 实体 ID 服务实现，统一封装 ID 生成、净化与冲突检测逻辑。
    /// </summary>
    public class EntityIdService : IEntityIdService
    {
        public string GenerateUniqueId(IStoryboardEntity entity, StoryboardRoot root)
        {
            if (entity == null) return "obj_1";

            string typeName = "obj";
            string coreValue = "";

            if (entity is C2Sprite s) { typeName = "sprite"; coreValue = s.BaseState?.Path; }
            else if (entity is C2Text t) { typeName = "text"; coreValue = t.BaseState?.TextContent; }
            else if (entity is C2Video v) { typeName = "video"; coreValue = v.BaseState?.Path; }
            else if (entity is C2Line) { typeName = "line"; coreValue = "pos"; }
            else if (entity is C2SceneController) { typeName = "controller"; coreValue = "scene"; }
            else if (entity is C2NoteController nc)
            {
                typeName = "note";
                if (nc.BaseState?.NoteTarget != null)
                {
                    string sVal = nc.BaseState.NoteTarget.ToString() ?? "";
                    coreValue = sVal.StartsWith("{") ? "selector" : sVal;
                }
            }

            coreValue = SanitizeCoreValue(coreValue);

            string baseId = $"{typeName}_{coreValue}".ToLowerInvariant();
            string finalId = baseId;
            int index = 1;

            while (IsIdExists(finalId, root))
            {
                finalId = $"{baseId}_{index}";
                index++;
            }

            return finalId;
        }

        public bool IsIdExists(string id, StoryboardRoot root)
        {
            if (root == null || string.IsNullOrEmpty(id)) return false;

            if (root.sprites?.Exists(x => x.Id == id) == true) return true;
            if (root.texts?.Exists(x => x.Id == id) == true) return true;
            if (root.videos?.Exists(x => x.Id == id) == true) return true;
            if (root.lines?.Exists(x => x.Id == id) == true) return true;
            if (root.controllers?.Exists(x => x.Id == id) == true) return true;
            if (root.note_controllers?.Exists(x => x.Id == id) == true) return true;

            return false;
        }

        public bool IsIdConflict(string id, string originalId, StoryboardRoot root)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (id == originalId) return false;
            return IsIdExists(id, root);
        }

        private static string SanitizeCoreValue(string? coreValue)
        {
            if (string.IsNullOrEmpty(coreValue)) return "new";

            try
            {
                string cleaned = Path.GetFileNameWithoutExtension(coreValue);
                cleaned = Regex.Replace(cleaned, @"[^a-zA-Z0-9\u4e00-\u9fa5]", "_");
                cleaned = Regex.Replace(cleaned, @"_+", "_").Trim('_');
                if (cleaned.Length > 15) cleaned = cleaned.Substring(0, 15);
                return string.IsNullOrEmpty(cleaned) ? "item" : cleaned;
            }
            catch
            {
                return "item";
            }
        }
    }
}
