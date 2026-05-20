using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace Naziki_Editor.Models
{
    // ==========================================
    // 🌟 一、 缓动函数枚举 (完全匹配 Cytoid 官方)
    // ==========================================
    public enum Ease
    {
        None = 0,
        EaseInQuad = 1,
        EaseOutQuad = 2,
        EaseInOutQuad = 3,
        EaseInCubic = 4,
        EaseOutCubic = 5,
        EaseInOutCubic = 6,
        EaseInQuart = 7,
        EaseOutQuart = 8,
        EaseInOutQuart = 9,
        EaseInQuint = 10,
        EaseOutQuint = 11,
        EaseInOutQuint = 12,
        EaseInSine = 13,
        EaseOutSine = 14,
        EaseInOutSine = 15,
        EaseInExpo = 16,
        EaseOutExpo = 17,
        EaseInOutExpo = 18,
        EaseInCirc = 19,
        EaseOutCirc = 20,
        EaseInOutCirc = 21,
        Linear = 22,
        Spring = 23,
        EaseInBounce = 24,
        EaseOutBounce = 25,
        EaseInOutBounce = 26,
        EaseInBack = 27,
        EaseOutBack = 28,
        EaseInOutBack = 29,
        EaseInElastic = 30,
        EaseOutElastic = 31,
        EaseInOutElastic = 32,
        Blink = 33
    }

    // ==========================================
    // 🌟 二、 参考系枚举
    // ==========================================
    public enum ReferenceUnit
    {
        World,
        StageX, StageY,   // 800x600 画布
        NoteX, NoteY,     // 音符坐标系 (0~1)
        CameraX, CameraY   // 正交相机坐标系
    }

    // ==========================================
    // 🌟 三、 自定义颜色 (RGBA，范围 0~255，Alpha 0~1)
    // ==========================================
    [Serializable]
    public class CytoidColor
    {
        public float A = 1f;
        public float R = 255f;
        public float G = 255f;
        public float B = 255f;

        public CytoidColor() { }
        public CytoidColor(float r, float g, float b, float a = 1f)
        {
            R = r; G = g; B = b; A = a;
        }
    }

    // ==========================================
    // 🌟 四、 带参考系的数值 (支持 "noteX:0.5" 或纯数字)
    // ==========================================
    [JsonConverter(typeof(UnitFloatConverter))]
    public class UnitFloat
    {
        public float Value { get; set; }
        public ReferenceUnit Unit { get; set; }
        public bool ScaleToCanvas { get; set; } = false;
        public bool Span { get; set; } = false;

        public UnitFloat() { }
        public UnitFloat(float value, ReferenceUnit unit, bool scaleToCanvas = false, bool span = false)
        {
            Value = value;
            Unit = unit;
            ScaleToCanvas = scaleToCanvas;
            Span = span;
        }

        public UnitFloat WithValue(float newValue) =>
            new UnitFloat(newValue, Unit, ScaleToCanvas, Span);
    }

    // ==========================================
    // 🌟 五、 高级时间转换器 (支持数字/字符串/数组)
    // ==========================================
    public class TimeObjectConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => true;
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            JToken token = JToken.Load(reader);
            if (token.Type == JTokenType.Array)
            {
                var list = new List<object>();
                foreach (var item in token)
                {
                    if (item.Type == JTokenType.Float || item.Type == JTokenType.Integer)
                        list.Add((float)item);
                    else
                        list.Add(item.ToString());
                }
                return list;
            }
            else if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
                return (float)token;
            else
                return token.ToString();
        }
        public override bool CanWrite => false;
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) =>
            throw new NotImplementedException();
    }

    // ==========================================
    // 🌟 六、 UnitFloat 专用转换器 (纯数字/字符串互转)
    // ==========================================
    public class UnitFloatConverter : JsonConverter<UnitFloat>
    {
        public override UnitFloat ReadJson(JsonReader reader, Type objectType, UnitFloat existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            JToken token = JToken.Load(reader);
            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
            {
                return new UnitFloat { Value = (float)token, Unit = ReferenceUnit.World };
            }
            else if (token.Type == JTokenType.String)
            {
                string str = token.Value<string>();
                var parts = str.Split(':');
                if (parts.Length == 2 && Enum.TryParse(parts[0], true, out ReferenceUnit unit))
                {
                    return new UnitFloat { Value = float.Parse(parts[1]), Unit = unit };
                }
                else if (parts.Length == 1 && float.TryParse(parts[0], out float val))
                {
                    return new UnitFloat { Value = val, Unit = ReferenceUnit.World };
                }
            }
            else if (token.Type == JTokenType.Object)
            {
                var obj = token as JObject;
                var uf = new UnitFloat();
                if (obj["Value"] != null) uf.Value = (float)obj["Value"];
                if (obj["Unit"] != null && Enum.TryParse(obj["Unit"].ToString(), true, out ReferenceUnit u)) uf.Unit = u;
                if (obj["ScaleToCanvas"] != null) uf.ScaleToCanvas = (bool)obj["ScaleToCanvas"];
                if (obj["Span"] != null) uf.Span = (bool)obj["Span"];
                return uf;
            }
            return new UnitFloat();
        }

        public override void WriteJson(JsonWriter writer, UnitFloat value, JsonSerializer serializer)
        {
            if (value.Unit == ReferenceUnit.World)
                writer.WriteValue(value.Value);
            else
            {
                string unitStr = char.ToLower(value.Unit.ToString()[0]) + value.Unit.ToString().Substring(1);
                writer.WriteValue($"{unitStr}:{value.Value}");
            }
        }
    }

    // ==========================================
    // 🌟 七、 音符选择器 (用于批量绑定)
    // ==========================================
    public class NoteSelector
    {
        public HashSet<int> Types { get; set; } = new HashSet<int>();
        public int Start { get; set; } = int.MinValue;
        public int End { get; set; } = int.MaxValue;
        public int? Direction { get; set; }
        public float MinX { get; set; } = int.MinValue;
        public float MaxX { get; set; } = int.MaxValue;

        public string DisplayName => $"选择器: {(Types.Count > 0 ? $"Type={string.Join(",", Types)}" : "所有类型")} ({Start}~{End})";
    }

    // ==========================================
    // 🌟 八、 线条端点坐标
    // ==========================================
    public class LinePosition
    {
        public UnitFloat X { get; set; }
        public UnitFloat Y { get; set; }
        public UnitFloat Z { get; set; }
    }

    // ==========================================
    // 🌟 九、 触发器 (暂未使用，完整保留)
    // ==========================================
    public enum TriggerType { NoteClear, Combo, Score, None }

    public class Trigger
    {
        public int? Combo { get; set; }
        [JsonIgnore] public int CurrentUses { get; set; }
        public List<string> Destroy { get; set; } = new List<string>();
        public List<int> Notes { get; set; } = new List<int>();
        public int? Score { get; set; }
        public List<string> Spawn { get; set; } = new List<string>();
        public TriggerType Type { get; set; } = TriggerType.None;
        public int? Uses { get; set; }
    }

    // ==========================================
    // 🌟 十、 状态基类 (所有状态共享)
    // ==========================================
    [Serializable]
    public class ObjectState
    {
        [JsonConverter(typeof(TimeObjectConverter))]
        public object AddTime { get; set; }
        public bool? Destroy { get; set; }
        public Ease? Easing { get; set; }
        [JsonConverter(typeof(TimeObjectConverter))]
        public object RelativeTime { get; set; }
        [JsonConverter(typeof(TimeObjectConverter))]
        public object Time { get; set; } = float.MaxValue;
    }

    // ==========================================
    // 🌟 十一、 场景对象状态 (所有带坐标/旋转/缩放的状态)
    // ==========================================
    [Serializable]
    public class StageObjectState : ObjectState
    {
        public bool? FillWidth { get; set; }
        public UnitFloat Height { get; set; }
        public int? Layer { get; set; }
        public float? Opacity { get; set; }
        public int? Order { get; set; }
        public float? PivotX { get; set; }
        public float? PivotY { get; set; }
        public float? RotX { get; set; }
        public float? RotY { get; set; }
        public float? RotZ { get; set; }
        public float? ScaleX { get; set; }
        public float? ScaleY { get; set; }
        public UnitFloat Width { get; set; }
        public UnitFloat X { get; set; }
        public UnitFloat Y { get; set; }
        public UnitFloat Z { get; set; }
    }

    // ==========================================
    // 🌟 十二、 各类型详细状态
    // ==========================================
    [Serializable]
    public class SpriteState : StageObjectState
    {
        public CytoidColor Color { get; set; }
        public string Path { get; set; }
        public bool? PreserveAspect { get; set; }
    }

    [Serializable]
    public class TextState : StageObjectState
    {
        public string Align { get; set; }
        public CytoidColor Color { get; set; }
        public string Font { get; set; }
        public int? Size { get; set; }
        public string Text { get; set; }
        public float? LetterSpacing { get; set; }
        public FontWeight? FontWeight { get; set; }
    }

    [Serializable]
    public class VideoState : StageObjectState
    {
        public CytoidColor Color { get; set; }
        public string Path { get; set; }
    }

    [Serializable]
    public class LineState : StageObjectState
    {
        public List<LinePosition> Pos { get; set; } = new List<LinePosition>();
        public UnitFloat Width { get; set; }
        public CytoidColor Color { get; set; }
        public float? Opacity { get; set; }
        public int? Layer { get; set; }
        public int? Order { get; set; }
    }

    [Serializable]
    public class ControllerState : ObjectState
    {
        // 所有官方特效字段
        public bool? Arcade { get; set; }
        public float? ArcadeContrast { get; set; }
        public float? ArcadeIntensity { get; set; }
        public float? ArcadeInterferanceSize { get; set; }
        public float? ArcadeInterferanceSpeed { get; set; }
        public bool? Artifact { get; set; }
        public float? ArtifactColorisation { get; set; }
        public float? ArtifactIntensity { get; set; }
        public float? ArtifactNoise { get; set; }
        public float? ArtifactParasite { get; set; }
        public float? BackgroundDim { get; set; }
        public bool? Bloom { get; set; }
        public float? BloomIntensity { get; set; }
        public float? Brightness { get; set; }
        public bool? Chromatic { get; set; }
        public bool? Chromatical { get; set; }
        public float? ChromaticalFade { get; set; }
        public float? ChromaticalIntensity { get; set; }
        public float? ChromaticalSpeed { get; set; }
        public float? ChromaticEnd { get; set; }
        public float? ChromaticIntensity { get; set; }
        public float? ChromaticStart { get; set; }
        public bool? ColorAdjustment { get; set; }
        public bool? ColorFilter { get; set; }
        public CytoidColor ColorFilterColor { get; set; }
        public float? Contrast { get; set; }
        public bool? Dream { get; set; }
        public float? DreamIntensity { get; set; }
        public bool? Fisheye { get; set; }
        public float? FisheyeIntensity { get; set; }
        public bool? Focus { get; set; }
        public CytoidColor FocusColor { get; set; }
        public float? FocusIntensity { get; set; }
        public float? FocusSize { get; set; }
        public float? FocusSpeed { get; set; }
        public float? Fov { get; set; }
        public bool? Glitch { get; set; }
        public float? GlitchIntensity { get; set; }
        public bool? GrayScale { get; set; }
        public float? GrayScaleIntensity { get; set; }
        public bool? Noise { get; set; }
        public float? NoiseIntensity { get; set; }
        public List<CytoidColor> NoteFillColors { get; set; }
        public float? NoteOpacityMultiplier { get; set; }
        public CytoidColor NoteRingColor { get; set; }
        public bool? OverrideScanlinePos { get; set; }
        public bool? Perspective { get; set; }
        public bool? RadialBlur { get; set; }
        public float? RadialBlurIntensity { get; set; }
        public float? RotX { get; set; }
        public float? RotY { get; set; }
        public float? RotZ { get; set; }
        public float? Saturation { get; set; }
        public CytoidColor ScanlineColor { get; set; }
        public float? ScanlineOpacity { get; set; }
        public UnitFloat ScanlinePos { get; set; }
        public bool? ScanlineSmoothing { get; set; }
        public bool? Sepia { get; set; }
        public float? SepiaIntensity { get; set; }
        public bool? Shockwave { get; set; }
        public float? ShockwaveSpeed { get; set; }
        public float? Size { get; set; }
        public float? StoryboardOpacity { get; set; }
        public bool? Tape { get; set; }
        public float? UiOpacity { get; set; }
        public bool? Vignette { get; set; }
        public CytoidColor VignetteColor { get; set; }
        public float? VignetteEnd { get; set; }
        public float? VignetteIntensity { get; set; }
        public float? VignetteStart { get; set; }
        public UnitFloat X { get; set; }
        public UnitFloat Y { get; set; }
        public UnitFloat Z { get; set; }
    }

    [Serializable]
    public class NoteControllerState : ObjectState
    {
        public int? Note { get; set; }
        public bool? OverrideX { get; set; }
        public UnitFloat X { get; set; }
        public float? XMultiplier { get; set; }
        public float? XOffset { get; set; }
        public bool? OverrideY { get; set; }
        public UnitFloat Y { get; set; }
        public float? YMultiplier { get; set; }
        public float? YOffset { get; set; }
        public bool? OverrideZ { get; set; }
        public UnitFloat Z { get; set; }
        public bool? OverrideRotX { get; set; }
        public float? RotX { get; set; }
        public bool? OverrideRotY { get; set; }
        public float? RotY { get; set; }
        public bool? OverrideRotZ { get; set; }
        public float? RotZ { get; set; }
        public bool? OverrideRingColor { get; set; }
        public CytoidColor RingColor { get; set; }
        public bool? OverrideFillColor { get; set; }
        public CytoidColor FillColor { get; set; }
        public float? OpacityMultiplier { get; set; }
        public float? SizeMultiplier { get; set; }
        public float? HitboxMultiplier { get; set; }
        public int? HoldDirection { get; set; }
        public int? Style { get; set; }
    }

    // ==========================================
    // 🌟 十三、 对象层次结构 (完全匹配官方)
    // ==========================================
    [Serializable]
    public abstract class StoryboardObject
    {
        public string Id { get; set; }
        public string TargetId { get; set; }
        public string ParentId { get; set; }
    }

    [Serializable]
    public class StoryboardObject<T> : StoryboardObject where T : ObjectState
    {
        public List<T> States { get; set; } = new List<T>();
    }

    [Serializable]
    public class StageObject<TS> : StoryboardObject<TS> where TS : StageObjectState { }

    // 具体类型
    [Serializable] public class Sprite : StageObject<SpriteState> { }
    [Serializable] public class Text : StageObject<TextState> { }
    [Serializable] public class Video : StageObject<VideoState> { }
    [Serializable] public class Line : StageObject<LineState> { }
    [Serializable] public class Controller : StoryboardObject<ControllerState> { }

    [Serializable]
    public class NoteController : StoryboardObject<NoteControllerState>
    {
        [JsonProperty("note")]
        public object NoteTarget { get; set; }  // 可以是 int 或 NoteSelector
    }

    // ==========================================
    // 🌟 十四、 故事板根节点 & 模板
    // ==========================================
    [Serializable]
    public class StoryboardRoot
    {
        public List<Sprite> sprites { get; set; } = new List<Sprite>();
        public List<Text> texts { get; set; } = new List<Text>();
        public List<Line> lines { get; set; } = new List<Line>();
        public List<Video> videos { get; set; } = new List<Video>();
        public List<Controller> controllers { get; set; } = new List<Controller>();
        public List<NoteController> note_controllers { get; set; } = new List<NoteController>();
        public Dictionary<string, StoryboardTemplate> templates { get; set; } = new Dictionary<string, StoryboardTemplate>();
    }

    [Serializable]
    public class StoryboardTemplate : StoryboardRoot { }

    // ==========================================
    // 🌟 十五、 字体粗细枚举 (官方值)
    // ==========================================
    public enum FontWeight
    {
        ExtraLight, Regular, Bold, ExtraBold
    }
}