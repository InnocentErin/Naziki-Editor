using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Compilation;
using Naziki_Editor.Models;
using Naziki_Editor.State;

namespace Naziki_Editor.Core.Storyboard.Compilation;

/// <summary>
/// Compatibility facade for commands that have not yet moved to
/// IStoryboardRuntimeExporter. It no longer calls StoryboardCompiler or
/// performs controller mitosis as a save side effect.
/// </summary>
public sealed class CompilationService : ICompilationService
{
    public void OptimizeScatteredControllers(ProjectDataContext context,
        OptimizeTarget target)
    {
        if (context is null) return;
        // Optimization remains an explicit edit command. It is intentionally
        // not invoked by save/export.
        ControllerOptimizer.OptimizeControllers(context, target,
            emptyShellCount => true);
    }

    public void SyncTemplateMetadata(ProjectDataContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.StoryboardMeta ??= new StoryboardMeta();
        context.StoryboardMeta.TemplateMetas ??=
            new Dictionary<string, EditorTemplateMeta>();

        foreach (var template in context.EditorStoryboard.Templates)
        {
            if (!context.StoryboardMeta.TemplateMetas.ContainsKey(template.Key))
                context.StoryboardMeta.TemplateMetas[template.Key] =
                    new EditorTemplateMeta();
        }
        foreach (var key in context.StoryboardMeta.TemplateMetas.Keys
                     .Where(key =>
                         !context.EditorStoryboard.Templates.ContainsKey(key))
                     .ToArray())
            context.StoryboardMeta.TemplateMetas.Remove(key);
    }

}
