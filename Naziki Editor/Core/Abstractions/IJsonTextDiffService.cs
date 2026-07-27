namespace Naziki_Editor.Core.Abstractions;

public sealed record JsonTextDiffResult(
    IReadOnlySet<int> BeforeChangedLines,
    IReadOnlySet<int> AfterChangedLines,
    int ChangeCount)
{
    public int? FirstBeforeLine =>
        BeforeChangedLines.Count == 0 ? null : BeforeChangedLines.Min();

    public int? FirstAfterLine =>
        AfterChangedLines.Count == 0 ? null : AfterChangedLines.Min();
}

public interface IJsonTextDiffService
{
    JsonTextDiffResult Analyze(string beforeJson, string afterJson);
}
