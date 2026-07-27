using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Naziki_Editor.Views.MainTimeline
{
    /// <summary>
    /// Manages the playhead marker: position updates, drag handling, and time display.
    /// </summary>
    public class TimelinePlayhead
    {
        private readonly TranslateTransform _transRulerHead;
        private readonly FrameworkElement _playheadMarker;
        private readonly TextBlock _txtCurrentTime;
        private readonly Line _audioPlayheadLine;
        private readonly FrameworkElement _audioMinimapGrid;
        private readonly ScrollViewer _scrollTimelineTracks;

        private double _pixelsPerSecond = 100.0;
        private double _totalDurationSeconds = 60.0;
        private double _currentPlayheadSeconds = 0.0;
        private bool _isDraggingPlayhead = false;
        private string _lastTimeText = "";

        public event Action<double> OnPlayheadTimeChanged;

        public TimelinePlayhead(
            TranslateTransform transRulerHead,
            FrameworkElement playheadMarker,
            TextBlock txtCurrentTime,
            Line audioPlayheadLine,
            FrameworkElement audioMinimapGrid,
            ScrollViewer scrollTimelineTracks)
        {
            _transRulerHead = transRulerHead;
            _playheadMarker = playheadMarker;
            _txtCurrentTime = txtCurrentTime;
            _audioPlayheadLine = audioPlayheadLine;
            _audioMinimapGrid = audioMinimapGrid;
            _scrollTimelineTracks = scrollTimelineTracks;
        }

        public double CurrentPlayheadSeconds => _currentPlayheadSeconds;
        public bool IsDragging => _isDraggingPlayhead;

        public void Update(double pixelsPerSecond, double totalDurationSeconds)
        {
            _pixelsPerSecond = pixelsPerSecond;
            _totalDurationSeconds = totalDurationSeconds;
        }

        public void UpdatePosition(double xPos)
        {
            double maxWidth = _totalDurationSeconds * _pixelsPerSecond;
            if (xPos < 0) xPos = 0;
            if (xPos > maxWidth) xPos = maxWidth;

            double currentOffset = _scrollTimelineTracks?.HorizontalOffset ?? 0;
            if (_transRulerHead != null) _transRulerHead.X = xPos - currentOffset;

            if (_audioMinimapGrid != null && _audioPlayheadLine != null && _totalDurationSeconds > 0)
            {
                double ratio = xPos / maxWidth;
                _audioPlayheadLine.X1 = ratio * _audioMinimapGrid.ActualWidth;
                _audioPlayheadLine.X2 = _audioPlayheadLine.X1;
            }

            _currentPlayheadSeconds = xPos / _pixelsPerSecond;
            UpdateTimeDisplay(_currentPlayheadSeconds);
        }

        public void UpdateTimeDisplay(double currentSeconds)
        {
            if (_txtCurrentTime != null)
            {
                string newText = currentSeconds.ToString("0.000") + "s";
                if (_lastTimeText != newText)
                {
                    _txtCurrentTime.Text = newText;
                    _lastTimeText = newText;
                }
            }
        }

        public void HandleRulerMouseDown(MouseButtonEventArgs e, FrameworkElement rulerBorder, ScrollViewer scrollTimelineTracks)
        {
            double visualX = e.GetPosition(rulerBorder).X;
            double offset = scrollTimelineTracks?.HorizontalOffset ?? 0;
            UpdatePosition(visualX + offset);
            OnPlayheadTimeChanged?.Invoke(_currentPlayheadSeconds);
        }

        public void HandlePlayheadMouseDown(MouseButtonEventArgs e)
        {
            _isDraggingPlayhead = true;
            if (_playheadMarker is UIElement uiElement)
                uiElement.CaptureMouse();
            e.Handled = true;
        }

        public void HandlePlayheadMouseMove(MouseEventArgs e, FrameworkElement scrollRuler)
        {
            if (_isDraggingPlayhead && scrollRuler != null)
            {
                double visualX = e.GetPosition(scrollRuler).X;
                double offset = _scrollTimelineTracks?.HorizontalOffset ?? 0;
                UpdatePosition(visualX + offset);
            }
        }

        public void HandlePlayheadMouseUp(MouseButtonEventArgs e)
        {
            if (_isDraggingPlayhead)
            {
                _isDraggingPlayhead = false;
                if (_playheadMarker is UIElement uiElement)
                    uiElement.ReleaseMouseCapture();
                OnPlayheadTimeChanged?.Invoke(_currentPlayheadSeconds);
            }
        }
    }
}