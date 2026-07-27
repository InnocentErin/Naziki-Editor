using System.Globalization;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Settings;

namespace Naziki_Editor.Core.Timeline.Settings;

public sealed class TimelineSettingsProvider : ITimelineSettings, IDisposable
{
    private readonly ISettingsStore _store;

    public TimelineSettingsProvider(ISettingsStore store)
    {
        _store = store;
        _store.SettingChanged += OnSettingChanged;
        Reload();
    }

    public event EventHandler? Changed;
    public TimelineSettingsSnapshot Current { get; private set; } = new();

    public void Reload()
    {
        var minimum = Clamp(Number("Timeline.MinimumPixelsPerSecond", 10), 1, 100);
        var maximum = Clamp(Number("Timeline.MaximumPixelsPerSecond", 1000), 200, 5000);
        if (maximum < minimum)
            maximum = minimum;

        Current = new TimelineSettingsSnapshot
        {
            AutoExpandTracks = Bool("Timeline.AutoExpandTracks", true),
            AutoScrollDuringPlayback = Bool("Timeline.AutoScrollDuringPlayback", true),
            PlayheadFollowMode = Text("Timeline.PlayheadFollowMode", "Page"),
            InitialPixelsPerSecond = Clamp(Number("Timeline.InitialPixelsPerSecond", 100), minimum, maximum),
            MinimumPixelsPerSecond = minimum,
            MaximumPixelsPerSecond = maximum,
            ZoomStepPercent = Clamp(Number("Timeline.ZoomStepPercent", 20), 5, 100),
            MouseWheelZoomModifier = Text("Timeline.MouseWheelZoomModifier", "Ctrl"),
            TrackHeight = Clamp(Number("Timeline.TrackHeight", 40), 24, 96),
            MicroTrackHeight = Clamp(Number("Timeline.MicroTrackHeight", 40), 28, 120),
            ZeroDurationMarkerWidth = Clamp(Number("Timeline.ZeroDurationMarkerWidth", 8), 3, 24),
            TimeDisplayFormat = Text("Timeline.TimeDisplayFormat", "Seconds"),
            ColorMode = Text("Timeline.ColorMode", "Category"),
            SnapEnabled = Bool("Timeline.SnapEnabled", true),
            SnapIntervalSeconds = Clamp(Number("Timeline.SnapIntervalSeconds", .1), .001, 10),
            SnapToPlayhead = Bool("Timeline.SnapToPlayhead", true),
            SnapToEventEdges = Bool("Timeline.SnapToEventEdges", true),
            SnapToKeyframes = Bool("Timeline.SnapToKeyframes", true),
            SnapToNotes = Bool("Timeline.SnapToNotes", true),
            SnapTolerancePixels = Clamp(Number("Timeline.SnapTolerancePixels", 8), 2, 30),
            NudgeStepSeconds = Clamp(Number("Timeline.NudgeStepSeconds", .01), .001, 10),
            LargeNudgeStepSeconds = Clamp(Number("Timeline.LargeNudgeStepSeconds", .1), .001, 30),
            DefaultEasing = Text("Timeline.DefaultEasing", "EaseInOutQuad"),
            TemplateResizePolicy = Text("Timeline.TemplateResizePolicy", "AskThenDetach"),
            ShowTemplateExpandedFrames = Bool("Timeline.ShowTemplateExpandedFrames", true),
            ShowTemplateSourceLabels = Bool("Timeline.ShowTemplateSourceLabels", true),
            ConfirmTemplateDetach = Bool("Timeline.ConfirmTemplateDetach", true),
            CurveDisplayMode = Text("Timeline.CurveDisplayMode", "Auto"),
            ShowInvalidTimeLane = Bool("Timeline.ShowInvalidTimeLane", true)
        };
    }

    private void OnSettingChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (!e.Key.StartsWith("Timeline.", StringComparison.OrdinalIgnoreCase))
            return;
        Reload();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private string Text(string key, string fallback) =>
        Convert.ToString(_store.Get<object?>(key), CultureInfo.InvariantCulture) ?? fallback;

    private bool Bool(string key, bool fallback)
    {
        var value = _store.Get<object?>(key);
        return value == null ? fallback : Convert.ToBoolean(value, CultureInfo.InvariantCulture);
    }

    private double Number(string key, double fallback)
    {
        var value = _store.Get<object?>(key);
        if (value == null) return fallback;
        try { return Convert.ToDouble(value, CultureInfo.InvariantCulture); }
        catch { return fallback; }
    }

    private static double Clamp(double value, double min, double max) => Math.Clamp(value, min, max);

    public void Dispose() => _store.SettingChanged -= OnSettingChanged;
}
