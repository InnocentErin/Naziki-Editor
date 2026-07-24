using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.ErrorHandling;
using Naziki_Editor.Core.Serialization.Converters;
using Naziki_Editor.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Naziki_Editor.Core
{
    // =========================================================================
    // 🔮 故事板全量智能赋名与顺位留痕枢纽（导入与存盘的终极安检门！）
    // =========================================================================
    public class StoryboardParser : IStoryboardParser
    {
        private readonly IErrorHandler _errorHandler;

        public StoryboardParser(IErrorHandler errorHandler)
        {
            _errorHandler = errorHandler;
        }
        // 📥 读盘/导入总线：全盘恢复或动态分配控制板身份证
        public void StandardizeStoryboardIds(StoryboardRoot root, NazikiProjectModel project)
        {
            if (root == null) return;

            _errorHandler.TryExecute(() =>
            {
                // 🌟 P0修复：先处理 $note 占位符，确保后续逻辑能正确处理
                ResolveNotePlaceholders(root);

                // 依次全量洗盘 6 大场景对象数组
                ProcessList(root.sprites, "sprite", root, project);
                ProcessList(root.texts, "text", root, project);
                ProcessList(root.videos, "video", root, project);
                ProcessList(root.lines, "line", root, project);
                ProcessList(root.controllers, "controller", root, project);
                ProcessList(root.note_controllers, "note", root, project);
            }, "DataValidation", "StoryboardParser.StandardizeStoryboardIds");
        }

        // 🌟 P0修复：$note 占位符处理
        // 当 note_controller 的 note 字段为具体数字时，替换 $note 占位符
        private static void ResolveNotePlaceholders(StoryboardRoot root)
        {
            if (root?.note_controllers == null) return;

            foreach (var nc in root.note_controllers)
            {
                // 只处理 note 为具体数字的情况（非选择器）
                if (nc.BaseState?.NoteTarget is long noteId || nc.BaseState?.NoteTarget is int noteIdInt)
                {
                    string noteIdStr = (nc.BaseState.NoteTarget is long l) ? l.ToString() : ((int)nc.BaseState.NoteTarget).ToString();
                    ReplaceNotePlaceholder(nc, noteIdStr);
                }
                // 对于 note 选择器 {}，保留 $note 占位符不变（游戏运行时会展开）
            }
        }

        private static void ReplaceNotePlaceholder(IStoryboardEntity entity, string noteId)
        {
            const string placeholder = "$note";

            if (entity.Id?.Contains(placeholder) == true)
                entity.Id = entity.Id.Replace(placeholder, noteId);

            if (entity.ParentId?.Contains(placeholder) == true)
                entity.ParentId = entity.ParentId.Replace(placeholder, noteId);

            if (entity.TargetId?.Contains(placeholder) == true)
                entity.TargetId = entity.TargetId.Replace(placeholder, noteId);

            // 处理 BaseState 中的 time 字段
            var baseState = entity.GetBaseState();
            if (baseState != null)
            {
                var timeProp = baseState.GetType().GetProperty("Time");
                if (timeProp != null)
                {
                    var timeVal = timeProp.GetValue(baseState);
                    if (timeVal is string timeStr && timeStr.Contains(placeholder))
                    {
                        timeProp.SetValue(baseState, timeStr.Replace(placeholder, noteId));
                    }
                }
            }
        }

        private static void ProcessList<T>(List<T> list, string typePrefix, StoryboardRoot root, NazikiProjectModel project) where T : IStoryboardEntity
        {
            if (list == null) return;

            // 针对每个宿主目标（TargetId）独立维护计数器，精准定位多胞胎控制板的出场顺位
            var targetCounters = new Dictionary<string, int>();

            foreach (var entity in list)
            {
                // 情况 A：如果在 JSON 里原本就有 id（属于有渲染肉体的标准场景实体），保持不变
                if (!string.IsNullOrEmpty(entity.Id)) continue;

                // 情况 B：如果是控制板对象（JSON里官方无id，但有 target_id）
                if (!string.IsNullOrEmpty(entity.TargetId))
                {
                    string targetId = entity.TargetId;
                    if (!targetCounters.ContainsKey(targetId)) targetCounters[targetId] = 0;
                    int index = targetCounters[targetId]++;

                    // 🛠️ 构造全宇宙唯一的顺位小账本检索钥匙
                    string mapKey = $"cb_{typePrefix}_{targetId}_{index}";

                    if (project != null && project.ControlBoardIdMaps != null && project.ControlBoardIdMaps.TryGetValue(mapKey, out string savedId))
                    {
                        // 📖 账本里有记录！说明是重启或二次打开，直接精准重合复活原有的唯一唯一ID！
                        entity.Id = savedId;
                    }
                    else
                    {
                        // 🆕 初次导入野生谱面，账本无记录，小艾动态为它捏一个合法的身份证，并立刻在账本上留痕！
                        string generatedId = $"{targetId}_target_{index + 1}_{Guid.NewGuid().ToString().Substring(0, 8)}";
                        entity.Id = generatedId;

                        if (project != null && project.ControlBoardIdMaps != null)
                        {
                            project.ControlBoardIdMaps[mapKey] = generatedId;
                        }
                    }
                }
                else
                {
                    // 情况 C：既没有id也没有target_id的野生实体，走原本的智能命名
                    entity.Id = GenerateSmartIdForImport(entity, typePrefix, root);
                }
            }
        }

        // 💾 存盘前夕反向同步总线：在写盘前，将内存中新创的控制板和顺位重新死死锁进 .nep 字典里！
        public void SyncControlBoardIdMaps(StoryboardRoot root, NazikiProjectModel project)
        {
            if (root == null || project == null || project.ControlBoardIdMaps == null) return;

            _errorHandler.TryExecute(() =>
            {
                project.ControlBoardIdMaps.Clear(); // 刷新旧账本，防残留
                SyncList(root.sprites, "sprite", project);
                SyncList(root.texts, "text", project);
                SyncList(root.videos, "video", project);
                SyncList(root.lines, "line", project);
                SyncList(root.controllers, "controller", project);
                SyncList(root.note_controllers, "note", project);
            }, "DataValidation", "StoryboardParser.SyncControlBoardIdMaps");
        }

        private static void SyncList<T>(List<T> list, string typePrefix, NazikiProjectModel project) where T : IStoryboardEntity
        {
            if (list == null) return;
            var targetCounters = new Dictionary<string, int>();

            foreach (var entity in list)
            {
                if (!string.IsNullOrEmpty(entity.TargetId) && !string.IsNullOrEmpty(entity.Id))
                {
                    string targetId = entity.TargetId;
                    if (!targetCounters.ContainsKey(targetId)) targetCounters[targetId] = 0;
                    int index = targetCounters[targetId]++;

                    string mapKey = $"cb_{typePrefix}_{targetId}_{index}";
                    project.ControlBoardIdMaps[mapKey] = entity.Id; // 将当前的活跃工作 ID 固化写盘
                }
            }
        }

        // =========================================================================
        // 🟢【灵魂归位】：智能野生实体赋名官
        // =========================================================================
        private static string GenerateSmartIdForImport(IStoryboardEntity entity, string typePrefix, StoryboardRoot root)
        {
            // 依据对象门派前缀，揉入一串轻量级的高强度随机码作为初创基因
            string baseId = $"{typePrefix}_{Guid.NewGuid().ToString().Substring(0, 8)}";
            string finalId = baseId;
            int index = 1;

            // 🛡️ 查户口：如果大本营根节点里已经有重名的倒霉蛋了，就不断自增数字后缀直到安全为止
            while (IsIdExistsInRoot(finalId, root))
            {
                finalId = $"{baseId}_{index}";
                index++;
            }

            return finalId;
        }


        private static bool IsIdExistsInRoot(string id, StoryboardRoot root)
        {
            if (root == null) return false;
            bool exists = false;
            if (root.sprites != null) exists |= root.sprites.Exists(x => x.Id == id);
            if (root.texts != null) exists |= root.texts.Exists(x => x.Id == id);
            if (root.videos != null) exists |= root.videos.Exists(x => x.Id == id);
            if (root.lines != null) exists |= root.lines.Exists(x => x.Id == id);
            if (root.controllers != null) exists |= root.controllers.Exists(x => x.Id == id);
            if (root.note_controllers != null) exists |= root.note_controllers.Exists(x => x.Id == id);
            return exists;
        }
    }

    // ==========================================
    // 🌟 全局 JSON 输出大管家 (供预览和保存使用)
    // ==========================================
    public static class StoryboardSerializer
    {
        public static JsonSerializerSettings GetSettings()
        {
            return new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                ContractResolver = new DefaultContractResolver
                {
                    NamingStrategy = new SnakeCaseNamingStrategy() // 依然保持蛇形命名
                },
                Formatting = Formatting.Indented,
                Converters = new List<JsonConverter>
                {
                    new StoryboardEntityConverter(), // ✨ 注入小艾定制的终极转换器！
                    new UnitFloatConverter()
                }
            };
        }

        public static string ToJson(object obj)
        {
            return JsonConvert.SerializeObject(obj, GetSettings());
        }
    }
}