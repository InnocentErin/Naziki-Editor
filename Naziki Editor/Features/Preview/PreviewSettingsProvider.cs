using Naziki_Editor.Core.Abstractions;

namespace Naziki_Editor.Features.Preview;

public interface IPreviewSettingsProvider
{
    PreviewSettings Current { get; }
    event EventHandler<PreviewSettings>? Changed;
}

public sealed class PreviewSettingsProvider : IPreviewSettingsProvider, IDisposable
{
    private readonly ISettingsStore _settings;

    public PreviewSettingsProvider(ISettingsStore settings)
    {
        _settings = settings;
        _settings.SettingChanged += OnSettingChanged;
    }

    public PreviewSettings Current => new(
        _settings.Get("Editor.PreviewQuality", "Medium"),
        ParseRenderScale(_settings.Get("Performance.PreviewRenderScale", "100%")),
        _settings.Get("Performance.PreviewFrameRate", "60"),
        _settings.Get("Performance.PreviewAdaptiveQuality", true),
        _settings.Get("Performance.PreviewInactiveFrameRate", 15),
        _settings.Get("Performance.FrameSkipThreshold", 16.67d),
        _settings.Get("Performance.RenderThreads", 4),
        Math.Max(64, _settings.Get("Performance.MaxCacheSize", 512)) * 1024L * 1024L,
        _settings.Get("Performance.HardwareAcceleration", true),
        ParseExternalClockRate(_settings.Get("Performance.PreviewExternalClockRate", "50")),
        ParseRenderScale(_settings.Get("Performance.PreviewAdaptiveMinimumScale", "50%")),
        ParseAspectRatio(_settings.Get("Editor.PreviewAspectRatio", "16:9")));

    public event EventHandler<PreviewSettings>? Changed;

    private void OnSettingChanged(object? sender, Core.Settings.SettingsChangedEventArgs e)
    {
        if (e.Key.StartsWith("Performance.", StringComparison.Ordinal) ||
            string.Equals(e.Key, "Editor.PreviewQuality", StringComparison.Ordinal) ||
            string.Equals(e.Key, "Editor.PreviewAspectRatio", StringComparison.Ordinal))
            Changed?.Invoke(this, Current);
    }

    internal static int ParseRenderScale(string? value) =>
        int.TryParse(value?.Trim().TrimEnd('%'), out var result)
            ? Math.Clamp(result, 50, 125)
            : 100;

    internal static string ParseAspectRatio(string? value) =>
        value is "4:3" or "21:9" ? value : "16:9";

    internal static int ParseExternalClockRate(string? value) =>
        int.TryParse(value, out var result) ? Math.Clamp(result, 30, 60) : 50;

    public void Dispose() => _settings.SettingChanged -= OnSettingChanged;
}
