using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Models;
using Naziki_Editor.State;
using Naziki_Editor.Views.Controls;
using Newtonsoft.Json;

namespace Naziki_Editor.Views.StoryboardCorrections;

public sealed class CorrectionPreviewWindow : Window
{
    private readonly string _beforeJson;
    private readonly IStoryboardDocumentReader _reader;
    private readonly IStoryboardDocumentValidator _validator;
    private readonly IJsonTextDiffService _diffService;
    private readonly ProjectDataContext _context;
    private readonly StoryboardRoot _baselineDocument;
    private readonly JsonCodeEditor _beforeEditor;
    private readonly JsonCodeEditor _afterEditor;
    private readonly TextBlock _status;
    private readonly DispatcherTimer _diffTimer;
    private bool _initializing = true;

    public StoryboardRoot? AcceptedDocument { get; private set; }

    public CorrectionPreviewWindow(
        string before,
        string after,
        IStoryboardDocumentReader reader,
        IStoryboardDocumentValidator validator,
        IJsonTextDiffService diffService,
        ProjectDataContext context,
        StoryboardRoot baselineDocument)
    {
        _beforeJson = before;
        _reader = reader;
        _validator = validator;
        _diffService = diffService;
        _context = context;
        _baselineDocument = baselineDocument;
        Title = "修正预览";
        Width = 1120;
        Height = 740;
        MinWidth = 760;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _beforeEditor = CreateEditor(isReadOnly: true);
        _afterEditor = CreateEditor(isReadOnly: false);
        _status = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = FindBrush("SecTextColor", Brushes.Gray),
            MaxHeight = 94
        };
        _diffTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _diffTimer.Tick += (_, _) =>
        {
            _diffTimer.Stop();
            RefreshDiff(navigate: false);
        };

        Content = BuildLayout();
        _beforeEditor.Text = before;
        _afterEditor.Text = after;
        _afterEditor.TextChanged += (_, _) =>
        {
            if (_initializing) return;
            _diffTimer.Stop();
            _diffTimer.Start();
        };
        _initializing = false;
        Loaded += (_, _) => RefreshDiff(navigate: true);
        Closed += (_, _) => _diffTimer.Stop();
    }

    private UIElement BuildLayout()
    {
        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition());
        root.ColumnDefinitions.Add(new ColumnDefinition());

        root.Children.Add(EditorPanel("修正前（只读）", _beforeEditor, 0));
        root.Children.Add(EditorPanel("修正后（可编辑）", _afterEditor, 1));

        var statusBorder = new Border
        {
            BorderBrush = FindBrush("BorderColor", Brushes.DimGray),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 10, 0, 0),
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _status
            }
        };
        Grid.SetRow(statusBorder, 1);
        Grid.SetColumnSpan(statusBorder, 2);
        root.Children.Add(statusBorder);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        var cancel = new Button
        {
            Content = "取消",
            Padding = new Thickness(16, 6, 16, 6)
        };
        cancel.Click += (_, _) => DialogResult = false;
        var apply = new Button
        {
            Content = "确认应用",
            Padding = new Thickness(16, 6, 16, 6),
            Margin = new Thickness(8, 0, 0, 0)
        };
        apply.Click += (_, _) => ValidateAndAccept();
        buttons.Children.Add(cancel);
        buttons.Children.Add(apply);
        Grid.SetRow(buttons, 2);
        Grid.SetColumnSpan(buttons, 2);
        root.Children.Add(buttons);
        return root;
    }

    private static JsonCodeEditor CreateEditor(bool isReadOnly) => new()
    {
        IsReadOnly = isReadOnly
    };

    private static UIElement EditorPanel(
        string title,
        JsonCodeEditor editor,
        int column)
    {
        var panel = new DockPanel
        {
            Margin = new Thickness(column == 0 ? 0 : 6, 0, column == 0 ? 6 : 0, 0)
        };
        var label = new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        };
        DockPanel.SetDock(label, Dock.Top);
        panel.Children.Add(label);
        panel.Children.Add(editor);
        Grid.SetColumn(panel, column);
        return panel;
    }

    private void RefreshDiff(bool navigate)
    {
        try
        {
            var result = _diffService.Analyze(_beforeJson, _afterEditor.Text);
            _beforeEditor.SetLineHighlights(
                result.BeforeChangedLines,
                new SolidColorBrush(Color.FromArgb(72, 230, 70, 70)));
            _afterEditor.SetLineHighlights(
                result.AfterChangedLines,
                new SolidColorBrush(Color.FromArgb(72, 55, 190, 95)));
            _status.Foreground = FindBrush("SecTextColor", Brushes.Gray);
            _status.Text = result.ChangeCount == 0
                ? "未检测到语义差异。"
                : $"检测到 {result.ChangeCount} 处语义变化。红色为原内容，绿色为修正后内容。";
            if (!navigate) return;
            if (result.FirstBeforeLine is int beforeLine)
                _beforeEditor.NavigateToLine(beforeLine);
            if (result.FirstAfterLine is int afterLine)
                _afterEditor.NavigateToLine(afterLine);
        }
        catch (Exception ex)
        {
            _status.Foreground = Brushes.OrangeRed;
            _status.Text = $"当前修正后代码不是合法 JSON，暂时保留上一次差异高亮：{ex.Message}";
        }
    }

    private void ValidateAndAccept()
    {
        StoryboardRoot candidate;
        try
        {
            candidate = _reader.Read(_afterEditor.Text);
        }
        catch (Exception ex)
        {
            _status.Foreground = Brushes.OrangeRed;
            _status.Text = $"JSON 格式或字段内容有误：{ex.Message}";
            return;
        }

        PreserveInternalIds(_baselineDocument, candidate);
        var diagnostics = _validator.Validate(candidate, _context);
        var errors = diagnostics
            .Where(item => item.Severity == StoryboardDiagnosticSeverity.Error)
            .ToArray();
        var warnings = diagnostics
            .Where(item => item.Severity == StoryboardDiagnosticSeverity.Warning)
            .ToArray();
        if (errors.Length > 0)
        {
            _status.Foreground = Brushes.OrangeRed;
            _status.Text = "属性或内容不合法，无法应用：" + Environment.NewLine +
                           string.Join(Environment.NewLine, errors.Take(12)
                               .Select(item => $"{item.Path}: {item.Message}")) +
                           (errors.Length > 12
                               ? $"{Environment.NewLine}另有 {errors.Length - 12} 个错误。"
                               : "");
            return;
        }

        AcceptedDocument = candidate;
        if (warnings.Length > 0)
        {
            _status.Foreground = Brushes.Goldenrod;
            _status.Text = $"验证通过，并保留了 {warnings.Length} 个警告。";
        }
        DialogResult = true;
    }

    private static void PreserveInternalIds(StoryboardRoot baseline, StoryboardRoot candidate)
    {
        PreserveListIds(baseline.sprites, candidate.sprites, "sprite");
        PreserveListIds(baseline.texts, candidate.texts, "text");
        PreserveListIds(baseline.lines, candidate.lines, "line");
        PreserveListIds(baseline.videos, candidate.videos, "video");
        PreserveListIds(baseline.controllers, candidate.controllers, "controller");
        PreserveListIds(baseline.note_controllers, candidate.note_controllers, "note");
        foreach (var (key, entity) in candidate.templates)
        {
            if (!string.IsNullOrWhiteSpace(entity.Id)) continue;
            if (baseline.templates.TryGetValue(key, out var previous) &&
                previous.IsIdSynthetic && !string.IsNullOrWhiteSpace(previous.Id))
            {
                entity.Id = previous.Id;
                entity.IsIdSynthetic = true;
                continue;
            }
            entity.Id = $"template_{Guid.NewGuid():N}"[..17];
            entity.IsIdSynthetic = true;
        }
    }

    private static void PreserveListIds<T>(
        IReadOnlyList<T> baseline,
        IReadOnlyList<T> candidate,
        string prefix)
        where T : IStoryboardEntity
    {
        for (var index = 0; index < candidate.Count; index++)
        {
            var entity = candidate[index];
            if (!string.IsNullOrWhiteSpace(entity.Id)) continue;
            if (index < baseline.Count &&
                baseline[index].IsIdSynthetic &&
                !string.IsNullOrWhiteSpace(baseline[index].Id))
            {
                entity.Id = baseline[index].Id;
                entity.IsIdSynthetic = true;
                continue;
            }

            entity.Id = $"{prefix}_{Guid.NewGuid():N}"[..(prefix.Length + 9)];
            entity.IsIdSynthetic = true;
        }
    }

    private static Brush FindBrush(string resourceKey, Brush fallback) =>
        Application.Current?.TryFindResource(resourceKey) as Brush ?? fallback;
}
