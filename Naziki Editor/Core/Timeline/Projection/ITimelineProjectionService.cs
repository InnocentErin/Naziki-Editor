using Naziki_Editor.Models;
using Naziki_Editor.State;

namespace Naziki_Editor.Core.Timeline.Projection;

public interface ITimelineProjectionService
{
    EntityTimelineProjection BuildEntityProjection(
        IStoryboardEntity entity,
        ProjectDataContext? context);
}
