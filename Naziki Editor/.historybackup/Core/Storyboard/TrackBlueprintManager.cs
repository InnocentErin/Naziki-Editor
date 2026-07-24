using Naziki_Editor.Models;
using System.Collections.Generic;

namespace Naziki_Editor.Core
{
    public enum TrackDataType { Float, String, Color, Boolean, NoteColorArray }

    public class TrackBlueprint
    {
        public string JsonName { get; set; }
        public string DisplayName { get; set; }
        public string GroupName { get; set; }
        public TrackDataType DataType { get; set; }
        public object DefaultValue { get; set; }
    }

    public static class TrackBlueprintManager
    {
        // ==========================================
        // 🔮 场景控制器 (Scene Controller) 终极蓝图
        // ==========================================
        public static readonly List<TrackBlueprint> ControllerBlueprints = new List<TrackBlueprint>
        {
            // 🏷️ 基础与UI控制
            new TrackBlueprint { GroupName = "基础透明度", JsonName = "storyboard_opacity", DisplayName = "故事板总透明度", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "基础透明度", JsonName = "ui_opacity", DisplayName = "游戏UI透明度", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "基础透明度", JsonName = "background_dim", DisplayName = "背景遮罩暗度", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "基础透明度", JsonName = "note_opacity_multiplier", DisplayName = "全场音符透明倍率", DataType = TrackDataType.Float },
            
            // 🏷️ 扫描线与色彩
            new TrackBlueprint { GroupName = "扫描线与色彩", JsonName = "scanline_opacity", DisplayName = "扫描线透明度", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "扫描线与色彩", JsonName = "override_scanline_pos", DisplayName = "允许覆盖扫描线坐标", DataType = TrackDataType.Boolean },
            new TrackBlueprint { GroupName = "扫描线与色彩", JsonName = "scanline_pos", DisplayName = "扫描线 Y 坐标", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "扫描线与色彩", JsonName = "scanline_color", DisplayName = "扫描线覆盖色", DataType = TrackDataType.Color },
            new TrackBlueprint { GroupName = "扫描线与色彩", JsonName = "note_ring_color", DisplayName = "音符外圈覆盖色", DataType = TrackDataType.Color },
            new TrackBlueprint { GroupName = "扫描线与色彩", JsonName = "note_fill_colors", DisplayName = "音符12色阵列", DataType = TrackDataType.NoteColorArray },

            // 🏷️ 相机与3D空间
            new TrackBlueprint { GroupName = "相机空间 (Camera)", JsonName = "perspective", DisplayName = "开启3D透视相机", DataType = TrackDataType.Boolean },
            new TrackBlueprint { GroupName = "相机空间 (Camera)", JsonName = "size", DisplayName = "相机视图大小 (正交)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "相机空间 (Camera)", JsonName = "fov", DisplayName = "相机视野 FOV (透视)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "相机空间 (Camera)", JsonName = "x", DisplayName = "相机 X 坐标", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "相机空间 (Camera)", JsonName = "y", DisplayName = "相机 Y 坐标", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "相机空间 (Camera)", JsonName = "z", DisplayName = "相机 Z 坐标", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "相机空间 (Camera)", JsonName = "rot_x", DisplayName = "相机 X轴 旋转", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "相机空间 (Camera)", JsonName = "rot_y", DisplayName = "相机 Y轴 旋转", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "相机空间 (Camera)", JsonName = "rot_z", DisplayName = "相机 Z轴 旋转", DataType = TrackDataType.Float },

            // 🏷️ 颜色滤镜组
            new TrackBlueprint { GroupName = "基本调色 (Color Adjust)", JsonName = "color_adjustment", DisplayName = "开启基础调色", DataType = TrackDataType.Boolean },
            new TrackBlueprint { GroupName = "基本调色 (Color Adjust)", JsonName = "brightness", DisplayName = "亮度 (0~10)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "基本调色 (Color Adjust)", JsonName = "saturation", DisplayName = "饱和度 (0~10)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "基本调色 (Color Adjust)", JsonName = "contrast", DisplayName = "对比度 (0~10)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "基本调色 (Color Adjust)", JsonName = "color_filter", DisplayName = "开启屏幕颜色滤镜", DataType = TrackDataType.Boolean },
            new TrackBlueprint { GroupName = "基本调色 (Color Adjust)", JsonName = "color_filter_color", DisplayName = "屏幕滤镜颜色", DataType = TrackDataType.Color },
            new TrackBlueprint { GroupName = "基本调色 (Color Adjust)", JsonName = "gray_scale", DisplayName = "开启灰度滤镜", DataType = TrackDataType.Boolean },
            new TrackBlueprint { GroupName = "基本调色 (Color Adjust)", JsonName = "gray_scale_intensity", DisplayName = "灰度强度 (0~1)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "基本调色 (Color Adjust)", JsonName = "sepia", DisplayName = "开启老照片滤镜", DataType = TrackDataType.Boolean },
            new TrackBlueprint { GroupName = "基本调色 (Color Adjust)", JsonName = "sepia_intensity", DisplayName = "老照片强度 (0~1)", DataType = TrackDataType.Float },

            // 🏷️ 特效滤镜组
            new TrackBlueprint { GroupName = "干扰特效 (Distort)", JsonName = "glitch", DisplayName = "开启Glitch(故障)滤镜", DataType = TrackDataType.Boolean },
            new TrackBlueprint { GroupName = "干扰特效 (Distort)", JsonName = "glitch_intensity", DisplayName = "Glitch强度", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "干扰特效 (Distort)", JsonName = "arcade", DisplayName = "开启Arcade(街机)滤镜", DataType = TrackDataType.Boolean },
            new TrackBlueprint { GroupName = "干扰特效 (Distort)", JsonName = "arcade_intensity", DisplayName = "街机效果强度", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "干扰特效 (Distort)", JsonName = "arcade_interference_size", DisplayName = "街机干扰条大小", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "干扰特效 (Distort)", JsonName = "arcade_interference_speed", DisplayName = "街机干扰条速度", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "干扰特效 (Distort)", JsonName = "arcade_contrast", DisplayName = "街机对比度", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "干扰特效 (Distort)", JsonName = "chromatical", DisplayName = "开启色差干扰", DataType = TrackDataType.Boolean },
            new TrackBlueprint { GroupName = "干扰特效 (Distort)", JsonName = "chromatical_intensity", DisplayName = "色差强度", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "干扰特效 (Distort)", JsonName = "chromatical_speed", DisplayName = "色差速度", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "干扰特效 (Distort)", JsonName = "noise", DisplayName = "开启噪点滤镜", DataType = TrackDataType.Boolean },
            new TrackBlueprint { GroupName = "干扰特效 (Distort)", JsonName = "noise_intensity", DisplayName = "噪点强度 (0~1)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "干扰特效 (Distort)", JsonName = "tape", DisplayName = "开启屏幕翻转(Tape)", DataType = TrackDataType.Boolean },

            // 🏷️ 环境滤镜组
            new TrackBlueprint { GroupName = "环境滤镜 (Environment)", JsonName = "bloom", DisplayName = "开启泛光(Bloom)", DataType = TrackDataType.Boolean },
            new TrackBlueprint { GroupName = "环境滤镜 (Environment)", JsonName = "bloom_intensity", DisplayName = "泛光强度 (0~5)", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "环境滤镜 (Environment)", JsonName = "focus", DisplayName = "开启漫画焦点线", DataType = TrackDataType.Boolean },
            new TrackBlueprint { GroupName = "环境滤镜 (Environment)", JsonName = "focus_size", DisplayName = "焦点线大小", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "环境滤镜 (Environment)", JsonName = "focus_color", DisplayName = "焦点线颜色", DataType = TrackDataType.Color },
            new TrackBlueprint { GroupName = "环境滤镜 (Environment)", JsonName = "focus_speed", DisplayName = "焦点线速度", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "环境滤镜 (Environment)", JsonName = "shockwave", DisplayName = "开启震荡波", DataType = TrackDataType.Boolean },
            new TrackBlueprint { GroupName = "环境滤镜 (Environment)", JsonName = "shockwave_speed", DisplayName = "震荡波速度", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "环境滤镜 (Environment)", JsonName = "fisheye", DisplayName = "开启鱼眼镜头", DataType = TrackDataType.Boolean },
            new TrackBlueprint { GroupName = "环境滤镜 (Environment)", JsonName = "fisheye_intensity", DisplayName = "鱼眼强度", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "环境滤镜 (Environment)", JsonName = "radial_blur", DisplayName = "开启径向模糊", DataType = TrackDataType.Boolean },
            new TrackBlueprint { GroupName = "环境滤镜 (Environment)", JsonName = "radial_blur_intensity", DisplayName = "径向模糊强度", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "环境滤镜 (Environment)", JsonName = "dream", DisplayName = "开启梦境滤镜", DataType = TrackDataType.Boolean },
            new TrackBlueprint { GroupName = "环境滤镜 (Environment)", JsonName = "dream_intensity", DisplayName = "梦境强度", DataType = TrackDataType.Float },
            new TrackBlueprint { GroupName = "环境滤镜 (Environment)", JsonName = "vignette", DisplayName = "开启暗角(Vignette)", DataType = TrackDataType.Boolean },
            new TrackBlueprint { GroupName = "环境滤镜 (Environment)", JsonName = "vignette_intensity", DisplayName = "暗角强度", DataType = TrackDataType.Float }
        };

        // 供 UI 调用的分发口
        public static List<TrackBlueprint> GetBlueprintsForType(System.Type type)
        {
            if (type == typeof(ControllerState) || type == typeof(C2SceneController))
                return ControllerBlueprints;
            // 其他如 C2Sprite, C2Text 可以以后继续补充...
            return new List<TrackBlueprint>();
        }
    }
}