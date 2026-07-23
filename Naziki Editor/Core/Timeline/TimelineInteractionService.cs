using System;
using System.Collections.Generic;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Models;
using Naziki_Editor.State;

namespace Naziki_Editor.Core.Timeline
{
    /// <summary>
    /// 时间轴交互服务实现，封装时间/坐标换算、关键帧解码、反写与缩放等核心操作。
    /// </summary>
    public class TimelineInteractionService : ITimelineInteractionService
    {
        private readonly ProjectDataContext _context;
        private readonly TimelineCoordEngine _coordEngine;

        public TimelineInteractionService(ProjectDataContext context, TimelineCoordEngine coordEngine)
        {
            _context = context;
            _coordEngine = coordEngine;
        }

        public double TimeToX(double seconds) => _coordEngine.TimeToX(seconds);

        public double XToTime(double x) => _coordEngine.XToTime(x);

        public double SnapTime(double seconds)
        {
            const double grid = 0.1;
            return Math.Round(seconds / grid) * grid;
        }

        public List<DecodedKeyframeBox> DecodeKeyframes(
            IStoryboardEntity entity,
            string propertyName,
            double clipStartTime)
        {
            return StoryboardTimeConverter.DecodeTimelineKeyframes(
                entity,
                propertyName,
                _context?.TimeEngine,
                _context?.Chart?.note_list,
                clipStartTime);
        }

        public void WriteBackVisualTime(
            IStoryboardEntity entity,
            ObjectState targetState,
            double newVisualRelTime,
            double clipStartTime)
        {
            StoryboardTimeConverter.WriteBackVisualTime(
                entity,
                targetState,
                newVisualRelTime,
                _context?.TimeEngine,
                _context?.Chart?.note_list,
                clipStartTime);
        }

        public void ScaleKeyframes(
            IStoryboardEntity entity,
            double oldStart,
            double oldEnd,
            double newStart,
            double newEnd,
            double clipStartTime)
        {
            StoryboardTimeConverter.ScaleInternalKeyframes(
                entity,
                oldStart,
                oldEnd,
                newStart,
                newEnd,
                _context?.TimeEngine,
                _context?.Chart?.note_list);
        }

        public double CalculateEntityEndTime(IStoryboardEntity entity, double startTime)
        {
            return StoryboardTimeConverter.CalculateEntityEndTime(
                entity,
                startTime,
                _context?.TimeEngine,
                _context?.Chart?.note_list);
        }

        public object UpdateTimeExpressionByDelta(object originalTime, double deltaTime)
        {
            return StoryboardTimeConverter.UpdateTimeExpressionByDelta(originalTime, deltaTime);
        }
    }
}
