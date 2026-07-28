using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Models;
using Newtonsoft.Json.Linq;

namespace Naziki_Editor.Core.Storyboard.Canonical;

/// <summary>
/// Mutation boundary for canonical storyboard data. UI code should use these
/// commands rather than writing wire DTO time/template fields by reflection.
/// </summary>
public sealed class EditorStoryboardEditService :
    IEditorStoryboardEditService
{
    private readonly IStoryboardMaterializer _materializer;

    public EditorStoryboardEditService(IStoryboardMaterializer materializer) =>
        _materializer = materializer;

    public void MoveFrame(EditorStoryboardDocument document, string frameId,
        double deltaSeconds)
    {
        var frame = FindFrame(document, frameId);
        if (!double.IsFinite(deltaSeconds))
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        // Shift changes OffsetSeconds for note/template/trigger anchors and
        // Seconds only for absolute positions.
        frame.Time = frame.Time.Shift(deltaSeconds);
        Touch(document);
    }

    public void ApplyFramePatch(EditorStoryboardDocument document,
        string frameId, JObject patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        var frame = FindFrame(document, frameId);
        var normalized = (JObject)patch.DeepClone();
        StoryboardCanonicalValues.NormalizeUnits(normalized);
        foreach (var property in normalized.Properties())
            frame.Patch[property.Name] = property.Value.DeepClone();
        Touch(document);
    }

    public void ApplyTemplateFrameOverride(EditorStoryboardDocument document,
        string entityId, string? bindingOwnerFrameId, string templateFrameId,
        JObject patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        var entity = FindEntity(document, entityId);
        var binding = bindingOwnerFrameId is null
            ? entity.RootTemplate
            : entity.Frames.FirstOrDefault(frame =>
                    frame.FrameId == bindingOwnerFrameId)?.Template;
        if (binding is null)
            throw new InvalidOperationException(
                "The selected entity/frame is not template-bound.");
        var normalized = (JObject)patch.DeepClone();
        StoryboardCanonicalValues.NormalizeUnits(normalized);
        if (binding.FrameOverrides.TryGetValue(templateFrameId,
                out var existing))
        {
            foreach (var property in normalized.Properties())
                existing[property.Name] = property.Value.DeepClone();
        }
        else
            binding.FrameOverrides[templateFrameId] = normalized;
        binding.OrphanedOverrides.Remove(templateFrameId);
        Touch(document);
    }

    public void SetNoteInstanceOverride(EditorStoryboardDocument document,
        string entityId, int noteId, JObject? basePatch = null,
        bool? excluded = null)
    {
        var entity = FindEntity(document, entityId);
        if (entity.NoteBinding?.Query is null)
            throw new InvalidOperationException(
                "Per-note overrides require a NoteQuery binding.");
        if (!entity.InstanceOverrides.TryGetValue(noteId, out var instance))
        {
            instance = new EditorNoteInstanceOverride();
            entity.InstanceOverrides[noteId] = instance;
        }
        if (basePatch is not null)
        {
            var normalized = (JObject)basePatch.DeepClone();
            StoryboardCanonicalValues.NormalizeUnits(normalized);
            foreach (var property in normalized.Properties())
                instance.BasePatch[property.Name] =
                    property.Value.DeepClone();
        }
        if (excluded.HasValue) instance.Excluded = excluded.Value;
        Touch(document);
    }

    public void DetachRootTemplate(EditorStoryboardDocument document,
        string entityId, C2Chart? chart, ITimeEngine? timeEngine)
    {
        var entity = FindEntity(document, entityId);
        if (entity.RootTemplate is null)
            throw new InvalidOperationException(
                "The selected entity has no root template binding.");
        if (entity.NoteBinding?.Query is not null)
            throw new InvalidOperationException(
                "Detach a query-bound template per note or remove the query first; a multi-occurrence source cannot be collapsed into one frame list safely.");

        var materialized = _materializer.Materialize(document, chart, timeEngine);
        var occurrence = materialized.Entities.SingleOrDefault(item =>
            item.EditorId == entityId) ??
            throw new InvalidOperationException(
                "The template instance could not be materialized.");
        var basePatch = (JObject)occurrence.BaseState.DeepClone();
        basePatch.Remove("note");
        entity.BasePatch = basePatch;
        entity.Frames = occurrence.Frames.Select((frame, index) =>
            new EditorStoryboardFrame
            {
                FrameId = StoryboardStableId.Create(entity.EditorId,
                    "detached", frame.OccurrenceId),
                Sequence = index,
                Time = frame.Time,
                Patch = (JObject)frame.EffectiveState.DeepClone(),
                Easing = frame.Easing,
                Destroy = frame.Destroy,
                Source = new EditorSourceInfo
                {
                    Path = $"{entity.Source.Path}.detached[{index}]",
                    ImportHash = entity.Source.ImportHash,
                    SourceOrder = index
                }
            }).ToList();
        foreach (var frame in entity.Frames)
            frame.Patch.Remove("note");
        entity.RootTemplate = null;
        entity.InstanceOverrides.Clear();
        Touch(document);
    }

    private static EditorStoryboardEntity FindEntity(
        EditorStoryboardDocument document, string entityId)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Entities.SingleOrDefault(entity =>
                   entity.EditorId == entityId) ??
               throw new KeyNotFoundException(
                   $"Canonical entity '{entityId}' was not found.");
    }

    private static EditorStoryboardFrame FindFrame(
        EditorStoryboardDocument document, string frameId)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Entities.SelectMany(entity => entity.Frames)
                   .Concat(document.Templates.Values.SelectMany(template =>
                       template.Frames))
                   .SingleOrDefault(frame => frame.FrameId == frameId) ??
               throw new KeyNotFoundException(
                   $"Canonical frame '{frameId}' was not found.");
    }

    private static void Touch(EditorStoryboardDocument document) =>
        document.Revision = checked(document.Revision + 1);
}
