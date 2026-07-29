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

    public EditorStoryboardTemplate AddTemplate(
        EditorStoryboardDocument document, string name)
    {
        ArgumentNullException.ThrowIfNull(document);
        name = ValidateTemplateName(name);
        if (document.Templates.ContainsKey(name))
            throw new InvalidOperationException(
                $"模板“{name}”已经存在。");

        var template = new EditorStoryboardTemplate
        {
            TemplateId = StoryboardStableId.Create(document.DocumentId,
                "template", Guid.NewGuid().ToString("N")),
            Name = name,
            Source = new EditorSourceInfo
            {
                Path = $"$.templates.{name}",
                SourceOrder = document.Templates.Count
            }
        };
        document.Templates.Add(name, template);
        Touch(document);
        return template;
    }

    public void UpdateTemplate(EditorStoryboardDocument document,
        string templateId, EditorStoryboardTemplate replacement)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(replacement);
        var (key, existing) = FindTemplate(document, templateId);

        var oldFrames = existing.Frames
            .OrderBy(frame => frame.Sequence).ToArray();
        var newFrames = replacement.Frames
            .OrderBy(frame => frame.Sequence).ToArray();
        var oldFrameIds = oldFrames.Select(frame => frame.FrameId)
            .ToHashSet(StringComparer.Ordinal);
        var claimedOldIds = newFrames.Select(frame => frame.FrameId)
            .Where(oldFrameIds.Contains)
            .ToHashSet(StringComparer.Ordinal);
        for (var index = 0; index < Math.Min(oldFrames.Length,
                 newFrames.Length); index++)
        {
            if (!oldFrameIds.Contains(newFrames[index].FrameId) &&
                !claimedOldIds.Contains(oldFrames[index].FrameId))
            {
                newFrames[index].FrameId = oldFrames[index].FrameId;
                claimedOldIds.Add(oldFrames[index].FrameId);
            }
        }

        var retainedFrameIds = newFrames.Select(frame => frame.FrameId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var binding in EnumerateBindings(document)
                     .Where(binding => string.Equals(binding.TemplateName,
                         existing.Name, StringComparison.Ordinal)))
        {
            foreach (var removedId in binding.FrameOverrides.Keys
                         .Where(id => !retainedFrameIds.Contains(id)).ToArray())
            {
                binding.OrphanedOverrides[removedId] =
                    binding.FrameOverrides[removedId];
                binding.FrameOverrides.Remove(removedId);
            }
        }

        replacement.TemplateId = existing.TemplateId;
        replacement.Name = existing.Name;
        replacement.Source.SourceOrder = existing.Source.SourceOrder;
        document.Templates[key] = replacement;
        Touch(document);
    }

    public void RenameTemplate(EditorStoryboardDocument document,
        string templateId, string newName)
    {
        ArgumentNullException.ThrowIfNull(document);
        newName = ValidateTemplateName(newName);
        var (oldName, template) = FindTemplate(document, templateId);
        if (string.Equals(oldName, newName, StringComparison.Ordinal))
            return;
        if (document.Templates.ContainsKey(newName))
            throw new InvalidOperationException(
                $"模板“{newName}”已经存在。");

        document.Templates.Remove(oldName);
        template.Name = newName;
        template.Source.Path = $"$.templates.{newName}";
        document.Templates.Add(newName, template);
        foreach (var binding in EnumerateBindings(document))
        {
            if (string.Equals(binding.TemplateName, oldName,
                    StringComparison.Ordinal))
            {
                binding.TemplateName = newName;
            }
        }
        Touch(document);
    }

    public IReadOnlyList<string> GetTemplateDependents(
        EditorStoryboardDocument document, string templateId)
    {
        ArgumentNullException.ThrowIfNull(document);
        var (_, template) = FindTemplate(document, templateId);
        var result = new List<string>();
        foreach (var entity in document.Entities)
        {
            if (References(entity.RootTemplate, template.Name))
                result.Add($"实体 {entity.EditorId}");
            foreach (var frame in entity.Frames.Where(frame =>
                         References(frame.Template, template.Name)))
                result.Add($"实体 {entity.EditorId} / 帧 {frame.FrameId}");
        }
        foreach (var owner in document.Templates.Values.Where(owner =>
                     owner.TemplateId != template.TemplateId))
        {
            if (References(owner.RootTemplate, template.Name))
                result.Add($"模板 {owner.Name}");
            foreach (var frame in owner.Frames.Where(frame =>
                         References(frame.Template, template.Name)))
                result.Add($"模板 {owner.Name} / 帧 {frame.FrameId}");
        }
        return result;
    }

    public void DeleteTemplate(EditorStoryboardDocument document,
        string templateId)
    {
        var (key, template) = FindTemplate(document, templateId);
        var dependents = GetTemplateDependents(document, template.TemplateId);
        if (dependents.Count > 0)
            throw new InvalidOperationException(
                $"模板“{template.Name}”仍被以下对象引用：{string.Join("、", dependents)}");
        document.Templates.Remove(key);
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

    private static (string Key, EditorStoryboardTemplate Template) FindTemplate(
        EditorStoryboardDocument document, string templateId)
    {
        var pair = document.Templates.FirstOrDefault(item =>
            string.Equals(item.Value.TemplateId, templateId,
                StringComparison.Ordinal));
        return pair.Value is null
            ? throw new KeyNotFoundException(
                $"Canonical template '{templateId}' was not found.")
            : (pair.Key, pair.Value);
    }

    private static IEnumerable<EditorTemplateBinding> EnumerateBindings(
        EditorStoryboardDocument document)
    {
        foreach (var entity in document.Entities)
        {
            if (entity.RootTemplate is not null)
                yield return entity.RootTemplate;
            foreach (var frame in entity.Frames)
                if (frame.Template is not null)
                    yield return frame.Template;
        }
        foreach (var template in document.Templates.Values)
        {
            if (template.RootTemplate is not null)
                yield return template.RootTemplate;
            foreach (var frame in template.Frames)
                if (frame.Template is not null)
                    yield return frame.Template;
        }
    }

    private static bool References(EditorTemplateBinding? binding,
        string templateName) =>
        binding is not null &&
        string.Equals(binding.TemplateName, templateName,
            StringComparison.Ordinal);

    private static string ValidateTemplateName(string name)
    {
        name = name?.Trim() ?? "";
        if (name.Length == 0)
            throw new ArgumentException("模板名称不能为空。", nameof(name));
        return name;
    }

    private static void Touch(EditorStoryboardDocument document) =>
        document.Revision = checked(document.Revision + 1);
}
