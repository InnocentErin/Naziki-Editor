using System;
using System.Linq;

namespace Naziki_Editor.Core
{
    // 🌟 统一的四大核心属性派别枚举 (Camera 已完美并入 Spatial)
    public enum PropertyCategory
    {
        Spatial,      // 📍 空间坐标与核心相机
        Appearance,   // 🎨 外观与内容
        UiControl,    // 🎛️ 游戏UI与控制
        Effects       // ✨ 屏幕特效
    }

    public static class PropertyClassifier
    {
        public static PropertyCategory GetCategory(string propertyName)
        {
            string n = propertyName;

            // 1. 📍 空间坐标与核心相机 (Spatial) - 融合了 Perspective 和 Fov！
            if (new[] { "X", "Y", "Z", "RotX", "RotY", "RotZ", "Scale", "ScaleX", "ScaleY", "Width", "Height", "W", "H", "PivotX", "PivotY", "X1", "X2", "Y1", "Y2", "OverrideX", "OverrideY", "OverrideZ", "OverrideRotX", "OverrideRotY", "OverrideRotZ", "Pos", "Perspective", "Fov" }.Contains(n))
            {
                return PropertyCategory.Spatial;
            }
            // 2. 🎛️ 游戏UI与控制 (UI Control)
            else if (new[] { "StoryboardOpacity", "UiOpacity", "BackgroundDim", "ScanlineOpacity", "NoteOpacityMultiplier", "ScanlineColor", "NoteRingColor", "OverrideScanlinePos", "ScanlinePos", "NoteFillColors", "HitboxMultiplier", "HoldDirection", "Style", "NoteTarget", "NoteSizeMultiplier" }.Contains(n))
            {
                return PropertyCategory.UiControl;
            }
            // 3. ✨ 屏幕特效 (Effects) - 囊括所有高级滤镜属性！
            else if (new[] {
                "Chromatical", "ChromaticalFade", "ChromaticalIntensity", "ChromaticalSpeed",
                "Chromatic", "ChromaticIntensity", "ChromaticStart", "ChromaticEnd",
                "Artifact", "ArtifactIntensity", "ArtifactColorisation", "ArtifactParasite", "ArtifactNoise",
                "Bloom", "BloomIntensity", "RadialBlur", "RadialBlurIntensity",
                "ColorAdjustment", "Brightness", "Saturation", "Contrast",
                "ColorFilter", "ColorFilterColor", "GrayScale", "GrayScaleIntensity",
                "Noise", "NoiseIntensity", "Sepia", "SepiaIntensity", "Dream", "DreamIntensity",
                "Fisheye", "FisheyeIntensity", "Shockwave", "ShockwaveSpeed",
                "Focus", "FocusSize", "FocusColor", "FocusSpeed", "FocusIntensity",
                "Glitch", "GlitchIntensity", "Arcade", "ArcadeIntensity", "ArcadeInterferenceSize", "ArcadeInterferenceSpeed", "ArcadeContrast",
                "Tape", "Vignette", "VignetteColor", "VignetteEnd", "VignetteIntensity", "VignetteStart"
            }.Contains(n))
            {
                return PropertyCategory.Effects;
            }
            // 4. 🎨 外观与内容 (Appearance) - 兜底：颜色、文字、透明度、图层等
            else
            {
                return PropertyCategory.Appearance;
            }
        }
    }
}
