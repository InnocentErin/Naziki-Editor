namespace Naziki_Editor.Features.EditorShell;

public enum ProjectLoadStage
{
    ProjectConfiguration,
    ResourcePaths,
    Chart,
    Storyboard,
    Events,
    Notes,
    Assets,
    EditorSurface,
    Audio,
    Commit
}

public sealed record ProjectLoadProgress(
    ProjectLoadStage Stage,
    string Message,
    int CompletedSteps,
    int TotalSteps)
{
    public double Percentage => TotalSteps == 0
        ? 0
        : CompletedSteps * 100d / TotalSteps;
}

public static class ProjectLoadPipeline
{
    public const int TotalSteps = 13;
    public const int DataPreparationComplete = 7;
    public const int EventsReady = 8;
    public const int EditorContextReady = 9;
    public const int NotesReady = 10;
    public const int AssetsReady = 11;
    public const int TimelineAndAudioReady = 12;
    public const int Complete = TotalSteps;
}

public sealed class ProjectLoadException : Exception
{
    public ProjectLoadStage Stage { get; }
    public string? ResourcePath { get; }

    public ProjectLoadException(
        ProjectLoadStage stage,
        string message,
        Exception innerException,
        string? resourcePath = null)
        : base(message, innerException)
    {
        Stage = stage;
        ResourcePath = resourcePath;
    }
}
