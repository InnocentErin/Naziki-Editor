using Microsoft.Win32;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Features.Project.Resources;
using Naziki_Editor.State;
using System.IO;
using System.Collections.ObjectModel;
using System.Windows;
using Naziki_Editor.Views.StoryboardCorrections;

namespace Naziki_Editor.Views.Dialogs;

public partial class NewProjectDialog : Window
{
    private readonly IProjectResourceService _resources;
    private readonly IProjectService _projects;
    private readonly ProjectDataContext? _repairContext;
    private readonly ObservableCollection<string> _assetSources = [];

    public ProjectCreationResult? CreatedProject { get; private set; }
    public bool IsRepairMode => _repairContext is not null;

    public NewProjectDialog()
        : this(
            AppServices.GetService<IProjectResourceService>(),
            AppServices.GetService<IProjectService>(),
            null)
    {
    }

    public NewProjectDialog(
        IProjectResourceService resources,
        IProjectService projects,
        ProjectDataContext? repairContext)
    {
        _resources = resources;
        _projects = projects;
        _repairContext = repairContext;
        InitializeComponent();
        AssetSourceList.ItemsSource = _assetSources;
        if (repairContext is not null)
            ConfigureRepairMode(repairContext);
    }

    private void ConfigureRepairMode(ProjectDataContext context)
    {
        Title = "修复工程资源";
        TxtTitle.Text = "补全工程必要资源";
        TxtSubtitle.Text = "缺失资源会锁定对应功能。选择文件后会复制进工程并保存为相对路径。";
        BtnConfirm.Content = "保存并修复";
        TxtFolderHint.Text = "已有有效资源不会重复复制；只处理新选择或缺失的资源。";
        TxtProjectName.Text = context.ProjectData?.ProjectName ?? "未命名工程";
        TxtProjectName.IsReadOnly = true;
        TxtProjectPath.Text = context.ProjectFilePath ?? string.Empty;
        TxtProjectPath.IsReadOnly = true;
        FillExisting(ProjectResourceKind.Level, TxtLevelPath);
        FillExisting(ProjectResourceKind.Chart, TxtChartPath);
        FillExisting(ProjectResourceKind.Music, TxtMusicPath);
        FillExisting(ProjectResourceKind.Background, TxtBackgroundPath);
        FillExisting(ProjectResourceKind.Storyboard, TxtStoryboardPath);
        AssetImportPanel.Visibility = Visibility.Collapsed;

        void FillExisting(ProjectResourceKind kind, System.Windows.Controls.TextBox target)
        {
            try
            {
                var path = _resources.ResolvePath(context, kind);
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    target.Text = path;
            }
            catch { }
        }
    }

    private void BrowseProject_Click(object sender, RoutedEventArgs e)
    {
        if (IsRepairMode) return;
        var dialog = new SaveFileDialog
        {
            Title = "选择工程文件位置",
            Filter = "Naziki 工程文件 (*.nep)|*.nep",
            FileName = string.IsNullOrWhiteSpace(TxtProjectName.Text)
                ? "storyboard.nep"
                : TxtProjectName.Text.Trim() + ".nep"
        };
        if (dialog.ShowDialog(this) == true)
        {
            TxtProjectPath.Text = dialog.FileName;
            if (string.IsNullOrWhiteSpace(TxtProjectName.Text) ||
                TxtProjectName.Text == "未命名故事板")
                TxtProjectName.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
        }
    }

    private void BrowseChart_Click(object sender, RoutedEventArgs e) =>
        Browse(TxtChartPath, "选择 Cytoid 谱面", "JSON 文件 (*.json)|*.json");

    private void BrowseLevel_Click(object sender, RoutedEventArgs e) =>
        Browse(TxtLevelPath, "选择 Cytoid 关卡 level 文件", "JSON 文件 (*.json)|*.json");

    private void BrowseMusic_Click(object sender, RoutedEventArgs e) =>
        Browse(TxtMusicPath, "选择关卡音乐", "音频文件 (*.mp3;*.wav;*.ogg)|*.mp3;*.wav;*.ogg");

    private void BrowseBackground_Click(object sender, RoutedEventArgs e) =>
        Browse(TxtBackgroundPath, "选择背景图片",
            "图片文件 (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp");

    private void BrowseStoryboard_Click(object sender, RoutedEventArgs e) =>
        Browse(TxtStoryboardPath, "选择已有故事板", "JSON 文件 (*.json)|*.json");

    private void BrowseAssets_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择初始素材",
            Multiselect = true,
            Filter = "所有支持的素材|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.mp4;*.webm;*.avi;*.mov|图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|视频文件|*.mp4;*.webm;*.avi;*.mov"
        };
        if (dialog.ShowDialog(this) != true) return;
        var existing = new HashSet<string>(_assetSources, StringComparer.OrdinalIgnoreCase);
        foreach (var file in dialog.FileNames.Select(Path.GetFullPath))
            if (existing.Add(file))
                _assetSources.Add(file);
        UpdateAssetSummary();
    }

    private void RemoveSelectedAssets_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in AssetSourceList.SelectedItems.Cast<string>().ToArray())
            _assetSources.Remove(item);
        UpdateAssetSummary();
    }

    private void ClearAssets_Click(object sender, RoutedEventArgs e)
    {
        _assetSources.Clear();
        UpdateAssetSummary();
    }

    private void UpdateAssetSummary() =>
        TxtAssetSummary.Text = _assetSources.Count == 0
            ? "尚未选择素材"
            : $"已选择 {_assetSources.Count} 个素材，将复制到 assets 文件夹";

    private void Browse(System.Windows.Controls.TextBox target, string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter };
        if (dialog.ShowDialog(this) == true)
            target.Text = dialog.FileName;
    }

    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        ErrorPanel.Visibility = Visibility.Collapsed;
        BtnConfirm.IsEnabled = false;
        try
        {
            if (IsRepairMode)
                await RepairAsync();
            else
                await CreateAsync();
            DialogResult = true;
        }
        catch (Exception ex)
        {
            TxtError.Text = ex.Message;
            ErrorPanel.Visibility = Visibility.Visible;
        }
        finally
        {
            BtnConfirm.IsEnabled = true;
        }
    }

    private async Task CreateAsync()
    {
        if (string.IsNullOrWhiteSpace(TxtProjectPath.Text))
            throw new InvalidDataException("请选择工程文件保存位置。");
        var progress = new Progress<ProjectCreationProgress>(value =>
        {
            TxtAssetSummary.Text = value.Message;
        });
        using var resolvedStoryboard =
            string.IsNullOrWhiteSpace(TxtStoryboardPath.Text)
                ? null
                : await StoryboardSourceConflictResolver.ResolveAsync(
                    this, TxtStoryboardPath.Text);
        if (!string.IsNullOrWhiteSpace(TxtStoryboardPath.Text) &&
            resolvedStoryboard is null)
            throw new OperationCanceledException(
                "已取消故事板属性冲突修正，工程未创建。");

        CreatedProject = await _resources.CreateProjectAsync(new ProjectCreationRequest(
            TxtProjectPath.Text,
            TxtProjectName.Text,
            TxtLevelPath.Text,
            TxtChartPath.Text,
            TxtMusicPath.Text,
            TxtBackgroundPath.Text,
            resolvedStoryboard?.Path,
            _assetSources.ToArray(),
            progress));
    }

    private async Task RepairAsync()
    {
        var context = _repairContext!;
        await ImportIfChanged(ProjectResourceKind.Level, TxtLevelPath.Text);
        await ImportIfChanged(ProjectResourceKind.Chart, TxtChartPath.Text);
        await ImportIfChanged(ProjectResourceKind.Music, TxtMusicPath.Text);
        await ImportIfChanged(ProjectResourceKind.Background, TxtBackgroundPath.Text);
        if (string.IsNullOrWhiteSpace(TxtStoryboardPath.Text))
            await _resources.EnsureStoryboardAsync(context);
        else
            await ImportIfChanged(ProjectResourceKind.Storyboard, TxtStoryboardPath.Text);

        _projects.SaveProjectNepFile(context, context.ProjectFilePath);

        async Task ImportIfChanged(ProjectResourceKind kind, string selected)
        {
            if (string.IsNullOrWhiteSpace(selected))
                throw new InvalidDataException($"{DisplayName(kind)}为必填资源。");
            string? current = null;
            try { current = _resources.ResolvePath(context, kind); }
            catch { }
            if (string.Equals(
                    current is null ? null : Path.GetFullPath(current),
                    Path.GetFullPath(selected),
                    StringComparison.OrdinalIgnoreCase) &&
                File.Exists(selected))
            {
                _resources.ValidateSource(kind, selected);
                return;
            }
            if (kind == ProjectResourceKind.Storyboard)
            {
                using var resolved =
                    await StoryboardSourceConflictResolver.ResolveAsync(
                        this, selected);
                if (resolved is null)
                    throw new OperationCanceledException(
                        "已取消故事板属性冲突修正。");
                await _resources.ImportAsync(context, kind, resolved.Path);
            }
            else
                await _resources.ImportAsync(context, kind, selected);
        }
    }

    private static string DisplayName(ProjectResourceKind kind) => kind switch
    {
        ProjectResourceKind.Level => "关卡 level 文件",
        ProjectResourceKind.Chart => "谱面",
        ProjectResourceKind.Music => "关卡音乐",
        ProjectResourceKind.Background => "背景图片",
        _ => "资源"
    };

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
