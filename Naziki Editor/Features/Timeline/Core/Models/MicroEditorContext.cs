using Naziki_Editor.Models;

namespace Naziki_Editor.Core.Timeline.Models
{
    /// <summary>
    /// 宏→微导航数据契约：从宏观时间轴向微观时光屋传递导航参数。
    /// 不直接传递 EventBlockViewModel 或 ProjectDataContext，实现接口隔离。
    /// </summary>
    public class MicroEditorContext
    {
        /// <summary>
        /// 关联的故事板实体对象
        /// </summary>
        public IStoryboardEntity Entity { get; init; }

        /// <summary>
        /// 方块显示名称
        /// </summary>
        public string DisplayName { get; init; }

        /// <summary>
        /// 宏观时间轴上的起点秒数（仅用于摄像机初始定位）
        /// </summary>
        public double MacroStartTime { get; init; }

        /// <summary>
        /// 宏观时间轴上的终点秒数（仅用于高亮生命周期）
        /// </summary>
        public double MacroEndTime { get; init; }

        /// <summary>
        /// 初始缩放倍率（像素/秒）
        /// </summary>
        public double InitialPixelsPerSecond { get; init; }
    }
}
