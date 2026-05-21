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
        // 🟢 场景对象 (Sprite, Text, Line, Video) 通用蓝图
        // ==========================================
        public static readonly List<TrackBlueprint> RenderObjectBlueprints = new List<TrackBlueprint>
        {
            new TrackBlueprint { GroupName = "基础变换", JsonName = "x", DisplayName = "水平坐标 (X)", DataType = TrackDataType.Float, DefaultValue = 0f },
            new TrackBlueprint { GroupName = "基础变换", JsonName = "y", DisplayName = "垂直坐标 (Y)", DataType = TrackDataType.Float, DefaultValue = 0f },
            new TrackBlueprint { GroupName = "基础变换", JsonName = "z", DisplayName = "深度层级 (Z)", DataType = TrackDataType.Float, DefaultValue = 0f },
            new TrackBlueprint { GroupName = "旋转与翻转", JsonName = "rot", DisplayName = "旋转角度 (Rot)", DataType = TrackDataType.Float, DefaultValue = 0f },
            new TrackBlueprint { GroupName = "旋转与翻转", JsonName = "rot_x", DisplayName = "X轴翻转 (Rot X)", DataType = TrackDataType.Float, DefaultValue = 0f },
            new TrackBlueprint { GroupName = "旋转与翻转", JsonName = "rot_y", DisplayName = "Y轴翻转 (Rot Y)", DataType = TrackDataType.Float, DefaultValue = 0f },
            new TrackBlueprint { GroupName = "尺寸与缩放", JsonName = "scale", DisplayName = "整体缩放 (Scale)", DataType = TrackDataType.Float, DefaultValue = 1f },
            new TrackBlueprint { GroupName = "尺寸与缩放", JsonName = "scale_x", DisplayName = "宽度缩放 (Scale X)", DataType = TrackDataType.Float, DefaultValue = 1f },
            new TrackBlueprint { GroupName = "尺寸与缩放", JsonName = "scale_y", DisplayName = "高度缩放 (Scale Y)", DataType = TrackDataType.Float, DefaultValue = 1f },
            new TrackBlueprint { GroupName = "色彩与外观", JsonName = "opacity", DisplayName = "透明度 (Opacity)", DataType = TrackDataType.Float, DefaultValue = 1f },
            new TrackBlueprint { GroupName = "色彩与外观", JsonName = "color", DisplayName = "颜色叠加 (Color)", DataType = TrackDataType.Color, DefaultValue = null }
        };

        // ==========================================
        // 🔵 场景控制器 (Controller) 包罗万象蓝图
        // ==========================================
        public static readonly List<TrackBlueprint> ControllerBlueprints = new List<TrackBlueprint>
        {
            // --- 界面与基础控制 ---
            new TrackBlueprint { GroupName = "界面与音符", JsonName = "ui_opacity", DisplayName = "UI 不透明度", DataType = TrackDataType.Float, DefaultValue = 1f },
            new TrackBlueprint { GroupName = "界面与音符", JsonName = "background_dim", DisplayName = "背景遮罩暗度", DataType = TrackDataType.Float, DefaultValue = 0.85f },
            new TrackBlueprint { GroupName = "界面与音符", JsonName = "note_opacity_multiplier", DisplayName = "音符透明度倍率", DataType = TrackDataType.Float, DefaultValue = 1f },
            new TrackBlueprint { GroupName = "界面与音符", JsonName = "note_ring_color", DisplayName = "音符外圈颜色", DataType = TrackDataType.Color, DefaultValue = null },
            new TrackBlueprint { GroupName = "界面与音符", JsonName = "note_fill_colors", DisplayName = "音符覆盖颜色组", DataType = TrackDataType.NoteColorArray, DefaultValue = null },
            
            // --- 扫描线 ---
            new TrackBlueprint { GroupName = "扫描线", JsonName = "scanline_opacity", DisplayName = "扫描线不透明度", DataType = TrackDataType.Float, DefaultValue = 1f },
            new TrackBlueprint { GroupName = "扫描线", JsonName = "scanline_color", DisplayName = "扫描线颜色", DataType = TrackDataType.Color, DefaultValue = null },
            new TrackBlueprint { GroupName = "扫描线", JsonName = "override_scanline_pos", DisplayName = "覆盖扫描线坐标开关", DataType = TrackDataType.Boolean, DefaultValue = false },
            new TrackBlueprint { GroupName = "扫描线", JsonName = "scanline_pos", DisplayName = "扫描线 Y 坐标", DataType = TrackDataType.Float, DefaultValue = 0f },
            
            // --- 相机系统 ---
            new TrackBlueprint { GroupName = "相机系统", JsonName = "perspective", DisplayName = "开启透视相机(3D)", DataType = TrackDataType.Boolean, DefaultValue = true },
            new TrackBlueprint { GroupName = "相机系统", JsonName = "x", DisplayName = "相机 X 坐标", DataType = TrackDataType.Float, DefaultValue = 0f },
            new TrackBlueprint { GroupName = "相机系统", JsonName = "y", DisplayName = "相机 Y 坐标", DataType = TrackDataType.Float, DefaultValue = 0f },
            new TrackBlueprint { GroupName = "相机系统", JsonName = "z", DisplayName = "相机 Z 坐标", DataType = TrackDataType.Float, DefaultValue = -10f },
            new TrackBlueprint { GroupName = "相机系统", JsonName = "rot_x", DisplayName = "相机旋转 X", DataType = TrackDataType.Float, DefaultValue = 0f },
            new TrackBlueprint { GroupName = "相机系统", JsonName = "rot_y", DisplayName = "相机旋转 Y", DataType = TrackDataType.Float, DefaultValue = 0f },
            new TrackBlueprint { GroupName = "相机系统", JsonName = "rot_z", DisplayName = "相机旋转 Z", DataType = TrackDataType.Float, DefaultValue = 0f },
            new TrackBlueprint { GroupName = "相机系统", JsonName = "size", DisplayName = "正交视图大小 (2D)", DataType = TrackDataType.Float, DefaultValue = 5f },
            new TrackBlueprint { GroupName = "相机系统", JsonName = "fov", DisplayName = "透视视野大小 (3D)", DataType = TrackDataType.Float, DefaultValue = 53.2f },

            // --- 滤镜大礼包 (按文档顺序整理) ---
            new TrackBlueprint { GroupName = "滤镜：色度异常 (Chromatical)", JsonName = "chromatical", DisplayName = "开启滤镜", DataType = TrackDataType.Boolean, DefaultValue = false },
            new TrackBlueprint { GroupName = "滤镜：色度异常 (Chromatical)", JsonName = "chromatical_fade", DisplayName = "透明度", DataType = TrackDataType.Float, DefaultValue = 1f },
            new TrackBlueprint { GroupName = "滤镜：色度异常 (Chromatical)", JsonName = "chromatical_intensity", DisplayName = "强度", DataType = TrackDataType.Float, DefaultValue = 1f },
            new TrackBlueprint { GroupName = "滤镜：色度异常 (Chromatical)", JsonName = "chromatical_speed", DisplayName = "速度", DataType = TrackDataType.Float, DefaultValue = 1f },

            new TrackBlueprint { GroupName = "滤镜：泛光 (Bloom)", JsonName = "bloom", DisplayName = "开启滤镜", DataType = TrackDataType.Boolean, DefaultValue = false },
            new TrackBlueprint { GroupName = "滤镜：泛光 (Bloom)", JsonName = "bloom_intensity", DisplayName = "泛光强度", DataType = TrackDataType.Float, DefaultValue = 2f },

            new TrackBlueprint { GroupName = "滤镜：径向模糊 (Radial Blur)", JsonName = "radial_blur", DisplayName = "开启滤镜", DataType = TrackDataType.Boolean, DefaultValue = false },
            new TrackBlueprint { GroupName = "滤镜：径向模糊 (Radial Blur)", JsonName = "radial_blur_intensity", DisplayName = "模糊强度", DataType = TrackDataType.Float, DefaultValue = 0.025f },

            new TrackBlueprint { GroupName = "滤镜：色彩调整 (Color Adj)", JsonName = "color_adjustment", DisplayName = "开启滤镜", DataType = TrackDataType.Boolean, DefaultValue = false },
            new TrackBlueprint { GroupName = "滤镜：色彩调整 (Color Adj)", JsonName = "brightness", DisplayName = "亮度", DataType = TrackDataType.Float, DefaultValue = 1f },
            new TrackBlueprint { GroupName = "滤镜：色彩调整 (Color Adj)", JsonName = "saturation", DisplayName = "饱和度", DataType = TrackDataType.Float, DefaultValue = 1f },
            new TrackBlueprint { GroupName = "滤镜：色彩调整 (Color Adj)", JsonName = "contrast", DisplayName = "对比度", DataType = TrackDataType.Float, DefaultValue = 1f },

            // 其他散装滤镜
            new TrackBlueprint { GroupName = "其他滤镜", JsonName = "color_filter", DisplayName = "开启屏幕纯色滤镜", DataType = TrackDataType.Boolean, DefaultValue = false },
            new TrackBlueprint { GroupName = "其他滤镜", JsonName = "color_filter_color", DisplayName = "屏幕滤镜颜色", DataType = TrackDataType.Color, DefaultValue = null },
            new TrackBlueprint { GroupName = "其他滤镜", JsonName = "gray_scale", DisplayName = "开启灰度滤镜", DataType = TrackDataType.Boolean, DefaultValue = false },
            new TrackBlueprint { GroupName = "其他滤镜", JsonName = "gray_scale_intensity", DisplayName = "灰度强度", DataType = TrackDataType.Float, DefaultValue = 1f },
            new TrackBlueprint { GroupName = "其他滤镜", JsonName = "noise", DisplayName = "开启噪点滤镜", DataType = TrackDataType.Boolean, DefaultValue = false },
            new TrackBlueprint { GroupName = "其他滤镜", JsonName = "noise_intensity", DisplayName = "噪点强度", DataType = TrackDataType.Float, DefaultValue = 0.235f },
            new TrackBlueprint { GroupName = "其他滤镜", JsonName = "sepia", DisplayName = "开启复古滤镜", DataType = TrackDataType.Boolean, DefaultValue = false },
            new TrackBlueprint { GroupName = "其他滤镜", JsonName = "sepia_intensity", DisplayName = "复古强度", DataType = TrackDataType.Float, DefaultValue = 1f },
            new TrackBlueprint { GroupName = "其他滤镜", JsonName = "dream", DisplayName = "开启梦幻滤镜", DataType = TrackDataType.Boolean, DefaultValue = false },
            new TrackBlueprint { GroupName = "其他滤镜", JsonName = "dream_intensity", DisplayName = "梦幻强度", DataType = TrackDataType.Float, DefaultValue = 1f },
            new TrackBlueprint { GroupName = "其他滤镜", JsonName = "fisheye", DisplayName = "开启鱼眼滤镜", DataType = TrackDataType.Boolean, DefaultValue = false },
            new TrackBlueprint { GroupName = "其他滤镜", JsonName = "fisheye_intensity", DisplayName = "鱼眼强度", DataType = TrackDataType.Float, DefaultValue = 0.5f },
            new TrackBlueprint { GroupName = "其他滤镜", JsonName = "shockwave", DisplayName = "开启冲击波滤镜", DataType = TrackDataType.Boolean, DefaultValue = false },
            new TrackBlueprint { GroupName = "其他滤镜", JsonName = "shockwave_speed", DisplayName = "冲击波速度", DataType = TrackDataType.Float, DefaultValue = 1f },
            
            // Focus 滤镜
            new TrackBlueprint { GroupName = "滤镜：漫画焦点 (Focus)", JsonName = "focus", DisplayName = "开启滤镜", DataType = TrackDataType.Boolean, DefaultValue = false },
            new TrackBlueprint { GroupName = "滤镜：漫画焦点 (Focus)", JsonName = "focus_size", DisplayName = "焦点大小", DataType = TrackDataType.Float, DefaultValue = 1f },
            new TrackBlueprint { GroupName = "滤镜：漫画焦点 (Focus)", JsonName = "focus_color", DisplayName = "焦点线颜色", DataType = TrackDataType.Color, DefaultValue = null },
            new TrackBlueprint { GroupName = "滤镜：漫画焦点 (Focus)", JsonName = "focus_speed", DisplayName = "速度", DataType = TrackDataType.Float, DefaultValue = 5f },
            new TrackBlueprint { GroupName = "滤镜：漫画焦点 (Focus)", JsonName = "focus_intensity", DisplayName = "强度", DataType = TrackDataType.Float, DefaultValue = 0.25f },

            // Arcade & Glitch & Tape
            new TrackBlueprint { GroupName = "滤镜：故障与复古电视", JsonName = "glitch", DisplayName = "开启故障抖动 (Glitch)", DataType = TrackDataType.Boolean, DefaultValue = false },
            new TrackBlueprint { GroupName = "滤镜：故障与复古电视", JsonName = "glitch_intensity", DisplayName = "抖动强度", DataType = TrackDataType.Float, DefaultValue = 1f },
            new TrackBlueprint { GroupName = "滤镜：故障与复古电视", JsonName = "arcade", DisplayName = "开启街机显像管 (Arcade)", DataType = TrackDataType.Boolean, DefaultValue = false },
            new TrackBlueprint { GroupName = "滤镜：故障与复古电视", JsonName = "arcade_intensity", DisplayName = "街机效果强度", DataType = TrackDataType.Float, DefaultValue = 1f },
            new TrackBlueprint { GroupName = "滤镜：故障与复古电视", JsonName = "arcade_interference_size", DisplayName = "干扰条大小", DataType = TrackDataType.Float, DefaultValue = 1f },
            new TrackBlueprint { GroupName = "滤镜：故障与复古电视", JsonName = "arcade_interference_speed", DisplayName = "干扰条速度", DataType = TrackDataType.Float, DefaultValue = 0.5f },
            new TrackBlueprint { GroupName = "滤镜：故障与复古电视", JsonName = "arcade_contrast", DisplayName = "街机对比度", DataType = TrackDataType.Float, DefaultValue = 1f },
            new TrackBlueprint { GroupName = "滤镜：故障与复古电视", JsonName = "tape", DisplayName = "开启录像带翻滚 (Tape)", DataType = TrackDataType.Boolean, DefaultValue = false }
        };

        // ==========================================
        // 🟣 音符控制器 (Note Controller) 蓝图
        // ==========================================
        public static readonly List<TrackBlueprint> NoteControllerBlueprints = new List<TrackBlueprint>
        {
            new TrackBlueprint { GroupName = "音符控制", JsonName = "y_multiplier", DisplayName = "掉落速度乘区 (Y Multiplier)", DataType = TrackDataType.Float, DefaultValue = 1f },
            new TrackBlueprint { GroupName = "音符控制", JsonName = "opacity_multiplier", DisplayName = "透明度乘区 (Opacity Mult)", DataType = TrackDataType.Float, DefaultValue = 1f },
            new TrackBlueprint { GroupName = "音符控制", JsonName = "size_multiplier", DisplayName = "大小乘区 (Size Mult)", DataType = TrackDataType.Float, DefaultValue = 1f }
        };
    }
}