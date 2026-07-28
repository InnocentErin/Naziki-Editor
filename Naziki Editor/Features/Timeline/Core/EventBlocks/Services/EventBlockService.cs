using System;
using Naziki_Editor.Core.Timeline.Shared;
using Naziki_Editor.Core.Timeline.EventBlocks.Abstractions;
using Naziki_Editor.Core.Timeline.EventBlocks.Models;
using Naziki_Editor.Models;
using Naziki_Editor.State;

namespace Naziki_Editor.Core.Timeline.EventBlocks.Services
{
    /// <summary>
    /// Implementation of <see cref="IEventBlockService"/> that delegates
    /// coordinate conversion to <see cref="TimelineCoordEngine"/> and
    /// keyframe operations to the shared engine modules.
    /// </summary>
    public class EventBlockService : IEventBlockService
    {
        private ProjectDataContext _context;
        private TimelineCoordEngine _coordEngine;

        public EventBlockService()
        {
            _coordEngine = new TimelineCoordEngine(100.0);
        }

        public void SetContext(ProjectDataContext context)
        {
            _context = context;
        }

        public void SetPixelsPerSecond(double pps)
        {
            _coordEngine.UpdatePixelsPerSecond(pps);
        }

        public double TimeToX(double seconds) => _coordEngine.TimeToX(seconds);

        public double XToTime(double x) => _coordEngine.XToTime(x);

        public void SettleDrag(IStoryboardEntity entity, double oldStartTime, double oldEndTime, double newStartTime, double newEndTime)
        {
            if (entity == null) return;

            // 1. Update the base state Time expression
            double deltaStart = newStartTime - oldStartTime;
            var baseState = entity.GetBaseState();
            if (baseState != null)
            {
                var timeProp = baseState.GetType().GetProperty("Time");
                if (timeProp != null)
                {
                    object oldTime = timeProp.GetValue(baseState);
                    object newTime = TimeExpressionUpdater.UpdateTimeExpressionByDelta(oldTime, deltaStart);
                    timeProp.SetValue(baseState, newTime);
                }
            }

            // 2. Scale internal keyframes proportionally
            KeyframeScaler.ScaleInternalKeyframes(
                entity, oldStartTime, oldEndTime, newStartTime, newEndTime,
                _context?.TimeEngine, _context?.Chart?.note_list);
        }

        public ClipGeneType InspectGenetics(IStoryboardEntity entity)
        {
            if (entity == null) return ClipGeneType.Normal;

            // Check for global controller
            if ((entity is C2SceneController || entity is C2NoteController) && string.IsNullOrEmpty(entity.TargetId))
                return ClipGeneType.GlobalController;

            // Check for $note macro binding
            var baseState = entity.GetBaseState();
            if (baseState != null)
            {
                try
                {
                    string rawTime = "";
                    var timeProp = baseState.GetType().GetProperty("Time");
                    if (timeProp != null)
                    {
                        var timeVal = timeProp.GetValue(baseState);
                        if (timeVal != null) rawTime = timeVal.ToString();
                    }

                    if (rawTime.Contains("$note"))
                        return ClipGeneType.MacroBinding;
                }
                catch { }
            }

            return ClipGeneType.Normal;
        }

        public bool IsGlobalController(IStoryboardEntity entity)
        {
            return (entity is C2SceneController || entity is C2NoteController) && string.IsNullOrEmpty(entity.TargetId);
        }
    }
}
