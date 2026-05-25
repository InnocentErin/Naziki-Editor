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
    // 🔮 故事板解析器快捷命名空间代理（小艾帮你补办的通行证！）
    // ==========================================
    public static class StoryboardParser
    {
        /// <summary>
        /// 全局高雅自增发证官：自动把无名氏 ID 规范化为 sprite001 等格式
        /// </summary>
        public static void StandardizeStoryboardIds(StoryboardRoot root)
        {
            // 转发给真正的转换器执行
            StoryboardEntityConverter.StandardizeStoryboardIds(root);
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
                    if (prop.Value.Type != JTokenType.Null && prop.Name != "time" && prop.Name != "easing" && prop.Name != "note")
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



        // ==========================================
        // 🔮 终极高雅自增发证官 (完全保留，无需导出删除)
        // ==========================================
        public static void StandardizeStoryboardIds(StoryboardRoot root)
        {
            if (root == null) return;

            // 用来统计当前宇宙中各类组件已经用到了多少序号，防止和已有的名字撞车
            int spriteCount = 1;
            int textCount = 1;
            int lineCount = 1;
            int videoCount = 1;
            int sceneCount = 1;
            int noteCtrlCount = 1;

            // 1. 先扫描全场，把别人本来就写好了的、优雅的数字后缀记录下来，继续往下数
            if (root.sprites != null) foreach (var x in root.sprites.Where(s => !string.IsNullOrEmpty(s.Id) && s.Id.StartsWith("sprite")))
            {
                if (int.TryParse(x.Id.Replace("sprite", ""), out int num)) spriteCount = Math.Max(spriteCount, num + 1);
            }
            if (root.texts != null) foreach (var x in root.texts.Where(s => !string.IsNullOrEmpty(s.Id) && s.Id.StartsWith("text")))
            {
                if (int.TryParse(x.Id.Replace("text", ""), out int num)) textCount = Math.Max(textCount, num + 1);
            }
            if (root.lines != null) foreach (var x in root.lines.Where(s => !string.IsNullOrEmpty(s.Id) && s.Id.StartsWith("line")))
            {
                if (int.TryParse(x.Id.Replace("line", ""), out int num)) lineCount = Math.Max(lineCount, num + 1);
            }
            if (root.controllers != null) foreach (var x in root.controllers.Where(s => !string.IsNullOrEmpty(s.Id) && s.Id.StartsWith("scene")))
            {
                if (int.TryParse(x.Id.Replace("scene", ""), out int num)) sceneCount = Math.Max(sceneCount, num + 1);
            }

            // 2. 开始给无名氏挨个补办高雅的 001 式身份证！
            if (root.sprites != null) foreach (var x in root.sprites.Where(s => string.IsNullOrEmpty(s.Id)))
                x.Id = $"sprite{spriteCount++:D3}"; // :D3 格式会自动把 1 变成 001 噢！卡哇伊！

            if (root.texts != null) foreach (var x in root.texts.Where(s => string.IsNullOrEmpty(s.Id)))
                x.Id = $"text{textCount++:D3}";

            if (root.lines != null) foreach (var x in root.lines.Where(s => string.IsNullOrEmpty(s.Id)))
                x.Id = $"line{lineCount++:D3}";

            if (root.videos != null) foreach (var x in root.videos.Where(s => string.IsNullOrEmpty(s.Id)))
                x.Id = $"video{videoCount++:D3}";

            if (root.controllers != null) foreach (var x in root.controllers.Where(s => string.IsNullOrEmpty(s.Id)))
                x.Id = $"scene{sceneCount++:D3}";

            if (root.note_controllers != null) foreach (var x in root.note_controllers.Where(s => string.IsNullOrEmpty(s.Id)))
                x.Id = $"notectrl{noteCtrlCount++:D3}";
        }
    }

}