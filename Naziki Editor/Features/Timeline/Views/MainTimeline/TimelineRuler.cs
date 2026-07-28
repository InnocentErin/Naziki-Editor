using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Naziki_Editor.Views.MainTimeline
{
    /// <summary>
    /// Renders the time ruler with tick marks at the top of the timeline.
    /// </summary>
    public class TimelineRuler
    {
        private readonly Canvas _rulerCanvas;
        private double _pixelsPerSecond = 100.0;
        private double _totalDurationSeconds = 60.0;

        public TimelineRuler(Canvas rulerCanvas)
        {
            _rulerCanvas = rulerCanvas;
        }

        public void Update(double pixelsPerSecond, double totalDurationSeconds)
        {
            _pixelsPerSecond = pixelsPerSecond;
            _totalDurationSeconds = totalDurationSeconds;
            DrawRuler();
        }

        public void DrawRuler()
        {
            if (_rulerCanvas == null) return;
            _rulerCanvas.Children.Clear();
            double majorStep = _pixelsPerSecond >= 100 ? 1.0 : (_pixelsPerSecond >= 40 ? 5.0 : 10.0);
            double minorStep = majorStep / 10.0;

            for (double time = 0; time <= _totalDurationSeconds; time += minorStep)
            {
                double xPos = time * _pixelsPerSecond;
                bool isMajor = Math.Abs(time % majorStep) < 0.001 || Math.Abs((time % majorStep) - majorStep) < 0.001;
                _rulerCanvas.Children.Add(new Line
                {
                    X1 = xPos, Y1 = isMajor ? 15 : 24, X2 = xPos, Y2 = 30,
                    Stroke = (Brush)Application.Current.Resources["BorderColor"],
                    StrokeThickness = isMajor ? 1.2 : 0.6,
                    Opacity = isMajor ? 1 : 0.5
                });
                if (isMajor)
                    _rulerCanvas.Children.Add(new TextBlock
                    {
                        Text = $"{time:0.#}s", FontSize = 9,
                        Foreground = (Brush)Application.Current.Resources["SecTextColor"],
                        RenderTransform = new TranslateTransform { X = xPos + 4, Y = 2 }
                    });
            }
        }
    }
}