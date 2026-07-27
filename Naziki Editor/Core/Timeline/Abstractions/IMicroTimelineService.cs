using System.Collections.Generic;
using Naziki_Editor.Core.Timeline.Shared;
using Naziki_Editor.Models;
using Naziki_Editor.State;

namespace Naziki_Editor.Core.Timeline.Abstractions
{
    /// <summary>
    /// 微观时光屋服务抽象，负责单实体属性关键帧的解码、视觉时间反写及时间/坐标换算。
    /// </summary>
    public interface IMicroTimelineService
    {
        /// <summary>Set the current project context (called after loading a project).</summary>
        void SetContext(ProjectDataContext context);

        /// <summary>
        /// 解码指定实体的指定属性关键帧，返回相对于方块起点的视觉时间节点集合。
        /// </summary>
        List<DecodedKeyframeBox> DecodeKeyframes(IStoryboardEntity entity, string propertyName, double clipStartTime);

        /// <summary>
        /// 拖拽后将新的视觉相对时间反写回底层关键帧数据。
        /// </summary>
        void WriteBackVisualTime(IStoryboardEntity entity, ObjectState targetState, double newVisualRelTime, double clipStartTime);

        /// <summary>
        /// 时间（秒）转换为像素 X 坐标。
        /// </summary>
        double TimeToX(double seconds);

        /// <summary>
        /// 像素 X 坐标转换为时间（秒）。
        /// </summary>
        double XToTime(double x);

        /// <summary>
        /// 当前缩放级别（像素/秒）。
        /// </summary>
        double PixelsPerSecond { get; }

        /// <summary>
        /// 更新缩放级别。
        /// </summary>
        void UpdatePixelsPerSecond(double newPps);
    }
}