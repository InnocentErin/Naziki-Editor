using Naziki_Editor.Models;
using Naziki_Editor.State;
using Naziki_Editor.Core.Timeline.Shared;

namespace Naziki_Editor.Core.Timeline.Editing;

public sealed class TimelineEditService : ITimelineEditService
{
    public TimelineEditResult MoveEntity(
        IStoryboardEntity entity,
        double deltaSeconds,
        ProjectDataContext context)
    {
        if (entity.GetBaseState() is not ObjectState baseState)
            return TimelineEditResult.Failed("事件没有可编辑的基准状态。");
        if (baseState.Time == null)
            return TimelineEditResult.Failed("事件基准时间为空。");

        baseState.Time = TimeExpressionUpdater.UpdateTimeExpressionByDelta(baseState.Time, deltaSeconds);
        foreach (var item in entity.GetKeyframes())
        {
            if (item is not ObjectState state || state.Time == null)
                continue;
            // Relative/additive times move together with their parent and must not be offset twice.
            if (!state.RelativeTime.HasValue && !state.AddTime.HasValue)
                state.Time = TimeExpressionUpdater.UpdateTimeExpressionByDelta(state.Time, deltaSeconds);
        }
        return TimelineEditResult.Ok();
    }

    public TimelineEditResult ScaleEntity(
        IStoryboardEntity entity,
        double oldStart,
        double oldEnd,
        double newStart,
        double newEnd,
        ProjectDataContext context)
    {
        if (oldEnd <= oldStart)
            return TimelineEditResult.Failed("零时长事件不能按比例缩放。");
        if (newEnd < newStart)
            return TimelineEditResult.Failed("事件结束时间不能早于开始时间。");

        KeyframeScaler.ScaleInternalKeyframes(
            entity, oldStart, oldEnd, newStart, newEnd,
            context.TimeEngine, context.Chart?.note_list);

        if (entity.GetBaseState() is ObjectState baseState && baseState.Time != null)
            baseState.Time = TimeExpressionUpdater.UpdateTimeExpressionByDelta(baseState.Time, newStart - oldStart);
        return TimelineEditResult.Ok();
    }
}
