using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
        [JsonIgnore]
        public bool HasExplicitUnit { get; set; }
        // 配合你的 UnitFloatConverter 使用
    }

    // ==========================================
    // 🌟 一、 超级实体包装盒 (分离时空悖论的核心)
    // ==========================================
    public interface IExtensibleStoryboardNode
    {
        IDictionary<string, JToken> UnknownProperties { get; }
        IList<StoryboardDiagnostic> Diagnostics { get; }
    }

    public interface IStoryboardEntity : IExtensibleStoryboardNode
    {
        string Id { get; set; }
        string TargetId { get; set; }
        string ParentId { get; set; }
        bool IsIdSynthetic { get; set; }
        object GetBaseState();
        System.Collections.IList GetKeyframes();
    }

    public abstract class StoryboardEntity<TState> : IStoryboardEntity where TState : ObjectState, new()
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("target_id", NullValueHandling = NullValueHandling.Ignore)]
        public string TargetId { get; set; }
        [JsonProperty("parent_id")] public string ParentId { get; set; }
        [JsonIgnore] public bool IsIdSynthetic { get; set; }

        [JsonIgnore] public TState BaseState { get; set; } = new TState();
        [JsonIgnore] public List<TState> Keyframes { get; set; } = new List<TState>();

        [JsonExtensionData]
        public IDictionary<string, JToken> UnknownProperties { get; set; } =
            new Dictionary<string, JToken>(StringComparer.Ordinal);
        [JsonIgnore] public IList<StoryboardDiagnostic> Diagnostics { get; } = new List<StoryboardDiagnostic>();

        public object GetBaseState() => BaseState;
        public System.Collections.IList GetKeyframes() => Keyframes;
        public bool ShouldSerializeId()
        {
            // 1. 如果它是全局场景控制器或音符控制器，它是无名氏，绝对隐藏 ID！
            if (this.GetType().Name == "C2SceneController" || this.GetType().Name == "C2NoteController")
                return false;

            // 2. 如果它有 TargetId（说明它是吸附在别人身上的控制板），它也是无名氏，绝对隐藏 ID！
            if (!string.IsNullOrEmpty(TargetId))
                return false;

            // 3. 其他正常跳舞的 Sprite、Text 等，乖乖把 ID 打印进 JSON 里
            return true;
        }
    }




    // 具体的七大造物！
    public class C2Sprite : StoryboardEntity<SpriteState> { }
    public class C2Text : StoryboardEntity<TextState> { }
    public class C2Line : StoryboardEntity<LineState> { }
    public class C2Video : StoryboardEntity<VideoState> { }
    public class C2SceneController : StoryboardEntity<ControllerState>
    {
        [JsonIgnore]
        public string EditorMode { get; set; } = "Camera"; // 默认为核心相机模式
    }
    public class C2NoteController : StoryboardEntity<NoteControllerState> { }
    public class C2Template : StoryboardEntity<TemplateState> { }

    // ==========================================
    // 🌟 二、 故事板大本营 (Storyboard Root)
    // ==========================================
    public class StoryboardRoot : IExtensibleStoryboardNode
    {
        [JsonProperty("sprites")]
        public List<C2Sprite> sprites { get; set; } = new List<C2Sprite>();
        [JsonProperty("texts")]
        public List<C2Text> texts { get; set; } = new List<C2Text>();
        [JsonProperty("lines")]
        public List<C2Line> lines { get; set; } = new List<C2Line>();
        [JsonProperty("videos")]
        public List<C2Video> videos { get; set; } = new List<C2Video>();
        [JsonProperty("controllers")]
        public List<C2SceneController> controllers { get; set; } = new List<C2SceneController>();
        [JsonProperty("note_controllers")]
        public List<C2NoteController> note_controllers { get; set; } = new List<C2NoteController>();
        [JsonProperty("templates")]
        public Dictionary<string, C2Template> templates { get; set; } = new Dictionary<string, C2Template>();
        [JsonProperty("triggers")]
        public List<C2Trigger> triggers { get; set; } = new List<C2Trigger>();

        [JsonExtensionData]
        public IDictionary<string, JToken> UnknownProperties { get; set; } =
            new Dictionary<string, JToken>(StringComparer.Ordinal);
        [JsonIgnore] public IList<StoryboardDiagnostic> Diagnostics { get; } = new List<StoryboardDiagnostic>();
    }

    // ==========================================
    // 🌟 三、 官方属性全集 (States) - 一个不差！
    // ==========================================
    public abstract class ObjectState : IExtensibleStoryboardNode
    {
        [JsonProperty("time")] public object Time { get; set; } // 可以是数字也可以是 "intro:123"
        [JsonProperty("relative_time")] public float? RelativeTime { get; set; }
        [JsonProperty("add_time")] public float? AddTime { get; set; }
        [JsonProperty("easing")] public string Easing { get; set; } // EasingFunction.Ease
        [JsonProperty("destroy")] public bool? Destroy { get; set; }
        [JsonProperty("template")] public string Template { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> UnknownProperties { get; set; } =
            new Dictionary<string, JToken>(StringComparer.Ordinal);
        [JsonIgnore] public IList<StoryboardDiagnostic> Diagnostics { get; } = new List<StoryboardDiagnostic>();
    }

    public abstract class StageObjectState : ObjectState
    {
        [JsonProperty("x")] public UnitFloat X { get; set; }
        [JsonProperty("y")] public UnitFloat Y { get; set; }
        [JsonProperty("z")] public UnitFloat Z { get; set; }
        [JsonProperty("rot_x")] public float? RotX { get; set; }
        [JsonProperty("rot_y")] public float? RotY { get; set; }
        [JsonProperty("rot_z")] public float? RotZ { get; set; }
        [JsonProperty("scale_x")] public float? ScaleX { get; set; }
        [JsonProperty("scale_y")] public float? ScaleY { get; set; }
        [JsonProperty("scale")] public float? Scale { get; set; }
        [JsonProperty("pivot_x")] public float? PivotX { get; set; }
        [JsonProperty("pivot_y")] public float? PivotY { get; set; }
        [JsonProperty("opacity")] public float? Opacity { get; set; }
        [JsonProperty("layer")] public int? Layer { get; set; }
        [JsonProperty("order")] public int? Order { get; set; }
        [JsonProperty("fill_width")] public bool? FillWidth { get; set; }
        [JsonProperty("width")] public UnitFloat Width { get; set; }
        [JsonProperty("height")] public UnitFloat Height { get; set; }
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
        // 🌟 P0修复：官方规范 align 为字符串类型 ("upperLeft", "middleCenter" 等)
        [JsonProperty("align")] public string Align { get; set; }
        [JsonProperty("letter_spacing")] public float? LetterSpacing { get; set; }
        [JsonProperty("line_spacing")] public float? LineSpacing { get; set; }
        [JsonProperty("font")] public string Font { get; set; }
        [JsonProperty("font_style")] public int? FontStyle { get; set; }
        // 🌟 P0修复：补充官方规范的 font_weight 属性
        [JsonProperty("font_weight")] public string FontWeight { get; set; }
        [JsonProperty("color")] public string Color { get; set; }
    }

    // 🌟 1. 确保在 LineState 的上方，加上官方的端点位置类型！
    public class LinePosition : IExtensibleStoryboardNode
    {
        [JsonProperty("x")] public UnitFloat X { get; set; }
        [JsonProperty("y")] public UnitFloat Y { get; set; }
        [JsonProperty("z")] public UnitFloat Z { get; set; }
        [JsonExtensionData]
        public IDictionary<string, JToken> UnknownProperties { get; set; } =
            new Dictionary<string, JToken>(StringComparer.Ordinal);
        [JsonIgnore] public IList<StoryboardDiagnostic> Diagnostics { get; } = new List<StoryboardDiagnostic>();
    }

    // 🌟 2. 改造线条状态模型
    public class LineState : StageObjectState
    {
        [JsonProperty("pos")] public List<LinePosition> Pos { get; set; } = new List<LinePosition>();
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
        [JsonProperty("w")] public UnitFloat W { get; set; } // 🌟 修复类型为 UnitFloat
        [JsonProperty("h")] public UnitFloat H { get; set; }
        public float? PivotX { get; set; }
        public float? PivotY { get; set; }
        [JsonProperty("pos")] public List<LinePosition> Pos { get; set; }
        // 🌟 小艾补全：最致命的物理与视觉基础属性
        [JsonProperty("x")] public UnitFloat X { get; set; }
        [JsonProperty("y")] public UnitFloat Y { get; set; }
        [JsonProperty("z")] public UnitFloat Z { get; set; }
        [JsonProperty("rot_x")] public float? RotX { get; set; }
        [JsonProperty("rot_y")] public float? RotY { get; set; }
        [JsonProperty("rot_z")] public float? RotZ { get; set; }
        [JsonProperty("opacity")] public float? Opacity { get; set; }
        [JsonProperty("layer")] public int? Layer { get; set; }
        [JsonProperty("order")] public int? Order { get; set; }

        // 2. 文本与精灵 (Text & Sprite)
        [JsonProperty("text")] public string TextContent { get; set; } // 🌟 强制牵线别名
        [JsonProperty("size")] public float? Size { get; set; } // 🌟 修复为浮点型防崩溃
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
        [JsonProperty("scanline_smoothing")] public bool? ScanlineSmoothing { get; set; }
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
        [JsonProperty("chromatic")] public bool? Chromatic { get; set; }
        [JsonProperty("chromatic_intensity")] public float? ChromaticIntensity { get; set; }
        [JsonProperty("chromatic_start")] public float? ChromaticStart { get; set; }
        [JsonProperty("chromatic_end")] public float? ChromaticEnd { get; set; }
        [JsonProperty("artifact")] public bool? Artifact { get; set; }
        [JsonProperty("artifact_intensity")] public float? ArtifactIntensity { get; set; }
        [JsonProperty("artifact_colorisation")] public float? ArtifactColorisation { get; set; }
        [JsonProperty("artifact_parasite")] public float? ArtifactParasite { get; set; }
        [JsonProperty("artifact_noise")] public float? ArtifactNoise { get; set; }
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
        // 🌟 小艾补全：被腰斩的后半段高级滤镜特效
        [JsonProperty("color_adjustment")] public bool? ColorAdjustment { get; set; }
        [JsonProperty("brightness")] public float? Brightness { get; set; }
        [JsonProperty("saturation")] public float? Saturation { get; set; }
        [JsonProperty("contrast")] public float? Contrast { get; set; }
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
        [JsonProperty("note_fill_colors")] public List<string> NoteFillColors { get; set; } // 补上遗漏的音符色彩阵列

        // 6. 音符控制器 (Note Controller) - 全属性收录
        public bool? OverrideX { get; set; }
        public bool? OverrideY { get; set; }
        public bool? OverrideZ { get; set; }
        public bool? OverrideRotX { get; set; }
        public bool? OverrideRotY { get; set; }
        public bool? OverrideRotZ { get; set; }
        public float? XMultiplier { get; set; }
        public float? Dx { get; set; }
        public float? YMultiplier { get; set; }
        public float? Dy { get; set; }
        public bool? OverrideRingColor { get; set; }
        public string RingColor { get; set; }
        public bool? OverrideFillColor { get; set; }
        public string FillColor { get; set; }
        public int? HoldDirection { get; set; }
        public int? Style { get; set; }
        [JsonProperty("note")] public object NoteTarget { get; set; } // 🌟 强制牵线别名，并改为 object 兼容占位符
        public float? NoteSizeMultiplier { get; set; }
        public float? HitboxMultiplier { get; set; }
    }

    // 🌟 核心：音符控制器规格 (官方全属性收录)
    public class NoteControllerState : ObjectState
    {
        [JsonProperty("override_x")] public bool? OverrideX { get; set; }
        [JsonProperty("x")] public UnitFloat X { get; set; }
        [JsonProperty("x_multiplier")] public float? XMultiplier { get; set; }
        [JsonProperty("dx")] public float? Dx { get; set; } // X轴偏移量 (官方已知BUG: 扫线方向-1时需+1)
        [JsonProperty("override_y")] public bool? OverrideY { get; set; }
        [JsonProperty("y")] public UnitFloat Y { get; set; }
        [JsonProperty("y_multiplier")] public float? YMultiplier { get; set; }
        [JsonProperty("dy")] public float? Dy { get; set; } // Y轴偏移量
        [JsonProperty("override_z")] public bool? OverrideZ { get; set; }
        [JsonProperty("z")] public UnitFloat Z { get; set; }

        [JsonProperty("override_rot_x")] public bool? OverrideRotX { get; set; }
        [JsonProperty("rot_x")] public float? RotX { get; set; }
        [JsonProperty("override_rot_y")] public bool? OverrideRotY { get; set; }
        [JsonProperty("rot_y")] public float? RotY { get; set; }
        [JsonProperty("override_rot_z")] public bool? OverrideRotZ { get; set; }
        [JsonProperty("rot_z")] public float? RotZ { get; set; }

        [JsonProperty("override_ring_color")] public bool? OverrideRingColor { get; set; }
        [JsonProperty("ring_color")] public string RingColor { get; set; }
        [JsonProperty("override_fill_color")] public bool? OverrideFillColor { get; set; }
        [JsonProperty("fill_color")] public string FillColor { get; set; }

        // 🌟 P0修复：官方 NoteController 使用 opacity_multiplier / size_multiplier（无 note_ 前缀）
        // 与 SceneController 的 note_opacity_multiplier（全局）不同
        [JsonProperty("opacity_multiplier")] public float? NoteOpacityMultiplier { get; set; }
        [JsonProperty("size_multiplier")] public float? NoteSizeMultiplier { get; set; }
        [JsonProperty("hitbox_multiplier")] public float? HitboxMultiplier { get; set; }

        [JsonProperty("hold_direction")] public int? HoldDirection { get; set; }
        [JsonProperty("style")] public int? Style { get; set; }

        // Target (通常写在外面，但状态里有时用于动态绑定)
        [JsonProperty("note")] public object NoteTarget { get; set; }
    }

    // 🌟 核心：场景控制器规格 (约 40 个官方属性全收录)
    public class ControllerState : ObjectState
    {
        [JsonProperty("storyboard_opacity")] public float? StoryboardOpacity { get; set; }
        [JsonProperty("ui_opacity")] public float? UiOpacity { get; set; }
        [JsonProperty("scanline_opacity")] public float? ScanlineOpacity { get; set; }
        [JsonProperty("scanline_smoothing")] public bool? ScanlineSmoothing { get; set; }
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
        [JsonProperty("chromatic")] public bool? Chromatic { get; set; }
        [JsonProperty("chromatic_intensity")] public float? ChromaticIntensity { get; set; }
        [JsonProperty("chromatic_start")] public float? ChromaticStart { get; set; }
        [JsonProperty("chromatic_end")] public float? ChromaticEnd { get; set; }
        [JsonProperty("artifact")] public bool? Artifact { get; set; }
        [JsonProperty("artifact_intensity")] public float? ArtifactIntensity { get; set; }
        [JsonProperty("artifact_colorisation")] public float? ArtifactColorisation { get; set; }
        [JsonProperty("artifact_parasite")] public float? ArtifactParasite { get; set; }
        [JsonProperty("artifact_noise")] public float? ArtifactNoise { get; set; }

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
    // 🎯 核心：音符雷达选择器模型 (Note Selector)
    // ==========================================
    public class NoteSelectorModel : IExtensibleStoryboardNode
    {
        [JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
        public List<int> Type { get; set; } // 如果没值，序列化时不输出，等效于全选

        [JsonProperty("start", NullValueHandling = NullValueHandling.Ignore)]
        public int? Start { get; set; }

        [JsonProperty("end", NullValueHandling = NullValueHandling.Ignore)]
        public int? End { get; set; }

        [JsonProperty("direction", NullValueHandling = NullValueHandling.Ignore)]
        public int? Direction { get; set; }

        [JsonProperty("min_x", NullValueHandling = NullValueHandling.Ignore)]
        public float? MinX { get; set; }

        [JsonProperty("max_x", NullValueHandling = NullValueHandling.Ignore)]
        public float? MaxX { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> UnknownProperties { get; set; } =
            new Dictionary<string, JToken>(StringComparer.Ordinal);
        [JsonIgnore] public IList<StoryboardDiagnostic> Diagnostics { get; } = new List<StoryboardDiagnostic>();
    }

    public sealed class C2Trigger : IExtensibleStoryboardNode
    {
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("combo")] public int? Combo { get; set; }
        [JsonProperty("score")] public int? Score { get; set; }
        [JsonProperty("notes")] public List<int> Notes { get; set; } = new();
        [JsonProperty("spawn")] public List<string> Spawn { get; set; } = new();
        [JsonProperty("destroy")] public List<string> Destroy { get; set; } = new();
        [JsonProperty("uses")] public int? Uses { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> UnknownProperties { get; set; } =
            new Dictionary<string, JToken>(StringComparer.Ordinal);
        [JsonIgnore] public IList<StoryboardDiagnostic> Diagnostics { get; } = new List<StoryboardDiagnostic>();
    }








    }
