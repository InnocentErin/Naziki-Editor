using System;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace Naziki_Editor.Core.Timeline.Shared
{
    /// <summary>
    /// 🔮 智能时空表达式微分器：能够完美识别纯数字或 "start:noteId:offset" 字符串，并精准对其应用 deltaTime 增量。
    /// </summary>
    public static class TimeExpressionUpdater
    {
        /// <summary>
        /// 🔮 核心黑科技 A：智能时空表达式微分器
        /// 能够完美识别纯数字或 "start:noteId:offset" 字符串，并精准对其应用 deltaTime 增量
        /// </summary>
        public static object UpdateTimeExpressionByDelta(object originalTime, double deltaTime)
        {
            if (originalTime == null) return (float)deltaTime;
            if (originalTime is JArray array)
                return new JArray(array.Select(item =>
                    item.Type is JTokenType.Integer or JTokenType.Float or JTokenType.String
                        ? JToken.FromObject(UpdateTimeExpressionByDelta(((JValue)item).Value!, deltaTime))
                        : item.DeepClone()));
            string str = originalTime.ToString().Trim();

            // 1. 如果是纯绝对秒数，直接做加减，保持 float 属性注入
            if (double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out double directVal))
            {
                return directVal + deltaTime;
            }

            // 2. 如果是复杂的音符锚点表达式 (例如 "start:1134:2" 或 "start:1134")
            string[] parts = str.Split(':');
            if (parts.Length >= 2)
            {
                string type = parts[0]; // start / end / intro
                string noteId = parts[1]; // 音符ID 或 $note
                double currentOffset = 0;

                if (parts.Length >= 3)
                {
                    double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out currentOffset);
                }

                double newOffset = currentOffset + deltaTime;
                return $"{type}:{noteId}:{newOffset.ToString("R", CultureInfo.InvariantCulture)}";
            }

            return originalTime; // 兜底
        }
    }
}
