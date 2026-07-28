using System;
using System.Threading.Tasks;
using Naziki_Editor.Core.Abstractions;

namespace Naziki_Editor.Core.Audio
{
    /// <summary>
    /// 音频同步引擎适配器，将单例 AudioSyncEngine 包装为 IAudioSyncEngine 接口实现。
    /// </summary>
    public class AudioSyncEngineAdapter : IAudioSyncEngine
    {
        private readonly AudioSyncEngine _engine;

        public AudioSyncEngineAdapter(AudioSyncEngine engine)
        {
            _engine = engine;
        }

        public bool IsLoaded => _engine.IsLoaded;

        public bool IsPlaying => _engine.IsPlaying;

        public double Duration => _engine.Duration;

        public double[]? WaveformSamples
        {
            get
            {
                var samples = _engine.WaveformSamples;
                if (samples == null) return null;

                var result = new double[samples.Length];
                for (int i = 0; i < samples.Length; i++)
                {
                    result[i] = samples[i];
                }

                return result;
            }
        }

        public double WaveformSampleRate => _engine.WaveformSampleRate;

        public event Action<bool>? OnPlayStateChanged
        {
            add { if (value != null) _engine.OnPlayStateChanged += value; }
            remove { if (value != null) _engine.OnPlayStateChanged -= value; }
        }

        public event Action<double>? OnTimeChanged
        {
            add { if (value != null) _engine.OnTimeChanged += value; }
            remove { if (value != null) _engine.OnTimeChanged -= value; }
        }

        public event Action? OnAudioLoaded
        {
            add { if (value != null) _engine.OnAudioLoaded += value; }
            remove { if (value != null) _engine.OnAudioLoaded -= value; }
        }

        public Task LoadAudioAsync(string filePath) => _engine.LoadAudioAsync(filePath);
        public void Unload() => _engine.Unload();

        public void Play() => _engine.Play();

        public void Pause() => _engine.Pause();

        public void Seek(double seconds) => _engine.Seek(seconds);

        public double GetCurrentSmoothTime() => _engine.GetCurrentSmoothTime();
    }
}
