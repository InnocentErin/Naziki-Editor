using System;
using System.Collections.Generic;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Timeline.Abstractions;
using Naziki_Editor.Core.Timeline.Shared;
using Naziki_Editor.Models;
using Naziki_Editor.State;
using Naziki_Editor.Core.Timeline.Projection;

namespace Naziki_Editor.Core.Timeline.Services
{
    /// <summary>
    /// Implementation of <see cref="IMainTimelineService"/> that delegates
    /// coordinate conversion to <see cref="TimelineCoordEngine"/> and
    /// entity/time operations to <see cref="StoryboardTimeConverter"/>.
    /// </summary>
    public class MainTimelineService : IMainTimelineService
    {
        private ProjectDataContext _context;
        private readonly TimelineCoordEngine _coordEngine;

        public MainTimelineService(TimelineCoordEngine coordEngine)
        {
            _coordEngine = coordEngine;
        }

        /// <summary>
        /// 设置当前项目上下文（在加载项目后调用）。
        /// </summary>
        public void SetContext(ProjectDataContext context)
        {
            _context = context;
        }

        public double TimeToX(double seconds)
        {
            return _coordEngine.TimeToX(seconds);
        }

        public double XToTime(double x)
        {
            return _coordEngine.XToTime(x);
        }

        public double SnapTime(double seconds)
        {
            const double grid = 0.1;
            return Math.Round(seconds / grid) * grid;
        }

        public double CalculateEntityEndTime(IStoryboardEntity entity, double startTime)
        {
            return new TimelineProjectionService()
                .BuildEntityProjection(entity, _context)
                .LastStateTime;
        }

        public object UpdateTimeExpressionByDelta(object originalTime, double deltaTime)
        {
            return TimeExpressionUpdater.UpdateTimeExpressionByDelta(originalTime, deltaTime);
        }

        public void ScaleKeyframes(IStoryboardEntity entity, double oldStart, double oldEnd, double newStart, double newEnd)
        {
            KeyframeScaler.ScaleInternalKeyframes(
                entity,
                oldStart,
                oldEnd,
                newStart,
                newEnd,
                _context?.TimeEngine,
                _context?.Chart?.note_list);
        }
    }

    [Obsolete("Use MainTimelineService.")]
    public sealed class MacroTimelineService : MainTimelineService, IMacroTimelineService
    {
        public MacroTimelineService(TimelineCoordEngine coordEngine) : base(coordEngine) { }
    }
}
