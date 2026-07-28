using Naziki_Editor.Core.Timeline.Shared;
using Naziki_Editor.Core.Timeline.EventBlocks.Models;
using Naziki_Editor.Models;
using Naziki_Editor.State;

namespace Naziki_Editor.Core.Timeline.EventBlocks.Abstractions
{
    /// <summary>
    /// Service interface for timeline clip operations: coordinate conversion,
    /// time expression updates, entity genetics inspection, and keyframe scaling.
    /// </summary>
    public interface IEventBlockService
    {
        /// <summary>Set the current project context.</summary>
        void SetContext(ProjectDataContext context);

        /// <summary>Update the pixels-per-second zoom level.</summary>
        void SetPixelsPerSecond(double pps);

        /// <summary>Convert time in seconds to pixel X coordinate.</summary>
        double TimeToX(double seconds);

        /// <summary>Convert pixel X coordinate to time in seconds.</summary>
        double XToTime(double x);

        /// <summary>
        /// Settle a clip drag operation: update the base state Time expression
        /// and scale all internal keyframes proportionally.
        /// </summary>
        void SettleDrag(IStoryboardEntity entity, double oldStartTime, double oldEndTime, double newStartTime, double newEndTime);

        /// <summary>
        /// Inspect the genetics of a clip entity to determine its type.
        /// </summary>
        ClipGeneType InspectGenetics(IStoryboardEntity entity);

        /// <summary>
        /// Determine if an entity is a global controller (scene or note controller without TargetId).
        /// </summary>
        bool IsGlobalController(IStoryboardEntity entity);
    }
}
