using System;
using System.Collections.Generic;
using Naziki_Editor.Models;

namespace Naziki_Editor.Core.Timeline.Shared
{
    /// <summary>
    /// 🧬 逆向精准反写引擎：拖拽时将视觉位移转换为 RelativeTime / AddTime / Time 进行微调更新。
    /// </summary>
    public static class KeyframeWriteBack
    {
        /// <summary>
        /// 🧬 逆向精准反写引擎（基础版）：拖拽时将视觉位移转换为 RelativeTime / AddTime 数字进行微调更新。
        /// </summary>
        public static void WriteBackVisualTime(IStoryboardEntity entity, ObjectState targetState, double newVisualRelTime)
        {
            if (entity == null || targetState == null) return;

            var kfs = entity.GetKeyframes();
            if (kfs == null || kfs.Count == 0) return;

            // 1. 获取平置的视觉时间列表，确定邻居排位
            double lastTimeAccumulator = 0.0;
            var sortedTimelineBoxes = new List<DecodedKeyframeBox>();

            foreach (var frame in kfs)
            {
                if (frame is ObjectState state)
                {
                    double delta = 0.0;
                    if (FastReflectionHelper.TryGetValue(state, "RelativeTime", out object rt) && rt != null)
                    {
                        double.TryParse(rt.ToString(), out delta); // 尝试转型
                    }
                    else if (FastReflectionHelper.TryGetValue(state, "AddTime", out object at) && at != null)
                    {
                        double.TryParse(at.ToString(), out delta);
                    }

                    double visualTime = lastTimeAccumulator + delta;
                    lastTimeAccumulator = visualTime;

                    sortedTimelineBoxes.Add(new DecodedKeyframeBox { State = state, VisualRelTime = visualTime });
                }
            }

            int targetIndex = sortedTimelineBoxes.FindIndex(b => b.State == targetState);
            if (targetIndex < 0) return;

            double prevNodeVisualRelTime = 0.0;
            if (targetIndex > 0) prevNodeVisualRelTime = sortedTimelineBoxes[targetIndex - 1].VisualRelTime;

            // 💡 拖拽微调：将拖拽位移转换为平直的相对秒数增量
            double newDeltaValue = newVisualRelTime - prevNodeVisualRelTime;

            var propRelative = targetState.GetType().GetProperty("RelativeTime");
            var propAdd = targetState.GetType().GetProperty("AddTime");

            // 如果原本是字符串锚点或 RelativeTime，被鼠标拖动位移微调后，我们将其更新为精确的浮点数步长
            if (propAdd != null && propAdd.GetValue(targetState) != null)
            {
                propAdd.SetValue(targetState, (float)newDeltaValue);
            }
            else if (propRelative != null)
            {
                propRelative.SetValue(targetState, (float)newDeltaValue);
            }

            // 蝴蝶效应级联修复后面那一帧的步长
            if (targetIndex < sortedTimelineBoxes.Count - 1)
            {
                var nextState = sortedTimelineBoxes[targetIndex + 1].State;
                var nextPropRelative = nextState.GetType().GetProperty("RelativeTime");
                var nextPropAdd = nextState.GetType().GetProperty("AddTime");

                double nextNodeOriginalVisualTime = sortedTimelineBoxes[targetIndex + 1].VisualRelTime;
                double nextNewDelta = nextNodeOriginalVisualTime - newVisualRelTime;

                if (nextPropAdd != null && nextPropAdd.GetValue(nextState) != null) nextPropAdd.SetValue(nextState, (float)nextNewDelta);
                else if (nextPropRelative != null) nextPropRelative.SetValue(nextState, (float)nextNewDelta);
            }
        }

        /// <summary>
        /// 🧬 升级版逆向精准反写引擎：全面解禁详细模式关键帧拖拽（支持 Time / RelativeTime / AddTime 三路流）
        /// </summary>
        public static void WriteBackVisualTime(
            IStoryboardEntity entity,
            ObjectState targetState,
            double newVisualRelTime,
            ChartTimeEngine timeEngine,
            List<C2Note> allNotes,
            double clipStartTime)
        {
            if (entity == null || targetState == null) return;

            var kfs = entity.GetKeyframes();
            if (kfs == null || kfs.Count == 0) return;

            double lastTimeAccumulator = 0.0;
            var sortedTimelineBoxes = new List<DecodedKeyframeBox>();

            string currentNoteIdStr = "";
            try
            {
                if (FastReflectionHelper.TryGetValue(entity, "Note", out object noteTarget) && noteTarget != null)
                    currentNoteIdStr = noteTarget.ToString().Trim();
            }
            catch { }

            // 1. 全量扫盘重建当前拖拽瞬间的时空骨架
            foreach (var frame in kfs)
            {
                if (frame is ObjectState state)
                {
                    double delta = 0.0;
                    object rawTimeObj = null;
                    bool isAbsoluteStyle = false;

                    if (FastReflectionHelper.TryGetValue(state, "RelativeTime", out object rt) && rt != null) rawTimeObj = rt;
                    else if (FastReflectionHelper.TryGetValue(state, "AddTime", out object at) && at != null) rawTimeObj = at;
                    else if (FastReflectionHelper.TryGetValue(state, "Time", out object t) && t != null)
                    {
                        rawTimeObj = t;
                        isAbsoluteStyle = true;
                    }

                    double visualTime = 0.0;
                    if (rawTimeObj != null)
                    {
                        string timeStr = rawTimeObj.ToString().Trim();
                        if (timeStr.Contains("$note") && !string.IsNullOrEmpty(currentNoteIdStr))
                        {
                            timeStr = timeStr.Replace("$note", currentNoteIdStr);
                        }

                        if (isAbsoluteStyle)
                        {
                            double absSeconds = 0;
                            if (timeStr.Contains("start") || timeStr.Contains("end") || timeStr.Contains("intro") || timeStr.Contains("at"))
                            {
                                if (timeEngine != null) absSeconds = timeEngine.ParseCytoidTimeExpression(timeStr, allNotes);
                            }
                            else double.TryParse(timeStr, out absSeconds);

                            visualTime = absSeconds - clipStartTime;
                            lastTimeAccumulator = visualTime;
                        }
                        else
                        {
                            double.TryParse(timeStr, out delta);
                            visualTime = lastTimeAccumulator + delta;
                            lastTimeAccumulator = visualTime;
                        }
                    }

                    sortedTimelineBoxes.Add(new DecodedKeyframeBox { State = state, VisualRelTime = visualTime });
                }
            }

            int targetIndex = sortedTimelineBoxes.FindIndex(b => b.State == targetState);
            if (targetIndex < 0) return;

            double originalVisualRelTime = sortedTimelineBoxes[targetIndex].VisualRelTime;
            double deltaSeconds = newVisualRelTime - originalVisualRelTime;

            var propRelative = targetState.GetType().GetProperty("RelativeTime");
            var propAdd = targetState.GetType().GetProperty("AddTime");
            var propTime = targetState.GetType().GetProperty("Time");

            // 🌟 判定处理：如果该帧身上直接有 Time 属性（大大的需求 1 & 2）
            if (propTime != null && propTime.GetValue(targetState) != null)
            {
                object oldTimeObj = propTime.GetValue(targetState);
                object updatedTimeObj = TimeExpressionUpdater.UpdateTimeExpressionByDelta(oldTimeObj, deltaSeconds);
                propTime.SetValue(targetState, updatedTimeObj);
            }
            // 🌟 如果该帧用的是传统的 RelativeTime / AddTime（大大的需求 3）
            else
            {
                double prevNodeVisualRelTime = 0.0;
                if (targetIndex > 0) prevNodeVisualRelTime = sortedTimelineBoxes[targetIndex - 1].VisualRelTime;
                double newDeltaValue = newVisualRelTime - prevNodeVisualRelTime;

                if (propAdd != null && propAdd.GetValue(targetState) != null) propAdd.SetValue(targetState, (float)newDeltaValue);
                else if (propRelative != null) propRelative.SetValue(targetState, (float)newDeltaValue);

                // 级联修复紧随其后的相对帧间距
                if (targetIndex < sortedTimelineBoxes.Count - 1)
                {
                    var nextState = sortedTimelineBoxes[targetIndex + 1].State;
                    var nextPropRelative = nextState.GetType().GetProperty("RelativeTime");
                    var nextPropAdd = nextState.GetType().GetProperty("AddTime");

                    double nextNodeOriginalVisualTime = sortedTimelineBoxes[targetIndex + 1].VisualRelTime;
                    double nextNewDelta = nextNodeOriginalVisualTime - newVisualRelTime;

                    if (nextPropAdd != null && nextPropAdd.GetValue(nextState) != null) nextPropAdd.SetValue(nextState, (float)nextNewDelta);
                    else if (nextPropRelative != null) nextPropRelative.SetValue(nextState, (float)nextNewDelta);
                }
            }
        }
    }
}