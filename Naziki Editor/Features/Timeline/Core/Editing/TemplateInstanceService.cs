using Naziki_Editor.Core.Timeline.Projection;
using Naziki_Editor.Models;
using Naziki_Editor.State;
using Newtonsoft.Json;

namespace Naziki_Editor.Core.Timeline.Editing;

/// <summary>
/// Materializes the expanded states of an entity so subsequent edits affect only this entity.
/// </summary>
public sealed class TemplateInstanceService : ITemplateInstanceService
{
    private readonly ITimelineProjectionService _projectionService;

    public TemplateInstanceService(ITimelineProjectionService projectionService)
    {
        _projectionService = projectionService;
    }

    public TimelineEditResult DetachInstance(IStoryboardEntity entity, ProjectDataContext context)
    {
        if (entity.GetBaseState() is not ObjectState baseState)
            return TimelineEditResult.Failed("事件没有可编辑的基准状态。");

        var projection = _projectionService.BuildEntityProjection(entity, context);
        if (projection.HasErrors)
            return TimelineEditResult.Failed("模板投影包含错误，修复模板引用后才能解绑。");

        var targetList = entity.GetKeyframes();
        var stateType = targetList.GetType().IsGenericType
            ? targetList.GetType().GetGenericArguments()[0]
            : baseState.GetType();
        var materialized = new List<ObjectState>();

        foreach (var projected in projection.States)
        {
            if (ReferenceEquals(projected.SourceState, baseState))
                continue;
            if (!projected.IsTemplateExpanded &&
                !string.IsNullOrWhiteSpace(projected.SourceState.Template))
                continue;

            var json = JsonConvert.SerializeObject(projected.SourceState);
            if (JsonConvert.DeserializeObject(json, stateType) is not ObjectState clone)
                continue;
            clone.Time = projected.AbsoluteTime;
            clone.RelativeTime = null;
            clone.AddTime = null;
            clone.Template = null;
            materialized.Add(clone);
        }

        targetList.Clear();
        foreach (var state in materialized.OrderBy(s => Convert.ToDouble(s.Time)))
            targetList.Add(state);
        baseState.Template = null;
        return TimelineEditResult.Ok();
    }
}
