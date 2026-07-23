//using Newtonsoft.Json;
//using Newtonsoft.Json.Linq;
//using Newtonsoft.Json.Serialization;
//using System;
//using System.Collections.Generic;
//using Naziki_Editor.Models;

//namespace Naziki_Editor.Core
//{
//    // =========================================================================
//    // 🌟 全局 JSON 序列化大管家 (StoryboardSerializer)
//    // =========================================================================
//    public static class StoryboardSerializer
//    {
//        public static JsonSerializerSettings GetSettings()
//        {
//            return new JsonSerializerSettings
//            {
//                NullValueHandling = NullValueHandling.Ignore,
//                ContractResolver = new DefaultContractResolver
//                {
//                    NamingStrategy = new SnakeCaseNamingStrategy() // 保持蛇形命名
//                },
//                Formatting = Formatting.Indented,
//                Converters = new List<JsonConverter>
//                {
//                    new StoryboardEntityConverter(), // 核心包装盒转换器
//                    new UnitFloatConverter(),        // 带有单位的浮点数转换器
//                    new StringArrayConverter()       // 数组字符串降维转换器
//                }
//            };
//        }

//        public static string ToJson(object obj)
//        {
//            return JsonConvert.SerializeObject(obj, GetSettings());
//        }
//    }

//    // =========================================================================
//    // 🌟 1. 数组字符串双向转换器 (专治 note_fill_colors 数组爆炸)
//    // =========================================================================
//    public class StringArrayConverter : JsonConverter
//    {
//        public override bool CanConvert(Type objectType) => objectType == typeof(string);

//        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
//        {
//            string str = value as string;
//            if (string.IsNullOrWhiteSpace(str))
//            {
//                writer.WriteNull();
//                return;
//            }

//            var arr = str.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
//            writer.WriteStartArray();
//            foreach (var item in arr)
//            {
//                writer.WriteValue(item.Trim());
//            }
//            writer.WriteEndArray();
//        }

//        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
//        {
//            if (reader.TokenType == JsonToken.StartArray)
//            {
//                var list = serializer.Deserialize<List<string>>(reader);
//                return string.Join(",", list);
//            }
//            else if (reader.TokenType == JsonToken.String)
//            {
//                return reader.Value?.ToString();
//            }
//            return null;
//        }
//    }

//    // =========================================================================
//    // 🌟 2. 带有单位的浮点数转换器 (UnitFloat 专属)
//    // =========================================================================
//    public class UnitFloatConverter : JsonConverter
//    {
//        public override bool CanConvert(Type objectType) => objectType == typeof(UnitFloat);

//        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
//        {
//            var uf = (UnitFloat)value;
//            if (uf == null) { writer.WriteNull(); return; }

//            if (uf.Unit == ReferenceUnit.World)
//                writer.WriteValue(uf.Value);
//            else
//                writer.WriteValue($"{uf.Value}{uf.Unit.ToString().ToLower()}");
//        }

//        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
//        {
//            if (reader.TokenType == JsonToken.Null) return null;

//            // 🛡️ 核心修复：如果遇到了随机范围数组（StartArray），比如 y: ["-10w", "10w"]
//            if (reader.TokenType == JsonToken.StartArray)
//            {
//                // 🌟 必须用 Load 完全吞掉这个数组！这样解析器才不会卡死在数组里面！
//                JArray array = JArray.Load(reader);
//                if (array.Count > 0)
//                {
//                    var first = array[0];
//                    if (first.Type == JTokenType.String) return ParseString(first.ToString());
//                    if (first.Type == JTokenType.Integer || first.Type == JTokenType.Float)
//                        return new UnitFloat { Value = first.Value<float>(), Unit = ReferenceUnit.World };
//                }
//                return new UnitFloat { Value = 0, Unit = ReferenceUnit.World };
//            }

//            if (reader.TokenType == JsonToken.Integer || reader.TokenType == JsonToken.Float)
//            {
//                return new UnitFloat { Value = Convert.ToSingle(reader.Value), Unit = ReferenceUnit.World };
//            }

//            if (reader.TokenType == JsonToken.String)
//            {
//                return ParseString(reader.Value.ToString());
//            }

//            // 兜底：如果遇到其他奇葩类型，也强行吃掉它，绝不让解析器卡死！
//            JToken.Load(reader);
//            return new UnitFloat { Value = 0, Unit = ReferenceUnit.World };
//        }

//        private UnitFloat ParseString(string raw)
//        {
//            var uf = new UnitFloat { Value = 0, Unit = ReferenceUnit.World };
//            raw = raw.Trim().ToLower();
//            if (raw.EndsWith("notex")) { uf.Unit = ReferenceUnit.NoteX; raw = raw.Replace("notex", ""); }
//            else if (raw.EndsWith("notey")) { uf.Unit = ReferenceUnit.NoteY; raw = raw.Replace("notey", ""); }
//            else if (raw.EndsWith("stagex")) { uf.Unit = ReferenceUnit.StageX; raw = raw.Replace("stagex", ""); }
//            else if (raw.EndsWith("stagey")) { uf.Unit = ReferenceUnit.StageY; raw = raw.Replace("stagey", ""); }
//            else if (raw.EndsWith("camerax")) { uf.Unit = ReferenceUnit.CameraX; raw = raw.Replace("camerax", ""); }
//            else if (raw.EndsWith("cameray")) { uf.Unit = ReferenceUnit.CameraY; raw = raw.Replace("cameray", ""); }

//            if (float.TryParse(raw, out float v)) uf.Value = v;
//            return uf;
//        }
//    }

//    // =========================================================================
//    // 🌟 3. 终极实体翻译官 (将 C2 包装盒无损翻译给 Cytoid 引擎)
//    // =========================================================================
//    public class StoryboardEntityConverter : JsonConverter
//    {
//        public override bool CanConvert(Type objectType)
//        {
//            return typeof(IStoryboardEntity).IsAssignableFrom(objectType);
//        }

//        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
//        {
//            var entity = (IStoryboardEntity)value;
//            JObject rootObj = new JObject();

//            if (!string.IsNullOrEmpty(entity.TargetId)) rootObj["target_id"] = entity.TargetId;
//            else if (!string.IsNullOrEmpty(entity.Id)) rootObj["id"] = entity.Id;

//            if (!string.IsNullOrEmpty(entity.ParentId)) rootObj["parent_id"] = entity.ParentId;

//            if (entity is C2NoteController ncObj && ncObj.BaseState.NoteTarget != null)
//            {
//                rootObj["note"] = JToken.FromObject(ncObj.BaseState.NoteTarget, serializer);
//            }

//            var baseState = entity.GetBaseState();
//            if (baseState != null)
//            {
//                var baseObj = JObject.FromObject(baseState, serializer);
//                foreach (var prop in baseObj.Properties())
//                {
//                    if (prop.Value.Type != JTokenType.Null && prop.Name != "note")
//                    {
//                        rootObj[prop.Name] = prop.Value;
//                    }
//                }
//            }

//            var keyframes = entity.GetKeyframes();
//            if (keyframes != null && keyframes.Count > 0)
//            {
//                JArray statesArray = new JArray();
//                foreach (var frame in keyframes)
//                {
//                    var frameObj = JObject.FromObject(frame, serializer);
//                    if (frameObj["easing"] != null) frameObj.Remove("easing");
//                    statesArray.Add(frameObj);
//                }
//                rootObj["states"] = statesArray;
//            }

//            rootObj.WriteTo(writer);
//        }

//        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
//        {
//            JObject jObj = JObject.Load(reader);
//            var entity = (IStoryboardEntity)Activator.CreateInstance(objectType);

//            if (jObj["id"] != null) entity.Id = jObj["id"].ToString();
//            if (jObj["target_id"] != null) entity.TargetId = jObj["target_id"].ToString();
//            if (jObj["parent_id"] != null) entity.ParentId = jObj["parent_id"].ToString();

//            var baseState = entity.GetBaseState();
//            if (baseState != null)
//            {
//                JObject baseObj = new JObject();
//                foreach (var prop in jObj.Properties())
//                {
//                    if (prop.Name != "id" && prop.Name != "parent_id" && prop.Name != "target_id" && prop.Name != "states" && prop.Name != "note")
//                    {
//                        baseObj[prop.Name] = prop.Value;
//                    }
//                }

//                if (entity is C2NoteController && jObj["note"] != null) baseObj["note"] = jObj["note"];

//                using (var subReader = baseObj.CreateReader())
//                {
//                    serializer.Populate(subReader, baseState);
//                }
//            }

//            if (jObj["states"] is JArray statesArray)
//            {
//                var keyframes = entity.GetKeyframes();
//                var stateType = baseState.GetType();
//                foreach (var stateToken in statesArray)
//                {
//                    var frameObj = JObject.FromObject(stateToken, serializer).ToObject(stateType, serializer);
//                    if (frameObj != null) keyframes.Add(frameObj);
//                }
//            }

//            return entity;
//        }
//    }
//}