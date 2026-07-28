using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Features.Preview;
using Naziki_Editor.Models;
using Naziki_Editor.State;

namespace Naziki_Editor.Features.Editing;

public sealed record EditorMutation(
    string Description,
    string Source,
    IReadOnlyList<string> EntityIds,
    IReadOnlyList<string> Properties,
    double? AffectedStartTime = null,
    double? AffectedEndTime = null,
    bool RequiresPreviewReset = false);

public interface IEditorMutationService
{
    void Execute(ProjectDataContext context, EditorMutation mutation, Action edit);
    EditorMutationScope Begin(ProjectDataContext context, EditorMutation mutation);
}

public sealed class EditorMutationScope : IDisposable
{
    private readonly Action _commit;
    private bool _completed;
    internal EditorMutationScope(Action commit) => _commit = commit;
    public void Complete()
    {
        if (_completed) return;
        _completed = true;
        _commit();
    }
    public void Dispose() { }
}

public sealed class EditorMutationService : IEditorMutationService
{
    private readonly IHistoryService _history;
    private readonly IStoryboardDocumentWriter _writer;
    private readonly IStoryboardPreviewPublisher _preview;

    public EditorMutationService(
        IHistoryService history,
        IStoryboardDocumentWriter writer,
        IStoryboardPreviewPublisher preview)
    {
        _history = history;
        _writer = writer;
        _preview = preview;
    }

    public void Execute(ProjectDataContext context, EditorMutation mutation, Action edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var before = FindEntities(context.Storyboard)
            .Where(entity => mutation.EntityIds.Contains(entity.Id, StringComparer.Ordinal))
            .ToDictionary(entity => entity.Id, StringComparer.Ordinal);
        _history.RecordSnapshot(context.Storyboard);
        edit();
        Commit(context, mutation, before);
    }

    public EditorMutationScope Begin(ProjectDataContext context, EditorMutation mutation)
    {
        var before = FindEntities(context.Storyboard)
            .Where(entity => mutation.EntityIds.Contains(entity.Id, StringComparer.Ordinal))
            .ToDictionary(entity => entity.Id, StringComparer.Ordinal);
        _history.RecordSnapshot(context.Storyboard);
        return new EditorMutationScope(() => Commit(context, mutation, before));
    }

    private void Commit(
        ProjectDataContext context,
        EditorMutation mutation,
        IReadOnlyDictionary<string, IStoryboardEntity> before)
    {
        context.MarkAsModified();
        if (mutation.RequiresPreviewReset)
        {
            _preview.PublishReset(mutation.Source);
            return;
        }

        var after = FindEntities(context.Storyboard)
            .Where(entity => mutation.EntityIds.Contains(entity.Id, StringComparer.Ordinal))
            .ToDictionary(entity => entity.Id, StringComparer.Ordinal);
        var entities = mutation.EntityIds
            .Distinct(StringComparer.Ordinal)
            .Select(id =>
            {
                after.TryGetValue(id, out var current);
                before.TryGetValue(id, out var previous);
                return new StoryboardEntityChange(
                    id,
                    current is null ? "Delete" : previous is null ? "Add" : "Update",
                    current is null ? null : _writer.WriteNode(current),
                    mutation.Properties)
                {
                    EntityType = (current ?? previous)?.GetType().Name,
                    DependencyIds = new[]
                    {
                        (current ?? previous)?.ParentId,
                        (current ?? previous)?.TargetId
                    }.Where(value => !string.IsNullOrWhiteSpace(value))
                     .Select(value => value!)
                     .Distinct(StringComparer.Ordinal)
                     .ToArray(),
                    AssetReferences = FindAssetReferences(current ?? previous)
                };
            })
            .ToArray();
        _preview.PublishIncremental(
            mutation.Source,
            entities,
            mutation.AffectedStartTime,
            mutation.AffectedEndTime);
    }

    private static IEnumerable<IStoryboardEntity> FindEntities(StoryboardRoot root) =>
        root.sprites.Cast<IStoryboardEntity>()
            .Concat(root.texts)
            .Concat(root.lines)
            .Concat(root.videos)
            .Concat(root.controllers)
            .Concat(root.note_controllers);

    private static IReadOnlyList<string> FindAssetReferences(IStoryboardEntity? entity)
    {
        if (entity is null)
            return [];
        return entity.GetKeyframes()
            .Cast<object?>()
            .Prepend(entity.GetBaseState())
            .Select(state => state?.GetType().GetProperty("Path")?.GetValue(state) as string)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
