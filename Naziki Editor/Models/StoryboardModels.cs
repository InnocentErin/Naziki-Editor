using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Naziki_Editor.Models
{
    // ==========================================
    // 🌟 一、 故事板大管家 (Storyboard Root)
    // ==========================================
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

    public class StoryboardTemplate : StoryboardRoot { } // 模板内部其实就是个小故事板

    // ==========================================
    // 🌟 二、 官方枚举字典 (Enums)
    // ==========================================
    public enum Easing
    {
        None = 0, EaseInQuad = 1, EaseOutQuad = 2, EaseInOutQuad = 3, EaseInCubic = 4, EaseOutCubic = 5, EaseInOutCubic = 6,
        EaseInQuart = 7, EaseOutQuart = 8, EaseInOutQuart = 9, EaseInQuint = 10, EaseOutQuint = 11, EaseInOutQuint = 12,
        EaseInSine = 13, EaseOutSine = 14, EaseInOutSine = 15, EaseInExpo = 16, EaseOutExpo = 17, EaseInOutExpo = 18,
        EaseInCirc = 19, EaseOutCirc = 20, EaseInOutCirc = 21, Linear = 22, Spring = 23, EaseInBounce = 24,
        EaseOutBounce = 25, EaseInOutBounce = 26, EaseInBack = 27, EaseOutBack = 28, EaseInOutBack = 29,
        EaseInElastic = 30, EaseOutElastic = 31, EaseInOutElastic = 32, Blink = 33
    }

    public enum ReferenceUnit { World, StageX, StageY, NoteX, NoteY, CameraX, CameraY }
    public enum FontWeight { ExtraLight, Regular, Bold, ExtraBold }
    public enum TriggerType { NoteClear, Combo, Score, None }



    // ==========================================
    // 🌟 高级翻译官：专门教程序怎么认 Cytoid 的混合坐标！
    // ==========================================
    public class UnitFloatConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(UnitFloat);

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            JToken token = JToken.Load(reader);

            // 🎯 情况 1：如果是纯数字 (比如 0.5)
            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
            {
                return new UnitFloat { Value = (float)token, Unit = ReferenceUnit.World };
            }
            // 🎯 情况 2：如果是带单位的字符串 (比如 "noteX:0.02")
            else if (token.Type == JTokenType.String)
            {
                string str = (string)token;
                var split = str.Split(':');
                if (split.Length == 2)
                {
                    // 尝试把 "noteX" 翻译成枚举 ReferenceUnit.NoteX
                    if (Enum.TryParse(split[0], true, out ReferenceUnit unit))
                    {
                        return new UnitFloat { Value = float.Parse(split[1]), Unit = unit };
                    }
                }
                else if (split.Length == 1)
                {
                    // 如果只有数字没有冒号
                    return new UnitFloat { Value = float.Parse(split[0]), Unit = ReferenceUnit.World };
                }
            }
            // 🎯 情况 3：如果是咱们自己编辑器保存出来的标准对象
            else if (token.Type == JTokenType.Object)
            {
                var obj = token as JObject;
                var uf = new UnitFloat();
                if (obj["Value"] != null) uf.Value = (float)obj["Value"];
                if (obj["Unit"] != null && Enum.TryParse(obj["Unit"].ToString(), true, out ReferenceUnit u)) uf.Unit = u;
                return uf;
            }

            return new UnitFloat(); // 兜底保护
        }

        public override bool CanWrite => true; // 序列化时按默认格式输出即可
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var uf = (UnitFloat)value;
            if (uf == null) { writer.WriteNull(); return; }

            // 🎯 如果是默认世界坐标系，直接输出纯数字 (比如 10.5)
            if (uf.Unit == ReferenceUnit.World)
            {
                writer.WriteValue(uf.Value);
            }
            else
            {
                // 🎯 如果有特殊参考系，转成官方驼峰字符串 (比如 "noteX:10.5")
                string unitStr = uf.Unit.ToString();
                unitStr = char.ToLower(unitStr[0]) + unitStr.Substring(1);
                writer.WriteValue($"{unitStr}:{uf.Value}");
            }
        }
    }

    // ==========================================
    // 🌟 高级翻译官 2 号：专门处理 Cytoid 的多维时间！
    // ==========================================
    public class TimeObjectConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(object);

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            JToken token = JToken.Load(reader);

            // 🎯 情况 1：如果是官方的“高级语法糖数组” (比如 ["start:57", "start:123"])
            if (token.Type == JTokenType.Array)
            {
                var list = new List<object>();
                foreach (var item in token)
                {
                    if (item.Type == JTokenType.Float || item.Type == JTokenType.Integer)
                        list.Add((float)item); // 纯数字
                    else
                        list.Add(item.ToString()); // 字符串锚点
                }
                return list; // 返回一个 C# 的 List<object>
            }
            // 🎯 情况 2：如果是普通的数字 (比如 3.14)
            else if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
            {
                return (float)token;
            }
            // 🎯 情况 3：如果是单一的字符串锚点 (比如 "start:1134")
            else if (token.Type == JTokenType.String)
            {
                return token.ToString();
            }
            else
            {
                // 如果遇到其他类型（理论上不应该出现），返回 null 或抛出异常
                return null;
            }
        }

        public override bool CanWrite => false;
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) => throw new NotImplementedException();
    }


    // ==========================================
    // 🌟 高级翻译官 3 号：专门教程序怎么认 Cytoid 的十六进制颜色！
    // ==========================================
    public class CytoidColorConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(CytoidColor);

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            JToken token = JToken.Load(reader);

            // 🎯 情况 1：如果是官方的十六进制字符串 (比如 "#FFFFFF" 或 "#4568dc")
            if (token.Type == JTokenType.String)
            {
                string hex = token.ToString().TrimStart('#');
                var color = new CytoidColor();
                try
                {
                    if (hex.Length == 6) // 标准 RGB
                    {
                        color.R = Convert.ToInt32(hex.Substring(0, 2), 16);
                        color.G = Convert.ToInt32(hex.Substring(2, 2), 16);
                        color.B = Convert.ToInt32(hex.Substring(4, 2), 16);
                        color.A = 1f;
                    }
                    else if (hex.Length == 8) // 带透明度的 RGBA
                    {
                        color.R = Convert.ToInt32(hex.Substring(0, 2), 16);
                        color.G = Convert.ToInt32(hex.Substring(2, 2), 16);
                        color.B = Convert.ToInt32(hex.Substring(4, 2), 16);
                        color.A = Convert.ToInt32(hex.Substring(6, 2), 16) / 255f;
                    }
                }
                catch { /* 解析失败则保留默认白色 */ }
                return color;
            }
            // 🎯 情况 2：如果是咱们编辑器自己存出来的标准对象，正常兜底读取
            else if (token.Type == JTokenType.Object)
            {
                var obj = (JObject)token;
                return new CytoidColor
                {
                    R = obj["R"] != null ? (float)obj["R"] : 255,
                    G = obj["G"] != null ? (float)obj["G"] : 255,
                    B = obj["B"] != null ? (float)obj["B"] : 255,
                    A = obj["A"] != null ? (float)obj["A"] : 1
                };
            }

            return new CytoidColor();
        }

        public override bool CanWrite => true; // 支持保存工程时输出
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var c = (CytoidColor)value;
            if (c == null) { writer.WriteNull(); return; }

            // 保存时转回十六进制，保持官方原汁原味
            if (c.A >= 1f)
                writer.WriteValue($"#{(int)c.R:X2}{(int)c.G:X2}{(int)c.B:X2}");
            else
                writer.WriteValue($"#{(int)c.R:X2}{(int)c.G:X2}{(int)c.B:X2}{(int)(c.A * 255):X2}");
        }
    }






    // ==========================================
    // 🌟 三、 官方核心数据结构 (Structs / Classes)
    // ==========================================
    [Serializable]
    [JsonConverter(typeof(UnitFloatConverter))] // ✨ 贴上这句！强制让所有序列化都走翻译官！
    public class UnitFloat
    {
        public float Value { get; set; }
        public ReferenceUnit Unit { get; set; }
        public bool ScaleToCanvas { get; set; }
        public bool Span { get; set; }

        public UnitFloat() { }
        public UnitFloat(float value, ReferenceUnit unit, bool scaleToCanvas, bool span)
        {
            Value = value; Unit = unit; ScaleToCanvas = scaleToCanvas; Span = span;
        }
    }

    [Serializable]
    [JsonConverter(typeof(CytoidColorConverter))] // ✨ 挂载新的颜色翻译官！
    public class CytoidColor
    {
        public float A { get; set; } = 1;
        public float B { get; set; } = 255;
        public float G { get; set; } = 255;
        public float R { get; set; } = 255;
    }

    [Serializable]
    public class LinePosition
    {
        public UnitFloat X { get; set; }
        public UnitFloat Y { get; set; }
        public UnitFloat Z { get; set; }
    }

    [Serializable]
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

    [Serializable]
    public class NoteCtrlEventSelect
    {
        public HashSet<int> Types { get; set; } = new HashSet<int>();
        public int Start { get; set; } = int.MinValue;
        public int End { get; set; } = int.MaxValue;
        public int? Direction { get; set; }
        public float MinX { get; set; } = int.MinValue;
        public float MaxX { get; set; } = int.MaxValue;

        [JsonIgnore]
        public string DisplayName => $"筛选器: {(Types.Count > 0 ? "Type:" + Types.First() : "范围")} ({Start}~{End})";
    }

    // ==========================================
    // 🌟 四、 状态基因组 (States)
    // ==========================================
    [Serializable]
    public class ObjectState
    {
        // 🌟 核心修正：为了兼容 "start:xxx" 这种字符串锚点，这里必须用 object！
        [JsonConverter(typeof(TimeObjectConverter))]
        public object AddTime { get; set; }
        public bool? Destroy { get; set; }
        public Easing? Easing { get; set; }
        public string Template { get; set; }

        // 🌟 核心修正：相对时间也可能有特殊格式，用 object！
        [JsonConverter(typeof(TimeObjectConverter))]
        public object RelativeTime { get; set; }

        // 🌟 核心修正：绝对时间/音符锚点，兼容数字与字符串！
        [JsonConverter(typeof(TimeObjectConverter))]
        public object Time { get; set; } = float.MaxValue;
    }

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
        public float? Scale { get; set; }
    }

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
        // 🔹 1. 游戏 UI 与透明度类
        public float? StoryboardOpacity { get; set; }
        public float? UiOpacity { get; set; }
        public float? ScanlineOpacity { get; set; }
        public float? BackgroundDim { get; set; }
        public float? NoteOpacityMultiplier { get; set; }
        public string ScanlineColor { get; set; }
        public string NoteRingColor { get; set; }
        public List<string> NoteFillColors { get; set; } // 覆盖不同种类note颜色数组
        public bool? OverrideScanlinePos { get; set; }
        public float? ScanlinePos { get; set; }

        // 🔹 2. 相机系统类
        public bool? Perspective { get; set; }
        public float? Size { get; set; }
        public float? Fov { get; set; }
        public UnitFloat X { get; set; }
        public UnitFloat Y { get; set; }
        public float? Z { get; set; }
        public float? RotX { get; set; }
        public float? RotY { get; set; }
        public float? RotZ { get; set; }

        // 🔹 3. 屏幕特效滤镜类 (Filters)
        // 色度 (Chromatical)
        public bool? Chromatical { get; set; }
        public float? ChromaticalFade { get; set; }
        public float? ChromaticalIntensity { get; set; }
        public float? ChromaticalSpeed { get; set; }

        // 泛光 (Bloom)
        public bool? Bloom { get; set; }
        public float? BloomIntensity { get; set; }

        // 模糊 (RadialBlur)
        public bool? RadialBlur { get; set; }
        public float? RadialBlurIntensity { get; set; }

        // 色调调节 (ColorAdjustment)
        public bool? ColorAdjustment { get; set; }
        public float? Brightness { get; set; }
        public float? Saturation { get; set; }
        public float? Contrast { get; set; }

        // 屏幕色滤镜 (ColorFilter)
        public bool? ColorFilter { get; set; }
        public string ColorFilterColor { get; set; }

        // 灰度 (GrayScale)
        public bool? GrayScale { get; set; }
        public float? GrayScaleIntensity { get; set; }

        // 噪点 (Noise)
        public bool? Noise { get; set; }
        public float? NoiseIntensity { get; set; }

        // 棕色老照片 (Sepia)
        public bool? Sepia { get; set; }
        public float? SepiaIntensity { get; set; }

        // 梦幻 (Dream)
        public bool? Dream { get; set; }
        public float? DreamIntensity { get; set; }

        // 鱼眼 (Fisheye)
        public bool? Fisheye { get; set; }
        public float? FisheyeIntensity { get; set; }

        // 冲击波 (Shockwave)
        public bool? Shockwave { get; set; }
        public float? ShockwaveSpeed { get; set; }

        // 漫画聚焦线 (Focus)
        public bool? Focus { get; set; }
        public float? FocusSize { get; set; }
        public string FocusColor { get; set; }
        public float? FocusSpeed { get; set; }
        public float? FocusIntensity { get; set; }

        // 故障艺术 (Glitch)
        public bool? Glitch { get; set; }
        public float? GlitchIntensity { get; set; }

        // 街机风格 (Arcade)
        public bool? Arcade { get; set; }
        public float? ArcadeIntensity { get; set; }
        public float? ArcadeInterferenceSize { get; set; }
        public float? ArcadeInterferenceSpeed { get; set; }
        public float? ArcadeContrast { get; set; }

        // 磁带翻转 (Tape)
        public bool? Tape { get; set; }
    }


    [Serializable]
    public class NoteControllerState : ObjectState
    {
        // 🎵 注意：因为在官方底层中，外层有一行专门的 noteTarget，所以在关键帧指令状态里，我们只保留可动增量参数

        // X轴覆盖与偏移
        public bool? OverrideX { get; set; }
        public UnitFloat X { get; set; }
        public float? XMultiplier { get; set; }
        public float? Dx { get; set; }
        public float? XOffset { get; set; }  // 官方新版本 x_offset，正是前台需要的它！

        // Y轴覆盖与偏移
        public bool? OverrideY { get; set; }
        public UnitFloat Y { get; set; }
        public float? YMultiplier { get; set; }
        public float? Dy { get; set; }
        public float? YOffset { get; set; }  // 官方新版本 y_offset

        // Z轴覆盖
        public bool? OverrideZ { get; set; }
        public float? Z { get; set; }

        // 3D相机旋转覆盖
        public bool? OverrideRotX { get; set; }
        public float? RotX { get; set; }
        public bool? OverrideRotY { get; set; }
        public float? RotY { get; set; }
        public bool? OverrideRotZ { get; set; }
        public float? RotZ { get; set; }

        // 换色与不透明度/大小倍率
        public bool? OverrideRingColor { get; set; }
        public string RingColor { get; set; }
        public bool? OverrideFillColor { get; set; }
        public string FillColor { get; set; }
        public float? OpacityMultiplier { get; set; }
        public float? SizeMultiplier { get; set; }

        // Hold（长条音符）专属控制
        public int? HoldDirection { get; set; } // 1向上，-1向下
        public int? Style { get; set; }         // 1默认样式，2隐藏射线连线下落式
    }

    // ==========================================
    // 🌟 五、 实体基类与泛型 (Objects)
    // ==========================================
    [Serializable]
    public abstract class StoryboardObject
    {
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public string Id { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public string TargetId { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public string ParentId { get; set; }
    }


    [Serializable]
    [JsonConverter(typeof(StoryboardObjectConverter))]
    public class StoryboardObject<T> : StoryboardObject where T : ObjectState
    {
        public List<T> States { get; set; } = new List<T>();
    }

    [Serializable]
    public class StageObject<TS> : StoryboardObject<TS> where TS : StageObjectState { }

    [Serializable] public class Sprite : StageObject<SpriteState> { }
    [Serializable] public class Text : StageObject<TextState> { }
    [Serializable] public class Video : StageObject<VideoState> { }
    [Serializable] public class Line : StageObject<LineState> { }
    [Serializable] public class Controller : StoryboardObject<ControllerState> { }

    // 🌟 音符控制器比较特殊，它有一个 note 字段用于绑定目标 (数字 或 JObject 选择器)
    [Serializable]
    public class NoteController : StoryboardObject<NoteControllerState>
    {
        [JsonProperty("note")]
        public object NoteTarget { get; set; }
    }


    // =========================================
    // 🌟 六、 万能翻译官：专门负责把 JSON 反序列化成 StoryboardObject<T> 的神奇工具！
    // 🌟 核心功能：在反序列化时，自动把 JSON 根部的属性（如 time、x、y 等）也填充到 States[0] 中，完美兼容官方的“语法糖”写法！
    // 🌟 适用范围：所有 StoryboardObject<T> 派生类（Sprite、Text、Line、Video、Controller、NoteController）都可以使用这个转换器，无需额外配置！
    // 🌟 使用方法：在 JsonSerializerSettings 中添加这个转换器，或者直接在 StoryboardObject<T> 类上使用 [JsonConverter(typeof(StoryboardObjectConverter))] 特性即可！
    // =========================================
    public class StoryboardObjectConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            // 检查类型本身或其任一基类是否为 StoryboardObject<T>
            Type current = objectType;
            while (current != null && current != typeof(object))
            {
                if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(StoryboardObject<>))
                    return true;
                current = current.BaseType;
            }
            return false;
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            JObject obj = JObject.Load(reader);

            // 创建目标对象实例（必须有无参构造函数）
            var target = (StoryboardObject)Activator.CreateInstance(objectType);

            // 🌟 修复点 1：安全获取 States 属性与真正的泛型参数 T (例如 SpriteState)
            var statesProp = objectType.GetProperty("States");
            Type stateType = statesProp.PropertyType.GetGenericArguments()[0]; // 完美避开越界报错！

            // 处理基类 StoryboardObject 的属性
            target.Id = obj["id"]?.ToString();
            target.TargetId = obj["target_id"]?.ToString();
            target.ParentId = obj["parent_id"]?.ToString();

            // 处理 NoteController 的特殊 note 字段 (保留了你的原始逻辑)
            if (target is NoteController noteCtrl && obj["note"] != null)
            {
                var noteToken = obj["note"];
                if (noteToken.Type == JTokenType.Integer || noteToken.Type == JTokenType.Float)
                    noteCtrl.NoteTarget = noteToken.Value<int>();
                else if (noteToken.Type == JTokenType.Object)
                    noteCtrl.NoteTarget = noteToken.ToObject<NoteCtrlEventSelect>();
                else
                    noteCtrl.NoteTarget = noteToken.ToString();
            }

            // 准备构建状态列表
            Type listType = typeof(List<>).MakeGenericType(stateType);
            System.Collections.IList statesList = (System.Collections.IList)Activator.CreateInstance(listType);

            // 🌟 修复点 2：放弃之前的粗暴 foreach，直接使用 serializer 解析！
            // 这样做会让 Newtonsoft 自动触发挂载在 Time 上的 TimeObjectConverter！
            object initialState = serializer.Deserialize(obj.CreateReader(), stateType);
            statesList.Add(initialState);

            // 🌟 修复点 3：如果有 states 数组，同样使用 serializer 解析并加入列表
            if (obj["states"] is JArray statesArray)
            {
                foreach (var item in statesArray)
                {
                    object extraState = serializer.Deserialize(item.CreateReader(), stateType);
                    statesList.Add(extraState);
                }
            }

            // 将拼装好的完整状态链赋值给主对象的 States 属性
            statesProp.SetValue(target, statesList);

            return target;
        }

        // ✨ 开启自定义写出魔法！
        public override bool CanWrite => true;

        // 🧹 专用状态净化器：剔除空值，并将 C# 命名转为官方蛇形命名
        private JObject CleanUpStateObject(JObject rawToken)
        {
            var cleanObj = new JObject();
            foreach (var prop in rawToken.Properties())
            {
                // 🛡️ 核心防御 1：坚决丢弃所有 null 值！
                if (prop.Value.Type == JTokenType.Null) continue;

                // 🛡️ 核心防御 2：强制把 PascalCase 变成 Cytoid 官方的 snake_case！
                // 比如：AddTime -> add_time，PivotX -> pivot_x，Color -> color
                string snakeName = string.Concat(prop.Name.Select((x, i) => i > 0 && char.IsUpper(x) ? "_" + x.ToString() : x.ToString())).ToLower();

                cleanObj[snakeName] = prop.Value;
            }
            return cleanObj;
        }



        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var target = (StoryboardObject)value;
            var objType = target.GetType();

            // 创建一个空白的 JSON 容器，准备手动按官方格式组装
            var jObj = new JObject();

            // 1. 写入基类属性 (id, target_id, parent_id)
            if (!string.IsNullOrEmpty(target.Id)) jObj["id"] = target.Id;
            if (!string.IsNullOrEmpty(target.TargetId)) jObj["target_id"] = target.TargetId;
            if (!string.IsNullOrEmpty(target.ParentId)) jObj["parent_id"] = target.ParentId;

            // 特殊处理 NoteController 的 note 字段
            if (target is NoteController noteCtrl && noteCtrl.NoteTarget != null)
            {
                jObj["note"] = JToken.FromObject(noteCtrl.NoteTarget, serializer);
            }

            // 2. 提取咱们存储的状态列表 States
            var statesProp = objType.GetProperty("States");
            var statesList = statesProp.GetValue(target) as System.Collections.IList;

            if (statesList != null && statesList.Count > 0)
            {
                // 🌟 3. 核心修复：把第 0 帧（初始状态）“平铺”到根节点！
                var initialState = statesList[0];
                var initialToken = JObject.FromObject(initialState, serializer);
                var cleanInitialToken = CleanUpStateObject(initialToken); // 🧹 呼叫净化器！

                // 将净化后的属性直接贴在根部
                foreach (var prop in cleanInitialToken.Properties())
                {
                    jObj[prop.Name] = prop.Value;
                }

                // 🌟 4. 如果还有更多的关键帧，才把它们塞进 states 数组里
                if (statesList.Count > 1)
                {
                    var statesArray = new JArray();
                    for (int i = 1; i < statesList.Count; i++)
                    {
                        var rawState = JObject.FromObject(statesList[i], serializer);
                        statesArray.Add(CleanUpStateObject(rawState)); // 🧹 呼叫净化器处理后续关键帧！
                    }
                    jObj["states"] = statesArray;
                }
            }

            // 将拼装好的纯正官方格式写出！
            jObj.WriteTo(writer);
        }

        private string GetJsonPropertyName(System.Reflection.PropertyInfo prop)
        {
            var attr = prop.GetCustomAttribute<JsonPropertyAttribute>();
            if (attr != null && !string.IsNullOrEmpty(attr.PropertyName))
                return attr.PropertyName;
            // 将属性名转换为 snake_case
            string name = prop.Name;
            return char.ToLower(name[0]) + name.Substring(1);
        }
    }

}