using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Naziki_Editor.Views.StoryboardCorrections;

public sealed class StoryboardSourceConflictDialog : Window
{
    private readonly JToken _source;
    private readonly IStoryboardJsonNormalizer _normalizer;
    private readonly Dictionary<string, ComboBox> _choices =
        new(StringComparer.Ordinal);
    private readonly TextBox _preview;

    public string? CorrectedJson { get; private set; }

    public StoryboardSourceConflictDialog(
        string json,
        IStoryboardJsonNormalizer normalizer)
    {
        _normalizer = normalizer;
        _source = JToken.Parse(json);
        var initial = normalizer.Normalize(_source);

        Title = "故事板检查与修正 - 属性名冲突";
        Width = 920;
        Height = 700;
        MinWidth = 720;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(18) };
        var title = new TextBlock
        {
            Text = $"发现 {initial.Conflicts.Count} 个属性名冲突",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        };
        DockPanel.SetDock(title, Dock.Top);
        root.Children.Add(title);
        var hint = new TextBlock
        {
            Text = "请选择每个标准属性应保留的原始值。取消不会修改工程或源文件。",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 12)
        };
        DockPanel.SetDock(hint, Dock.Top);
        root.Children.Add(hint);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var cancel = new Button
        {
            Content = "取消导入",
            MinWidth = 100,
            Margin = new Thickness(0, 0, 8, 0)
        };
        cancel.Click += (_, _) => DialogResult = false;
        var apply = new Button
        {
            Content = "应用修正并继续",
            MinWidth = 140,
            IsDefault = true
        };
        apply.Click += (_, _) => Apply();
        buttons.Children.Add(cancel);
        buttons.Children.Add(apply);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var tabs = new TabControl();
        var conflictPanel = new StackPanel { Margin = new Thickness(8) };
        foreach (var conflict in initial.Conflicts)
        {
            var card = new Border
            {
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 9)
            };
            var content = new StackPanel();
            content.Children.Add(new TextBlock
            {
                Text = $"{conflict.Path}.{conflict.CanonicalName}",
                FontWeight = FontWeights.SemiBold
            });
            var choice = new ComboBox
            {
                Margin = new Thickness(0, 7, 0, 0),
                ItemsSource = conflict.Candidates,
                DisplayMemberPath = nameof(
                    StoryboardPropertyConflictCandidate.OriginalName),
                SelectedIndex = 0
            };
            choice.SelectionChanged += (_, _) => RefreshPreview();
            _choices[StoryboardJsonNormalizer.ConflictKey(
                conflict.Path, conflict.CanonicalName)] = choice;
            content.Children.Add(choice);
            foreach (var candidate in conflict.Candidates)
                content.Children.Add(new TextBlock
                {
                    Text = $"{candidate.OriginalName}: " +
                           candidate.Value.ToString(Formatting.None),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(12, 4, 0, 0)
                });
            card.Child = content;
            conflictPanel.Children.Add(card);
        }
        tabs.Items.Add(new TabItem
        {
            Header = "冲突处理",
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = conflictPanel
            }
        });
        tabs.Items.Add(new TabItem
        {
            Header = "修正前 JSON",
            Content = CreateJsonPreview(_source.ToString(
                Formatting.Indented))
        });
        _preview = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            FontFamily = new FontFamily("Consolas"),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        tabs.Items.Add(new TabItem
        {
            Header = "修正后 JSON 预览",
            Content = _preview
        });
        root.Children.Add(tabs);
        Content = root;
        RefreshPreview();
    }

    private static TextBox CreateJsonPreview(string text) => new()
    {
        Text = text,
        IsReadOnly = true,
        AcceptsReturn = true,
        AcceptsTab = true,
        FontFamily = new FontFamily("Consolas"),
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
    };

    private Dictionary<string, string> Selections() =>
        _choices.ToDictionary(pair => pair.Key, pair =>
            ((StoryboardPropertyConflictCandidate)pair.Value.SelectedItem)
            .OriginalName, StringComparer.Ordinal);

    private void RefreshPreview()
    {
        if (_choices.Values.Any(choice => choice.SelectedItem is null)) return;
        var result = _normalizer.Normalize(_source, Selections());
        _preview.Text = result.Token.ToString(Formatting.Indented);
    }

    private void Apply()
    {
        var result = _normalizer.Normalize(_source, Selections());
        if (result.Conflicts.Count > 0)
        {
            MessageBox.Show(this, "仍有未解决的属性名冲突。",
                Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        CorrectedJson = result.Token.ToString(Formatting.Indented);
        DialogResult = true;
    }
}

public sealed class ResolvedStoryboardSource : IDisposable
{
    public string Path { get; }
    private readonly bool _temporary;

    internal ResolvedStoryboardSource(string path, bool temporary)
    {
        Path = path;
        _temporary = temporary;
    }

    public void Dispose()
    {
        if (!_temporary) return;
        try { File.Delete(Path); }
        catch { }
    }
}

public static class StoryboardSourceConflictResolver
{
    public static async Task<ResolvedStoryboardSource?> ResolveAsync(
        Window owner,
        string path,
        CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        var normalizer = AppServices.GetService<IStoryboardJsonNormalizer>();
        var parsed = JToken.Parse(json);
        var result = normalizer.Normalize(parsed);
        if (result.Conflicts.Count == 0)
            return new ResolvedStoryboardSource(path, false);

        var dialog = new StoryboardSourceConflictDialog(json, normalizer)
        {
            Owner = owner
        };
        if (dialog.ShowDialog() != true ||
            string.IsNullOrWhiteSpace(dialog.CorrectedJson))
            return null;

        var temporaryPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"naziki-storyboard-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(
            temporaryPath, dialog.CorrectedJson, cancellationToken);
        return new ResolvedStoryboardSource(temporaryPath, true);
    }
}
