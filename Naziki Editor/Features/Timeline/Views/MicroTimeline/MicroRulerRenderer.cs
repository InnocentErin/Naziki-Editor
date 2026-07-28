using Naziki_Editor.Core.Timeline.Shared;
using Naziki_Editor.Models;
using Naziki_Editor.State;
using System;
using System.Text;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Naziki_Editor.Views.MicroTimeline
{
    /// <summary>
    /// Renders the micro-editor ruler: time ticks, note markers, and blue highlight zone.
    /// </summary>
    public class MicroRulerRenderer
    {
        private readonly Canvas _microRulerCanvas;
        private readonly UI.Rendering.NoteVisualEngine _noteVisualEngine;

        public MicroRulerRenderer(Canvas microRulerCanvas, UI.Rendering.NoteVisualEngine noteVisualEngine)
        {
            _microRulerCanvas = microRulerCanvas;
            _noteVisualEngine = noteVisualEngine;
        }

        public void RenderTicks(double pixelsPerSecond, double microStartTime, double microEndTime, double macroStartTime, double macroEndTime, int clipWidth)
        {
            if (_microRulerCanvas == null) return;
            _microRulerCanvas.Children.Clear();
            _microRulerCanvas.Width = Math.Max(clipWidth, 200);

            double microDuration = microEndTime - microStartTime;
            double step = microDuration > 10 ? 1.0 : (microDuration > 2 ? 0.5 : 0.1);

            for (double t = microStartTime; t <= microEndTime + step; t += step)
            {
                double xPos = (t - microStartTime) * pixelsPerSecond;
                bool isMajor = t >= microStartTime && t <= microEndTime;
                _microRulerCanvas.Children.Add(new Line
                {
                    X1 = xPos, Y1 = 12, X2 = xPos, Y2 = 20,
                    Stroke = (Brush)Application.Current.FindResource("BorderColor"),
                    StrokeThickness = 0.8,
                    Opacity = isMajor ? 1 : 0.4
                });
                if (Math.Abs(t % 1) < 0.001 || Math.Abs(t % 1 - 1) < 0.001)
                {
                    _microRulerCanvas.Children.Add(new TextBlock
                    {
                        Text = $"{t:F1}s",
                        FontSize = 8,
                        Foreground = (Brush)Application.Current.FindResource("SecTextColor"),
                        RenderTransform = new TranslateTransform { X = xPos + 2, Y = 0 }
                    });
                }
            }

            // Blue highlight zone
            double highlightStart = (macroStartTime - microStartTime) * pixelsPerSecond;
            double highlightEnd = (macroEndTime - microStartTime) * pixelsPerSecond;
            var highlightRect = new Rectangle
            {
                Width = Math.Max(0, highlightEnd - highlightStart + 2),
                Height = 20,
                Fill = new SolidColorBrush(Color.FromArgb(30, 0, 120, 215)),
                Stroke = new SolidColorBrush(Color.FromArgb(80, 0, 120, 215)),
                StrokeThickness = 1
            };
            Canvas.SetLeft(highlightRect, highlightStart - 1);
            _microRulerCanvas.Children.Add(highlightRect);
        }

        public void RenderNoteMarkers(ProjectDataContext context, double pixelsPerSecond, double microStartTime, double microEndTime)
        {
            if (_microRulerCanvas == null || context == null || !context.HasChart) return;

            _noteVisualEngine.RenderNoteRuler(_microRulerCanvas, context.Chart.note_list, context.TimeEngine, pixelsPerSecond, true);
        }

        public void RenderNoteMarkersCapped(
            ProjectDataContext context,
            double pixelsPerSecond,
            double microStartTime,
            double microEndTime,
            int maximumMarkers)
        {
            if (_microRulerCanvas == null || context == null || !context.HasChart ||
                maximumMarkers <= 0)
                return;

            var visibleNotes = context.Chart.note_list
                .Where(note =>
                {
                    var seconds = context.TimeEngine.TickToSeconds(note.tick);
                    return seconds >= microStartTime && seconds <= microEndTime;
                })
                .Take(maximumMarkers)
                .ToList();
            var stagingCanvas = new Canvas();
            _noteVisualEngine.RenderNoteRuler(
                stagingCanvas,
                visibleNotes,
                context.TimeEngine,
                pixelsPerSecond,
                true);
            while (stagingCanvas.Children.Count > 0)
            {
                var child = stagingCanvas.Children[0];
                stagingCanvas.Children.RemoveAt(0);
                _microRulerCanvas.Children.Add(child);
            }
        }

        public void FastUpdateZoom(double pixelsPerSecond, double microStartTime, double microEndTime, double macroStartTime, double macroEndTime, int clipWidth)
        {
            if (_microRulerCanvas == null) return;
            _microRulerCanvas.Width = Math.Max(clipWidth, 200);
            double microDuration = microEndTime - microStartTime;

            foreach (UIElement child in _microRulerCanvas.Children)
            {
                if (child is Line line && line.Tag is double tickTime)
                {
                    double xPos = (tickTime - microStartTime) * pixelsPerSecond;
                    line.X1 = xPos; line.X2 = xPos;
                }
                else if (child is TextBlock tb && tb.Tag is double textTime)
                {
                    double xPos = (textTime - microStartTime) * pixelsPerSecond;
                    tb.RenderTransform = new TranslateTransform { X = xPos + 2, Y = 0 };
                }
                else if (child is Rectangle && child is not null)
                {
                    double highlightStart = (macroStartTime - microStartTime) * pixelsPerSecond;
                    double highlightEnd = (macroEndTime - microStartTime) * pixelsPerSecond;
                    ((Rectangle)child).Width = Math.Max(0, highlightEnd - highlightStart + 2);
                    Canvas.SetLeft(child, highlightStart - 1);
                }
            }
        }
    }
}
