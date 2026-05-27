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
    // ==========================================
    // 🔮 故事板全量智能赋名枢纽（导入故事板时的终极安检门！）
    // ==========================================
    public static class StoryboardParser
    {
        public static void StandardizeStoryboardIds(StoryboardRoot root)
        {
            if (root == null) return;

            // 依次全量洗盘 6 大场景对象数组
            ProcessList(root.sprites, "sprite", root);
            ProcessList(root.texts, "text", root);
            ProcessList(root.videos, "video", root);
            ProcessList(root.lines, "line", root);
            ProcessList(root.controllers, "controller", root);
            ProcessList(root.note_controllers, "note", root);
        }

        private static void ProcessList<T>(List<T> list, string typePrefix, StoryboardRoot root) where T : IStoryboardEntity
        {
            if (list == null) return;
            foreach (var entity in list)
            {
                if (string.IsNullOrEmpty(entity.Id))
                {
                    entity.Id = GenerateSmartIdForImport(entity, typePrefix, root);
                }
            }
        }

        private static string GenerateSmartIdForImport(IStoryboardEntity obj, string typePrefix, StoryboardRoot root)
        {
            string coreValue = "new";

            // 🧬 提取核心特征
            if (obj is C2Sprite s) coreValue = s.BaseState?.Path;
            else if (obj is C2Text t) coreValue = t.BaseState?.TextContent;
            else if (obj is C2Video v) coreValue = v.BaseState?.Path;
            else if (obj is C2Line) coreValue = "pos";
            else if (obj is C2SceneController) coreValue = "scene";
            else if (obj is C2NoteController nc && nc.BaseState?.NoteTarget != null)
            {
                string sVal = nc.BaseState.NoteTarget.ToString();
                coreValue = sVal.StartsWith("{") ? "selector" : sVal;
            }

            // 🧹 净化特征文字
            if (string.IsNullOrEmpty(coreValue)) coreValue = "item";
            else
            {
                try
                {
                    coreValue = System.IO.Path.GetFileNameWithoutExtension(coreValue);
                    coreValue = System.Text.RegularExpressions.Regex.Replace(coreValue, @"[^a-zA-Z0-9\u4e00-\u9fa5]", "_");
                    coreValue = System.Text.RegularExpressions.Regex.Replace(coreValue, @"_+", "_").Trim('_');
                    if (coreValue.Length > 15) coreValue = coreValue.Substring(0, 15);
                    if (string.IsNullOrEmpty(coreValue)) coreValue = "item";
                }
                catch { coreValue = "item"; }
            }

            string baseId = $"{typePrefix}_{coreValue}".ToLower();
            string finalId = baseId;
            int index = 1;

            // 🛡️ 严格查户口，防止在导入的对象之间互相撞名！
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

            // 1. 写出基础身份
            if (!string.IsNullOrEmpty(entity.Id)) rootObj["id"] = entity.Id;
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
                    statesArray.Add(JObject.FromObject(frame, serializer));
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
            if (jObj["parent_id"] != null) entity.ParentId = jObj["parent_id"].ToString();

            // 🧹 核心反求算：将除了核心标识外的所有扁平属性，全部塞进 BaseState 肚子里！
            var baseState = entity.GetBaseState();
            if (baseState != null)
            {
                JObject baseObj = new JObject();
                foreach (var prop in jObj.Properties())
                {
                    if (prop.Name != "id" && prop.Name != "parent_id" && prop.Name != "states" && prop.Name != "note")
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