using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Naziki_Editor.Models;
using System;
using System.Collections.Generic;

namespace Naziki_Editor.Core.Serialization.Converters
{
    // ==========================================
    // 🌟 终极转换器：将我们的"实体包装盒"翻译给 Cytoid 官方听
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
                        // 🌟 P0修复：跳过 float.MaxValue 的 time 值（表示"未设置"，不应序列化）
                        if (prop.Name == "time")
                        {
                            if (prop.Value.Type == JTokenType.Float && Math.Abs((float)prop.Value - float.MaxValue) < 0.01f)
                                continue;
                            // 也检查字符串形式的 float.MaxValue
                            if (prop.Value.Type == JTokenType.String && prop.Value.ToString() == float.MaxValue.ToString())
                                continue;
                        }
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