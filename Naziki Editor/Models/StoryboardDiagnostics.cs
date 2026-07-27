namespace Naziki_Editor.Models;

public enum StoryboardDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed record StoryboardDiagnostic(
    string Code,
    string Path,
    string Message,
    StoryboardDiagnosticSeverity Severity,
    IExtensibleStoryboardNode? Node = null);

public sealed record UnknownJsonProperty(
    string Name,
    string Path,
    Newtonsoft.Json.Linq.JToken Value,
    IExtensibleStoryboardNode Owner);

public static class StoryboardDiagnosticExtensions
{
    public static IReadOnlyList<StoryboardDiagnostic> AllDiagnostics(this IStoryboardEntity entity)
    {
        var result = new List<StoryboardDiagnostic>(entity.Diagnostics);
        if (entity.GetBaseState() is IExtensibleStoryboardNode baseState)
            result.AddRange(baseState.Diagnostics);
        foreach (var state in entity.GetKeyframes())
            if (state is IExtensibleStoryboardNode node) result.AddRange(node.Diagnostics);
        return result;
    }
}
