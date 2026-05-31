using System;
using System.Collections.Generic;
using System.Linq;
using Naziki_Editor.Models;
using Naziki_Editor.State;

namespace Naziki_Editor.Core.Timeline
{
    public static class TimelineLayoutEngine
    {
        /// <summary>
        /// 🧙‍♂️ 俄罗斯方块智能排版引擎：基于“类型图层”与“时间碰撞”，分配无重叠的 Order！
        /// </summary>
        public static void AutoAssignOrderForVisualEntities(ProjectDataContext context)
        {
            var root = context?.Storyboard;
            if (root == null) return;

            // 1. 抓取所有具有实体画面、需要分配轨道的对象
            var visualEntities = new List<IStoryboardEntity>();
            if (root.sprites != null) visualEntities.AddRange(root.sprites);
            if (root.texts != null) visualEntities.AddRange(root.texts);
            if (root.videos != null) visualEntities.AddRange(root.videos);
            if (root.lines != null) visualEntities.AddRange(root.lines);

            // 2. ✨ 大大指定的新法则：按“类型”进行硬性物理分组！并顺手改写它们的 Layer！
            // 组0: Sprite, Video (归入 Layer 0)
            // 组1: Line (归入 Layer 1)
            // 组2: Text (归入 Layer 2)
            var layerGroups = new Dictionary<int, List<EntityTimeBox>>();
            var allNotes = context.Chart?.note_list;

            foreach (var entity in visualEntities)
            {
                var baseState = entity.GetBaseState();
                if (baseState == null) continue;

                // 🌟 核心逻辑 1：根据类型强制分配 Layer，并写入底层模型！
                int targetLayer = 0;
                if (entity is C2Sprite || entity is C2Video) targetLayer = 0;
                else if (entity is C2Line) targetLayer = 1;
                else if (entity is C2Text) targetLayer = 2;

                WritePropertyToEntity(baseState, "Layer", targetLayer);

                // 提取精确的起止时间
                double start = 0, end = 9999;
                if (FastReflectionHelper.TryGetValue(baseState, "Time", out object startObj))
                    start = SafeResolveTime(startObj, context, allNotes);

                var kfs = entity.GetKeyframes();
                if (kfs != null && kfs.Count > 0)
                {
                    var lastFrame = kfs[kfs.Count - 1];
                    if (FastReflectionHelper.TryGetValue(lastFrame, "Time", out object endObj))
                        end = SafeResolveTime(endObj, context, allNotes);
                }
                if (end <= start) end = start + 2.0;

                if (!layerGroups.ContainsKey(targetLayer)) layerGroups[targetLayer] = new List<EntityTimeBox>();
                layerGroups[targetLayer].Add(new EntityTimeBox { Entity = entity, Start = start, End = end, BaseState = baseState });
            }

            // 3. 核心法术：对每个 Layer 分别执行“从下往上 (Order 0, 1...) 寻找空位”的避让计算
            foreach (var layerKvp in layerGroups)
            {
                // 先出现的方块优先排座位
                var sortedBoxes = layerKvp.Value.OrderBy(b => b.Start).ToList();

                // 记录每条轨道 (Order) 目前被占用到什么时候结束 (轨道索引 -> 结束时间)
                var trackEnds = new Dictionary<int, double>();

                foreach (var box in sortedBoxes)
                {
                    // 🌟 核心逻辑 2：从最底部的 0 号轨道开始往上爬
                    int bestOrder = 0;

                    // 只要当前轨道已经被占用，且那个占用者的结束时间，晚于我的出生时间（也就是撞车了）
                    while (trackEnds.ContainsKey(bestOrder) && trackEnds[bestOrder] > box.Start)
                    {
                        bestOrder++; // 就去上一层轨道找空位
                    }

                    // 找到空位啦！登记我的死亡时间，霸占这条轨道！
                    trackEnds[bestOrder] = box.End;

                    // ✨ 终极操作：将算好的 Order 写入对象的基因里！
                    WritePropertyToEntity(box.BaseState, "Order", bestOrder);
                }
            }
        }

        // ==========================================
        // 🛠️ 内部小助手们
        // ==========================================
        private static double SafeResolveTime(object timeObj, ProjectDataContext context, List<C2Note> allNotes)
        {
            if (timeObj == null) return 0;
            if (context != null && context.TimeEngine != null) return context.TimeEngine.ParseCytoidTimeExpression(timeObj, allNotes);
            if (double.TryParse(timeObj.ToString(), out double val)) return val;
            return 0;
        }

        private static void WritePropertyToEntity(object baseState, string propName, int newValue)
        {
            var propInfo = baseState.GetType().GetProperty(propName);
            if (propInfo != null && propInfo.CanWrite)
            {
                Type targetType = propInfo.PropertyType;
                if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Nullable<>))
                {
                    targetType = Nullable.GetUnderlyingType(targetType);
                }
                object safeValue = Convert.ChangeType(newValue, targetType);
                propInfo.SetValue(baseState, safeValue);
            }
        }

        private class EntityTimeBox
        {
            public IStoryboardEntity Entity { get; set; }
            public object BaseState { get; set; }
            public double Start { get; set; }
            public double End { get; set; }
        }
    }
}