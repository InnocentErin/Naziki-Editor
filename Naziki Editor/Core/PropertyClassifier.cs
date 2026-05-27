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
            if (new[] { "X", "Y", "Z", "RotX", "RotY", "RotZ", "Scale", "ScaleX", "ScaleY", "Width", "Height", "W", "H", "PivotX", "PivotY", "X1", "X2", "Y1", "Y2", "OverrideX", "OverrideY", "OverrideZ", "OverrideRotX", "OverrideRotY", "OverrideRotZ" }.Contains(n))
            {
                return PropertyCategory.Spatial;
            }
            // 3. 🎛️ 游戏UI与控制 (UI Control)
            else if (new[] { "StoryboardOpacity", "UiOpacity", "BackgroundDim", "ScanlineOpacity", "NoteOpacityMultiplier", "ScanlineColor", "NoteRingColor", "OverrideScanlinePos", "ScanlinePos", "HitboxMultiplier", "HoldDirection", "Style", "NoteFillColors" }.Contains(n))
            {
                return PropertyCategory.UiControl;
            }
            // 4. 🎥 相机 (Camera)
            else if (new[] { "Perspective", "Fov" }.Contains(n))
            {
                return PropertyCategory.Camera;
            }
            // 5. ✨ 屏幕特效 (Effects)
            else if (n.Contains("Bloom") || n.Contains("Vignette") || n.Contains("Glitch") || n.Contains("Chromatic") || n.Contains("Blur") || n.Contains("Dream") || n.Contains("Noise") || n.Contains("Arcade") || n.Contains("Tape") || n.Contains("Fisheye") || n.Contains("Shockwave") || n.Contains("Focus") || n.Contains("Sepia") || n.Contains("GrayScale") || n.Contains("ColorFilter") || n.Contains("Artifact"))
            {
                return PropertyCategory.Effects;
            }
            // 2. 🎨 外观与内容 (Appearance) - 默认兜底（Template 属性也将自动归于此派）
            else
            {
                return PropertyCategory.Appearance;
            }
        }
    }
}