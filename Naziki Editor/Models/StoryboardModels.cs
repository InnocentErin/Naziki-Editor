using System.Collections.Generic;

namespace Naziki_Editor.Models
{
    // JSON 文件的总入口模具 (已加入所有对象大类！)
    public class StoryboardRoot
    {
        public List<SpriteObject> sprites { get; set; } = new List<SpriteObject>();
        public List<TextObject> texts { get; set; } = new List<TextObject>();
        public List<LineObject> lines { get; set; } = new List<LineObject>();
        public List<VideoObject> videos { get; set; } = new List<VideoObject>();
        public List<ControllerObject> controllers { get; set; } = new List<ControllerObject>();
        public List<NoteControllerObject> note_controllers { get; set; } = new List<NoteControllerObject>();
        // 🌟 修正：Cytoid 的模板是用名字作为 Key 的字典，不是列表！
        public Dictionary<string, StoryboardTemplate> templates { get; set; } = new Dictionary<string, StoryboardTemplate>();
    }
        // 状态模具 (不变)
        public class State
    {
        public object time { get; set; }
        public object x { get; set; }
        public object y { get; set; }
        public double? opacity { get; set; }
        public string easing { get; set; } = "linear";
    }

    // 基础对象模具 (不变)
    public class StoryboardObject
    {
        public string id { get; set; }
        public object time { get; set; }
        public object x { get; set; }
        public object y { get; set; }
        public double opacity { get; set; } = 0;
        public List<State> states { get; set; } = new List<State>();
    }

    // 1. 图片模具
    public class SpriteObject : StoryboardObject
    {
        public string path { get; set; }
    }

    // 2. 文本模具
    public class TextObject : StoryboardObject
    {
        public string text { get; set; }
        public int size { get; set; } = 20;
        public string color { get; set; } = "#fff";
    }

    // 🌟 3. 新增：视频模具
    public class VideoObject : StoryboardObject
    {
        public string path { get; set; }
        public string color { get; set; } = "#fff";
    }

    // 🌟 4. 新增：线条模具
    public class LineObject : StoryboardObject
    {
        public double width { get; set; } = 0.05;
        public string color { get; set; } = "#fff";
        public List<Vertex> pos { get; set; } = new List<Vertex>(); // 线条有很多端点
    }

    // 线条端点专用的模具
    public class Vertex
    {
        public object x { get; set; }
        public object y { get; set; }
        public object z { get; set; }
    }

    // 🌟 5. 新增：场景控制器模具 (它没有具体的坐标，主要管大局)
    public class ControllerObject
    {
        public object time { get; set; }
        // 这里面的属性太多太杂，对于初学者，我们先用百宝箱接着，保证不崩溃
        public List<object> states { get; set; } = new List<object>();
    }

    // 🌟 6. 新增：音符控制器模具 (操控游戏里的 note)
    public class NoteControllerObject
    {
        // 音符对象不仅能填数字，还能填选择器，必须用 object！
        public object note { get; set; }
        public object time { get; set; }
        public List<object> states { get; set; } = new List<object>();

        // 🌟 7. 新增：模板专用模具 (Template)
        // 模板其实就是一个带名字(id)的“微型故事板”，它可以打包一堆子对象！
        
    }
    public class StoryboardTemplate
    {
        // 模板的唯一名字（我们刚才就是靠读这个名字在列表里显示的！）
        // 模板肚子里能装的宝贝（和外层的 Root 一模一样，准备好篮子接着！）
        public List<SpriteObject> sprites { get; set; } = new List<SpriteObject>();
        public List<TextObject> texts { get; set; } = new List<TextObject>();
        public List<LineObject> lines { get; set; } = new List<LineObject>();
        public List<VideoObject> videos { get; set; } = new List<VideoObject>();
        public List<ControllerObject> controllers { get; set; } = new List<ControllerObject>();
        public List<NoteControllerObject> note_controllers { get; set; } = new List<NoteControllerObject>();
    }
    // ==========================================
    // 🌟 8. 专属子对象：音符控制器事件选择器 
    // ==========================================
    public class NoteCtrlEventSelect
    {
        // Cytoid 官方支持的常见筛选属性
        public int? type { get; set; }       // 过滤音符类型 (如 Click=0, Hold=1 等)
        public int? page { get; set; }       // 过滤所在的页码
        public int? id { get; set; }         // 显式指定起始 ID
        public int? min_tick { get; set; }   // 时间轴 Tick 下限
        public int? max_tick { get; set; }   // 时间轴 Tick 上限

        // 🪄 核心魔法：动态计算对象名称（严格遵守主人定的规矩：只抓取前三个有值的属性！）
        public string DisplayName
        {
            get
            {
                var activeProps = new List<string>();

                // 挨个检查哪些属性被主人宠幸（赋值）了
                if (type.HasValue) activeProps.Add($"类型={type.Value}");
                if (page.HasValue) activeProps.Add($"页码={page.Value}");
                if (id.HasValue) activeProps.Add($"ID={id.Value}");
                if (min_tick.HasValue) activeProps.Add($"MinTick={min_tick.Value}");
                if (max_tick.HasValue) activeProps.Add($"MaxTick={max_tick.Value}");

                // 如果全都是空的，给一个温馨的兜底提示
                if (activeProps.Count == 0) return "未配置选择器";

                // 🔮 关键点：使用 Linq 的 Take(3) 强行截取前三个有效属性进行拼接
                var displayList = activeProps.Take(3);
                return "选择器: {" + string.Join(", ", displayList) + "}";
            }
        }
    }
}