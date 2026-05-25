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

        // 🚨 记录编译期间产生的所有警告和红牌异常！
        public List<string> CompileWarnings { get; private set; } = new List<string>();

        public StoryboardCompiler(C2Chart chart, ChartTimeEngine engine, Dictionary<string, C2Template> templates)
        {
            _chart = chart;
            _engine = engine;
            _templates = templates;
        }

        // ==========================================
        // 🚀 1. 总控中心：一键全量展平整个故事板！
        // ==========================================
        public void FlattenStoryboard(StoryboardRoot root)
        {
            CompileWarnings.Clear();

            // 🎯【完美对齐】：将原先错误的 C2Controller 修正为正统的 C2SceneController！
            ProcessEntityList<C2Sprite, SpriteState>(root.sprites);
            ProcessEntityList<C2Text, TextState>(root.texts);
            ProcessEntityList<C2Line, LineState>(root.lines);
            ProcessEntityList<C2Video, VideoState>(root.videos);
            ProcessEntityList<C2SceneController, ControllerState>(root.controllers); // 🌟 修正这里
            ProcessEntityList<C2NoteController, NoteControllerState>(root.note_controllers);
        }

        private void ProcessEntityList<TEntity, TState>(List<TEntity> entities)
            where TEntity : StoryboardEntity<TState>
            where TState : ObjectState, new()
        {
            if (entities == null) return;
            foreach (var entity in entities)
            {
                // 递归展平该实体的所有关键帧状态
                entity.Keyframes = FlattenStates<TState>(entity.Keyframes);
            }
        }

        // ==========================================
        // 🧬 2. 核心展平引擎：递归降维打击
        // ==========================================
        private List<TState> FlattenStates<TState>(List<TState> originalStates) where TState : ObjectState, new()
        {
            if (originalStates == null || originalStates.Count == 0) return originalStates;

            var flattenedList = new List<TState>();
            float lastAbsoluteTime = 0f; // 追踪时间轴上的上一帧绝对时间

            foreach (var state in originalStates)
            {
                // 1. ⏱️ 预编译：计算当前状态的所有绝对触发时间！
                List<float> triggerTimes = new List<float>();

                if (state.RelativeTime.HasValue)
                {
                    triggerTimes.Add(lastAbsoluteTime + state.RelativeTime.Value);
                }
                else if (state.AddTime.HasValue)
                {
                    triggerTimes.Add(lastAbsoluteTime + state.AddTime.Value);
                }
                else if (state.Time != null && state.Time.ToString() != float.MaxValue.ToString())
                {
                    triggerTimes = ResolveAbsoluteTimes(state.Time);
                }
                else
                {
                    triggerTimes.Add(float.MaxValue); // 隐藏/未触发状态
                }

                // 🚨 探测时空破坏级 Bug：多次克隆叠加 Destroy！
                if (triggerTimes.Count > 1 && state.Destroy == true)
                {
                    CompileWarnings.Add($"⚠️ 警告：检测到属性包含了 destroy:true，且被应用在了一个包含 {triggerTimes.Count} 个时间锚点的数组中！这将导致第一次执行后对象彻底粉碎，后续的动画全部失效！");
                }

                // 2. 🪄 裂变与模板递归展开
                if (string.IsNullOrEmpty(state.Template) || _templates == null || !_templates.ContainsKey(state.Template))
                {
                    // 纯净状态，无模板：直接为每个时间点克隆一个自己
                    foreach (float t in triggerTimes)
                    {
                        var clone = DeepClone(state);
                        clone.Time = t;
                        clone.RelativeTime = null; clone.AddTime = null;
                        flattenedList.Add(clone);
                        if (t != float.MaxValue) lastAbsoluteTime = t;
                    }
                }
                else
                {
                    // 套娃模板状态：执行降维展开！
                    var template = _templates[state.Template];

                    foreach (float baseTime in triggerTimes)
                    {
                        // 🧬 基因融合：显式属性优先覆盖模板的基础属性
                        TState mergedBaseState = MergeProperties<TState>(state, template.BaseState);
                        mergedBaseState.Template = null; // 剥离旧的印章

                        if (template.Keyframes == null || template.Keyframes.Count == 0)
                        {
                            // 这是一个【叶子模板】(无子帧)
                            var clone = DeepClone(mergedBaseState);
                            clone.Time = baseTime;
                            clone.RelativeTime = null; clone.AddTime = null;
                            flattenedList.Add(clone);
                            if (baseTime != float.MaxValue) lastAbsoluteTime = baseTime;
                        }
                        else
                        {
                            // 这是一个【组合模板】(内含子帧)，进入深度优先递归！
                            var expandedChildren = ExpandTemplateKeyframes<TState>(template.Keyframes, baseTime, mergedBaseState, new HashSet<string> { state.Template });
                            flattenedList.AddRange(expandedChildren);

                            if (expandedChildren.Count > 0)
                            {
                                float lastChildTime = (float)expandedChildren.Last().Time;
                                if (lastChildTime != float.MaxValue) lastAbsoluteTime = lastChildTime;
                            }
                        }
                    }
                }
            }

            // 3. 🧹 清洗排序：保证最终吐出的一维数组是按时间严格递增的官方格式！
            return flattenedList.OrderBy(s => (float)s.Time).ToList();
        }

        // ==========================================
        // 🪆 3. 子帧递归拆解术 (根据法则 B：使用绝对时间轴平移展开，不污染相对邻居)
        // ==========================================
        private List<TState> ExpandTemplateKeyframes<TState>(List<TemplateState> templateStates, float baseTime, TState inheritedBaseState, HashSet<string> visitedTemplates) where TState : ObjectState, new()
        {
            var result = new List<TState>();
            float currentContextTime = baseTime;

            foreach (var tState in templateStates)
            {
                // 计算子状态的绝对时间
                float currentTriggerTime = currentContextTime;

                if (tState.RelativeTime.HasValue) currentTriggerTime = currentContextTime + tState.RelativeTime.Value;
                else if (tState.AddTime.HasValue) currentTriggerTime = currentContextTime + tState.AddTime.Value;
                else if (tState.Time != null && tState.Time.ToString() != float.MaxValue.ToString())
                {
                    // 如果模板内居然写死了绝对时间/锚点！
                    var absTimes = ResolveAbsoluteTimes(tState.Time);
                    if (absTimes.Count > 0)
                    {
                        currentTriggerTime = absTimes[0];
                        CompileWarnings.Add("⚠️ 警告：检测到一个模板的内部子帧使用了绝对时间或音符锚点！这会破坏模板随父级触发时间平移的设计初衷。");
                    }
                }
                else currentTriggerTime = float.MaxValue;

                // 基因双重融合：显式覆盖
                TState mergedState = MergeProperties<TState>(inheritedBaseState, tState);
                mergedState.Template = null;

                if (string.IsNullOrEmpty(tState.Template) || _templates == null || !_templates.ContainsKey(tState.Template))
                {
                    // 已到达叶子节点
                    var clone = DeepClone(mergedState);
                    clone.Time = currentTriggerTime;
                    clone.RelativeTime = null; clone.AddTime = null;
                    result.Add(clone);
                }
                else
                {
                    // 🚨 防御塔：无限套娃死循环拦截！
                    if (visitedTemplates.Contains(tState.Template))
                    {
                        CompileWarnings.Add($"❌ 致命错误拦截：检测到模板【{tState.Template}】发生了循环嵌套引用！为了防止时空坍缩，已强制截断该分支的展平！");
                        continue;
                    }

                    var childTemplate = _templates[tState.Template];
                    if (childTemplate.Keyframes == null || childTemplate.Keyframes.Count == 0)
                    {
                        var clone = DeepClone(mergedState);
                        clone.Time = currentTriggerTime;
                        clone.RelativeTime = null; clone.AddTime = null;
                        result.Add(clone);
                    }
                    else
                    {
                        var newVisited = new HashSet<string>(visitedTemplates) { tState.Template };
                        var subExpanded = ExpandTemplateKeyframes<TState>(childTemplate.Keyframes, currentTriggerTime, mergedState, newVisited);
                        result.AddRange(subExpanded);
                    }
                }

                if (currentTriggerTime != float.MaxValue) currentContextTime = currentTriggerTime;
            }

            return result;
        }

        // ==========================================
        // ⏱️ 4. 万能时空雷达：将一切神鬼锚点提纯为绝对秒数
        // ==========================================
        private List<float> ResolveAbsoluteTimes(object timeObj)
        {
            var result = new List<float>();
            if (timeObj == null) return result;

            if (timeObj is System.Collections.IList list)
            {
                foreach (var item in list) result.AddRange(ResolveAbsoluteTimes(item));
                return result;
            }

            string tStr = timeObj.ToString().Trim();
            if (float.TryParse(tStr, out float fVal))
            {
                result.Add(fVal);
                return result;
            }

            // 🎯 音符锚点解析引擎
            if (_chart?.note_list != null && _engine != null && tStr.Contains(":"))
            {
                var parts = tStr.Split(':');
                if (parts.Length >= 2 && int.TryParse(parts[1], out int noteId))
                {
                    dynamic targetNote = default;
                    foreach (var n in _chart.note_list)
                    {
                        if (((dynamic)n).id == noteId) { targetNote = n; break; }
                    }

                    if (targetNote != null)
                    {
                        int tick = (int)targetNote.tick;
                        int holdTick = 0;
                        try { holdTick = (int)targetNote.hold_tick; } catch { }

                        string anchor = parts[0];
                        float offset = 0f;
                        if (anchor != "at" && parts.Length >= 3) float.TryParse(parts[2], out offset);

                        if (anchor == "start")
                        {
                            result.Add((float)_engine.TickToSeconds(tick) + offset);
                        }
                        else if (anchor == "end")
                        {
                            result.Add((float)_engine.TickToSeconds(tick + holdTick) + offset);
                        }
                        else if (anchor == "intro")
                        {
                            result.Add((float)_engine.TickToSeconds(tick) - 1.5f + offset);
                        }
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

        // ==========================================
        // 🧬 5. 泛型基因融合器
        // ==========================================
        private T DeepClone<T>(T source)
        {
            var json = JsonConvert.SerializeObject(source, Formatting.None);
            return JsonConvert.DeserializeObject<T>(json);
        }

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