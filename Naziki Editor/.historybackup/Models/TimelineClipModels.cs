using System;

namespace Naziki_Editor.Models
{
    // ==========================================
    // 🌟 时间轴专属数据模型 (Timeline 专用)
    // ==========================================

    /// <summary>
    /// 时间轴事件方块的视图模式
    /// </summary>
    public enum ClipViewMode
    {
        /// <summary>
        /// 关键帧模式 (固定Y轴，小菱形 ♦，只允许左右拖拽修改时间)
        /// </summary>
        Keyframe,

        /// <summary>
        /// 透明度模式 (Y轴代表属性大小，小圆点 ● + 连线，允许上下左右自由拖拽)
        /// </summary>
        Opacity
    }

    // TODO: 未来如果咱们要写类似 TimelineClipViewModel 这种帮方块计算坐标的中间层，统统放在这个文件里！
}