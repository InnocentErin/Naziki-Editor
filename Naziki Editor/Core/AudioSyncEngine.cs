using System;
using System.Threading.Tasks;
using System.Windows.Threading;
using NAudio.Wave;
using NAudio.Vorbis; // 🌟 核心增补：OGG 解码法术！
using System.Diagnostics; // 🌟 高精度时钟

namespace Naziki_Editor.Core
{
    public class AudioSyncEngine
    {
        private static AudioSyncEngine _instance;
        public static AudioSyncEngine Instance => _instance ??= new AudioSyncEngine();

        // 🌟 彻底抛弃 MediaPlayer，换上 NAudio 纯血播放器和读取器！
        private WaveOutEvent _player;
        private WaveStream _audioReader;
        private DispatcherTimer _renderTimer;


        // 🌟 核心增补 1：高精度补帧秒表（取代 DispatcherTimer）
        private Stopwatch _smoothTimer = new Stopwatch();
        private double _seekOffsetSeconds = 0;

        public bool IsPlaying { get; private set; } = false;
        public bool IsLoaded { get; private set; } = false;
        public double Duration { get; private set; } = 0;
        public float[] WaveformSamples { get; private set; }

        // 🌟 核心增补：把采样率公开给全宇宙！(您可以把 100 改成 200 或 400)
        public int WaveformSampleRate { get; private set; } = 400;

        public event Action<double> OnTimeChanged;
        public event Action<bool> OnPlayStateChanged;
        public event Action OnAudioLoaded;



        private AudioSyncEngine()
        {
            _player = new WaveOutEvent() { DesiredLatency = 80 };
            {
                if (IsPlaying && IsLoaded && _audioReader != null)
                {
                    // 🌟 实时广播当前绝对秒数！
                    OnTimeChanged?.Invoke(_audioReader.CurrentTime.TotalSeconds);
                }
            };
        }


        // ==========================================
        // 🌟 新增绝招：向全局渲染引擎提供亚毫秒级丝滑时间
        // ==========================================
        public double GetCurrentSmoothTime()
        {
            if (!IsPlaying || _audioReader == null) return _audioReader?.CurrentTime.TotalSeconds ?? 0;

            double predictedTime = _seekOffsetSeconds + _smoothTimer.Elapsed.TotalSeconds;
            double readerTime = _audioReader.CurrentTime.TotalSeconds;

            // 🌟 核心修复 1：放宽校准阈值！
            // 硬盘缓冲区的读取永远是分块且跳跃的。如果卡得太死(0.05秒)，游标会被拉扯导致严重视觉卡顿！
            // 放宽到 0.3 秒，让 144Hz 的秒表完全接管屏幕滑行，彻底消除拉扯感！
            if (Math.Abs(predictedTime - readerTime) > 0.3)
            {
                _seekOffsetSeconds = readerTime;
                _smoothTimer.Restart();
                return readerTime;
            }

            return predictedTime;
        }




        // =========================================
        // 🌟 核心增补 2：智能音频加载器，自动识别格式并抽取波形数据
        // =========================================
        public async Task LoadAudioAsync(string filePath)
        {
            IsLoaded = false;
            _player.Stop();
            _audioReader?.Dispose();

            // 🌟 智能武器库：如果是 ogg 就用 Vorbis 枪，否则用普通枪！
            string ext = System.IO.Path.GetExtension(filePath).ToLower();
            if (ext == ".ogg")
                _audioReader = new VorbisWaveReader(filePath);
            else
                _audioReader = new AudioFileReader(filePath);

            _player.Init(_audioReader);
            Duration = _audioReader.TotalTime.TotalSeconds;
            IsLoaded = true;

            await ExtractWaveformDataAsync(filePath, ext);
            OnAudioLoaded?.Invoke();
        }

        private async Task ExtractWaveformDataAsync(string filePath, string ext)
        {
            await Task.Run(() =>
            {
                try
                {
                    // 🌟 波形抽取也要智能换枪！
                    using (WaveStream reader = ext == ".ogg" ? (WaveStream)new VorbisWaveReader(filePath) : new AudioFileReader(filePath))
                    {
                        // 🌟 核心修复 1：请出翻译官！将底层的字节流统一转换为浮点采样流
                        ISampleProvider sampleProvider = reader.ToSampleProvider();

                        int totalSamples = (int)(reader.TotalTime.TotalSeconds * WaveformSampleRate);
                        WaveformSamples = new float[totalSamples];

                        // 🌟 核心修复 2：以 SampleProvider 的格式为准去创建盆子
                        float[] buffer = new float[sampleProvider.WaveFormat.SampleRate * sampleProvider.WaveFormat.Channels];
                        int samplesRead;
                        double sampleIndex = 0;

                        // 🌟 核心修复 3：现在 Read 方法接收的就是 float[] 啦！不会再报错了！
                        while ((samplesRead = sampleProvider.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            for (int i = 0; i < samplesRead; i += sampleProvider.WaveFormat.Channels)
                            {
                                float val = Math.Abs(buffer[i]);
                                // 🌟 核心修复：这里也使用 WaveformSampleRate
                                int targetSlot = (int)(sampleIndex / sampleProvider.WaveFormat.SampleRate * WaveformSampleRate);

                                if (targetSlot < totalSamples)
                                {
                                    if (val > WaveformSamples[targetSlot]) WaveformSamples[targetSlot] = val;
                                }
                                sampleIndex++;
                            }
                        }
                    }
                }
                catch { WaveformSamples = null; }
            });
        }

        public void Play()
        {
            if (!IsLoaded) return;
            _player.Play();
            IsPlaying = true;
            _seekOffsetSeconds = _audioReader.CurrentTime.TotalSeconds;
            _smoothTimer.Restart();
            OnPlayStateChanged?.Invoke(IsPlaying);
        }

        public void Pause()
        {
            _player.Pause();
            IsPlaying = false;
            _smoothTimer.Stop();

            // 🌟 核心修复 2：暂停时，强行将底层音频对齐到屏幕看到的绝对平滑时间！
            // 防止按下暂停瞬间，游标因为缓冲区的误差出现“幽灵位移”。
            double exactTime = _seekOffsetSeconds + _smoothTimer.Elapsed.TotalSeconds;
            Seek(exactTime);

            OnPlayStateChanged?.Invoke(IsPlaying);
        }


        // =========================================
        // ⚡ 绝对时间跳转法术：秒表式精准定位
        // =========================================
        public void Seek(double seconds)
        {
            if (!IsLoaded || _audioReader == null) return;
            if (seconds < 0) seconds = 0;
            if (seconds > Duration) seconds = Duration;

            _audioReader.CurrentTime = TimeSpan.FromSeconds(seconds);
            _seekOffsetSeconds = seconds;

            if (IsPlaying) _smoothTimer.Restart();
            if (!IsPlaying) OnTimeChanged?.Invoke(seconds);
        }

    }
}