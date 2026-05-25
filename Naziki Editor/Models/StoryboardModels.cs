using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Naziki_Editor.Models
{
    // ==========================================
    // 🌟 核心基建：带有单位的浮点数与坐标参考系
    // ==========================================
    public enum ReferenceUnit { World, NoteX, NoteY, StageX, StageY, CameraX, CameraY }

    public class UnitFloat
    {
        public float Value { get; set; }
        public ReferenceUnit Unit { get; set; } = ReferenceUnit.World;
        // 配合你的 UnitFloatConverter 使用
    }

    // ==========================================
    // 🌟 一、 超级实体包装盒 (分离时空悖论的核心)
    // ==========================================
    public interface IStoryboardEntity
    {
        string Id { get; set; }
        string ParentId { get; set; }
        object GetBaseState();
        System.Collections.IList GetKeyframes();
    }

    public abstract class StoryboardEntity<TState> : IStoryboardEntity where TState : ObjectState, new()
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("parent_id")] public string ParentId { get; set; }

        [JsonIgnore] public TState BaseState { get; set; } = new TState();
        [JsonIgnore] public List<TState> Keyframes { get; set; } = new List<TState>();

        public object GetBaseState() => BaseState;
        public System.Collections.IList GetKeyframes() => Keyframes;
    }

    // 具体的七大造物！
    public class C2Sprite : StoryboardEntity<SpriteState> { }
    public class C2Text : StoryboardEntity<TextState> { }
    public class C2Line : StoryboardEntity<LineState> { }
    public class C2Video : StoryboardEntity<VideoState> { }
    public class C2SceneController : StoryboardEntity<ControllerState> { }
    public class C2NoteController : StoryboardEntity<NoteControllerState> { }
    public class C2Template : StoryboardEntity<TemplateState> { }

    // ==========================================
    // 🌟 二、 故事板大本营 (Storyboard Root)
    // ==========================================
    public class StoryboardRoot
    {
        public List<C2Sprite> sprites { get; set; } = new List<C2Sprite>();
        public List<C2Text> texts { get; set; } = new List<C2Text>();
        public List<C2Line> lines { get; set; } = new List<C2Line>();
        public List<C2Video> videos { get; set; } = new List<C2Video>();
        public List<C2SceneController> controllers { get; set; } = new List<C2SceneController>();
        public List<C2NoteController> note_controllers { get; set; } = new List<C2NoteController>();
        public Dictionary<string, C2Template> templates { get; set; } = new Dictionary<string, C2Template>();
    }

    // ==========================================
    // 🌟 三、 官方属性全集 (States) - 一个不差！
    // ==========================================
    public abstract class ObjectState
    {
        [JsonProperty("time")] public object Time { get; set; } // 可以是数字也可以是 "intro:123"
        [JsonProperty("relative_time")] public float? RelativeTime { get; set; }
        [JsonProperty("add_time")] public float? AddTime { get; set; }
        [JsonProperty("easing")] public string Easing { get; set; } // EasingFunction.Ease
        [JsonProperty("destroy")] public bool? Destroy { get; set; }
        [JsonProperty("template")] public string Template { get; set; }
    }

    public abstract class StageObjectState : ObjectState
    {
        [JsonProperty("x")] public UnitFloat X { get; set; }
        [JsonProperty("y")] public UnitFloat Y { get; set; }
        [JsonProperty("z")] public UnitFloat Z { get; set; }
        [JsonProperty("rot_x")] public float? RotX { get; set; }
        [JsonProperty("rot_y")] public float? RotY { get; set; }
        [JsonProperty("rot_z")] public float? RotZ { get; set; }
        [JsonProperty("opacity")] public float? Opacity { get; set; }
        [JsonProperty("layer")] public int? Layer { get; set; }
        [JsonProperty("order")] public int? Order { get; set; }
    }

    public class SpriteState : StageObjectState
    {
        [JsonProperty("path")] public string Path { get; set; }
        [JsonProperty("w")] public UnitFloat W { get; set; }
        [JsonProperty("h")] public UnitFloat H { get; set; }
        [JsonProperty("preserve_aspect")] public bool? PreserveAspect { get; set; }
        [JsonProperty("color")] public string Color { get; set; } // Hex string
    }

    public class TextState : StageObjectState
    {
        [JsonProperty("text")] public string TextContent { get; set; }
        [JsonProperty("size")] public float? Size { get; set; }
        [JsonProperty("align")] public int? Align { get; set; }
        [JsonProperty("letter_spacing")] public float? LetterSpacing { get; set; }
        [JsonProperty("line_spacing")] public float? LineSpacing { get; set; }
        [JsonProperty("font")] public string Font { get; set; }
        [JsonProperty("font_style")] public int? FontStyle { get; set; }
        [JsonProperty("color")] public string Color { get; set; }
    }

    // 🌟 1. 确保在 LineState 的上方，加上官方的端点位置类型！
    public class LinePosition
    {
        [JsonProperty("x")] public UnitFloat X { get; set; }
        [JsonProperty("y")] public UnitFloat Y { get; set; }
        [JsonProperty("z")] public UnitFloat Z { get; set; }
    }

    // 🌟 2. 改造线条状态模型
    public class LineState : StageObjectState
    {
        [JsonProperty("pos")] public List<LinePosition> Pos { get; set; } = new List<LinePosition>();
        [JsonProperty("width")] public float? Width { get; set; }
        [JsonProperty("color")] public string Color { get; set; }
    }

    public class VideoState : StageObjectState
    {
        [JsonProperty("path")] public string Path { get; set; }
        [JsonProperty("w")] public UnitFloat W { get; set; }
        [JsonProperty("h")] public UnitFloat H { get; set; }
        [JsonProperty("preserve_aspect")] public bool? PreserveAspect { get; set; }
        [JsonProperty("loop")] public bool? Loop { get; set; }
        [JsonProperty("speed")] public float? Speed { get; set; }
    }

    // ==========================================\
    // 🌟 TemplateState 终极进化：囊括宇宙万物属性！
    // ==========================================\
    public class TemplateState : ObjectState
    {
        // 1. 场景/尺寸属性 (继承的基础 XYZ 之外)
        public float? Scale { get; set; }
        public float? ScaleX { get; set; }
        public float? ScaleY { get; set; }
        public UnitFloat Width { get; set; }
        public UnitFloat Height { get; set; }
        public float? W { get; set; } // 兼容你提到的 w, h
        public float? H { get; set; }
        public float? PivotX { get; set; }
        public float? PivotY { get; set; }
        [JsonProperty("pos")] public List<LinePosition> Pos { get; set; }

        // 2. 文本与精灵 (Text & Sprite)
        public string TextContent { get; set; }
        public int? Size { get; set; }
        public string Align { get; set; }
        public float? LetterSpacing { get; set; }
        public float? LineSpacing { get; set; }
        public string Font { get; set; }
        public string FontStyle { get; set; }
        public string Path { get; set; }
        public string Color { get; set; }
        public bool? PreserveAspect { get; set; }
        public bool? Loop { get; set; }
        public float? Speed { get; set; }

        // 3. 线条 (Line)


        // 4. 游戏UI与相机控制 (Controller)
        public float? StoryboardOpacity { get; set; }
        public float? UiOpacity { get; set; }
        public float? ScanlineOpacity { get; set; }
        public float? BackgroundDim { get; set; }
        public float? NoteOpacityMultiplier { get; set; }
        public string ScanlineColor { get; set; }
        public string NoteRingColor { get; set; }
        public bool? OverrideScanlinePos { get; set; }
        public UnitFloat ScanlinePos { get; set; }
        public bool? Perspective { get; set; }
        public float? Fov { get; set; }

        // 5. 屏幕滤镜特效 (Effects - 这里列出你提到的核心词缀)
        public bool? Chromatical { get; set; }
        public float? ChromaticalFade { get; set; }
        public float? ChromaticalIntensity { get; set; }
        public float? ChromaticalSpeed { get; set; }
        public bool? Bloom { get; set; }
        public float? BloomIntensity { get; set; }
        public bool? RadialBlur { get; set; }
        public float? RadialBlurIntensity { get; set; }
        public bool? ColorFilter { get; set; }
        public string ColorFilterColor { get; set; }
        public bool? GrayScale { get; set; }
        public float? GrayScaleIntensity { get; set; }
        public bool? Noise { get; set; }
        public float? NoiseIntensity { get; set; }
        public bool? Sepia { get; set; }
        public float? SepiaIntensity { get; set; }
        public bool? Dream { get; set; }
        public float? DreamIntensity { get; set; }

        // 6. 音符控制器 (Note Controller)
        public bool? OverrideX { get; set; }
        public bool? OverrideY { get; set; }
        public bool? OverrideZ { get; set; }
        public bool? OverrideRotX { get; set; }
        public bool? OverrideRotY { get; set; }
        public bool? OverrideRotZ { get; set; }
        public string NoteTarget { get; set; }
        public float? NoteSizeMultiplier { get; set; }
        public float? HitboxMultiplier { get; set; }
    }

    // 🌟 核心：音符控制器规格
    public class NoteControllerState : ObjectState
    {
        [JsonProperty("override_x")] public bool? OverrideX { get; set; }
        [JsonProperty("x")] public UnitFloat X { get; set; }
        [JsonProperty("override_y")] public bool? OverrideY { get; set; }
        [JsonProperty("y")] public UnitFloat Y { get; set; }
        [JsonProperty("override_z")] public bool? OverrideZ { get; set; }
        [JsonProperty("z")] public UnitFloat Z { get; set; }

        [JsonProperty("override_rot_x")] public bool? OverrideRotX { get; set; }
        [JsonProperty("rot_x")] public float? RotX { get; set; }
        [JsonProperty("override_rot_y")] public bool? OverrideRotY { get; set; }
        [JsonProperty("rot_y")] public float? RotY { get; set; }
        [JsonProperty("override_rot_z")] public bool? OverrideRotZ { get; set; }
        [JsonProperty("rot_z")] public float? RotZ { get; set; }

        [JsonProperty("note_opacity_multiplier")] public float? NoteOpacityMultiplier { get; set; }
        [JsonProperty("note_size_multiplier")] public float? NoteSizeMultiplier { get; set; }

        // Target (通常写在外面，但状态里有时用于动态绑定)
        [JsonProperty("note")] public object NoteTarget { get; set; }
    }

    // 🌟 核心：场景控制器规格 (约 40 个官方属性全收录)
    public class ControllerState : ObjectState
    {
        [JsonProperty("storyboard_opacity")] public float? StoryboardOpacity { get; set; }
        [JsonProperty("ui_opacity")] public float? UiOpacity { get; set; }
        [JsonProperty("scanline_opacity")] public float? ScanlineOpacity { get; set; }
        [JsonProperty("background_dim")] public float? BackgroundDim { get; set; }
        [JsonProperty("note_opacity_multiplier")] public float? NoteOpacityMultiplier { get; set; }

        [JsonProperty("scanline_color")] public string ScanlineColor { get; set; }
        [JsonProperty("note_ring_color")] public string NoteRingColor { get; set; }
        [JsonProperty("note_fill_colors")] public List<string> NoteFillColors { get; set; } // 12色阵列

        [JsonProperty("override_scanline_pos")] public bool? OverrideScanlinePos { get; set; }
        [JsonProperty("scanline_pos")] public UnitFloat ScanlinePos { get; set; }

        [JsonProperty("perspective")] public bool? Perspective { get; set; }
        [JsonProperty("size")] public float? Size { get; set; }
        [JsonProperty("fov")] public float? Fov { get; set; }
        [JsonProperty("x")] public UnitFloat X { get; set; }
        [JsonProperty("y")] public UnitFloat Y { get; set; }
        [JsonProperty("z")] public UnitFloat Z { get; set; }
        [JsonProperty("rot_x")] public float? RotX { get; set; }
        [JsonProperty("rot_y")] public float? RotY { get; set; }
        [JsonProperty("rot_z")] public float? RotZ { get; set; }

        [JsonProperty("chromatical")] public bool? Chromatical { get; set; }
        [JsonProperty("chromatical_fade")] public float? ChromaticalFade { get; set; }
        [JsonProperty("chromatical_intensity")] public float? ChromaticalIntensity { get; set; }
        [JsonProperty("chromatical_speed")] public float? ChromaticalSpeed { get; set; }

        [JsonProperty("bloom")] public bool? Bloom { get; set; }
        [JsonProperty("bloom_intensity")] public float? BloomIntensity { get; set; }

        [JsonProperty("radial_blur")] public bool? RadialBlur { get; set; }
        [JsonProperty("radial_blur_intensity")] public float? RadialBlurIntensity { get; set; }

        [JsonProperty("color_adjustment")] public bool? ColorAdjustment { get; set; }
        [JsonProperty("brightness")] public float? Brightness { get; set; }
        [JsonProperty("saturation")] public float? Saturation { get; set; }
        [JsonProperty("contrast")] public float? Contrast { get; set; }

        [JsonProperty("color_filter")] public bool? ColorFilter { get; set; }
        [JsonProperty("color_filter_color")] public string ColorFilterColor { get; set; }

        [JsonProperty("gray_scale")] public bool? GrayScale { get; set; }
        [JsonProperty("gray_scale_intensity")] public float? GrayScaleIntensity { get; set; }

        [JsonProperty("noise")] public bool? Noise { get; set; }
        [JsonProperty("noise_intensity")] public float? NoiseIntensity { get; set; }

        [JsonProperty("sepia")] public bool? Sepia { get; set; }
        [JsonProperty("sepia_intensity")] public float? SepiaIntensity { get; set; }

        [JsonProperty("dream")] public bool? Dream { get; set; }
        [JsonProperty("dream_intensity")] public float? DreamIntensity { get; set; }

        [JsonProperty("fisheye")] public bool? Fisheye { get; set; }
        [JsonProperty("fisheye_intensity")] public float? FisheyeIntensity { get; set; }

        [JsonProperty("shockwave")] public bool? Shockwave { get; set; }
        [JsonProperty("shockwave_speed")] public float? ShockwaveSpeed { get; set; }

        [JsonProperty("focus")] public bool? Focus { get; set; }
        [JsonProperty("focus_size")] public float? FocusSize { get; set; }
        [JsonProperty("focus_color")] public string FocusColor { get; set; }
        [JsonProperty("focus_speed")] public float? FocusSpeed { get; set; }
        [JsonProperty("focus_intensity")] public float? FocusIntensity { get; set; }

        [JsonProperty("glitch")] public bool? Glitch { get; set; }
        [JsonProperty("glitch_intensity")] public float? GlitchIntensity { get; set; }

        [JsonProperty("arcade")] public bool? Arcade { get; set; }
        [JsonProperty("arcade_intensity")] public float? ArcadeIntensity { get; set; }
        [JsonProperty("arcade_interference_size")] public float? ArcadeInterferenceSize { get; set; }
        [JsonProperty("arcade_interference_speed")] public float? ArcadeInterferenceSpeed { get; set; }
        [JsonProperty("arcade_contrast")] public float? ArcadeContrast { get; set; }

        [JsonProperty("tape")] public bool? Tape { get; set; }

        [JsonProperty("vignette")] public bool? Vignette { get; set; }
        [JsonProperty("vignette_color")] public string VignetteColor { get; set; }
        [JsonProperty("vignette_end")] public float? VignetteEnd { get; set; }
        [JsonProperty("vignette_intensity")] public float? VignetteIntensity { get; set; }
        [JsonProperty("vignette_start")] public float? VignetteStart { get; set; }




    }
    // ==========================================
    // 🌟 完美归位的 UnitFloatConverter 专属车间
    // ==========================================
    public class UnitFloatConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(UnitFloat);

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var uf = (UnitFloat)value;
            if (uf == null) { writer.WriteNull(); return; }

            // 如果是默认的 World 参考系（或者纯数字），直接输出数字
            if (uf.Unit == ReferenceUnit.World)
                writer.WriteValue(uf.Value);
            else
                writer.WriteValue($"{uf.Value}{uf.Unit.ToString().ToLower()}");
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;

            var uf = new UnitFloat { Value = 0, Unit = ReferenceUnit.World };

            // 🟢 兼容读取：万一读到的是纯数字
            if (reader.TokenType == JsonToken.Integer || reader.TokenType == JsonToken.Float)
            {
                uf.Value = Convert.ToSingle(reader.Value);
                return uf;
            }

            // 🔵 兼容读取：读到的是带单位的字符串（如 "0.5notex"）
            if (reader.TokenType == JsonToken.String)
            {
                string raw = reader.Value.ToString().Trim().ToLower();
                // 简单的正规化截取
                if (raw.EndsWith("notex")) { uf.Unit = ReferenceUnit.NoteX; raw = raw.Replace("notex", ""); }
                else if (raw.EndsWith("notey")) { uf.Unit = ReferenceUnit.NoteY; raw = raw.Replace("notey", ""); }
                else if (raw.EndsWith("stagex")) { uf.Unit = ReferenceUnit.StageX; raw = raw.Replace("stagex", ""); }
                else if (raw.EndsWith("stagey")) { uf.Unit = ReferenceUnit.StageY; raw = raw.Replace("stagey", ""); }
                else if (raw.EndsWith("camerax")) { uf.Unit = ReferenceUnit.CameraX; raw = raw.Replace("camerax", ""); }
                else if (raw.EndsWith("cameray")) { uf.Unit = ReferenceUnit.CameraY; raw = raw.Replace("cameray", ""); }

                if (float.TryParse(raw, out float v)) uf.Value = v;
                return uf;
            }

            return uf;
        }
    }
}