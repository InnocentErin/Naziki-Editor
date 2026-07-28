using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Globalization;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Storyboard.Corrections;
using Naziki_Editor.State;

namespace Naziki_Editor.Views.StoryboardCorrections;

public partial class StoryboardCorrectionWindow : Window
{
    private readonly ProjectDataContext _context;
    private readonly IStoryboardCorrectionAnalyzer _analyzer;
    private readonly IStoryboardCorrectionService _correctionService;
    private readonly IStoryboardDocumentValidator _validator;
    private readonly IHistoryService _history;
    private readonly IMessageBroker _messageBroker;
    private readonly IDialogService _dialogService;
    private StoryboardCorrectionReport? _report;

    public bool HasAppliedChanges { get; private set; }

    public StoryboardCorrectionWindow(
        ProjectDataContext context,
        IStoryboardCorrectionAnalyzer analyzer,
        IStoryboardCorrectionService correctionService,
        IStoryboardDocumentValidator validator,
        IHistoryService history,
        IMessageBroker messageBroker,
        IDialogService dialogService)
    {
        InitializeComponent();
        _context = context;
        _analyzer = analyzer;
        _correctionService = correctionService;
        _validator = validator;
        _history = history;
        _messageBroker = messageBroker;
        _dialogService = dialogService;
        TxtPath.Text = string.IsNullOrWhiteSpace(context.StoryboardPath)
            ? "当前故事板：尚未设置导出路径"
            : $"当前故事板：{context.StoryboardPath}";
        Loaded += (_, _) => Search();
    }

    private void Search()
    {
        _validator.Validate(_context.Storyboard, _context);
        _messageBroker.Publish("RefreshStoryboardDiagnostics");
        _report = _analyzer.Scan(_context.Storyboard, _context);
        TxtSummary.Text =
            $"共 {_report.Issues.Count} 个问题 · 可交互修正 {_report.RepairableCount} 个 · " +
            $"需手动处理 {_report.Issues.Count - _report.RepairableCount} 个";
        BtnFixMissing.IsEnabled = _report.Issues.Any(issue =>
            issue.Kind == StoryboardCorrectionKind.MissingBaseTime &&
            issue.CanAutomaticallyRepair);
        BtnSafeMerge.IsEnabled = _report.Issues.Any(StoryboardCorrectionPolicy.CanSafelyMerge);
        BtnOffsetAll.IsEnabled = _report.Issues.Any(issue =>
            issue.Kind == StoryboardCorrectionKind.SameTimeConflict &&
            issue.Participants.Any(participant => !participant.IsBaseState));
        RenderIssues();
    }

    private void RenderIssues()
    {
        IssueList.Items.Clear();
        if (_report is null) return;
        var filter = (CmbFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "All";
        var search = TxtSearch.Text?.Trim() ?? string.Empty;
        var visible = _report.Issues.Where(issue =>
            (filter == "All" || issue.Kind.ToString() == filter) &&
            (search.Length == 0 ||
             issue.Path.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             (issue.EntityId?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
             issue.EntityType.Contains(search, StringComparison.OrdinalIgnoreCase)));

        foreach (var issue in visible)
            IssueList.Items.Add(new ListBoxItem
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 8),
                Content = BuildIssueCard(issue)
            });
    }

    private UIElement BuildIssueCard(StoryboardCorrectionIssue issue)
    {
        var border = new Border
        {
            BorderBrush = issue.CanAutomaticallyRepair ? Brushes.DarkOrange : Brushes.Crimson,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(12),
            Background = new SolidColorBrush(Color.FromArgb(25, 255, 255, 255))
        };
        var panel = new StackPanel();
        border.Child = panel;
        panel.Children.Add(new TextBlock
        {
            Text = $"{KindTitle(issue.Kind)} · {issue.EntityType} · {issue.EntityId ?? "（匿名对象）"}",
            FontWeight = FontWeights.SemiBold,
            FontSize = 15
        });
        panel.Children.Add(new TextBlock
        {
            Text = issue.Path,
            Margin = new Thickness(0, 3, 0, 0),
            Foreground = Brushes.Gray
        });
        panel.Children.Add(new TextBlock
        {
            Text = issue.Message,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });

        if (issue.Participants.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = string.Join(Environment.NewLine,
                    issue.Participants.Select(p =>
                        $"关键帧 {FrameName(p)} · 时间 {p.RawTime} · 属性 {string.Join(", ", p.Properties.Keys)}")),
                Margin = new Thickness(0, 6, 0, 0),
                Foreground = Brushes.LightGray,
                TextWrapping = TextWrapping.Wrap
            });
        }

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 10, 0, 0)
        };
        var repair = new Button
        {
            Content = issue.Kind == StoryboardCorrectionKind.SameTimeConflict
                ? "处理冲突…"
                : "预览并修正…",
            Padding = new Thickness(12, 5, 12, 5),
            IsEnabled = issue.CanAutomaticallyRepair
        };
        repair.Click += (_, _) => Repair(issue);
        actions.Children.Add(repair);
        if (StoryboardCorrectionPolicy.CanSafelyMerge(issue))
        {
            var quickMerge = new Button
            {
                Content = "一键安全合并",
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(8, 0, 0, 0)
            };
            quickMerge.Click += (_, _) => PreviewAndCommit(
                StoryboardCorrectionPolicy.BuildSafeMergePlan(
                    _report!.DocumentFingerprint, issue));
            actions.Children.Add(quickMerge);
        }
        var openEditor = new Button
        {
            Content = "打开属性编辑器",
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(8, 0, 0, 0)
        };
        openEditor.Click += (_, _) =>
        {
            if (Owner is MainWindow mainWindow)
                mainWindow.OpenPropertyEditor(issue.Entity);
        };
        actions.Children.Add(openEditor);
        var locate = new Button
        {
            Content = "复制 JSON 路径",
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(8, 0, 0, 0)
        };
        locate.Click += (_, _) =>
        {
            Clipboard.SetText(issue.Path);
            _dialogService.ShowMessage("JSON 路径已复制。", "定位");
        };
        actions.Children.Add(locate);
        panel.Children.Add(actions);
        return border;
    }

    private void Repair(StoryboardCorrectionIssue issue)
    {
        if (_report is null) return;
        StoryboardCorrectionPlan? plan;
        if (issue.Kind == StoryboardCorrectionKind.MissingBaseTime)
        {
            plan = new StoryboardCorrectionPlan
            {
                DocumentFingerprint = _report.DocumentFingerprint,
                IssueId = issue.Id,
                KeepParticipantIndex = 0
            };
        }
        else
        {
            var dialog = new ConflictCorrectionDialog(issue, _report.DocumentFingerprint)
            {
                Owner = this
            };
            if (dialog.ShowDialog() != true) return;
            plan = dialog.Plan;
        }

        try
        {
            PreviewAndCommit(plan);
        }
        catch (Exception ex)
        {
            _dialogService.ShowErrorDialog(ex.Message, "修正失败", ex.ToString());
            Search();
        }
    }

    private void PreviewAndCommit(StoryboardCorrectionPlan plan)
    {
        try
        {
            var preview = _correctionService.Preview(_context.Storyboard, _context, plan);
            var previewWindow = new CorrectionPreviewWindow(
                preview.BeforeJson,
                preview.AfterJson,
                AppServices.GetService<IStoryboardDocumentReader>(),
                _validator,
                AppServices.GetService<IJsonTextDiffService>(),
                _context,
                preview.CorrectedDocument)
            {
                Owner = this
            };
            if (previewWindow.ShowDialog() != true) return;
            if (previewWindow.AcceptedDocument is null) return;
            Commit(previewWindow.AcceptedDocument);
            Search();
        }
        catch (Exception ex)
        {
            _dialogService.ShowErrorDialog(ex.Message, "修正失败", ex.ToString());
            Search();
        }
    }

    private void Commit(Models.StoryboardRoot corrected)
    {
        _history.RecordSnapshot(_context.Storyboard);
        _context.Storyboard = corrected;
        _validator.Validate(_context.Storyboard, _context);
        _context.MarkAsModified();
        _messageBroker.Publish("RefreshTimeline");
        HasAppliedChanges = true;
    }

    private void BtnFixMissing_Click(object sender, RoutedEventArgs e)
    {
        if (_report is null) return;
        var count = _report.Issues.Count(issue =>
            issue.Kind == StoryboardCorrectionKind.MissingBaseTime &&
            issue.CanAutomaticallyRepair);
        if (count == 0) return;
        if (!_dialogService.ShowYesNo(
                $"将依次提升并合并 {count} 个实体的首关键帧。是否继续？",
                "批量修正缺失初始时间"))
            return;

        try
        {
            _history.RecordSnapshot(_context.Storyboard);
            var working = _context.Storyboard;
            while (true)
            {
                var report = _analyzer.Scan(working, _context);
                var issue = report.Issues.FirstOrDefault(item =>
                    item.Kind == StoryboardCorrectionKind.MissingBaseTime &&
                    item.CanAutomaticallyRepair);
                if (issue is null) break;
                working = _correctionService.Apply(working, _context,
                    new StoryboardCorrectionPlan
                    {
                        DocumentFingerprint = report.DocumentFingerprint,
                        IssueId = issue.Id,
                        KeepParticipantIndex = 0
                    });
            }
            _context.Storyboard = working;
            _validator.Validate(working, _context);
            _context.MarkAsModified();
            _messageBroker.Publish("RefreshTimeline");
            HasAppliedChanges = true;
            Search();
        }
        catch (Exception ex)
        {
            _dialogService.ShowErrorDialog(ex.Message, "批量修正失败", ex.ToString());
            Search();
        }
    }

    private void BtnSafeMerge_Click(object sender, RoutedEventArgs e)
    {
        if (_report is null) return;
        var count = _report.Issues.Count(StoryboardCorrectionPolicy.CanSafelyMerge);
        if (count == 0) return;
        if (!_dialogService.ShowYesNo(
                $"将合并 {count} 个不存在异值属性的时间冲突组。数组只移除冲突时刻，是否继续？",
                "一键安全合并"))
            return;
        try
        {
            _history.RecordSnapshot(_context.Storyboard);
            var working = _context.Storyboard;
            while (true)
            {
                var report = _analyzer.Scan(working, _context);
                var issue = report.Issues.FirstOrDefault(
                    StoryboardCorrectionPolicy.CanSafelyMerge);
                if (issue is null) break;
                working = _correctionService.Apply(
                    working, _context,
                    StoryboardCorrectionPolicy.BuildSafeMergePlan(report, issue));
            }
            CommitBatch(working);
        }
        catch (Exception ex)
        {
            _dialogService.ShowErrorDialog(ex.Message, "一键安全合并失败", ex.ToString());
            Search();
        }
    }

    private void BtnOffsetAll_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadOffset(TxtBatchOffset.Text, out var delta))
        {
            _dialogService.ShowMessage("请输入非零的有限秒数，例如 +0.2 或 -0.1。",
                "错位参数无效", DialogMessageType.Warning);
            return;
        }
        if (_report is null) return;
        var count = _report.Issues.Count(issue =>
            issue.Kind == StoryboardCorrectionKind.SameTimeConflict);
        if (count == 0) return;
        if (!_dialogService.ShowYesNo(
                $"将依次选择每个冲突组中最后一个非初始关键帧并偏移 {delta:+0.######;-0.######} 秒。" +
                "若偏移后产生新冲突，将继续按相同步长错位。是否继续？",
                "一键错位冲突"))
            return;
        try
        {
            _history.RecordSnapshot(_context.Storyboard);
            var working = _context.Storyboard;
            var operations = 0;
            while (true)
            {
                var report = _analyzer.Scan(working, _context);
                var issue = report.Issues.FirstOrDefault(item =>
                    item.Kind == StoryboardCorrectionKind.SameTimeConflict &&
                    item.Participants.Any(participant => !participant.IsBaseState));
                if (issue is null) break;
                if (++operations > 10000)
                    throw new InvalidOperationException(
                        "批量错位达到安全上限，请缩小扫描范围或更换错位步长。");
                var target = issue.Participants.Last(participant => !participant.IsBaseState);
                working = _correctionService.Apply(working, _context,
                    new StoryboardCorrectionPlan
                    {
                        DocumentFingerprint = report.DocumentFingerprint,
                        IssueId = issue.Id,
                        KeepParticipantIndex = 0,
                        TimeOffset = new StoryboardTimeOffsetCorrection(
                            target.ParticipantIndex, delta)
                    });
            }
            CommitBatch(working);
        }
        catch (Exception ex)
        {
            _dialogService.ShowErrorDialog(ex.Message, "一键错位失败", ex.ToString());
            Search();
        }
    }

    private void CommitBatch(Models.StoryboardRoot corrected)
    {
        _context.Storyboard = corrected;
        _validator.Validate(corrected, _context);
        _context.MarkAsModified();
        _messageBroker.Publish("RefreshTimeline");
        HasAppliedChanges = true;
        Search();
    }

    internal static bool TryReadOffset(string? text, out double delta)
    {
        var valid = double.TryParse(text, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out delta) ||
                    double.TryParse(text, NumberStyles.Float,
                        CultureInfo.CurrentCulture, out delta);
        return valid && double.IsFinite(delta) &&
               Math.Abs(delta) >= StoryboardCorrectionAnalyzer.SameTimeTolerance;
    }

    private static string FrameName(StoryboardCorrectionParticipant participant) =>
        participant.IsBaseState
            ? "初始状态"
            : $"states[{participant.StateIndex}]" +
              (participant.ArrayIndex.HasValue ? $".time[{participant.ArrayIndex}]" : "");

    private static string KindTitle(StoryboardCorrectionKind kind) => kind switch
    {
        StoryboardCorrectionKind.MissingBaseTime => "缺失初始时间",
        StoryboardCorrectionKind.SameTimeConflict => "同时间关键帧冲突",
        _ => "时间无法解析"
    };

    private void BtnSearch_Click(object sender, RoutedEventArgs e) => Search();
    private void CmbFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) RenderIssues();
    }
    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded) RenderIssues();
    }
    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}
