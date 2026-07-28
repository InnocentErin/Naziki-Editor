using Naziki_Editor.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Naziki_Editor.Core.Settings;
using Naziki_Editor.Features.Audio.Playback;
using Naziki_Editor.Features.Preview;
using Xunit;

namespace Naziki_Editor.Tests;

public sealed class PlaybackCoordinatorTests
{
    [Fact]
    public void ClockSetting_AppliesOnNextPlayAndKeepsPlayersMutuallyExclusive()
    {
        var audio = new FakeAudio { IsLoaded = true };
        var preview = new FakePreview { IsAvailable = true };
        var settings = new FakeSettings();
        var notifications = new FakeNotifications();
        using var coordinator = new PlaybackCoordinator(audio, preview, settings, notifications);

        coordinator.Seek(3.5);
        coordinator.Play();
        Assert.Equal(PlaybackClockSource.UnityPreview, coordinator.EffectiveClock);
        Assert.True(preview.PlayCalled);
        Assert.False(audio.IsPlaying);

        settings.Set("Playback.PrimaryClock", "EditorAudio");
        Assert.Equal(PlaybackClockSource.UnityPreview, coordinator.EffectiveClock);
        coordinator.Pause();
        coordinator.Play();

        Assert.Equal(PlaybackClockSource.EditorAudio, coordinator.EffectiveClock);
        Assert.True(audio.IsPlaying);
        Assert.Equal(PreviewClockMode.External, preview.ClockMode);
        Assert.True(preview.PauseCalled);
    }

    [Fact]
    public void MissingUnity_FallsBackToEditorAudioForThatPlay()
    {
        var audio = new FakeAudio { IsLoaded = true };
        var preview = new FakePreview { IsAvailable = false };
        using var coordinator = new PlaybackCoordinator(
            audio,
            preview,
            new FakeSettings(),
            new FakeNotifications());

        coordinator.Play();

        Assert.Equal(PlaybackClockSource.EditorAudio, coordinator.EffectiveClock);
        Assert.True(audio.IsPlaying);
    }

    [Fact]
    public void PlayAtEnd_SeeksBothClocksToZeroBeforePlaying()
    {
        var audio = new FakeAudio { IsLoaded = true };
        var preview = new FakePreview { IsAvailable = true, Duration = 120 };
        using var coordinator = new PlaybackCoordinator(
            audio,
            preview,
            new FakeSettings(),
            new FakeNotifications());

        coordinator.Seek(120);
        coordinator.Play();

        Assert.Equal(0, coordinator.CurrentTime);
        Assert.Equal(0, audio.Time);
        Assert.Equal(0, preview.CurrentTime);
        Assert.True(preview.PlayCalled);
    }

    [Fact]
    public void UnityEndState_PausesCoordinatorWithoutResettingTime()
    {
        var audio = new FakeAudio { IsLoaded = true };
        var preview = new FakePreview { IsAvailable = true, Duration = 120 };
        using var coordinator = new PlaybackCoordinator(
            audio,
            preview,
            new FakeSettings(),
            new FakeNotifications());

        coordinator.Seek(119);
        coordinator.Play();
        preview.ReportPausedAt(120);

        Assert.False(coordinator.IsPlaying);
        Assert.Equal(120, coordinator.CurrentTime);
    }

    private sealed class FakeAudio : IAudioSyncEngine
    {
        public bool IsLoaded { get; set; }
        public bool IsPlaying { get; private set; }
        public double Duration => 120;
        public double[]? WaveformSamples => [];
        public double WaveformSampleRate => 400;
        public double Time { get; private set; }
        public event Action<bool>? OnPlayStateChanged;
        public event Action<double>? OnTimeChanged;
        public event Action? OnAudioLoaded;
        public Task LoadAudioAsync(string filePath)
        {
            IsLoaded = true;
            OnAudioLoaded?.Invoke();
            return Task.CompletedTask;
        }
        public void Unload()
        {
            IsLoaded = false;
            IsPlaying = false;
            Time = 0;
        }
        public void Play()
        {
            IsPlaying = true;
            OnPlayStateChanged?.Invoke(true);
        }
        public void Pause()
        {
            IsPlaying = false;
            OnPlayStateChanged?.Invoke(false);
        }
        public void Seek(double seconds)
        {
            Time = seconds;
            OnTimeChanged?.Invoke(seconds);
        }
        public double GetCurrentSmoothTime() => Time;
    }

    private sealed class FakePreview : IPreviewPlaybackController
    {
        public bool IsAvailable { get; set; }
        public double CurrentTime { get; private set; }
        public double Duration { get; set; } = 120;
        public PreviewPlaybackState State { get; private set; }
        public bool PlayCalled { get; private set; }
        public bool PauseCalled { get; private set; }
        public PreviewClockMode ClockMode { get; private set; }
        public event EventHandler<double>? TimeChanged;
        public event EventHandler<PreviewPlaybackState>? StateChanged;
        public void Play()
        {
            PlayCalled = true;
            State = PreviewPlaybackState.Playing;
            StateChanged?.Invoke(this, State);
        }
        public void Pause()
        {
            PauseCalled = true;
            State = PreviewPlaybackState.Paused;
            StateChanged?.Invoke(this, State);
        }
        public void Stop()
        {
            State = PreviewPlaybackState.Stopped;
            CurrentTime = 0;
            StateChanged?.Invoke(this, State);
        }
        public void Seek(double seconds)
        {
            CurrentTime = seconds;
            TimeChanged?.Invoke(this, seconds);
        }
        public void BeginScrub(double seconds) => Seek(seconds);
        public void UpdateScrub(double seconds) => Seek(seconds);
        public void CommitScrub(double seconds) => Seek(seconds);
        public void SetClockMode(PreviewClockMode mode) => ClockMode = mode;
        public void SetExternalTime(double seconds) => Seek(seconds);
        public void ReportPausedAt(double seconds)
        {
            CurrentTime = seconds;
            TimeChanged?.Invoke(this, seconds);
            State = PreviewPlaybackState.Paused;
            StateChanged?.Invoke(this, State);
        }
    }

    private sealed class FakeSettings : ISettingsStore
    {
        private readonly Dictionary<string, object?> _values = new();
        public event EventHandler<SettingsChangedEventArgs>? SettingChanged;
        public T Get<T>(string key, T defaultValue = default!) =>
            _values.TryGetValue(key, out var value) && value is T typed ? typed : defaultValue;
        public void Set<T>(string key, T value)
        {
            _values.TryGetValue(key, out var old);
            _values[key] = value;
            SettingChanged?.Invoke(this, new SettingsChangedEventArgs(key, old, value, "Playback"));
        }
        public bool ContainsKey(string key) => _values.ContainsKey(key);
        public IReadOnlyList<SettingsCategory> GetCategories() => [];
        public IReadOnlyList<SettingItem> GetCategoryItems(string categoryKey) => [];
        public void Load() { }
        public void Save() { }
        public void Reset(string key) => _values.Remove(key);
        public void ResetCategory(string categoryKey) { }
        public void RegisterCategory(SettingsCategory category) { }
    }

    private sealed class FakeNotifications : INotificationService
    {
        public void Show(string message, NotificationType type = NotificationType.Info, int durationMs = 3000) { }
        public void ShowSuccess(string message, int durationMs = 3000) { }
        public void ShowWarning(string message, int durationMs = 4000) { }
        public void ShowError(string message, int durationMs = 5000) { }
        public Task ShowAsync(string message, NotificationType type = NotificationType.Info, int durationMs = 3000) =>
            Task.CompletedTask;
        public void DismissAll() { }
    }
}
