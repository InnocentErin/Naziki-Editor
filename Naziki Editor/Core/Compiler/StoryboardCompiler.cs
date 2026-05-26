using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Naziki_Editor.Models;
using Naziki_Editor.Core;

namespace Naziki_Editor.Core.Compiler
{
    public class StoryboardCompiler
    {
        private C2Chart _chart;
        private ChartTimeEngine _engine;
        private Dictionary<string, C2Template> _templates;

        public List<string> CompileWarnings { get; private set; } = new List<string>();
        // 构造函数接受整个 Chart、时间引擎和模板字典，准备好一切进行编译
        public StoryboardCompiler(C2Chart chart, ChartTimeEngine engine, Dictionary<string, C2Template> templates)
        {
            _chart = chart;
            _engine = engine;
            _templates = templates;
        }
        // 主入口：展平整个 Storyboard，处理所有对象类型
        public void FlattenStoryboard(StoryboardRoot root)
        {
            CompileWarnings.Clear();
            ProcessEntityList<C2Sprite, SpriteState>(root.sprites);
            ProcessEntityList<C2Text, TextState>(root.texts);
            ProcessEntityList<C2Line, LineState>(root.lines);
            ProcessEntityList<C2Video, VideoState>(root.videos);
            ProcessEntityList<C2SceneController, ControllerState>(root.controllers);
            ProcessEntityList<C2NoteController, NoteControllerState>(root.note_controllers);
        }
        // 通用处理函数：针对每种对象列表，调用展平函数处理它们的关键帧
        private void ProcessEntityList<TEntity, TState>(List<TEntity> entities)
            where TEntity : StoryboardEntity<TState>
            where TState : ObjectState, new()
        {
            if (entities == null) return;
            foreach (var entity in entities)
                entity.Keyframes = FlattenStates<TState>(entity.Keyframes);
        }

        // ==========================================
        // 🧬 1. 顶层对象展平引擎 (遵循官方时间推算规范)
        // ==========================================
        private List<TState> FlattenStates<TState>(List<TState> originalStates) where TState : ObjectState, new()
        {
            if (originalStates == null || originalStates.Count == 0) return originalStates;

            var flattenedList = new List<TState>();
            float lastStateTime = 0f; // 追踪“最后定义的状态的时间”，专供 add_time 使用！

            foreach (var state in originalStates)
            {
                List<float> triggerTimes = new List<float>();

                // 🎯【官方宪法第 1 条】：如果定义了 add_time -> 最后定义的状态时间 + add_time
                if (state.AddTime.HasValue)
                {
                    triggerTimes.Add(lastStateTime + state.AddTime.Value);
                }
                // 🎯【官方宪法第 2 条】：如果定义了 relative_time ,同时也定义了 time -> time + relative_time
                else if (state.RelativeTime.HasValue && state.Time != null && state.Time.ToString() != float.MaxValue.ToString())
                {
                    var absTimes = ResolveAbsoluteTimes(state.Time);
                    foreach (var t in absTimes) triggerTimes.Add(t + state.RelativeTime.Value);
                }
                // 🎯【官方宪法第 4 条】：定义了 relative_time, 但没有父状态(顶层对象) -> 当前游戏时间(0) + relative_time
                else if (state.RelativeTime.HasValue)
                {
                    triggerTimes.Add(0f + state.RelativeTime.Value);
                }
                // 🎯【官方宪法第 5 条】：都没有定义 -> 直接使用 time
                else if (state.Time != null && state.Time.ToString() != float.MaxValue.ToString())
                {
                    triggerTimes = ResolveAbsoluteTimes(state.Time);
                }
                else
                {
                    triggerTimes.Add(float.MaxValue); // 隐藏/未触发状态
                }

                if (triggerTimes.Count > 1 && state.Destroy == true)
                    CompileWarnings.Add($"⚠️ 警告：检测到属性包含了 destroy:true，且被应用在了包含 {triggerTimes.Count} 个时间锚点的数组中！");

                // 🪄 裂变与模板递归展开
                if (string.IsNullOrEmpty(state.Template) || _templates == null || !_templates.ContainsKey(state.Template))
                {
                    foreach (float t in triggerTimes)
                    {
                        var clone = DeepClone(state);
                        clone.Time = t; clone.RelativeTime = null; clone.AddTime = null;
                        flattenedList.Add(clone);
                        if (t != float.MaxValue) lastStateTime = t; // 更新上一个状态的时间
                    }
                }
                else
                {
                    var template = _templates[state.Template];
                    foreach (float baseTime in triggerTimes)
                    {
                        TState mergedBaseState = MergeProperties<TState>(state, template.BaseState);
                        mergedBaseState.Template = null;

                        if (template.Keyframes == null || template.Keyframes.Count == 0)
                        {
                            var clone = DeepClone(mergedBaseState);
                            clone.Time = baseTime; clone.RelativeTime = null; clone.AddTime = null;
                            flattenedList.Add(clone);
                            if (baseTime != float.MaxValue) lastStateTime = baseTime;
                        }
                        else
                        {
                            // 进入模板，将当前的 baseTime 作为【父状态的时间】传递进去！
                            var expandedChildren = ExpandTemplateKeyframes<TState>(template.Keyframes, baseTime, mergedBaseState, new HashSet<string> { state.Template });
                            flattenedList.AddRange(expandedChildren);

                            if (expandedChildren.Count > 0)
                            {
                                float lastChildTime = (float)expandedChildren.Last().Time;
                                if (lastChildTime != float.MaxValue) lastStateTime = lastChildTime; // 跨模板的多米诺传递
                            }
                        }
                    }
                }
            }

            return flattenedList.OrderBy(s => (float)s.Time).ToList();
        }

        // ==========================================
        // 🪆 2. 子帧递归拆解术 (严格使用 baseTime 作为 relative_time 的父锚点！)
        // ==========================================
        private List<TState> ExpandTemplateKeyframes<TState>(List<TemplateState> templateStates, float baseTime, TState inheritedBaseState, HashSet<string> visitedTemplates) where TState : ObjectState, new()
        {
            var result = new List<TState>();
            float lastStateTime = baseTime; // 初始的“上一个状态时间”默认从父状态开始

            foreach (var tState in templateStates)
            {
                float currentTriggerTime = 0f;

                // 🎯【官方宪法第 1 条】：最后定义的状态时间 + add_time
                if (tState.AddTime.HasValue)
                {
                    currentTriggerTime = lastStateTime + tState.AddTime.Value;
                }
                // 🎯【官方宪法第 2 条】：time + relative_time
                else if (tState.RelativeTime.HasValue && tState.Time != null && tState.Time.ToString() != float.MaxValue.ToString())
                {
                    var absTimes = ResolveAbsoluteTimes(tState.Time);
                    currentTriggerTime = (absTimes.Count > 0 ? absTimes[0] : baseTime) + tState.RelativeTime.Value;
                }
                // 🎯【官方宪法第 3 条】：存在父状态 -> 父状态的时间 (baseTime) + relative_time
                else if (tState.RelativeTime.HasValue)
                {
                    currentTriggerTime = baseTime + tState.RelativeTime.Value; // 🌟 核心锚点：死死咬住父时间！
                }
                // 🎯【官方宪法第 5 条】：都没有定义 -> time
                else if (tState.Time != null && tState.Time.ToString() != float.MaxValue.ToString())
                {
                    var absTimes = ResolveAbsoluteTimes(tState.Time);
                    currentTriggerTime = absTimes.Count > 0 ? absTimes[0] : float.MaxValue;
                    CompileWarnings.Add("⚠️ 警告：检测到一个模板的内部子帧使用了绝对时间或音符锚点！这会破坏模板随父级平移的设计初衷。");
                }
                else
                {
                    currentTriggerTime = float.MaxValue;
                }

                // 成功计算出时间后，立刻更新为下一个元素的“上一个时间”
                if (currentTriggerTime != float.MaxValue) lastStateTime = currentTriggerTime;

                TState mergedState = MergeProperties<TState>(inheritedBaseState, tState);
                mergedState.Template = null;

                if (string.IsNullOrEmpty(tState.Template) || _templates == null || !_templates.ContainsKey(tState.Template))
                {
                    var clone = DeepClone(mergedState);
                    clone.Time = currentTriggerTime; clone.RelativeTime = null; clone.AddTime = null;
                    result.Add(clone);
                }
                else
                {
                    if (visitedTemplates.Contains(tState.Template))
                    {
                        CompileWarnings.Add($"❌ 致命错误拦截：检测到模板【{tState.Template}】发生循环嵌套！已强制截断！");
                        continue;
                    }
                    var childTemplate = _templates[tState.Template];
                    if (childTemplate.Keyframes == null || childTemplate.Keyframes.Count == 0)
                    {
                        var clone = DeepClone(mergedState);
                        clone.Time = currentTriggerTime; clone.RelativeTime = null; clone.AddTime = null;
                        result.Add(clone);
                    }
                    else
                    {
                        var newVisited = new HashSet<string>(visitedTemplates) { tState.Template };
                        var subExpanded = ExpandTemplateKeyframes<TState>(childTemplate.Keyframes, currentTriggerTime, mergedState, newVisited);
                        result.AddRange(subExpanded);
                    }
                }
            }
            return result;
        }

        // ==========================================
        // ⏱️ 3. 万能时空雷达 & 基因融合器 (保持原样)
        // ==========================================
        private List<float> ResolveAbsoluteTimes(object timeObj)
        {
            var result = new List<float>();
            if (timeObj == null) return result;
            if (timeObj is System.Collections.IList list) { foreach (var item in list) result.AddRange(ResolveAbsoluteTimes(item)); return result; }
            string tStr = timeObj.ToString().Trim();
            if (float.TryParse(tStr, out float fVal)) { result.Add(fVal); return result; }

            if (_chart?.note_list != null && _engine != null && tStr.Contains(":"))
            {
                var parts = tStr.Split(':');
                if (parts.Length >= 2 && int.TryParse(parts[1], out int noteId))
                {
                    dynamic targetNote = default;
                    foreach (var n in _chart.note_list) if (((dynamic)n).id == noteId) { targetNote = n; break; }
                    if (targetNote != null)
                    {
                        int tick = (int)targetNote.tick; int holdTick = 0;
                        try { holdTick = (int)targetNote.hold_tick; } catch { }

                        string anchor = parts[0]; float offset = 0f;
                        if (anchor != "at" && parts.Length >= 3) float.TryParse(parts[2], out offset);

                        if (anchor == "start") result.Add((float)_engine.TickToSeconds(tick) + offset);
                        else if (anchor == "end") result.Add((float)_engine.TickToSeconds(tick + holdTick) + offset);
                        else if (anchor == "intro") result.Add((float)_engine.TickToSeconds(tick) - 1.5f + offset);
                        else if (anchor == "at" && parts.Length >= 3 && float.TryParse(parts[2], out float percent))
                        {
                            int targetTick = tick + (int)(holdTick * percent);
                            result.Add((float)_engine.TickToSeconds(targetTick));
                        }
                    }
                }
            }
            return result;
        }

        private T DeepClone<T>(T source) => JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(source, Formatting.None));

        private TState MergeProperties<TState>(TState explicitState, TemplateState templateState) where TState : ObjectState, new()
        {
            TState merged = DeepClone(explicitState);
            if (templateState == null) return merged;
            PropertyInfo[] props = typeof(TState).GetProperties();
            Type templateType = typeof(TemplateState);

            foreach (var prop in props)
            {
                if (prop.Name == "Time" || prop.Name == "RelativeTime" || prop.Name == "AddTime" || prop.Name == "Template") continue;
                object explicitVal = prop.GetValue(merged);
                bool isExplicitNull = (explicitVal == null);
                if (explicitVal is UnitFloat uf && uf.Value == 0 && uf.Unit == ReferenceUnit.World) isExplicitNull = true;

                if (isExplicitNull)
                {
                    PropertyInfo tProp = templateType.GetProperty(prop.Name);
                    if (tProp != null)
                    {
                        object tVal = tProp.GetValue(templateState);
                        if (tVal != null)
                        {
                            string tJson = JsonConvert.SerializeObject(tVal);
                            object clonedTVal = JsonConvert.DeserializeObject(tJson, prop.PropertyType);
                            prop.SetValue(merged, clonedTVal);
                        }
                    }
                }
            }
            return merged;
        }
    }
}