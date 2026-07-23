using System.Threading.Tasks;

namespace Naziki_Editor.Core.Abstractions
{
    /// <summary>
    /// 音频同步引擎抽象，负责音频加载、播放控制与时间输出。
    /// </summary>
    public interface IAudioSyncEngine
    {
        bool IsLoaded { get; }
        bool IsPlaying { get; }
        double Duration { get; }
        double[]? WaveformSamples { get; }
        double WaveformSampleRate { get; }

        event System.Action<bool>? OnPlayStateChanged;
        event System.Action<double>? OnTimeChanged;
        event System.Action? OnAudioLoaded;

        Task LoadAudioAsync(string filePath);
        void Play();
        void Pause();
        void Seek(double seconds);
        double GetCurrentSmoothTime();
    }
}
