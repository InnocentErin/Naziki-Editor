using System.Collections.Generic;

namespace Naziki_Editor.Core
{
    /// <summary>
    /// 定义轨道的数据类型，决定 UI 打印机生成什么控件
    /// </summary>
    public enum TrackDataType
    {
        Float,          // 生成 TextBox (带数字校验) 或 Slider
        String,         // 生成 TextBox (文本)
        Color,          // 生成颜色选择器 (CytoidColor)
        Boolean,        // 生成 Checkbox (开关类，如 bloom: true)
        NoteColorArray  // ✨ 专为 note_fill_colors 定制的 12 色选择器容器
    }

    /// <summary>
    /// 一条属性轨道的蓝图
    /// </summary>
    public class TrackBlueprint
    {
        public string JsonName { get; set; }      // 底层 JSON 字段名
        public string DisplayName { get; set; }   // UI 显示名
        public string GroupName { get; set; }     // ✨ 新增：分组名 (方便 UI 把几十个属性分类折叠展示)
        public TrackDataType DataType { get; set; } // 数据类型
        public object DefaultValue { get; set; }  // 默认值
    }

    /// <summary>
    /// 全局蓝图管理中心
    /// </summary>
    public static class TrackBlueprintManager
    {
        // ==========================================
        // 🖼️ 图片对象 (Sprite) 蓝图
        // ==========================================
        public static readonly List<TrackBlueprint> SpriteBlueprints = new List<TrackBlueprint>
        {
            // 📐 空间坐标与尺寸控制
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "x", DisplayName = "X 坐标 (X)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "y", DisplayName = "Y 坐标 (Y)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "z", DisplayName = "Z 层级 (Z)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "width", DisplayName = "宽度 (Width)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "height", DisplayName = "高度 (Height)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "scale", DisplayName = "整体缩放 (Scale)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "scale_x", DisplayName = "X 轴缩放 (Scale X)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "scale_y", DisplayName = "Y 轴缩放 (Scale Y)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "pivot_x", DisplayName = "X 轴锚点 (Pivot X)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "pivot_y", DisplayName = "Y 轴锚点 (Pivot Y)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "rot_x", DisplayName = "X 轴旋转 (Rot X)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "rot_y", DisplayName = "Y 轴旋转 (Rot Y)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "rot_z", DisplayName = "Z 轴旋转 (Rot Z)", DataType = TrackDataType.Float },
            
            // 🌌 层级与环境表现
            new TrackBlueprint { GroupName = "层级与环境表现", JsonName = "layer", DisplayName = "渲染图层 (Layer)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "层级与环境表现", JsonName = "order", DisplayName = "图层内顺序 (Order)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "层级与环境表现", JsonName = "opacity", DisplayName = "不透明度 (Opacity)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "层级与环境表现", JsonName = "fill_width", DisplayName = "宽度适配屏幕 (Fill Width)", DataType = TrackDataType.Boolean },

            // 🎨 外观颜色与内容
            new TrackBlueprint { GroupName = "外观颜色与内容", JsonName = "color", DisplayName = "颜色 (Color)", DataType = TrackDataType.Color },
            new TrackBlueprint { GroupName = "外观颜色与内容", JsonName = "path", DisplayName = "图片路径 (Path)", DataType = TrackDataType.String }
        };

        // ==========================================
        // 📝 文字对象 (Text) 蓝图
        // ==========================================
        public static readonly List<TrackBlueprint> TextBlueprints = new List<TrackBlueprint>
        {
            // 📐 空间坐标与尺寸控制
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "x", DisplayName = "X 坐标 (X)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "y", DisplayName = "Y 坐标 (Y)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "z", DisplayName = "Z 层级 (Z)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "width", DisplayName = "文本框宽度 (Width)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "height", DisplayName = "文本框高度 (Height)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "scale", DisplayName = "整体缩放 (Scale)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "scale_x", DisplayName = "X 轴缩放 (Scale X)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "scale_y", DisplayName = "Y 轴缩放 (Scale Y)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "pivot_x", DisplayName = "X 轴锚点 (Pivot X)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "pivot_y", DisplayName = "Y 轴锚点 (Pivot Y)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "rot_x", DisplayName = "X 轴旋转 (Rot X)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "rot_y", DisplayName = "Y 轴旋转 (Rot Y)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "rot_z", DisplayName = "Z 轴旋转 (Rot Z)", DataType = TrackDataType.Float },
            
            // 🌌 层级与环境表现
            new TrackBlueprint { GroupName = "层级与环境表现", JsonName = "layer", DisplayName = "渲染图层 (Layer)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "层级与环境表现", JsonName = "order", DisplayName = "图层内顺序 (Order)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "层级与环境表现", JsonName = "opacity", DisplayName = "不透明度 (Opacity)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "层级与环境表现", JsonName = "fill_width", DisplayName = "宽度适配屏幕 (Fill Width)", DataType = TrackDataType.Boolean },

            // 🎨 外观颜色与文字属性
            new TrackBlueprint { GroupName = "外观颜色与文字属性", JsonName = "text", DisplayName = "文字内容 (Text)", DataType = TrackDataType.String },
            new TrackBlueprint { GroupName = "外观颜色与文字属性", JsonName = "color", DisplayName = "颜色 (Color)", DataType = TrackDataType.Color },
            new TrackBlueprint { GroupName = "外观颜色与文字属性", JsonName = "size", DisplayName = "字号大小 (Size)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "外观颜色与文字属性", JsonName = "align", DisplayName = "对齐方式 (Align)", DataType = TrackDataType.String },
            new TrackBlueprint { GroupName = "外观颜色与文字属性", JsonName = "font", DisplayName = "字体文件 (Font)", DataType = TrackDataType.String },
            new TrackBlueprint { GroupName = "外观颜色与文字属性", JsonName = "letter_spacing", DisplayName = "字间距 (Letter Spacing)", DataType = TrackDataType.Float }
        };

        // ==========================================
        // 🎬 视频对象 (Video) 蓝图
        // ==========================================
        public static readonly List<TrackBlueprint> VideoBlueprints = new List<TrackBlueprint>
        {
            // 📐 空间坐标与尺寸控制
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "x", DisplayName = "X 坐标 (X)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "y", DisplayName = "Y 坐标 (Y)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "z", DisplayName = "Z 层级 (Z)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "width", DisplayName = "宽度 (Width)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "height", DisplayName = "高度 (Height)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "scale", DisplayName = "整体缩放 (Scale)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "scale_x", DisplayName = "X 轴缩放 (Scale X)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "scale_y", DisplayName = "Y 轴缩放 (Scale Y)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "pivot_x", DisplayName = "X 轴锚点 (Pivot X)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "pivot_y", DisplayName = "Y 轴锚点 (Pivot Y)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "rot_x", DisplayName = "X 轴旋转 (Rot X)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "rot_y", DisplayName = "Y 轴旋转 (Rot Y)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "rot_z", DisplayName = "Z 轴旋转 (Rot Z)", DataType = TrackDataType.Float },
            
            // 🌌 层级与环境表现
            new TrackBlueprint { GroupName = "层级与环境表现", JsonName = "layer", DisplayName = "渲染图层 (Layer)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "层级与环境表现", JsonName = "order", DisplayName = "图层内顺序 (Order)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "层级与环境表现", JsonName = "opacity", DisplayName = "不透明度 (Opacity)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "层级与环境表现", JsonName = "fill_width", DisplayName = "宽度适配屏幕 (Fill Width)", DataType = TrackDataType.Boolean },

            // 🎨 外观颜色与内容
            new TrackBlueprint { GroupName = "外观颜色与内容", JsonName = "color", DisplayName = "颜色 (Color)", DataType = TrackDataType.Color },
            new TrackBlueprint { GroupName = "外观颜色与内容", JsonName = "path", DisplayName = "视频路径 (Path)", DataType = TrackDataType.String },
            new TrackBlueprint { GroupName = "外观颜色与内容", JsonName = "preserve_aspect_ratio", DisplayName = "保持宽高比 (Keep Ratio)", DataType = TrackDataType.Boolean }
        };

        // ==========================================
        // 〰️ 线条对象 (Line) 蓝图
        // ==========================================
        public static readonly List<TrackBlueprint> LineBlueprints = new List<TrackBlueprint>
        {
            // 📐 空间坐标与尺寸控制
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "x", DisplayName = "X 坐标 (X)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "y", DisplayName = "Y 坐标 (Y)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "z", DisplayName = "Z 层级 (Z)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "width", DisplayName = "线段粗细 (Width)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "height", DisplayName = "高度 (Height)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "scale", DisplayName = "整体缩放 (Scale)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "scale_x", DisplayName = "X 轴缩放 (Scale X)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "scale_y", DisplayName = "Y 轴缩放 (Scale Y)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "pivot_x", DisplayName = "X 轴锚点 (Pivot X)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "pivot_y", DisplayName = "Y 轴锚点 (Pivot Y)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "rot_x", DisplayName = "X 轴旋转 (Rot X)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "rot_y", DisplayName = "Y 轴旋转 (Rot Y)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "空间坐标与尺寸控制", JsonName = "rot_z", DisplayName = "Z 轴旋转 (Rot Z)", DataType = TrackDataType.Float },
            
            // 🌌 层级与环境表现
            new TrackBlueprint { GroupName = "层级与环境表现", JsonName = "layer", DisplayName = "渲染图层 (Layer)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "层级与环境表现", JsonName = "order", DisplayName = "图层内顺序 (Order)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "层级与环境表现", JsonName = "opacity", DisplayName = "不透明度 (Opacity)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "层级与环境表现", JsonName = "fill_width", DisplayName = "宽度适配屏幕 (Fill Width)", DataType = TrackDataType.Boolean },

            // 🎨 外观颜色与内容
            new TrackBlueprint { GroupName = "外观颜色与内容", JsonName = "color", DisplayName = "颜色 (Color)", DataType = TrackDataType.Color },
            new TrackBlueprint { GroupName = "外观颜色与内容", JsonName = "path", DisplayName = "线条材质路径 (Path)", DataType = TrackDataType.String }
        };

        // ==========================================
        // 🎛️ 场景控制器 (Controller) 蓝图
        // ==========================================
        public static readonly List<TrackBlueprint> ControllerBlueprints = new List<TrackBlueprint>
        {
            // 🎥 镜头控制
            new TrackBlueprint { GroupName = "镜头与透视控制", JsonName = "x", DisplayName = "相机 X 坐标", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "镜头与透视控制", JsonName = "y", DisplayName = "相机 Y 坐标", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "镜头与透视控制", JsonName = "z", DisplayName = "相机 Z 坐标", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "镜头与透视控制", JsonName = "fov", DisplayName = "正交视野大小 (Fov)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "镜头与透视控制", JsonName = "perspective", DisplayName = "开启 3D 透视 (Perspective)", DataType = TrackDataType.Boolean },
            
            // 🌌 场景全局控制
            new TrackBlueprint { GroupName = "场景全局控制", JsonName = "background_dim", DisplayName = "背景暗化 (BG Dim)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "场景全局控制", JsonName = "ui_opacity", DisplayName = "游戏UI透明度 (UI Opacity)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "场景全局控制", JsonName = "storyboard_opacity", DisplayName = "故事板总透明度 (SB Opacity)", DataType = TrackDataType.Float },
            
            // ✨ 画面滤镜与特效
            new TrackBlueprint { GroupName = "画面滤镜与特效", JsonName = "bloom", DisplayName = "开启泛光滤镜 (Bloom)", DataType = TrackDataType.Boolean },
            new TrackBlueprint { GroupName = "画面滤镜与特效", JsonName = "bloom_color", DisplayName = "泛光颜色", DataType = TrackDataType.Color },
            new TrackBlueprint { GroupName = "画面滤镜与特效", JsonName = "bloom_intensity", DisplayName = "泛光强度", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "画面滤镜与特效", JsonName = "bloom_threshold", DisplayName = "泛光阈值", DataType = TrackDataType.Float },

            new TrackBlueprint { GroupName = "画面滤镜与特效", JsonName = "vignette", DisplayName = "开启暗角滤镜 (Vignette)", DataType = TrackDataType.Boolean },
            new TrackBlueprint { GroupName = "画面滤镜与特效", JsonName = "vignette_color", DisplayName = "暗角颜色", DataType = TrackDataType.Color },
            new TrackBlueprint { GroupName = "画面滤镜与特效", JsonName = "vignette_start", DisplayName = "暗角起始范围", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "画面滤镜与特效", JsonName = "vignette_end", DisplayName = "暗角结束范围", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "画面滤镜与特效", JsonName = "vignette_intensity", DisplayName = "暗角强度", DataType = TrackDataType.Float },

            new TrackBlueprint { GroupName = "画面滤镜与特效", JsonName = "arcade", DisplayName = "开启街机模式 (Arcade)", DataType = TrackDataType.Boolean },
            new TrackBlueprint { GroupName = "画面滤镜与特效", JsonName = "arcade_contrast", DisplayName = "街机对比度", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "画面滤镜与特效", JsonName = "arcade_interference_size", DisplayName = "干扰条大小", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "画面滤镜与特效", JsonName = "arcade_interference_speed", DisplayName = "干扰条速度", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "画面滤镜与特效", JsonName = "tape", DisplayName = "开启录像带翻滚 (Tape)", DataType = TrackDataType.Boolean }
        };

        // ==========================================
        // 🟣 音符控制器 (Note Controller) 蓝图
        // ==========================================
        public static readonly List<TrackBlueprint> NoteControllerBlueprints = new List<TrackBlueprint>
        {
            // 📐 音符位移与乘区
            new TrackBlueprint { GroupName = "音符位移与乘区", JsonName = "override_x", DisplayName = "覆盖原始X坐标 (Override X)", DataType = TrackDataType.Boolean },
            new TrackBlueprint { GroupName = "音符位移与乘区", JsonName = "override_y", DisplayName = "覆盖原始Y坐标 (Override Y)", DataType = TrackDataType.Boolean },
            new TrackBlueprint { GroupName = "音符位移与乘区", JsonName = "x_offset", DisplayName = "X轴偏移 (X Offset)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "音符位移与乘区", JsonName = "y_offset", DisplayName = "Y轴偏移 (Y Offset)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "音符位移与乘区", JsonName = "z_offset", DisplayName = "Z轴偏移 (Z Offset)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "音符位移与乘区", JsonName = "x_multiplier", DisplayName = "X轴拉伸乘区 (X Mult)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "音符位移与乘区", JsonName = "y_multiplier", DisplayName = "Y轴速度乘区 (Y Mult)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "音符位移与乘区", JsonName = "z_multiplier", DisplayName = "Z轴拉伸乘区 (Z Mult)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "音符位移与乘区", JsonName = "size_multiplier", DisplayName = "音符大小乘区 (Size Mult)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "音符位移与乘区", JsonName = "opacity_multiplier", DisplayName = "透明度乘区 (Opacity Mult)", DataType = TrackDataType.Float },
            
            // 🎨 音符颜色与特效
            new TrackBlueprint { GroupName = "音符颜色与特效", JsonName = "note_color", DisplayName = "音符主颜色 (Note Color)", DataType = TrackDataType.Color },
            new TrackBlueprint { GroupName = "音符颜色与特效", JsonName = "fallback_color", DisplayName = "拖拽轨迹颜色 (Fallback Color)", DataType = TrackDataType.Color },
            new TrackBlueprint { GroupName = "音符颜色与特效", JsonName = "note_fill_colors", DisplayName = "12等分内部颜色组", DataType = TrackDataType.NoteColorArray }
        };
    }
}