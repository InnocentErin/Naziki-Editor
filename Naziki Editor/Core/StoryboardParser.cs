using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
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
    public static class StoryboardParser
    {
        // 📥 读盘/导入总线：全盘恢复或动态分配控制板身份证
        public static void StandardizeStoryboardIds(StoryboardRoot root, NazikiProjectModel project)
        {
            if (root == null) return;

            // 依次全量洗盘 6 大场景对象数组
            ProcessList(root.sprites, "sprite", root, project);
            ProcessList(root.texts, "text", root, project);
            ProcessList(root.videos, "video", root, project);
            ProcessList(root.lines, "line", root, project);
            ProcessList(root.controllers, "controller", root, project);
            ProcessList(root.note_controllers, "note", root, project);
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
        public static void SyncControlBoardIdMaps(StoryboardRoot root, NazikiProjectModel project)
        {
            if (root == null || project == null || project.ControlBoardIdMaps == null) return;

            project.ControlBoardIdMaps.Clear(); // 刷新旧账本，防残留
            SyncList(root.sprites, "sprite", project);
            SyncList(root.texts, "text", project);
            SyncList(root.videos, "video", project);
            SyncList(root.lines, "line", project);
            SyncList(root.controllers, "controller", project);
            SyncList(root.note_controllers, "note", project);
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

    // ==========================================
    // 🌟 终极转换器：将我们的“实体包装盒”翻译给 Cytoid 官方听
    // ==========================================
    public class StoryboardEntityConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return typeof(IStoryboardEntity).IsAssignableFrom(objectType);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var entity = (IStoryboardEntity)value;
            JObject rootObj = new JObject();

            // 1. 写出基础身份 (🌟 时空两栖隔离：如果是控制板，隐藏 id 身份证以防播放器冲突)
            if (!string.IsNullOrEmpty(entity.TargetId))
            {
                rootObj["target_id"] = entity.TargetId;
            }
            else
            {
                if (!string.IsNullOrEmpty(entity.Id)) rootObj["id"] = entity.Id;
            }
            if (!string.IsNullOrEmpty(entity.ParentId)) rootObj["parent_id"] = entity.ParentId;

            // 如果是音符控制器，提取它特有的 note 绑定目标（放在最外层）
            if (entity is C2NoteController ncObj && ncObj.BaseState.NoteTarget != null)
            {
                rootObj["note"] = JToken.FromObject(ncObj.BaseState.NoteTarget, serializer);
            }

            // 2. 将 BaseState (第0帧/初始状态) 完美铺平在根节点！
            var baseState = entity.GetBaseState();
            if (baseState != null)
            {
                var baseObj = JObject.FromObject(baseState, serializer);
                foreach (var prop in baseObj.Properties())
                {
                    // 踢掉无效数据和特权属性
                    // 🌟 核心修复：放行 time 和 easing，只拦截特权属性 note！彻底修复初始属性时间无法保存的 Bug！
                    if (prop.Value.Type != JTokenType.Null && prop.Name != "note")
                    {
                        rootObj[prop.Name] = prop.Value;
                    }
                }
            }

            // 3. 将所有动画关键帧塞进 "states" 数组！
            var keyframes = entity.GetKeyframes();
            if (keyframes != null && keyframes.Count > 0)
            {
                JArray statesArray = new JArray();
                foreach (var frame in keyframes)
                {
                    // 先将帧状态反射转换为 JSON 对象字典
                    var frameObj = JObject.FromObject(frame, serializer);

                    // ✨【时空硬性铁律】：关键帧内的 easing 只准在前台显示，绝对不准写入代码！在此处无情抹除！
                    if (frameObj["easing"] != null)
                    {
                        frameObj.Remove("easing");
                    }

                    statesArray.Add(frameObj);
                }
                rootObj["states"] = statesArray;
            }

            rootObj.WriteTo(writer);
        }
        // ==========================================
        // 🌟 填补缺失的 UnitFloatConverter！
        // ==========================================
        public class UnitFloatConverter : JsonConverter
        {
            public override bool CanConvert(System.Type objectType) => objectType == typeof(UnitFloat);

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                var uf = (UnitFloat)value;
                if (uf == null) { writer.WriteNull(); return; }

                // 如果是默认的 World (或者纯数字)，直接输出数字
                if (uf.Unit == ReferenceUnit.World)
                    writer.WriteValue(uf.Value);
                else
                    writer.WriteValue($"{uf.Value}{uf.Unit.ToString().ToLower()}");
            }

            public override object ReadJson(JsonReader reader, System.Type objectType, object existingValue, JsonSerializer serializer)
            {
                // 暂时返回默认值，如果需要读取带有单位的字符串，未来我们可以在这里扩充算法
                return new UnitFloat { Value = 0, Unit = ReferenceUnit.World };
            }
        }




        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            JObject jObj = JObject.Load(reader);
            var entity = (IStoryboardEntity)Activator.CreateInstance(objectType);

            if (jObj["id"] != null) entity.Id = jObj["id"].ToString();
            if (jObj["target_id"] != null) entity.TargetId = jObj["target_id"].ToString();
            if (jObj["parent_id"] != null) entity.ParentId = jObj["parent_id"].ToString();

            // 🧹 核心反求算：将除了核心标识外的所有扁平属性，全部塞进 BaseState 肚子里！
            var baseState = entity.GetBaseState();
            if (baseState != null)
            {
                JObject baseObj = new JObject();
                foreach (var prop in jObj.Properties())
                {
                    if (prop.Name != "id" && prop.Name != "parent_id" && prop.Name != "target_id" && prop.Name != "states" && prop.Name != "note")
                    {
                        baseObj[prop.Name] = prop.Value;
                    }
                }

                // 如果是音符控制器，特殊把外层的 note 目标也塞给状态类
                if (entity is C2NoteController && jObj["note"] != null)
                {
                    baseObj["note"] = jObj["note"];
                }

                using (var subReader = baseObj.CreateReader())
                {
                    serializer.Populate(subReader, baseState);
                }
            }

            // 🎬 关键帧时光倒流：如果存在 states 数组，自动还原成纯净的 Keyframes 列表！
            if (jObj["states"] is JArray statesArray)
            {
                var keyframes = entity.GetKeyframes();
                var stateType = baseState.GetType();
                foreach (var stateToken in statesArray)
                {
                    var frameObj = JObject.FromObject(stateToken, serializer).ToObject(stateType, serializer);
                    if (frameObj != null)
                    {
                        keyframes.Add(frameObj);
                    }
                }
            }

            return entity;
        }
    }

}