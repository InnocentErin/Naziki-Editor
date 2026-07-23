using System.Collections.Generic;
using Naziki_Editor.Models;
using Naziki_Editor.Core;

namespace Naziki_Editor.Core.Abstractions
{
    /// <summary>
    /// 时间轴交互服务抽象，负责时间/坐标换算、关键帧解码/反写/缩放等时间轴核心操作。
    /// </summary>
    public interface ITimelineInteractionService
    {
        double TimeToX(double seconds);
        double XToTime(double x);
        double SnapTime(double seconds);

        List<DecodedKeyframeBox> DecodeKeyframes(
            IStoryboardEntity entity,
            string propertyName,
            double clipStartTime);

        void ScaleKeyframes(
            IStoryboardEntity entity,
            double oldStart,
            double oldEnd,
            double newStart,
            double newEnd,
            double clipStartTime);

        double CalculateEntityEndTime(IStoryboardEntity entity, double startTime);
        object UpdateTimeExpressionByDelta(object originalTime, double deltaTime);

        void WriteBackVisualTime(
            IStoryboardEntity entity,
            ObjectState targetState,
            double newVisualRelTime,
            double clipStartTime);
    }
}
