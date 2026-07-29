using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Models;

namespace Naziki_Editor.Core.Storyboard.Canonical;

public sealed record CanonicalTemplateListItem(
    string TemplateId,
    string Name,
    int SourceOrder,
    int FrameCount,
    int BindingCount,
    EditorStoryboardTemplate Template);

public static class StoryboardTemplateListProjection
{
    public static IReadOnlyList<CanonicalTemplateListItem> Build(
        EditorStoryboardDocument document,
        IEditorStoryboardEditService editService)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(editService);
        return document.Templates.Values
            .OrderBy(template => template.Source.SourceOrder)
            .Select(template => new CanonicalTemplateListItem(
                template.TemplateId,
                template.Name,
                template.Source.SourceOrder,
                template.Frames.Count,
                editService.GetTemplateDependents(document,
                    template.TemplateId).Count,
                template))
            .ToArray();
    }
}
