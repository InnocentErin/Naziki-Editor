using Naziki_Editor.Models;
using System.Collections.Generic;

namespace Naziki_Editor.Core
{
    public static class TemplateManager
    {
        public static bool CheckForCircularDependency(StoryboardRoot root, string templateName, string targetTemplateToInject)
        {
            if (string.IsNullOrEmpty(targetTemplateToInject)) return false;
            if (templateName == targetTemplateToInject) return true;

            if (root.templates != null && root.templates.TryGetValue(targetTemplateToInject, out var nextTemplate))
            {
                var allStates = GetAllStatesFromTemplate(nextTemplate);
                foreach (var state in allStates)
                {
                    if (state.Template == templateName) return true;
                    if (CheckForCircularDependency(root, templateName, state.Template)) return true;
                }
            }
            return false;
        }

        public static void RenameTemplateGlobally(StoryboardRoot root, string oldName, string newName)
        {
            var allStates = GetAllStatesInStoryboard(root);
            foreach (var state in allStates)
            {
                if (state.Template == oldName)
                {
                    state.Template = newName;
                }
            }
        }

        public static List<ObjectState> GetAllStatesInStoryboard(StoryboardRoot root)
        {
            var allStates = new List<ObjectState>();
            if (root == null) return allStates;

            if (root.sprites != null) foreach (var o in root.sprites) ExtractStates(o, allStates);
            if (root.texts != null) foreach (var o in root.texts) ExtractStates(o, allStates);
            if (root.lines != null) foreach (var o in root.lines) ExtractStates(o, allStates);
            if (root.videos != null) foreach (var o in root.videos) ExtractStates(o, allStates);
            if (root.controllers != null) foreach (var o in root.controllers) ExtractStates(o, allStates);
            if (root.note_controllers != null) foreach (var o in root.note_controllers) ExtractStates(o, allStates);

            if (root.templates != null)
            {
                foreach (var t in root.templates.Values)
                {
                    allStates.AddRange(GetAllStatesFromTemplate(t));
                }
            }
            return allStates;
        }

        private static IEnumerable<ObjectState> GetAllStatesFromTemplate(C2Template template)
        {
            var allStates = new List<ObjectState>();
            if (template.BaseState != null) allStates.Add(template.BaseState);
            if (template.Keyframes != null) allStates.AddRange(template.Keyframes);
            return allStates;
        }

        private static void ExtractStates(IStoryboardEntity entity, List<ObjectState> target)
        {
            if (entity.GetBaseState() is ObjectState bs) target.Add(bs);
            if (entity.GetKeyframes() != null)
            {
                foreach (var frame in entity.GetKeyframes())
                {
                    if (frame is ObjectState os) target.Add(os);
                }
            }
        }


        // ==========================================\
        // 🔮 智能法术 1：根据属性残留，推测模板的真实身份
        // ==========================================\
        public static TemplateType InferTemplateType(C2Template template)
        {
            var states = GetAllStatesFromTemplate(template);
            bool hasText = false, hasPath = false, hasPos = false;
            bool hasControllerProp = false, hasNoteCtrlProp = false;
            bool hasSpatialProp = false;

            foreach (var state in states)
            {
                // 利用反射嗅探非空属性
                var props = state.GetType().GetProperties();
                foreach (var prop in props)
                {
                    if (prop.GetValue(state) == null) continue;

                    string n = prop.Name.ToLower();
                    if (n == "text" || n == "textcontent" || n == "font_weight") hasText = true;
                    else if (n == "path") hasPath = true;
                    else if (n == "pos") hasPos = true;
                    else if (n == "fov" || n == "ui_opacity" || n.Contains("bloom") || n.Contains("vignette")) hasControllerProp = true;
                    else if (n.Contains("override_") || n == "hitbox_multiplier") hasNoteCtrlProp = true;
                    else if (n == "x" || n == "y" || n == "scale") hasSpatialProp = true;
                }
            }

            // 按照特征的稀有度进行身份宣判！
            if (hasControllerProp) return TemplateType.Controller;
            if (hasNoteCtrlProp) return TemplateType.NoteController;
            if (hasText) return TemplateType.Text;
            if (hasPos) return TemplateType.Line;
            if (hasPath) return TemplateType.Sprite; // 视频和精灵特征太像，默认给精灵，设计师可手动改
            if (hasSpatialProp) return TemplateType.StageObject;

            return TemplateType.Generic; // 实在认不出来，归入混沌！
        }

        // ==========================================\
        // 🛡️ 智能法术 2：为 8 大门派建立专属的属性白名单！
        // ==========================================\
        public static HashSet<string> GetAllowedPropertiesForType(TemplateType type)
        {
            var baseProps = new[] { "Time", "Easing", "AddTime", "RelativeTime", "Destroy" };

            // 通用的空间与外观（根据你的统计）
            var spatialProps = new[] { "X", "Y", "Z", "RotX", "RotY", "RotZ", "Scale", "ScaleX", "ScaleY", "Width", "Height", "W", "H", "PivotX", "PivotY" };
            var appearanceProps = new[] { "Opacity", "Layer", "Order", "Color", "PreserveAspect" };

            var allowed = new HashSet<string>(baseProps);

            switch (type)
            {
                case TemplateType.Generic:
                    return null; // 混沌模式放行一切

                case TemplateType.StageObject:
                    allowed.UnionWith(spatialProps);
                    allowed.UnionWith(appearanceProps);
                    break;

                case TemplateType.Text:
                    allowed.UnionWith(spatialProps);
                    allowed.UnionWith(appearanceProps);
                    // 追加文字专属
                    allowed.UnionWith(new[] { "TextContent", "Size", "Align", "LetterSpacing", "LineSpacing", "Font", "FontStyle" });
                    break;

                case TemplateType.Sprite:
                    allowed.UnionWith(spatialProps);
                    allowed.UnionWith(appearanceProps);
                    allowed.UnionWith(new[] { "Path" }); // 精灵专属
                    break;

                case TemplateType.Video:
                    allowed.UnionWith(spatialProps);
                    allowed.UnionWith(appearanceProps);
                    allowed.UnionWith(new[] { "Path", "Loop", "Speed" }); // 视频专属
                    break;

                case TemplateType.Line:
                    allowed.UnionWith(baseProps);
                    // 线条特殊空间坐标体系
                    allowed.UnionWith(new[] { "Width", "X", "Y", "Z", "RotX", "RotY", "RotZ", "X1", "X2", "Y1", "Y2", "Opacity", "Layer", "Order", "Color" });
                    break;

                case TemplateType.Controller:
                    // 彻底补全控制器的三大体系
                    allowed.UnionWith(new[] {
                        "X", "Y", "Z", "RotX", "RotY", "RotZ", // 核心相机
                        "Perspective", "Fov",
                        "StoryboardOpacity", "UiOpacity", "ScanlineOpacity", "BackgroundDim", "NoteOpacityMultiplier", // UI控制
                        "ScanlineColor", "NoteRingColor", "OverrideScanlinePos", "ScanlinePos","NoteFillColors",
                        // 滤镜阵列
                        "Chromatical", "ChromaticalFade", "ChromaticalIntensity", "ChromaticalSpeed",
                        "Bloom", "BloomIntensity", "RadialBlur", "RadialBlurIntensity",
                        "ColorFilter", "ColorFilterColor", "GrayScale", "GrayScaleIntensity",
                        "Noise", "NoiseIntensity", "Sepia", "SepiaIntensity", "Dream", "DreamIntensity"
                    });
                    break;

                case TemplateType.NoteController:
                    allowed.UnionWith(new[] {
                        "OverrideX", "X", "OverrideY", "Y", "OverrideZ", "Z",
                        "OverrideRotX", "RotX", "OverrideRotY", "RotY", "OverrideRotZ", "RotZ",
                        "NoteTarget", "NoteSizeMultiplier", "HitboxMultiplier"
                    });
                    break;
            }
            return allowed;
        }

        // ==========================================\
        // 🔍 智能法术 3：判断某个属性是否允许出现在当前模板中
        // ==========================================\
        public static bool IsPropertyAllowed(string propertyName, TemplateType type)
        {
            var allowedSet = GetAllowedPropertiesForType(type);
            if (allowedSet == null) return true; // Generic 放行所有
            return allowedSet.Contains(propertyName);
        }
    }
}