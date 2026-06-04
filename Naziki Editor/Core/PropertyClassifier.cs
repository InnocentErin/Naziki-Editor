using System;
using System.Linq;

namespace Naziki_Editor.Core
{
    // 🌟 统一的五大核心属性派别枚举
    public enum PropertyCategory
    {
        Spatial,      // 📏 空间与尺寸
        Appearance,   // 🎨 外观与内容
        UiControl,    // 🎛️ 游戏UI与控制
        Camera,       // 🎥 相机
        Effects       // ✨ 屏幕特效
    }

    public static class PropertyClassifier
    {
        public static PropertyCategory GetCategory(string propertyName)
        {
            string n = propertyName;

            // 1. 📏 空间与尺寸 (Spatial)
            if (new[] { "X", "Y", "Z", "RotX", "RotY", "RotZ", "Scale", "ScaleX", "ScaleY", "Width", "Height", "W", "H", "PivotX", "PivotY", "X1", "X2", "Y1", "Y2", "OverrideX", "OverrideY", "OverrideZ", "OverrideRotX", "OverrideRotY", "OverrideRotZ", "Pos" }.Contains(n))
            {
                return PropertyCategory.Spatial;
            }
            // 2. 🎥 相机 (Camera)
            else if (new[] { "Perspective", "Fov" }.Contains(n))
            {
                return PropertyCategory.Camera;
            }
            // 3. 🎛️ 游戏UI与控制 (UI Control)
            else if (new[] { "StoryboardOpacity", "UiOpacity", "BackgroundDim", "ScanlineOpacity", "NoteOpacityMultiplier", "ScanlineColor", "NoteRingColor", "OverrideScanlinePos", "ScanlinePos", "NoteFillColors", "HitboxMultiplier", "HoldDirection", "Style", "NoteTarget", "NoteSizeMultiplier" }.Contains(n))
            {
                return PropertyCategory.UiControl;
            }
            // 4. ✨ 屏幕特效 (Effects) - 囊括所有 20+ 高级滤镜属性！
            else if (new[] {
                "Chromatical", "ChromaticalFade", "ChromaticalIntensity", "ChromaticalSpeed",
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
            // 5. 🎨 外观与内容 (Appearance) - 兜底：颜色、文字、透明度、图层等
            else
            {
                return PropertyCategory.Appearance;
            }
        }
    }
}