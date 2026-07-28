using Naziki_Editor.Models;

namespace Naziki_Editor.Core.Abstractions;

public enum StoryboardTemplateApplyMode
{
    FillMissing,
    Override
}

public sealed record StoryboardTemplatePropertyIssue(
    string Code,
    string JsonPropertyName,
    string SourcePath,
    string Message,
    StoryboardDiagnosticSeverity Severity);

public interface IStoryboardTemplatePropertyMapper
{
    IReadOnlyList<StoryboardTemplatePropertyIssue> Apply(
        ObjectState target,
        TemplateState source,
        StoryboardTemplateApplyMode mode,
        string sourcePath);

    IReadOnlyList<StoryboardTemplatePropertyIssue> Analyze(
        Type targetStateType,
        TemplateState source,
        string sourcePath);
}
