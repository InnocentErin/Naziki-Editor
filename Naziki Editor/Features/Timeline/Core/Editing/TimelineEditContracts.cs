using Naziki_Editor.Models;
using Naziki_Editor.State;

namespace Naziki_Editor.Core.Timeline.Editing;

public sealed record TimelineEditResult(bool Success, string? Error = null)
{
    public static TimelineEditResult Ok() => new(true);
    public static TimelineEditResult Failed(string error) => new(false, error);
}

public interface ITimelineEditService
{
    TimelineEditResult MoveEntity(IStoryboardEntity entity, double deltaSeconds, ProjectDataContext context);
    TimelineEditResult ScaleEntity(
        IStoryboardEntity entity,
        double oldStart,
        double oldEnd,
        double newStart,
        double newEnd,
        ProjectDataContext context);
}

public interface ITemplateInstanceService
{
    TimelineEditResult DetachInstance(IStoryboardEntity entity, ProjectDataContext context);
}
