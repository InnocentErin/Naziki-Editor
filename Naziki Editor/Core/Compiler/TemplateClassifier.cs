using Naziki_Editor.Models;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Naziki_Editor.Core.Compiler
{
    public static class TemplateClassifier
    {
        private static readonly HashSet<string> SpriteExclusive = new HashSet<string> { "Path", "PreserveAspect", "W", "H" };
        private static readonly HashSet<string> TextExclusive = new HashSet<string> { "TextContent", "Size", "Align", "LetterSpacing", "LineSpacing", "Font", "FontStyle" };
        private static readonly HashSet<string> VideoExclusive = new HashSet<string> { "Path", "Loop", "Speed", "PreserveAspect", "W", "H" };
        private static readonly HashSet<string> LineExclusive = new HashSet<string> { "Pos", "X1", "X2", "Y1", "Y2", "Width" };

        private static readonly HashSet<string> ControllerExclusive = new HashSet<string>
        {
            "StoryboardOpacity", "UiOpacity", "BackgroundDim", "ScanlineOpacity", "ScanlineColor", "NoteRingColor", "NoteFillColors",
            "OverrideScanlinePos", "ScanlinePos", "Perspective", "Fov", "Chromatical", "Bloom", "RadialBlur", "ColorFilter",
            "GrayScale", "Noise", "Sepia", "Dream", "Fisheye", "Shockwave", "Focus", "Glitch", "Arcade", "Tape", "Vignette"
        };

        private static readonly HashSet<string> NoteControllerExclusive = new HashSet<string>
        {
            "OverrideX", "OverrideY", "OverrideZ", "OverrideRotX", "OverrideRotY", "OverrideRotZ",
            "NoteOpacityMultiplier", "NoteSizeMultiplier", "HitboxMultiplier", "HoldDirection", "Style"
        };

        private static readonly HashSet<string> StageObjectGeneric = new HashSet<string>
        {
            "X", "Y", "Z", "RotX", "RotY", "RotZ", "Scale", "ScaleX", "ScaleY", "Opacity", "Layer", "Order", "PivotX", "PivotY", "Color"
        };

        // ==========================================
        // 📡 核心雷达扫描算法：自动鉴定模板门派！
        // ==========================================
        public static TemplateType AnalyzeTemplate(TemplateState state)
        {
            if (state == null) return TemplateType.Generic;

            var activeProps = new HashSet<string>();
            PropertyInfo[] props = state.GetType().GetProperties();

            foreach (var prop in props)
            {
                if (prop.Name == "Time" || prop.Name == "RelativeTime" || prop.Name == "AddTime" || prop.Name == "Easing" || prop.Name == "Destroy")
                    continue;

                object val = prop.GetValue(state);
                if (val != null)
                {
                    if (val is UnitFloat uf && uf.Value == 0 && uf.Unit == ReferenceUnit.World) continue;
                    activeProps.Add(prop.Name);
                }
            }

            int spriteScore = 0, textScore = 0, videoScore = 0, lineScore = 0, controllerScore = 0, noteScore = 0, genericStageScore = 0;

            foreach (var prop in activeProps)
            {
                if (SpriteExclusive.Contains(prop)) spriteScore++;
                if (TextExclusive.Contains(prop)) textScore++;
                if (VideoExclusive.Contains(prop)) videoScore++;
                if (LineExclusive.Contains(prop)) lineScore++;
                if (ControllerExclusive.Contains(prop)) controllerScore++;
                if (NoteControllerExclusive.Contains(prop)) noteScore++;
                if (StageObjectGeneric.Contains(prop)) genericStageScore++;
            }

            int exclusiveClassesCount = 0;
            if (spriteScore > 0) exclusiveClassesCount++;
            if (textScore > 0) exclusiveClassesCount++;
            if (videoScore > 0 && !activeProps.Contains("TextContent")) exclusiveClassesCount++;
            if (lineScore > 0) exclusiveClassesCount++;
            if (controllerScore > 0) exclusiveClassesCount++;
            if (noteScore > 0) exclusiveClassesCount++;

            // 🎯【核心修正】：如果冲突，不再返回不存在的 MixedOrInvalid，而是优雅退化为 Generic (通用动画模板)！
            if (exclusiveClassesCount > 1) return TemplateType.Generic;

            if (textScore > 0) return TemplateType.Text;
            if (lineScore > 0) return TemplateType.Line;
            if (controllerScore > 0) return TemplateType.Controller;
            if (noteScore > 0) return TemplateType.NoteController;

            if (videoScore > 0)
            {
                if (activeProps.Contains("Loop") || activeProps.Contains("Speed")) return TemplateType.Video;
                return TemplateType.Sprite;
            }
            if (spriteScore > 0) return TemplateType.Sprite;

            if (genericStageScore > 0) return TemplateType.StageObject;

            return TemplateType.Generic;
        }
    }
}