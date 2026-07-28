namespace Naziki_Editor.Core.Timeline.EventBlocks.Models
{
    /// <summary>
    /// Represents the genetic type of a timeline clip entity.
    /// </summary>
    public enum ClipGeneType
    {
        /// <summary>Normal entity with standard time-based keyframes.</summary>
        Normal,

        /// <summary>Entity bound to a $note macro expression.</summary>
        MacroBinding,

        /// <summary>Global controller (scene or note controller without TargetId).</summary>
        GlobalController
    }

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
}
