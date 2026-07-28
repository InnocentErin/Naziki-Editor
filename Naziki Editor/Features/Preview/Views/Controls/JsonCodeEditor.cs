using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Rendering;

namespace Naziki_Editor.Views.Controls;

/// <summary>
/// Shared AvalonEdit surface for JSON source viewing and editing.
/// Parsing and validation deliberately remain outside this UI control.
/// </summary>
public sealed class JsonCodeEditor : TextEditor
{
    private readonly LineHighlightRenderer _highlightRenderer;

    public JsonCodeEditor()
    {
        FontFamily = new FontFamily("Consolas");
        FontSize = 13;
        ShowLineNumbers = true;
        HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto;
        Padding = new Thickness(5);
        SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("JavaScript");
        SetResourceReference(BackgroundProperty, "MainBgColor");
        SetResourceReference(ForegroundProperty, "MainTextColor");

        _highlightRenderer = new LineHighlightRenderer(this);
        TextArea.TextView.BackgroundRenderers.Add(_highlightRenderer);
    }

    public void SetLineHighlights(IEnumerable<int> lineNumbers, Brush brush)
    {
        ArgumentNullException.ThrowIfNull(lineNumbers);
        ArgumentNullException.ThrowIfNull(brush);
        _highlightRenderer.Set(lineNumbers, brush);
    }

    public void ClearLineHighlights() => _highlightRenderer.Clear();

    public void NavigateToLine(int lineNumber, bool selectLine = true)
    {
        if (Document is null || Document.LineCount == 0) return;
        var safeLine = Math.Clamp(lineNumber, 1, Document.LineCount);
        Dispatcher.BeginInvoke(() =>
        {
            if (Document is null || safeLine > Document.LineCount) return;
            var line = Document.GetLineByNumber(safeLine);
            ScrollToLine(safeLine);
            TextArea.Caret.Line = safeLine;
            TextArea.Caret.BringCaretToView();
            if (selectLine) Select(line.Offset, line.Length);
        }, DispatcherPriority.Loaded);
    }

    private sealed class LineHighlightRenderer : IBackgroundRenderer
    {
        private readonly JsonCodeEditor _editor;
        private readonly Dictionary<int, Brush> _lines = [];

        public LineHighlightRenderer(JsonCodeEditor editor) => _editor = editor;

        public KnownLayer Layer => KnownLayer.Background;

        public void Set(IEnumerable<int> lineNumbers, Brush brush)
        {
            _lines.Clear();
            foreach (var line in lineNumbers.Where(line => line > 0).Distinct())
                _lines[line] = brush;
            _editor.TextArea.TextView.InvalidateLayer(Layer);
        }

        public void Clear()
        {
            _lines.Clear();
            _editor.TextArea.TextView.InvalidateLayer(Layer);
        }

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (_lines.Count == 0 || !textView.VisualLinesValid ||
                _editor.Document is null)
                return;

            foreach (var visualLine in textView.VisualLines)
            {
                var lineNumber = visualLine.FirstDocumentLine.LineNumber;
                if (!_lines.TryGetValue(lineNumber, out var brush)) continue;
                var top = visualLine.VisualTop - textView.VerticalOffset;
                drawingContext.DrawRectangle(
                    brush,
                    null,
                    new Rect(0, top, textView.ActualWidth, visualLine.Height));
            }
        }
    }
}
