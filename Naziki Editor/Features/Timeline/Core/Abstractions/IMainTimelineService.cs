using System.Collections.Generic;
using Naziki_Editor.Core.Timeline.Shared;
using Naziki_Editor.Models;
using Naziki_Editor.State;

namespace Naziki_Editor.Core.Timeline.Abstractions
{
    /// <summary>
    /// Macro-level timeline operations: coordinate conversion, time snapping,
    /// entity duration calculation, time expression updates, and keyframe scaling.
    /// </summary>
    public interface IMainTimelineService
    {
        /// <summary>Set the current project context (called after loading a project).</summary>
        void SetContext(ProjectDataContext context);

        /// <summary>Convert time in seconds to a pixel X coordinate.</summary>
        double TimeToX(double seconds);

        /// <summary>Convert a pixel X coordinate to time in seconds.</summary>
        double XToTime(double x);

        /// <summary>Snap time to the grid (0.1s intervals).</summary>
        double SnapTime(double seconds);

        /// <summary>Calculate the end time of an entity given its start time.</summary>
        double CalculateEntityEndTime(IStoryboardEntity entity, double startTime);

        /// <summary>Update a time expression by a delta value.</summary>
        object UpdateTimeExpressionByDelta(object originalTime, double deltaTime);

        /// <summary>
        /// Scale all keyframes of an entity proportionally from old time range to new time range.
        /// </summary>
        void ScaleKeyframes(IStoryboardEntity entity, double oldStart, double oldEnd, double newStart, double newEnd);
    }

    [Obsolete("Use IMainTimelineService.")]
    public interface IMacroTimelineService : IMainTimelineService { }
}
