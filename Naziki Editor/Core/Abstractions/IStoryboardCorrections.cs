using Naziki_Editor.Core.Storyboard.Corrections;
using Naziki_Editor.Models;
using Naziki_Editor.State;

namespace Naziki_Editor.Core.Abstractions;

public interface IStoryboardTimeResolver
{
    EntityTimeResolution ResolveEntity(
        IStoryboardEntity entity,
        ProjectDataContext? context,
        string path);
}

public interface IStoryboardCorrectionAnalyzer
{
    StoryboardCorrectionReport Scan(
        StoryboardRoot document,
        ProjectDataContext? context);
}

public interface IStoryboardCorrectionService
{
    StoryboardCorrectionPreview Preview(
        StoryboardRoot document,
        ProjectDataContext? context,
        StoryboardCorrectionPlan plan);

    StoryboardRoot Apply(
        StoryboardRoot document,
        ProjectDataContext? context,
        StoryboardCorrectionPlan plan);
}
