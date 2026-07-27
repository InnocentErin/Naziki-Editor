using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Naziki_Editor.Views.MainTimeline
{
    /// <summary>
    /// Manages the audio minimap: waveform drawing, viewport box, and playhead line.
    /// </summary>
    public class TimelineAudioBar
    {
        private readonly FrameworkElement _audioMinimapGrid;
        private readonly Path _waveformPath;
        private readonly Thumb _audioViewportBox;
        private readonly ScrollViewer _scrollTimelineTracks;
        private readonly Line _audioPlayheadLine;

        private double _pixelsPerSecond = 100.0;
        private double _totalDurationSeconds = 60.0;
        private float[] _waveformSamples;

        public TimelineAudioBar(
            FrameworkElement audioMinimapGrid,
            Path waveformPath,
            Thumb audioViewportBox,
            ScrollViewer scrollTimelineTracks,
            Line audioPlayheadLine)
        {
            _audioMinimapGrid = audioMinimapGrid;
            _waveformPath = waveformPath;
            _audioViewportBox = audioViewportBox;
            _scrollTimelineTracks = scrollTimelineTracks;
            _audioPlayheadLine = audioPlayheadLine;
        }

        public void SetWaveformSamples(double[] samples)
        {
            if (samples == null) { _waveformSamples = null; return; }
            _waveformSamples = Array.ConvertAll(samples, x => (float)x);
        }

        public void Update(double pixelsPerSecond, double totalDurationSeconds)
        {
            _pixelsPerSecond = pixelsPerSecond;
            _totalDurationSeconds = totalDurationSeconds;
        }

        public void DrawWaveform()
        {
            if (_waveformPath == null || _waveformSamples == null || _audioMinimapGrid.ActualWidth <= 0) return;
            var samples = _waveformSamples;
            double width = _audioMinimapGrid.ActualWidth, height = 40, midY = height / 2;
            int step = Math.Max(1, samples.Length / (int)width);
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(0, midY), false, false);
                for (int i = 0; i < samples.Length; i += step)
                    ctx.LineTo(new Point((double)i / samples.Length * width, midY - (samples[i] * midY)), true, false);
            }
            geometry.Freeze();
            _waveformPath.Data = geometry;
        }

        public void UpdateViewportBox()
        {
            if (_audioMinimapGrid.ActualWidth == 0 || _totalDurationSeconds <= 0 || _scrollTimelineTracks == null) return;
            double totalWidth = _totalDurationSeconds * _pixelsPerSecond + 200;
            double visibleWidth = _scrollTimelineTracks.ViewportWidth == 0 ? _audioMinimapGrid.ActualWidth : _scrollTimelineTracks.ViewportWidth;
            double scale = _audioMinimapGrid.ActualWidth / totalWidth;
            _audioViewportBox.Width = Math.Max(10, Math.Min(_audioMinimapGrid.ActualWidth, visibleWidth * scale));
            Canvas.SetLeft(_audioViewportBox, _scrollTimelineTracks.HorizontalOffset * scale);
        }

        public void HandleViewportBoxDragDelta(DragDeltaEventArgs e)
        {
            if (_audioMinimapGrid.ActualWidth == 0 || _totalDurationSeconds <= 0 || _scrollTimelineTracks == null) return;
            double totalWidth = _totalDurationSeconds * _pixelsPerSecond + 200;
            double newOffset = _scrollTimelineTracks.HorizontalOffset + e.HorizontalChange * (totalWidth / _audioMinimapGrid.ActualWidth);
            _scrollTimelineTracks.ScrollToHorizontalOffset(Math.Max(0, Math.Min(newOffset, totalWidth - _scrollTimelineTracks.ViewportWidth)));
        }
    }
}