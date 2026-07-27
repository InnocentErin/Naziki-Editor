using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Globalization;
using Naziki_Editor.Core.Storyboard.Corrections;
using Newtonsoft.Json.Linq;

namespace Naziki_Editor.Views.StoryboardCorrections;

public sealed class ConflictCorrectionDialog : Window
{
    private readonly StoryboardCorrectionIssue _issue;
    private readonly string _fingerprint;
    private readonly StackPanel _participantsPanel = new();
    private readonly TextBlock _validationText = new();
    private readonly Dictionary<int, RadioButton> _keeperButtons = new();
    private readonly List<LoserEditor> _loserEditors = [];
    private Button _previewButton = null!;

    public StoryboardCorrectionPlan? Plan { get; private set; }

    public ConflictCorrectionDialog(
        StoryboardCorrectionIssue issue,
        string fingerprint)
    {
        _issue = issue;
        _fingerprint = fingerprint;
        Title = $"处理同时间冲突 · {issue.EffectiveTime:0.######}s";
        Width = 980;
        Height = 720;
        MinWidth = 760;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Build();
    }

    private void Build()
    {
        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Content = root;

        var header = new StackPanel();
        header.Children.Add(new TextBlock
        {
            Text = $"同一有效时间 {_issue.EffectiveTime:0.######} 秒存在 {_issue.Participants.Count} 个状态",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold
        });
        header.Children.Add(new TextBlock
        {
            Text = "先选择一个保留关键帧，再为其他关键帧选择删除范围及逐属性迁移方式。",
            Margin = new Thickness(0, 5, 0, 10),
            TextWrapping = TextWrapping.Wrap
        });
        root.Children.Add(header);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _participantsPanel
        };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        var footer = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _validationText.Foreground = Brushes.OrangeRed;
        _validationText.VerticalAlignment = VerticalAlignment.Center;
        footer.Children.Add(_validationText);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var cancel = new Button { Content = "取消", Padding = new Thickness(16, 6, 16, 6) };
        cancel.Click += (_, _) => DialogResult = false;
        if (StoryboardCorrectionPolicy.CanSafelyMerge(_issue))
        {
            var safeMerge = new Button
            {
                Content = "一键安全合并",
                Padding = new Thickness(16, 6, 16, 6),
                Margin = new Thickness(8, 0, 0, 0)
            };
            safeMerge.Click += (_, _) =>
            {
                Plan = StoryboardCorrectionPolicy.BuildSafeMergePlan(_fingerprint, _issue);
                DialogResult = true;
            };
            buttons.Children.Add(safeMerge);
        }
        _previewButton = new Button
        {
            Content = "生成修正预览",
            Padding = new Thickness(16, 6, 16, 6),
            Margin = new Thickness(8, 0, 0, 0),
            IsEnabled = false
        };
        _previewButton.Click += (_, _) => Complete();
        buttons.Children.Add(cancel);
        buttons.Children.Add(_previewButton);
        Grid.SetColumn(buttons, 1);
        footer.Children.Add(buttons);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        BuildKeeperSelection();
    }

    private void BuildKeeperSelection()
    {
        _participantsPanel.Children.Clear();
        _keeperButtons.Clear();
        var containsBase = _issue.Participants.Any(item => item.IsBaseState);
        foreach (var participant in _issue.Participants)
        {
            var radio = new RadioButton
            {
                Content = $"{FrameName(participant)} · 时间 {participant.RawTime}",
                GroupName = "Keeper",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 5),
                IsEnabled = !containsBase || participant.IsBaseState,
                IsChecked = containsBase && participant.IsBaseState
            };
            radio.Checked += (_, _) => BuildLoserEditors();
            _keeperButtons[participant.ParticipantIndex] = radio;
            var card = new Border
            {
                BorderBrush = Brushes.DimGray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 8),
                Child = new StackPanel
                {
                    Children =
                    {
                        radio,
                        new TextBlock
                        {
                            Text = PropertySummary(participant.Properties),
                            Foreground = Brushes.Gray,
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                }
            };
            _participantsPanel.Children.Add(card);
        }
        if (!containsBase && _keeperButtons.Count > 0)
            _keeperButtons.Values.First().IsChecked = true;
        else
            BuildLoserEditors();
    }

    private void BuildLoserEditors()
    {
        while (_participantsPanel.Children.Count > _issue.Participants.Count)
            _participantsPanel.Children.RemoveAt(_participantsPanel.Children.Count - 1);
        _loserEditors.Clear();
        var keeper = SelectedKeeper();
        if (keeper is null) return;

        _participantsPanel.Children.Add(new TextBlock
        {
            Text = "待删除关键帧及属性迁移",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 10, 0, 8)
        });

        foreach (var participant in _issue.Participants.Where(item =>
                     item.ParticipantIndex != keeper.ParticipantIndex))
        {
            var editor = new LoserEditor(participant, keeper, ValidateSelections);
            _loserEditors.Add(editor);
            _participantsPanel.Children.Add(editor.Root);
        }
        BuildOffsetEditor();
        ValidateSelections();
    }

    private void BuildOffsetEditor()
    {
        var candidates = _issue.Participants.Where(item => !item.IsBaseState).ToArray();
        if (candidates.Length == 0) return;
        var panel = new StackPanel();
        var border = new Border
        {
            BorderBrush = Brushes.SteelBlue,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 6, 0, 10),
            Child = panel
        };
        panel.Children.Add(new TextBlock
        {
            Text = "时间冲突关键帧错位",
            FontWeight = FontWeights.SemiBold,
            FontSize = 15
        });
        panel.Children.Add(new TextBlock
        {
            Text = "不合并属性，改为将选中的关键帧向前（负数）或向后（正数）偏移。数组只拆出当前冲突时刻。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 4, 0, 7)
        });
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var offsetParticipant = new ComboBox { Width = 360 };
        foreach (var candidate in candidates)
            offsetParticipant.Items.Add(new ComboBoxItem
            {
                Content = $"{FrameName(candidate)} · {candidate.RawTime}",
                Tag = candidate
            });
        offsetParticipant.SelectedIndex = offsetParticipant.Items.Count - 1;
        row.Children.Add(offsetParticipant);
        row.Children.Add(new TextBlock
        {
            Text = " 偏移 ",
            VerticalAlignment = VerticalAlignment.Center
        });
        var offsetText = new TextBox
        {
            Text = "+0.2",
            Width = 90,
            Padding = new Thickness(5)
        };
        row.Children.Add(offsetText);
        row.Children.Add(new TextBlock
        {
            Text = " 秒 ",
            VerticalAlignment = VerticalAlignment.Center
        });
        var preview = new Button
        {
            Content = "生成错位预览",
            Padding = new Thickness(12, 5, 12, 5)
        };
        preview.Click += (_, _) => CompleteOffset(offsetParticipant, offsetText);
        row.Children.Add(preview);
        panel.Children.Add(row);
        _participantsPanel.Children.Add(border);
    }

    private void CompleteOffset(ComboBox offsetParticipant, TextBox offsetText)
    {
        if ((offsetParticipant.SelectedItem as ComboBoxItem)?.Tag is not
            StoryboardCorrectionParticipant participant)
            return;
        if (!StoryboardCorrectionWindow.TryReadOffset(offsetText.Text, out var delta))
        {
            MessageBox.Show(this,
                "请输入非零的有限秒数，例如 +0.2 或 -0.1。",
                "错位参数无效",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }
        Plan = new StoryboardCorrectionPlan
        {
            DocumentFingerprint = _fingerprint,
            IssueId = _issue.Id,
            KeepParticipantIndex = 0,
            TimeOffset = new StoryboardTimeOffsetCorrection(
                participant.ParticipantIndex, delta)
        };
        DialogResult = true;
    }

    private void ValidateSelections()
    {
        var keeper = SelectedKeeper();
        var valid = keeper is not null &&
                    _loserEditors.Count == _issue.Participants.Count - 1 &&
                    _loserEditors.All(editor => editor.IsComplete);
        _previewButton.IsEnabled = valid;
        _validationText.Text = valid
            ? "所有冲突项均已配置，可生成预览。"
            : "请为每个属性选择“不迁移、添加或覆盖”。";
    }

    private void Complete()
    {
        var keeper = SelectedKeeper();
        if (keeper is null || !_loserEditors.All(item => item.IsComplete)) return;
        Plan = new StoryboardCorrectionPlan
        {
            DocumentFingerprint = _fingerprint,
            IssueId = _issue.Id,
            KeepParticipantIndex = keeper.ParticipantIndex,
            Losers = _loserEditors.Select(editor => editor.Build()).ToArray()
        };
        DialogResult = true;
    }

    private StoryboardCorrectionParticipant? SelectedKeeper() =>
        _issue.Participants.FirstOrDefault(participant =>
            _keeperButtons.TryGetValue(participant.ParticipantIndex, out var radio) &&
            radio.IsChecked == true);

    private static string FrameName(StoryboardCorrectionParticipant participant) =>
        participant.IsBaseState
            ? "初始状态（必须保留）"
            : $"states[{participant.StateIndex}]" +
              (participant.ArrayIndex.HasValue ? $".time[{participant.ArrayIndex}]" : "");

    private static string PropertySummary(IReadOnlyDictionary<string, JToken> values) =>
        values.Count == 0
            ? "没有动画属性"
            : string.Join(" · ", values.Select(pair =>
                $"{pair.Key}={Compact(pair.Value)}"));

    private static string Compact(JToken token)
    {
        var text = token.ToString(Newtonsoft.Json.Formatting.None);
        return text.Length <= 80 ? text : text[..77] + "...";
    }

    private sealed class LoserEditor
    {
        private readonly StoryboardCorrectionParticipant _participant;
        private readonly ComboBox _scope = new();
        private readonly List<(string Name, ComboBox Choice)> _choices = [];
        private readonly Action _changed;

        public Border Root { get; }
        public bool IsComplete => _choices.All(item => item.Choice.SelectedItem is ComboBoxItem);

        public LoserEditor(
            StoryboardCorrectionParticipant participant,
            StoryboardCorrectionParticipant keeper,
            Action changed)
        {
            _participant = participant;
            _changed = changed;
            Root = new Border
            {
                BorderBrush = Brushes.DarkOrange,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var panel = new StackPanel();
            Root.Child = panel;
            panel.Children.Add(new TextBlock
            {
                Text = $"删除 {FrameName(participant)} · 时间 {participant.RawTime}",
                FontWeight = FontWeights.SemiBold
            });

            var scopeRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 7, 0, 7)
            };
            scopeRow.Children.Add(new TextBlock
            {
                Text = "删除范围：",
                VerticalAlignment = VerticalAlignment.Center
            });
            _scope.Width = 240;
            if (participant.ArrayIndex.HasValue)
                _scope.Items.Add(new ComboBoxItem
                {
                    Content = "仅删除当前冲突时刻",
                    Tag = StoryboardDeleteScope.ConflictOccurrence
                });
            _scope.Items.Add(new ComboBoxItem
            {
                Content = participant.ArrayIndex.HasValue
                    ? "删除整个关键帧（含数组其他时刻）"
                    : "删除整个关键帧",
                Tag = StoryboardDeleteScope.EntireKeyframe
            });
            _scope.SelectedIndex = 0;
            scopeRow.Children.Add(_scope);
            panel.Children.Add(scopeRow);

            if (participant.ArrayIndex.HasValue)
                panel.Children.Add(new TextBlock
                {
                    Text = "选择“删除整个关键帧”会同时删除 time 数组中的其他时刻。",
                    Foreground = Brushes.OrangeRed,
                    Margin = new Thickness(0, 0, 0, 6)
                });

            var allProperties = participant.Properties.Keys.OrderBy(name => name).ToArray();
            foreach (var name in allProperties)
            {
                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
                row.Children.Add(new TextBlock
                {
                    Text = $"{name}: {Compact(participant.Properties[name])}",
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                });
                var keeperValue = keeper.Properties.TryGetValue(name, out var value)
                    ? Compact(value)
                    : "（保留帧中不存在）";
                var target = new TextBlock
                {
                    Text = $"保留帧：{keeperValue}",
                    Foreground = Brushes.Gray,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(8, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(target, 1);
                row.Children.Add(target);

                var choice = new ComboBox();
                choice.Items.Add(new ComboBoxItem
                {
                    Content = "不迁移（保留现值）",
                    Tag = StoryboardPropertyMigrationMode.Skip
                });
                if (!keeper.Properties.ContainsKey(name))
                    choice.Items.Add(new ComboBoxItem
                    {
                        Content = "添加到保留帧",
                        Tag = StoryboardPropertyMigrationMode.Add
                    });
                else if (!JToken.DeepEquals(participant.Properties[name], keeper.Properties[name]))
                    choice.Items.Add(new ComboBoxItem
                    {
                        Content = "覆盖保留帧",
                        Tag = StoryboardPropertyMigrationMode.Overwrite
                    });
                choice.SelectionChanged += (_, _) => _changed();
                choice.SelectedIndex = keeper.Properties.ContainsKey(name) ? 0 : 1;
                Grid.SetColumn(choice, 2);
                row.Children.Add(choice);
                _choices.Add((name, choice));
                panel.Children.Add(row);
            }
        }

        public StoryboardLoserCorrection Build()
        {
            var scope = (_scope.SelectedItem as ComboBoxItem)?.Tag as StoryboardDeleteScope?
                        ?? StoryboardDeleteScope.EntireKeyframe;
            return new StoryboardLoserCorrection
            {
                ParticipantIndex = _participant.ParticipantIndex,
                DeleteScope = scope,
                PropertyMigrations = _choices.Select(item =>
                    new StoryboardPropertyMigration(
                        item.Name,
                        (StoryboardPropertyMigrationMode)
                        ((ComboBoxItem)item.Choice.SelectedItem).Tag)).ToArray()
            };
        }
    }
}
