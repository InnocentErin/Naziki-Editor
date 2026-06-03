using System;
using System.Collections.Generic;
using Naziki_Editor.Models;

namespace Naziki_Editor.Core.Timeline
{
    /// <summary>
    /// 🌌 时空核心转换引擎：负责底层级联时间（RelativeTime/AddTime）与统一视觉时间（VisualRelTime）的双向翻译
    /// </summary>
    /// <summary>
    /// 🌌 时空完全体转换引擎：支持 add_time、relative_time、音符锚点表达式(start/end/intro)与 $note 占位符的无损翻译官
    /// </summary>
    public static class StoryboardTimeConverter
    {
        /// <summary>
        /// 🔬 1. 全能时空解码流水线：将整个 Keyframes 列表安全压平成【相对于方块起点的视觉秒数】
        /// </summary>
        /// <param name="entity">当前的事件实体（如 AssociatedObject）</param>
        /// <param name="propertyName">关注的属性名（如 "X", "Opacity"）</param>
        /// <param name="timeEngine">大本营的音符时间换算引擎</param>
        /// <param name="allNotes">全量音符列表缓存</param>
        /// <param name="clipStartTime">当前事件方块在主轴上的 StartTime 起点秒数</param>
        /// <returns>解码后的纯净视觉节点包裹集合</returns>
        public static List<DecodedKeyframeBox> DecodeTimelineKeyframes(
            IStoryboardEntity entity,
            string propertyName,
            ChartTimeEngine timeEngine,
            List<C2Note> allNotes,
            double clipStartTime)
        {
            var result = new List<DecodedKeyframeBox>();
            if (entity == null) return result;

            var kfs = entity.GetKeyframes();
            if (kfs == null || kfs.Count == 0) return result;

            // ⚡ 占位符追踪：尝试反查当前对象是否是绑定的音符控制器，提取出真实绑定的 Note ID
            string currentNoteIdStr = "";
            try
            {
                // 利用反射从 AssociatedObject 肚子里挖出绑定的 note 属性（可以是具体数字或选择器对象）
                if (FastReflectionHelper.TryGetValue(entity, "Note", out object noteTarget) && noteTarget != null)
                {
                    currentNoteIdStr = noteTarget.ToString().Trim();
                }
            }
            catch { }

            // 核心时空累加器：记录上一帧计算出来的【相对于方块出生点的累计秒数】
            double lastFrameVisualRelTime = 0.0;

            foreach (var frame in kfs)
            {
                if (frame is ObjectState state)
                {
                    double thisFrameDelta = 0.0;
                    bool hasTimeProp = false;

                    // 🔍 ✨ 升级版全能时空雷达：无缝支持 RelativeTime、AddTime 以及原生字符串锚点的 Time 字段！
                    object rawTimeObj = null;

                    if (FastReflectionHelper.TryGetValue(state, "RelativeTime", out object rt) && rt != null)
                    {
                        rawTimeObj = rt;
                    }
                    else if (FastReflectionHelper.TryGetValue(state, "AddTime", out object at) && at != null)
                    {
                        rawTimeObj = at;
                    }
                    else if (FastReflectionHelper.TryGetValue(state, "Time", out object t) && t != null)
                    {
                        // 🌟 抓到你啦！当使用音符锚点时，时间直接存在 Time 字段中！
                        rawTimeObj = t;
                    }

                    if (rawTimeObj != null)
                    {
                        string timeStr = rawTimeObj.ToString().Trim();

                        // 🚀 基因替代雷达：如果包含 $note 占位符，强行替换为当前控制器的真实音符 ID！
                        if (timeStr.Contains("$note") && !string.IsNullOrEmpty(currentNoteIdStr))
                        {
                            timeStr = timeStr.Replace("$note", currentNoteIdStr);
                        }

                        // 判断它是复杂的【音符锚点表达式】还是单纯的【数字增量】
                        if (timeStr.Contains("start") || timeStr.Contains("end") || timeStr.Contains("intro") || timeStr.Contains("at"))
                        {
                            hasTimeProp = true;
                            // 呼叫大大的高级翻译官 ParseCytoidTimeExpression，直接算出该锚点的【绝对时间秒数】！
                            double frameAbsSeconds = timeEngine.ParseCytoidTimeExpression(timeStr, allNotes);

                            // 换算公式：该锚点关键帧相对于事件方块起点的【视觉相对时间】 = 绝对秒数 - 方块起点
                            double visualRelTime = frameAbsSeconds - clipStartTime;

                            // 修正滚动累加器，确保后续若有连续用 add_time 挂靠在它后面的帧能够正确级联
                            thisFrameDelta = visualRelTime - lastFrameVisualRelTime;
                        }
                        else if (double.TryParse(timeStr, out double numericDelta))
                        {
                            hasTimeProp = true;
                            thisFrameDelta = numericDelta;
                        }

                        if (hasTimeProp)
                        {
                            // 级联滚动：当前视觉时间 = 上一帧累计视觉时间 + 本帧的时空步长
                            double visualRelTime = lastFrameVisualRelTime + thisFrameDelta;
                            lastFrameVisualRelTime = visualRelTime;

                            // 过滤机制：只有当这一帧真的修改了当前轨道关心的属性时，才将其捕获到时间轴渲染中
                            if (FastReflectionHelper.TryGetValue(state, propertyName, out object val) && val != null)
                            {
                                result.Add(new DecodedKeyframeBox
                                {
                                    State = state,
                                    VisualRelTime = visualRelTime,
                                    Value = val
                                });
                            }
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 🧬 2. 逆向精准反写引擎：由于音符锚点是用户钦定的神圣标记，拖拽时我们统一转换为 RelativeTime 数字进行微调更新
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
    }

    /// <summary>
    /// 📦 解码数据时空快递盒
    /// </summary>
    public class DecodedKeyframeBox
    {
        public ObjectState State { get; set; }
        public double VisualRelTime { get; set; } // 统一转换后：相对于方块出生点（0.0s）的视觉相对秒数
        public object Value { get; set; }           // 属性的当前数值
    }
}