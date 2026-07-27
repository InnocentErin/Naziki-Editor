namespace Naziki_Editor.Core.Timeline.Settings;

public interface ITimelineSettings
{
    event EventHandler? Changed;
    TimelineSettingsSnapshot Current { get; }
    void Reload();
}

public sealed record TimelineSettingsSnapshot
{
    public bool AutoExpandTracks { get; init; } = true;
    public bool AutoScrollDuringPlayback { get; init; } = true;
    public string PlayheadFollowMode { get; init; } = "Page";
    public double InitialPixelsPerSecond { get; init; } = 100;
    public double MinimumPixelsPerSecond { get; init; } = 10;
    public double MaximumPixelsPerSecond { get; init; } = 1000;
    public double ZoomStepPercent { get; init; } = 20;
    public string MouseWheelZoomModifier { get; init; } = "Ctrl";
    public double TrackHeight { get; init; } = 40;
    public double MicroTrackHeight { get; init; } = 40;
    public double ZeroDurationMarkerWidth { get; init; } = 8;
    public string TimeDisplayFormat { get; init; } = "Seconds";
    public string ColorMode { get; init; } = "Category";
    public bool SnapEnabled { get; init; } = true;
    public double SnapIntervalSeconds { get; init; } = .1;
    public bool SnapToPlayhead { get; init; } = true;
    public bool SnapToEventEdges { get; init; } = true;
    public bool SnapToKeyframes { get; init; } = true;
    public bool SnapToNotes { get; init; } = true;
    public double SnapTolerancePixels { get; init; } = 8;
    public double NudgeStepSeconds { get; init; } = .01;
    public double LargeNudgeStepSeconds { get; init; } = .1;
    public string DefaultEasing { get; init; } = "EaseInOutQuad";
    public string TemplateResizePolicy { get; init; } = "AskThenDetach";
    public bool ShowTemplateExpandedFrames { get; init; } = true;
    public bool ShowTemplateSourceLabels { get; init; } = true;
    public bool ConfirmTemplateDetach { get; init; } = true;
    public string CurveDisplayMode { get; init; } = "Auto";
    public bool ShowInvalidTimeLane { get; init; } = true;
}
