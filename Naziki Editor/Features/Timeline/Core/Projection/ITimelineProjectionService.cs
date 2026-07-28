using Naziki_Editor.Models;
using Naziki_Editor.State;
using Naziki_Editor.Core.Abstractions;

namespace Naziki_Editor.Core.Timeline.Projection;

public interface ITimelineProjectionService
{
    EntityTimelineProjection BuildEntityProjection(
        IStoryboardEntity entity,
        ProjectDataContext? context);

    IReadOnlyList<CanonicalEntityTimelineProjection> BuildCanonicalProjections(
        EditorStoryboardDocument document,
        C2Chart? chart,
        ITimeEngine? timeEngine);
}
