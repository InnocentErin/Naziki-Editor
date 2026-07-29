using System.Globalization;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Models;
using Newtonsoft.Json.Linq;

namespace Naziki_Editor.Core.Storyboard.Canonical;

public sealed class StoryboardTemplateViewAdapter :
    IStoryboardTemplateViewAdapter
{
    private const string EditorFrameIdProperty =
        "$naziki_editor_frame_id";
    private readonly IStoryboardDocumentReader _reader;
    private readonly IStoryboardDocumentWriter _writer;
    private readonly IStoryboardImportService _importer;

    public StoryboardTemplateViewAdapter(
        IStoryboardDocumentReader reader,
        IStoryboardDocumentWriter writer,
        IStoryboardImportService importer)
    {
        _reader = reader;
        _writer = writer;
        _importer = importer;
    }

    public C2Template CreateWireView(EditorStoryboardTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        var wire = StoryboardCanonicalValues.ToWireObject(template.BasePatch);
        ApplyBinding(wire, template.RootTemplate);
        if (template.DefaultRelativeSeconds.HasValue)
            wire["relative_time"] = template.DefaultRelativeSeconds.Value;
        if (template.DefaultAddSeconds.HasValue)
            wire["add_time"] = template.DefaultAddSeconds.Value;

        var states = new JArray();
        var previousTemplateOffset = 0d;
        foreach (var frame in template.Frames.OrderBy(item => item.Sequence))
        {
            var state = StoryboardCanonicalValues.ToWireObject(frame.Patch);
            ApplyBinding(state, frame.Template);
            WriteTime(state, frame.Time, ref previousTemplateOffset);
            if (!string.IsNullOrWhiteSpace(frame.Easing))
                state["easing"] = frame.Easing;
            if (frame.Destroy.HasValue)
                state["destroy"] = frame.Destroy.Value;
            if (frame.Reset)
                state["reset"] = true;
            states.Add(state);
        }
        if (states.Count > 0)
            wire["states"] = states;

        var root = new JObject
        {
            ["templates"] = new JObject
            {
                [template.Name] = wire
            }
        };
        var view = _reader.Read(root.ToString()).templates[template.Name];
        var sourceFrames = template.Frames.OrderBy(item => item.Sequence)
            .ToArray();
        for (var index = 0; index < Math.Min(sourceFrames.Length,
                 view.Keyframes.Count); index++)
        {
            view.Keyframes[index].UnknownProperties[
                EditorFrameIdProperty] = sourceFrames[index].FrameId;
        }
        return view;
    }

    public EditorStoryboardTemplate ParseWireView(string name,
        C2Template wireTemplate)
    {
        ArgumentNullException.ThrowIfNull(wireTemplate);
        name = name?.Trim() ?? "";
        if (name.Length == 0)
            throw new ArgumentException("模板名称不能为空。", nameof(name));

        var frameIds = wireTemplate.Keyframes.Select(frame =>
        {
            var id = frame.UnknownProperties.TryGetValue(
                EditorFrameIdProperty, out var value)
                ? value.Value<string>()
                : null;
            frame.UnknownProperties.Remove(EditorFrameIdProperty);
            return id;
        }).ToArray();
        var root = new StoryboardRoot();
        root.templates[name] = wireTemplate;
        var imported = _importer.Import(_writer.Write(root));
        if (!imported.CanReplace || imported.Document is null ||
            !imported.Document.Templates.TryGetValue(name, out var template))
        {
            var details = string.Join(Environment.NewLine,
                imported.Issues.Select(issue =>
                    $"{issue.Path}: {issue.Message}"));
            throw new InvalidOperationException(
                $"模板无法规范化。{Environment.NewLine}{details}");
        }
        for (var index = 0; index < Math.Min(frameIds.Length,
                 template.Frames.Count); index++)
        {
            if (!string.IsNullOrWhiteSpace(frameIds[index]))
                template.Frames[index].FrameId = frameIds[index]!;
        }
        return template;
    }

    private static void ApplyBinding(JObject target,
        EditorTemplateBinding? binding)
    {
        if (binding is null)
            return;
        target["template"] = binding.TemplateName;
        foreach (var property in binding.Overrides.Properties())
            target[property.Name] = property.Value.DeepClone();
    }

    private static void WriteTime(JObject state,
        StoryboardTimePosition position, ref double previousTemplateOffset)
    {
        switch (position.Kind)
        {
            case StoryboardTimeAnchorKind.Absolute:
                state["time"] = position.Seconds ?? 0;
                break;
            case StoryboardTimeAnchorKind.TemplateStart:
                state["relative_time"] =
                    position.OffsetSeconds - previousTemplateOffset;
                previousTemplateOffset = position.OffsetSeconds;
                break;
            case StoryboardTimeAnchorKind.TriggerSpawn:
                state["relative_time"] = position.OffsetSeconds;
                break;
            case StoryboardTimeAnchorKind.Unresolved:
                if (!string.IsNullOrWhiteSpace(position.SourceExpression))
                    state["time"] = position.SourceExpression;
                break;
            default:
                state["time"] = FormatNoteAnchor(position);
                if (position.Kind is StoryboardTimeAnchorKind.NoteAt or
                    StoryboardTimeAnchorKind.CurrentNoteAt &&
                    position.OffsetSeconds != 0)
                {
                    state["add_time"] = position.OffsetSeconds;
                }
                break;
        }
    }

    private static string FormatNoteAnchor(StoryboardTimePosition position)
    {
        var current = position.Kind is
            StoryboardTimeAnchorKind.CurrentNoteIntro or
            StoryboardTimeAnchorKind.CurrentNoteStart or
            StoryboardTimeAnchorKind.CurrentNoteEnd or
            StoryboardTimeAnchorKind.CurrentNoteAt;
        var prefix = position.Kind switch
        {
            StoryboardTimeAnchorKind.NoteIntro or
                StoryboardTimeAnchorKind.CurrentNoteIntro => "intro",
            StoryboardTimeAnchorKind.NoteStart or
                StoryboardTimeAnchorKind.CurrentNoteStart => "start",
            StoryboardTimeAnchorKind.NoteEnd or
                StoryboardTimeAnchorKind.CurrentNoteEnd => "end",
            _ => "at"
        };
        var note = current
            ? "$note"
            : (position.NoteId ?? 0).ToString(CultureInfo.InvariantCulture);
        var third = prefix == "at"
            ? position.HoldPosition ?? 0
            : position.OffsetSeconds;
        return $"{prefix}:{note}:{third.ToString("R", CultureInfo.InvariantCulture)}";
    }
}
