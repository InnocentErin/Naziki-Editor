using System;
using System.Collections.Generic;
using Naziki_Editor.Models;

namespace Naziki_Editor.Core.Timeline.Shared
{
    /// <summary>
    /// 🧬 空间折叠级联缩放器：当宏观方块整体位移或拉伸缩窄时，内部所有关键帧自动等比例缩放或平移。
    /// </summary>
    public static class KeyframeScaler
    {
        /// <summary>
        /// 🧬 核心黑科技 B：空间折叠级联缩放器（100% 完美落地需求 4）
        /// 当宏观方块整体位移或拉伸缩窄时，内部所有关键帧自动等比例缩放或平移
        /// </summary>
        public static void ScaleInternalKeyframes(
            IStoryboardEntity entity,
            double oldStart,
            double oldEnd,
            double newStart,
            double newEnd,
            ChartTimeEngine timeEngine,
            List<C2Note> allNotes)
        {
            if (entity == null) return;
            var kfs = entity.GetKeyframes();
            if (kfs == null || kfs.Count == 0) return;

            double oldDuration = oldEnd - oldStart;
            double newDuration = newEnd - newStart;
            if (oldDuration <= 0.001 || newDuration <= 0.001) return;

            // 计算时间轴膨胀/收缩系数
            double scaleFactor = newDuration / oldDuration;

            string currentNoteIdStr = "";
            try
            {
                if (FastReflectionHelper.TryGetValue(entity, "Note", out object noteTarget) && noteTarget != null)
                    currentNoteIdStr = noteTarget.ToString().Trim();
            }
            catch { }

            foreach (var frame in kfs)
            {
                if (frame is ObjectState state)
                {
                    var propRel = state.GetType().GetProperty("RelativeTime");
                    var propAdd = state.GetType().GetProperty("AddTime");
                    var propTime = state.GetType().GetProperty("Time");

                    // A. 如果关键帧采用的是相对时间，其时间步长直接乘以缩放系数
                    if (propRel != null && propRel.GetValue(state) != null)
                    {
                        if (double.TryParse(propRel.GetValue(state).ToString(), out double relVal))
                            propRel.SetValue(state, (float)(relVal * scaleFactor));
                    }
                    // B. 如果采用的是级联附加时间，步长同样乘以缩放系数
                    else if (propAdd != null && propAdd.GetValue(state) != null)
                    {
                        if (double.TryParse(propAdd.GetValue(state).ToString(), out double addVal))
                            propAdd.SetValue(state, (float)(addVal * scaleFactor));
                    }
                    // C. 若采用的是绝对/锚点 Time 属性（大大的需求 1 & 2）
                    else if (propTime != null && propTime.GetValue(state) != null)
                    {
                        object rawTimeObj = propTime.GetValue(state);
                        string timeStr = rawTimeObj.ToString().Trim();
                        if (timeStr.Contains("$note") && !string.IsNullOrEmpty(currentNoteIdStr))
                        {
                            timeStr = timeStr.Replace("$note", currentNoteIdStr);
                        }

                        double oldAbsTime = 0;
                        if (timeStr.Contains("start") || timeStr.Contains("end") || timeStr.Contains("intro") || timeStr.Contains("at"))
                        {
                            if (timeEngine != null) oldAbsTime = timeEngine.ParseCytoidTimeExpression(timeStr, allNotes);
                        }
                        else double.TryParse(timeStr, out oldAbsTime);

                        // 🧙‍♂️ 空间几何映射方程：算出该帧原先在方块内的百分比位置，映射到新时空边界中
                        double ratio = (oldAbsTime - oldStart) / oldDuration;
                        double newAbsTime = newStart + ratio * newDuration;
                        double deltaAbsTime = newAbsTime - oldAbsTime;

                        // 应用智能微分增量更新
                        object updatedTimeObj = TimeExpressionUpdater.UpdateTimeExpressionByDelta(rawTimeObj, deltaAbsTime);
                        propTime.SetValue(state, updatedTimeObj);
                    }
                }
            }
        }
    }
}