using System.Collections.Generic;

namespace Naziki_Editor.Core
{
    // ==========================================
    // 🏷️ 属性 UI 展现形态枚举 (决定属性编辑器的模样)
    // ==========================================
    public enum PropertyUIType
    {
        FloatBox,    // 🔢 普通数值框 (无严格限制，如 RotX, Size)
        UnitBox,     // 📏 带坐标系单位的数值 (如 X, Y, Width - 对应 UnitFloat)
        Slider,      // 🎚️ 滑动条 (严格限制 Min~Max，如 Opacity 0~1)
        IntBox,      // 📦 纯整数框 (如 Layer 0~2, Order)
        ColorPicker, // 🎨 十六进制颜色输入框 (如 Color, RingColor)
        Toggle,      // 🔘 布尔值开关 (如 Destroy, Perspective)
        ComboBox,    // 📋 枚举下拉菜单 (如 Align, Font)
        StringBox    // 📝 纯文本输入框 (如 TextContent, Path)
    }

    // ==========================================
    // 📏 单个属性的最高约束法则模具
    // ==========================================
    public class PropertyConstraint
    {
        public PropertyUIType UIType { get; set; } = PropertyUIType.FloatBox;
        public float Min { get; set; } = float.MinValue;
        public float Max { get; set; } = float.MaxValue;
        public object DefaultValue { get; set; } = null;
        public string[] Options { get; set; } = null; // 仅供 ComboBox 使用
    }

    // ==========================================
    // 🧠 全局参数宪法大管家：所有属性的合法范围都在这里登记！
    // ==========================================
    public static class PropertyConstraintManager
    {
        public static readonly Dictionary<string, PropertyConstraint> Rules = new Dictionary<string, PropertyConstraint>
        {
            // --- ⏱️ 1. 基础动画控制 ---
            { "Easing", new PropertyConstraint { UIType = PropertyUIType.StringBox, DefaultValue = "linear" } },
            { "Destroy", new PropertyConstraint { UIType = PropertyUIType.Toggle, DefaultValue = false } },

            // --- 📐 2. 空间与尺寸 (使用特制的 UnitBox 处理坐标系转换) ---
            { "X", new PropertyConstraint { UIType = PropertyUIType.UnitBox, DefaultValue = 0f } },
            { "Y", new PropertyConstraint { UIType = PropertyUIType.UnitBox, DefaultValue = 0f } },
            { "Z", new PropertyConstraint { UIType = PropertyUIType.UnitBox, DefaultValue = 0f } },
            { "Width", new PropertyConstraint { UIType = PropertyUIType.UnitBox } },
            { "Height", new PropertyConstraint { UIType = PropertyUIType.UnitBox } },
            { "ScanlinePos", new PropertyConstraint { UIType = PropertyUIType.UnitBox, DefaultValue = 0f } },
            
            // --- 🔄 3. 常规数值与缩放 ---
            { "RotX", new PropertyConstraint { UIType = PropertyUIType.FloatBox, DefaultValue = 0f } },
            { "RotY", new PropertyConstraint { UIType = PropertyUIType.FloatBox, DefaultValue = 0f } },
            { "RotZ", new PropertyConstraint { UIType = PropertyUIType.FloatBox, DefaultValue = 0f } },
            { "Scale", new PropertyConstraint { UIType = PropertyUIType.FloatBox, DefaultValue = 1f } },
            { "ScaleX", new PropertyConstraint { UIType = PropertyUIType.FloatBox, DefaultValue = 1f } },
            { "ScaleY", new PropertyConstraint { UIType = PropertyUIType.FloatBox, DefaultValue = 1f } },
            
            // --- 🎚️ 4. 0~1 的滑动条特供区 (透明度与中心点) ---
            { "Opacity", new PropertyConstraint { UIType = PropertyUIType.Slider, Min = 0f, Max = 1f, DefaultValue = 0f } },
            { "StoryboardOpacity", new PropertyConstraint { UIType = PropertyUIType.Slider, Min = 0f, Max = 1f, DefaultValue = 1f } },
            { "UiOpacity", new PropertyConstraint { UIType = PropertyUIType.Slider, Min = 0f, Max = 1f, DefaultValue = 1f } },
            { "BackgroundDim", new PropertyConstraint { UIType = PropertyUIType.Slider, Min = 0f, Max = 1f, DefaultValue = 0.85f } },
            { "PivotX", new PropertyConstraint { UIType = PropertyUIType.Slider, Min = 0f, Max = 1f, DefaultValue = 0.5f } },
            { "PivotY", new PropertyConstraint { UIType = PropertyUIType.Slider, Min = 0f, Max = 1f, DefaultValue = 0.5f } },

            // --- 📦 5. 整数类型 (层级与大小) ---
            { "Layer", new PropertyConstraint { UIType = PropertyUIType.IntBox, Min = 0, Max = 2, DefaultValue = 0 } },
            { "Order", new PropertyConstraint { UIType = PropertyUIType.IntBox, DefaultValue = 0 } },
            { "Size", new PropertyConstraint { UIType = PropertyUIType.IntBox, Min = 1, DefaultValue = 20 } },

            // --- 🎨 6. 颜色与文本 ---
            { "Color", new PropertyConstraint { UIType = PropertyUIType.ColorPicker, DefaultValue = "#FFFFFF" } },
            { "ScanlineColor", new PropertyConstraint { UIType = PropertyUIType.ColorPicker } },
            { "NoteRingColor", new PropertyConstraint { UIType = PropertyUIType.ColorPicker } },
            { "NoteFillColors", new PropertyConstraint { UIType = PropertyUIType.FloatBox } },
            { "Path", new PropertyConstraint { UIType = PropertyUIType.StringBox } },
            { "TextContent", new PropertyConstraint { UIType = PropertyUIType.StringBox } },
            { "Note", new PropertyConstraint { UIType = PropertyUIType.StringBox } },

            // --- 📋 7. 枚举限定词 (下拉菜单) ---
            { "Align", new PropertyConstraint { UIType = PropertyUIType.ComboBox, Options = new[] { "upperLeft", "upperCenter", "upperRight", "middleLeft", "middleCenter", "middleRight", "lowerLeft", "lowerCenter", "lowerRight" }, DefaultValue = "middleCenter" } },
            { "FontWeight", new PropertyConstraint { UIType = PropertyUIType.ComboBox, Options = new[] { "extraLight", "regular", "bold", "extraBold" }, DefaultValue = "regular" } },

            // --- ✨ 8. 特殊滤镜强度滑动条 ---
            { "BloomIntensity", new PropertyConstraint { UIType = PropertyUIType.Slider, Min = 0f, Max = 5f, DefaultValue = 1f } },
            { "RadialBlurIntensity", new PropertyConstraint { UIType = PropertyUIType.Slider, Min = -0.5f, Max = 0.5f, DefaultValue = 0.025f } },
            { "ChromaticalIntensity", new PropertyConstraint { UIType = PropertyUIType.Slider, Min = 0f, Max = 1f, DefaultValue = 0f } },
            { "NoiseIntensity", new PropertyConstraint { UIType = PropertyUIType.Slider, Min = 0f, Max = 1f, DefaultValue = 0.235f } },
            { "FisheyeIntensity", new PropertyConstraint { UIType = PropertyUIType.Slider, Min = 0f, Max = 1f, DefaultValue = 0.5f } },

            // --- 🔘 9. 全局开关 (布尔值) ---
            { "Perspective", new PropertyConstraint { UIType = PropertyUIType.Toggle, DefaultValue = true } },
            { "PreserveAspect", new PropertyConstraint { UIType = PropertyUIType.Toggle, DefaultValue = true } },
            { "OverrideX", new PropertyConstraint { UIType = PropertyUIType.Toggle, DefaultValue = false } },
            // (注：由于滤镜开关和 Override 系列太多，可以直接在获取时动态判断，或者在此处补全)
        };

        // ==========================================
        // 🔍 快捷查询法术：UI 生成前，先来问问大管家！
        // ==========================================
        public static PropertyConstraint GetConstraint(string propName)
        {
            if (Rules.TryGetValue(propName, out var rule))
                return rule;

            // 🌟 1. 绝对精准匹配：强迫症级别的颜色探针
            if (propName == "Color" || propName == "ScanlineColor" || propName == "NoteRingColor" ||
                propName == "ColorFilterColor" || propName == "FocusColor" || propName == "VignetteColor")
                return new PropertyConstraint { UIType = PropertyUIType.ColorPicker, DefaultValue = "#FFFFFF" };

            // 🌟 2. 绝对精准匹配：强迫症级别的文本探针
            if (propName == "TextContent" || propName == "Path" || propName == "NoteTarget" || propName == "Font" || propName == "FontStyle")
                return new PropertyConstraint { UIType = PropertyUIType.StringBox, DefaultValue = "" };

            // 🌟 3. 先拦截所有滑块类 (包含 Intensity, Opacity, Fade)
            // 必须放在 Bool 拦截之前，否则 BloomIntensity 会被后面的 Bloom 拦截当成开关！
            if (propName.Contains("Intensity") || propName.Contains("Opacity") || propName.Contains("Fade") || propName == "BackgroundDim")
                return new PropertyConstraint { UIType = PropertyUIType.Slider, Min = 0f, Max = 1f, DefaultValue = 0.5f };

            // 🌟 4. 再拦截所有开关类 (绝对不能使用含糊的 Contains 匹配容易误伤，改为后缀检查或精准列举)
            if (propName.StartsWith("Override") || propName == "Loop" || propName == "PreserveAspect" || propName == "Perspective" ||
                propName == "Chromatical" || propName == "Bloom" || propName == "RadialBlur" || propName == "ColorAdjustment" ||
                propName == "ColorFilter" || propName == "GrayScale" || propName == "Noise" || propName == "Sepia" ||
                propName == "Dream" || propName == "Fisheye" || propName == "Shockwave" || propName == "Focus" ||
                propName == "Glitch" || propName == "Arcade" || propName == "Tape" || propName == "Vignette")
                return new PropertyConstraint { UIType = PropertyUIType.Toggle, DefaultValue = false };

            // 🌟 5. 其他浮点数 (如 Fov, Speed, X, Y)
            return new PropertyConstraint { UIType = PropertyUIType.FloatBox, DefaultValue = 0f };
        }
    }
}