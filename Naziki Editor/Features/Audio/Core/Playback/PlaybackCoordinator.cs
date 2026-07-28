using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Features.Preview;

namespace Naziki_Editor.Features.Audio.Playback;

public enum PlaybackClockSource
{
    UnityPreview,
    EditorAudio
}

public interface IPlaybackCoordinator : IDisposable
{
    bool IsAvailable { get; }
    bool IsPlaying { get; }
    double CurrentTime { get; }
    PlaybackClockSource EffectiveClock { get; }
    event EventHandler<double>? TimeChanged;
    event EventHandler<bool>? PlayStateChanged;
    Task LoadAudioAsync(string filePath);
    void UnloadAudio();
    void Play();
    void Pause();
    void Stop();
    void Seek(double seconds);
    void BeginScrub(double seconds);
    void UpdateScrub(double seconds);
    void CommitScrub(double seconds);
}

public sealed class PlaybackCoordinator : IPlaybackCoordinator
{
    private readonly IAudioSyncEngine _audio;
    private readonly IPreviewPlaybackController _preview;
    private readonly ISettingsStore _settings;
    private readonly INotificationService _notifications;
    private readonly Timer _editorClockTimer;
    private readonly object _sync = new();
    private bool _isPlaying;
    private bool _resumeAfterScrub;
    private double _currentTime;
    private PlaybackClockSource _effectiveClock = PlaybackClockSource.UnityPreview;

    public PlaybackCoordinator(
        IAudioSyncEngine audio,
        IPreviewPlaybackController preview,
        ISettingsStore settings,
        INotificationService notifications)
    {
        _audio = audio;
        _preview = preview;
        _settings = settings;
        _notifications = notifications;
        _editorClockTimer = new Timer(OnEditorClockTick, null, Timeout.Infinite, Timeout.Infinite);
        _preview.TimeChanged += PreviewOnTimeChanged;
        _preview.StateChanged += PreviewOnStateChanged;
        _audio.OnTimeChanged += AudioOnTimeChanged;
        _audio.OnPlayStateChanged += AudioOnPlayStateChanged;
    }

    public bool IsAvailable => _preview.IsAvailable || _audio.IsLoaded;
    public bool IsPlaying => _isPlaying;
    public double CurrentTime
    {
        get { lock (_sync) return _currentTime; }
    }
    public double Duration => Math.Max(_audio.Duration, _preview.Duration);
    public PlaybackClockSource EffectiveClock => _effectiveClock;

    public event EventHandler<double>? TimeChanged;
    public event EventHandler<bool>? PlayStateChanged;

    public Task LoadAudioAsync(string filePath) => _audio.LoadAudioAsync(filePath);

    public void UnloadAudio()
    {
        Pause();
        _audio.Unload();
        SetCurrentTime(0);
    }

    public void Play()
    {
        var requested = ReadDesiredClock();
        var effective = requested;
        if (requested == PlaybackClockSource.UnityPreview && !_preview.IsAvailable)
        {
            if (!_audio.IsLoaded)
            {
                _notifications.ShowWarning("Unity 预览和编辑器音频均不可用，无法播放。");
                return;
            }
            effective = PlaybackClockSource.EditorAudio;
            _notifications.ShowWarning("Unity 预览尚不可用，本次播放已降级到编辑器音频。");
        }
        if (effective == PlaybackClockSource.EditorAudio && !_audio.IsLoaded)
        {
            _notifications.ShowWarning("工程音乐尚未加载，无法使用编辑器音频播放。");
            return;
        }

        var time = CurrentTime;
        var duration = Duration;
        if (duration > 0 && time >= duration - 0.001)
        {
            time = 0;
            SetCurrentTime(0);
            _audio.Seek(0);
            _preview.Seek(0);
        }
        _effectiveClock = effective;
        if (effective == PlaybackClockSource.UnityPreview)
        {
            _editorClockTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _audio.Pause();
            _audio.Seek(time);
            _preview.SetClockMode(PreviewClockMode.Internal);
            _preview.Seek(time);
            _preview.Play();
        }
        else
        {
            _preview.Pause();
            _preview.SetClockMode(PreviewClockMode.External);
            _audio.Seek(time);
            _audio.Play();
            _editorClockTimer.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(20));
        }
        SetPlaying(true);
    }

    public void Pause()
    {
        if (_effectiveClock == PlaybackClockSource.UnityPreview)
            _preview.Pause();
        else
            CaptureEditorTime();
        _audio.Pause();
        _editorClockTimer.Change(Timeout.Infinite, Timeout.Infinite);
        SetPlaying(false);
    }

    public void Stop()
    {
        _audio.Pause();
        _audio.Seek(0);
        _preview.Stop();
        _editorClockTimer.Change(Timeout.Infinite, Timeout.Infinite);
        SetCurrentTime(0);
        SetPlaying(false);
    }

    public void Seek(double seconds)
    {
        var safe = Math.Max(0, seconds);
        SetCurrentTime(safe);
        _audio.Seek(safe);
        if (_effectiveClock == PlaybackClockSource.EditorAudio)
            _preview.SetExternalTime(safe);
        else
            _preview.Seek(safe);
    }

    public void BeginScrub(double seconds)
    {
        _resumeAfterScrub = IsPlaying;
        Pause();
        SetCurrentTime(Math.Max(0, seconds));
        if (_effectiveClock == PlaybackClockSource.UnityPreview)
            _preview.BeginScrub(CurrentTime);
        else
            _preview.SetExternalTime(CurrentTime);
    }

    public void UpdateScrub(double seconds)
    {
        SetCurrentTime(Math.Max(0, seconds));
        if (_effectiveClock == PlaybackClockSource.UnityPreview)
            _preview.UpdateScrub(CurrentTime);
        else
            _preview.SetExternalTime(CurrentTime);
    }

    public void CommitScrub(double seconds)
    {
        SetCurrentTime(Math.Max(0, seconds));
        _audio.Seek(CurrentTime);
        if (_effectiveClock == PlaybackClockSource.UnityPreview)
            _preview.CommitScrub(CurrentTime);
        else
            _preview.SetExternalTime(CurrentTime);
        if (_resumeAfterScrub)
            Play();
        _resumeAfterScrub = false;
    }

    private PlaybackClockSource ReadDesiredClock() =>
        string.Equals(
            _settings.Get("Playback.PrimaryClock", "UnityPreview"),
            "EditorAudio",
            StringComparison.OrdinalIgnoreCase)
            ? PlaybackClockSource.EditorAudio
            : PlaybackClockSource.UnityPreview;

    private void OnEditorClockTick(object? state)
    {
        if (!_isPlaying || _effectiveClock != PlaybackClockSource.EditorAudio || !_audio.IsPlaying)
            return;
        var time = _audio.GetCurrentSmoothTime();
        SetCurrentTime(time);
        _preview.SetExternalTime(time);
    }

    private void CaptureEditorTime()
    {
        if (_audio.IsLoaded)
            SetCurrentTime(_audio.GetCurrentSmoothTime());
    }

    private void PreviewOnTimeChanged(object? sender, double seconds)
    {
        if (_effectiveClock == PlaybackClockSource.UnityPreview)
            SetCurrentTime(seconds);
    }

    private void PreviewOnStateChanged(object? sender, PreviewPlaybackState state)
    {
        if (_effectiveClock == PlaybackClockSource.UnityPreview)
            SetPlaying(state == PreviewPlaybackState.Playing);
        if (state == PreviewPlaybackState.Paused && _effectiveClock == PlaybackClockSource.EditorAudio)
        {
            _audio.Pause();
            _editorClockTimer.Change(Timeout.Infinite, Timeout.Infinite);
            SetCurrentTime(_preview.CurrentTime);
            _audio.Seek(CurrentTime);
            SetPlaying(false);
        }
    }

    private void AudioOnTimeChanged(double seconds)
    {
        if (_effectiveClock == PlaybackClockSource.EditorAudio && !_audio.IsPlaying)
            SetCurrentTime(seconds);
    }

    private void AudioOnPlayStateChanged(bool playing)
    {
        if (_effectiveClock == PlaybackClockSource.EditorAudio)
            SetPlaying(playing);
    }

    private void SetCurrentTime(double seconds)
    {
        lock (_sync) _currentTime = Math.Max(0, seconds);
        TimeChanged?.Invoke(this, CurrentTime);
    }

    private void SetPlaying(bool value)
    {
        if (_isPlaying == value) return;
        _isPlaying = value;
        PlayStateChanged?.Invoke(this, value);
    }

    public void Dispose()
    {
        _editorClockTimer.Dispose();
        _preview.TimeChanged -= PreviewOnTimeChanged;
        _preview.StateChanged -= PreviewOnStateChanged;
        _audio.OnTimeChanged -= AudioOnTimeChanged;
        _audio.OnPlayStateChanged -= AudioOnPlayStateChanged;
    }
}
