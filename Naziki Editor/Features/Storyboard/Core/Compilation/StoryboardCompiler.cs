using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Models;
using Naziki_Editor.Core;

namespace Naziki_Editor.Core.Compilation
{
    [Obsolete("Legacy regression fixture only. Runtime export uses IStoryboardRuntimeExporter.")]
    public class StoryboardCompiler
    {
        private static IDialogService? _dialogService;

        public static void Initialize(IDialogService dialogService) { _dialogService = dialogService; }
        private C2Chart _chart;
        private ChartTimeEngine _engine;
        private Dictionary<string, C2Template> _templates;
        private readonly IStoryboardTemplatePropertyMapper _templatePropertyMapper;

        public List<string> CompileWarnings { get; private set; } = new List<string>();

        // 构造函数接受整个 Chart、时间引擎和模板字典，准备好一切进行编译
        public StoryboardCompiler(
            C2Chart chart,
            ChartTimeEngine engine,
            Dictionary<string, C2Template> templates,
            IStoryboardTemplatePropertyMapper? templatePropertyMapper = null)
        {
            _chart = chart;
            _engine = engine;
            _templates = templates;
            _templatePropertyMapper =
                templatePropertyMapper ?? new StoryboardTemplatePropertyMapper();
        }

        // 主入口：展平整个 Storyboard，处理所有对象类型
        public void FlattenStoryboard(StoryboardRoot root)
        {
            CompileWarnings.Clear();
            ProcessEntityList<C2Sprite, SpriteState>(root.sprites, "sprites");
            ProcessEntityList<C2Text, TextState>(root.texts, "texts");
            ProcessEntityList<C2Line, LineState>(root.lines, "lines");
            ProcessEntityList<C2Video, VideoState>(root.videos, "videos");
            ProcessEntityList<C2SceneController, ControllerState>(root.controllers, "controllers");
            ProcessEntityList<C2NoteController, NoteControllerState>(root.note_controllers, "note_controllers");

            // 🌟 终极进化：细胞分裂法术！
            // 在所有控制器的相对时间被展平为绝对时间后，执行高维到低维的物理拆解！
            MitosisSceneControllers(root.controllers);
            root.templates.Clear();
        }

        // 通用处理函数：针对每种对象列表，调用展平函数处理它们的关键帧
        private void ProcessEntityList<TEntity, TState>(List<TEntity> entities, string collectionName)
            where TEntity : StoryboardEntity<TState>
            where TState : ObjectState, new()
        {
            if (entities == null) return;
            for (var entityIndex = 0; entityIndex < entities.Count; entityIndex++)
            {
                var entity = entities[entityIndex];
                var entityPath = $"$.{collectionName}[{entityIndex}]";
                // 🌟 提取基准状态的时间作为相对时间计算的起点
                float baseTime = 0f;
                if (entity.BaseState?.Time != null && entity.BaseState.Time.ToString() != float.MaxValue.ToString())
                {
                    var absTimes = ResolveAbsoluteTimes(entity.BaseState.Time);
                    if (absTimes.Count > 0) baseTime = absTimes[0];
                }

                // 🌟 如果基准状态中引用了模板，且关键帧列表为空，则从基准状态展开模板
                if (!string.IsNullOrEmpty(entity.BaseState?.Template))
                {
                    var templateName = entity.BaseState.Template;
                    if (_templates != null &&
                        _templates.TryGetValue(templateName, out var baseTemplate))
                    {
                        HandleMappingIssues(_templatePropertyMapper.Apply(
                                entity.BaseState,
                                baseTemplate.BaseState,
                                StoryboardTemplateApplyMode.FillMissing,
                                $"$.templates.{EscapePathSegment(templateName)}"),
                            $"{entityPath}.template");

                        if (entity.Keyframes == null || entity.Keyframes.Count == 0)
                        {
                            var templateKeyframe = new TState
                            {
                                Template = templateName,
                                Time = baseTime
                            };
                            entity.Keyframes = new List<TState> { templateKeyframe };
                        }
                    }
                    entity.BaseState.Template = null; // 清除基准状态中的模板引用，避免重复展开
                }

                entity.Keyframes = FlattenStates<TState>(
                    entity.Keyframes, baseTime, entityPath);
            }
        }

        // ==========================================
        // 🧬 1. 顶层对象展平引擎 (遵循官方时间推算规范)
        // ==========================================
        private List<TState> FlattenStates<TState>(
            List<TState> originalStates,
            float baseStateTime,
            string entityPath) where TState : ObjectState, new()
        {
            if (originalStates == null || originalStates.Count == 0) return originalStates;

            var flattenedList = new List<TState>();
            float lastStateTime = baseStateTime;

            for (var stateIndex = 0; stateIndex < originalStates.Count; stateIndex++)
            {
                var state = originalStates[stateIndex];
                var referencePath = $"{entityPath}.states[{stateIndex}]";
                List<float> triggerTimes = new List<float>();

                if (state.AddTime.HasValue)
                {
                    triggerTimes.Add(lastStateTime + state.AddTime.Value);
                }
                else if (state.RelativeTime.HasValue && state.Time != null && state.Time.ToString() != float.MaxValue.ToString())
                {
                    var absTimes = ResolveAbsoluteTimes(state.Time);
                    foreach (var t in absTimes) triggerTimes.Add(t + state.RelativeTime.Value);
                }
                else if (state.RelativeTime.HasValue)
                {
                    triggerTimes.Add(lastStateTime + state.RelativeTime.Value);
                }
                else if (state.Time != null && state.Time.ToString() != float.MaxValue.ToString())
                {
                    triggerTimes = ResolveAbsoluteTimes(state.Time);
                }
                else
                {
                    triggerTimes.Add(float.MaxValue);
                }

                if (triggerTimes.Count > 1 && state.Destroy == true)
                    CompileWarnings.Add($"⚠️ 警告：检测到属性包含了 destroy:true，且被应用在了包含 {triggerTimes.Count} 个时间锚点的数组中！");

                if (string.IsNullOrEmpty(state.Template) || _templates == null || !_templates.ContainsKey(state.Template))
                {
                    foreach (float t in triggerTimes)
                    {
                        var clone = DeepClone(state);
                        clone.Time = t; clone.RelativeTime = null; clone.AddTime = null;
                        flattenedList.Add(clone);
                        if (t != float.MaxValue) lastStateTime = t;
                    }
                }
                else
                {
                    var template = _templates[state.Template];
                    foreach (float baseTime in triggerTimes)
                    {
                        TState mergedBaseState = MergeProperties<TState>(
                            state,
                            template.BaseState,
                            $"$.templates.{EscapePathSegment(state.Template)}",
                            referencePath);
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
                            var expandedChildren = ExpandTemplateKeyframes<TState>(
                                template.Keyframes,
                                baseTime,
                                mergedBaseState,
                                new HashSet<string> { state.Template },
                                state.Template,
                                referencePath);
                            flattenedList.AddRange(expandedChildren);

                            if (expandedChildren.Count > 0)
                            {
                                float lastChildTime = (float)expandedChildren.Last().Time;
                                if (lastChildTime != float.MaxValue) lastStateTime = lastChildTime;
                            }
                        }
                    }
                }
            }

            return flattenedList.OrderBy(s => (float)s.Time).ToList();
        }

        // ==========================================
        // 🪆 2. 子帧递归拆解术
        // ==========================================
        private List<TState> ExpandTemplateKeyframes<TState>(
            List<TemplateState> templateStates,
            float baseTime,
            TState inheritedBaseState,
            HashSet<string> visitedTemplates,
            string currentTemplateName,
            string referencePath) where TState : ObjectState, new()
        {
            var result = new List<TState>();
            float lastStateTime = baseTime;

            for (var templateStateIndex = 0;
                 templateStateIndex < templateStates.Count;
                 templateStateIndex++)
            {
                var tState = templateStates[templateStateIndex];
                float currentTriggerTime = 0f;

                if (tState.AddTime.HasValue)
                {
                    currentTriggerTime = lastStateTime + tState.AddTime.Value;
                }
                else if (tState.RelativeTime.HasValue && tState.Time != null && tState.Time.ToString() != float.MaxValue.ToString())
                {
                    var absTimes = ResolveAbsoluteTimes(tState.Time);
                    currentTriggerTime = (absTimes.Count > 0 ? absTimes[0] : baseTime) + tState.RelativeTime.Value;
                }
                else if (tState.RelativeTime.HasValue)
                {
                    // 🌟 修复：模板子帧的相对时间应基于上一个子帧的时间（lastStateTime），而非模板基准时间
                    currentTriggerTime = lastStateTime + tState.RelativeTime.Value;
                }
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

                if (currentTriggerTime != float.MaxValue) lastStateTime = currentTriggerTime;

                // 🌟 子帧覆盖术：模板关键帧的值应覆盖继承的基准状态
                TState mergedState = ApplyKeyframeOverrides<TState>(
                    inheritedBaseState,
                    tState,
                    $"$.templates.{EscapePathSegment(currentTemplateName)}.states[{templateStateIndex}]",
                    referencePath);
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
                        var subExpanded = ExpandTemplateKeyframes<TState>(
                            childTemplate.Keyframes,
                            currentTriggerTime,
                            mergedState,
                            newVisited,
                            tState.Template,
                            referencePath);
                        result.AddRange(subExpanded);
                    }
                }
            }
            return result;
        }

        // ==========================================
        // ⏱️ 3. 万能时空雷达 & 基因融合器
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

        private TState MergeProperties<TState>(
            TState explicitState,
            TemplateState templateState,
            string templatePath,
            string referencePath) where TState : ObjectState, new()
        {
            TState merged = DeepClone(explicitState);
            if (templateState == null) return merged;
            HandleMappingIssues(_templatePropertyMapper.Apply(
                merged,
                templateState,
                StoryboardTemplateApplyMode.FillMissing,
                templatePath),
                referencePath);
            return merged;
        }

        // 🌟 子帧覆盖术：将模板关键帧的值覆盖到继承的基准状态上
        private TState ApplyKeyframeOverrides<TState>(
            TState baseState,
            TemplateState keyframeState,
            string templateStatePath,
            string referencePath) where TState : ObjectState, new()
        {
            TState result = DeepClone(baseState);
            if (keyframeState == null) return result;
            HandleMappingIssues(_templatePropertyMapper.Apply(
                result,
                keyframeState,
                StoryboardTemplateApplyMode.Override,
                templateStatePath),
                referencePath);
            return result;
        }

        private void HandleMappingIssues(
            IReadOnlyList<StoryboardTemplatePropertyIssue> issues,
            string referencePath)
        {
            foreach (var warning in issues.Where(issue =>
                         issue.Severity == StoryboardDiagnosticSeverity.Warning))
                CompileWarnings.Add(
                    $"⚠️ {warning.SourcePath}（引用：{referencePath}）: {warning.Message}");
            var errors = issues.Where(issue =>
                issue.Severity == StoryboardDiagnosticSeverity.Error).ToArray();
            if (errors.Length > 0)
                throw new InvalidOperationException(string.Join(Environment.NewLine,
                    errors.Select(error =>
                        $"{error.SourcePath}（引用：{referencePath}）: {error.Message}")));
        }

        private static string EscapePathSegment(string value) =>
            value.All(character => char.IsLetterOrDigit(character) || character == '_')
                ? value
                : $"['{value.Replace("'", "\\'")}']";

        // ==========================================
        // 🦠 4. 细胞分裂引擎 (Controller Mitosis)
        // ==========================================
        private void MitosisSceneControllers(List<C2SceneController> controllers)
        {
            if (controllers == null || controllers.Count == 0) return;

            var newControllersList = new List<C2SceneController>();

            // 🌟 弹窗情报收集器
            int targetSplitCount = 0; // 发现了多少个需要分裂的混合体
            int newSpawnCount = 0;    // 分裂出了多少个纯净单体
            int idSequence = 1;

            foreach (var ctrl in controllers)
            {
                var usedModes = new HashSet<string>();

                // 嗅探当前控制器到底跨界了几个门派
                CheckStateModes(ctrl.BaseState, usedModes);
                if (ctrl.Keyframes != null)
                {
                    foreach (var state in ctrl.Keyframes) CheckStateModes(state, usedModes);
                }

                if (usedModes.Count == 0)
                {
                    ctrl.EditorMode = "Camera"; // 兜底：纯空壳直接分配给相机
                    newControllersList.Add(ctrl);
                }
                else if (usedModes.Count == 1)
                {
                    ctrl.EditorMode = usedModes.First(); // 单维度对象，直接发身份证！
                    newControllersList.Add(ctrl);
                }
                else
                {
                    // 💥 细胞分裂法术触发！
                    targetSplitCount++;

                    foreach (var mode in usedModes)
                    {
                        var clone = DeepClone(ctrl); // 完美复制基因

                        // 赋予分裂体独立身份证，防止 ID 冲突
                        clone.Id = string.IsNullOrEmpty(ctrl.Id) ? $"mitosis_ctrl_{idSequence++}" : $"{ctrl.Id}_{mode.ToLower()}";
                        clone.EditorMode = mode;

                        // 净化 BaseState，擦除不属于当前门派的属性
                        PurgeNonModeProperties(clone.BaseState, mode);

                        // 净化所有帧，剔除彻底无用的空壳帧
                        var cleanKeyframes = new List<ControllerState>();
                        if (clone.Keyframes != null)
                        {
                            foreach (var state in clone.Keyframes)
                            {
                                PurgeNonModeProperties(state, mode);
                                // 如果这一帧在净化后，还有属于自己维度的属性，才保留它！
                                if (HasAnyActiveProperty(state, mode))
                                {
                                    cleanKeyframes.Add(state);
                                }
                            }
                        }
                        clone.Keyframes = cleanKeyframes;
                        newControllersList.Add(clone);
                        newSpawnCount++;
                    }
                }
            }

            // 偷天换日：用分裂后的纯净大军替换掉原来杂交的控制器
            controllers.Clear();
            controllers.AddRange(newControllersList);

            // 📢 终极智能弹窗：只有发生分裂时，才去打扰大大！
            if (targetSplitCount > 0)
            {
                _dialogService?.ShowMessage(
                    $"✨ 细胞分裂引擎运转报告 ✨\n\n" +
                    $"嗅探雷达发现外部谱面中存在 {targetSplitCount} 个【多维度混合控制器】！\n" +
                    $"为了保证编辑器微观时光屋的纯净，小艾已在底层将它们无损拆分成了 {newSpawnCount} 个【纯净单维度控制器】啦！\n\n" +
                    $"(各属性的时间链均已自动展平对齐，打谱师可以放心修改！)",
                    "🧬 细胞分裂成功",
                    DialogMessageType.Info);
            }
        }

        private void CheckStateModes(ControllerState state, HashSet<string> usedModes)
        {
            if (state == null) return;
            var props = typeof(ControllerState).GetProperties();
            foreach (var prop in props)
            {
                if (!StoryboardTemplatePropertyMapper.IsExportableProperty(prop)) continue;
                if (IsBaseProperty(prop.Name)) continue;

                object val = prop.GetValue(state);
                bool isExplicitNull = (val == null);

                if (!isExplicitNull)
                {
                    var cat = PropertyClassifier.GetCategory(prop.Name);
                    usedModes.Add(GetModeByCategory(cat));
                }
            }
        }

        private void PurgeNonModeProperties(ControllerState state, string targetMode)
        {
            if (state == null) return;
            var props = typeof(ControllerState).GetProperties();
            foreach (var prop in props)
            {
                if (!StoryboardTemplatePropertyMapper.IsExportableProperty(
                        prop, requireWrite: true))
                    continue;
                if (IsBaseProperty(prop.Name)) continue;

                var cat = PropertyClassifier.GetCategory(prop.Name);
                if (GetModeByCategory(cat) != targetMode)
                {
                    prop.SetValue(state, null); // 橡皮擦法术：不属于自己的属性全部设为空！
                }
            }
        }

        private bool HasAnyActiveProperty(ControllerState state, string targetMode)
        {
            if (state == null) return false;
            var props = typeof(ControllerState).GetProperties();
            foreach (var prop in props)
            {
                if (!StoryboardTemplatePropertyMapper.IsExportableProperty(prop)) continue;
                if (IsBaseProperty(prop.Name)) continue;

                var cat = PropertyClassifier.GetCategory(prop.Name);
                if (GetModeByCategory(cat) == targetMode)
                {
                    object val = prop.GetValue(state);
                    bool isExplicitNull = (val == null);
                    if (!isExplicitNull) return true;
                }
            }
            return false;
        }

        private string GetModeByCategory(PropertyCategory category)
        {
            return category switch
            {
                PropertyCategory.Spatial => "Camera",
                PropertyCategory.Appearance => "Appearance",
                PropertyCategory.UiControl => "UI",
                PropertyCategory.Effects => "Effects",
                _ => "Camera"
            };
        }

        private bool IsBaseProperty(string propName)
        {
            return propName == "Time" || propName == "RelativeTime" || propName == "AddTime" ||
                   propName == "Easing" || propName == "Destroy" || propName == "Template" ||
                   propName == "Layer" || propName == "Order";
        }
    }
}
