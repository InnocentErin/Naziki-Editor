namespace Naziki_Editor.Features.Preview;

public interface IPreviewClock
{
    double CurrentTime { get; }
    double Duration { get; }
    PreviewPlaybackState State { get; }
    event EventHandler<double>? TimeChanged;
    event EventHandler<PreviewPlaybackState>? StateChanged;
}

public interface IPreviewPlaybackController : IPreviewClock
{
    bool IsAvailable { get; }
    void Play();
    void Pause();
    void Stop();
    void Seek(double seconds);
    void BeginScrub(double seconds);
    void UpdateScrub(double seconds);
    void CommitScrub(double seconds);
    void SetClockMode(PreviewClockMode mode);
    void SetExternalTime(double seconds);
}

public enum PreviewClockMode
{
    Internal,
    External
}

public sealed record PreviewPlaybackRestorePoint(
    double Time,
    PreviewPlaybackState State,
    PreviewClockMode ClockMode,
    long SnapshotVersion);

public interface IPreviewReloadCoordinator
{
    PreviewPlaybackRestorePoint CaptureRestorePoint();
    Task RestartPlayerAsync();
    Task ReloadLevelAsync(Naziki_Editor.State.ProjectDataContext context, double playbackTime);
    Task RefreshViewportAsync(string aspectRatio, int pixelWidth, int pixelHeight);
}

public interface IUnityPreviewSessionService : IPreviewReloadCoordinator
{
    Task AttachWindowAsync(IntPtr parentWindow, int pixelWidth, int pixelHeight);
    Task ResizeAsync(int pixelWidth, int pixelHeight, bool active);
    Task OpenProjectAsync(Naziki_Editor.State.ProjectDataContext context, double playbackTime = 0);
    Task RetryAsync();
    Task ShutdownAsync();
}
