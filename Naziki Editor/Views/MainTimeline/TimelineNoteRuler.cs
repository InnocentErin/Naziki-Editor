using Naziki_Editor.Models;
using Naziki_Editor.State;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Naziki_Editor.Views.MainTimeline
{
    /// <summary>
    /// Renders the note ruler at the bottom of the timeline showing chart note positions.
    /// </summary>
    public class TimelineNoteRuler
    {
        private readonly Canvas _notePreviewCanvas;
        private readonly UI.Rendering.NoteVisualEngine _noteVisualEngine;

        public TimelineNoteRuler(Canvas notePreviewCanvas, UI.Rendering.NoteVisualEngine noteVisualEngine)
        {
            _notePreviewCanvas = notePreviewCanvas;
            _noteVisualEngine = noteVisualEngine;
        }

        public void Draw(ProjectDataContext context, double pixelsPerSecond, double totalDurationSeconds)
        {
            if (_notePreviewCanvas == null) return;
            _notePreviewCanvas.Children.Clear();

            if (context == null || !context.HasChart || context.Chart.note_list == null) return;

            double totalWidth = totalDurationSeconds * pixelsPerSecond + 200;
            _notePreviewCanvas.Width = totalWidth;

            _noteVisualEngine.RenderNoteRuler(_notePreviewCanvas, context.Chart.note_list, context.TimeEngine, pixelsPerSecond, false);
        }

        public void FastUpdateZoom(ProjectDataContext context, double pixelsPerSecond, double totalDurationSeconds)
        {
            if (_notePreviewCanvas == null) return;
            double newWidth = totalDurationSeconds * pixelsPerSecond + 200;
            _notePreviewCanvas.Width = newWidth;

            foreach (UIElement child in _notePreviewCanvas.Children)
            {
                if (child is FrameworkElement fe && fe.Tag is C2Note note)
                {
                    double seconds = context.TimeEngine.TickToSeconds(note.tick);
                    double absoluteX = seconds * pixelsPerSecond;

                    if (child is Image img)
                        Canvas.SetLeft(img, absoluteX - (img.Width / 2.0));
                    else if (child is TextBlock)
                        Canvas.SetLeft(fe, absoluteX - 5.0);
                    else if (child is Line line && line.DataContext is C2Note lastChild)
                    {
                        double lastChildSeconds = context.TimeEngine.TickToSeconds(lastChild.tick);
                        line.X1 = absoluteX;
                        line.X2 = lastChildSeconds * pixelsPerSecond;
                    }
                    else if (child is Rectangle rect)
                    {
                        if (rect.Height == 2)
                        {
                            Canvas.SetLeft(rect, absoluteX);
                            double endSec = context.TimeEngine.TickToSeconds(note.tick + note.hold_tick);
                            double durSec = endSec - seconds;
                            rect.Width = durSec * pixelsPerSecond;
                        }
                        else
                        {
                            Canvas.SetLeft(rect, absoluteX - (rect.Width / 2.0));
                        }
                    }
                }
            }
        }
    }
}