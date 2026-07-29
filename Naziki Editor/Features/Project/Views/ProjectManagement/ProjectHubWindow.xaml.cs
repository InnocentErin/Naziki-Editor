using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Models;
using Naziki_Editor.Views;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;
using Naziki_Editor.Features.EditorShell;
using Naziki_Editor.Features.Project.Loading;
using Naziki_Editor.Views.Loading;

namespace Naziki_Editor.ProjectManagement
{
    public partial class ProjectHubWindow : Window
    {
        private readonly IDialogService _dialogService;
        private readonly IProjectOpenPreparationService _projectLoader;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILoadingService _loading;
        private bool _projectOpening;

        // 💾 档案馆模型：专门用来存储单条历史足迹
        public class ProjectHistoryItem
        {
            public string ProjectName { get; set; }
            public string FilePath { get; set; }
            public DateTime LastOpened { get; set; }
        }

        public ProjectHubWindow(IDialogService dialogService,
            IServiceProvider serviceProvider,
            IProjectOpenPreparationService projectLoader)
        {
            InitializeComponent();

            _dialogService = dialogService;
            _projectLoader = projectLoader;
            _serviceProvider = serviceProvider;
            _loading = AppServices.GetService<ILoadingService>();
            _loading.Register(this, LoadingOverlay);

            // 📡 在构造时死死拴住线缆：监听左侧历史列表的"点选"动作！
            HistoryListBox.SelectionChanged += HistoryListBox_SelectionChanged;

            LoadHistory();
        }

        // 获取程序本体所在目录下的记账本路径
        private string GetHistoryFilePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "project_history.json");
        }

        // ==========================================
        // 📥 档案馆核心：从物理磁盘读取记录
        // ==========================================
        private List<ProjectHistoryItem> GetHistoryList()
        {
            string path = GetHistoryFilePath();
            if (!File.Exists(path)) return new List<ProjectHistoryItem>();
            try
            {
                string json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<List<ProjectHistoryItem>>(json) ?? new List<ProjectHistoryItem>();
            }
            catch
            {
                return new List<ProjectHistoryItem>(); // 账本坏了就返回空列表防崩溃
            }
        }

        // ==========================================
        // 🎨 UI 渲染：把历史记录优雅地画到左侧黑板上
        // ==========================================
        private void LoadHistory()
        {
            if (HistoryListBox == null) return;
            HistoryListBox.Items.Clear();

            var list = GetHistoryList();

            // 如果账本空空如也，挂上温馨提示
            if (list.Count == 0)
            {
                HistoryListBox.Items.Add(new TextBlock
                {
                    Text = "暂无历史记录...",
                    Foreground = (Brush)FindResource("TipsColor"),
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(10)
                });
                return;
            }

            // 循环把足迹画出来
            foreach (var item in list)
            {
                ListBoxItem listBoxItem = new ListBoxItem { Tag = item, Margin = new Thickness(0, 2, 0, 2) };

                // 用 Grid 编织两行排版：上面大字项目名，下面灰色小字路径
                Grid grid = new Grid { Margin = new Thickness(5), Width = 240 };
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                TextBlock nameTxt = new TextBlock { Text = item.ProjectName, FontSize = 13, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("MainTextColor") };
                // 开启 TextTrimming，路径太长会自动变成漂亮的小省略号哦！
                TextBlock pathTxt = new TextBlock { Text = item.FilePath, FontSize = 10, Foreground = (Brush)FindResource("SecTextColor"), Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };

                Grid.SetRow(nameTxt, 0);
                Grid.SetRow(pathTxt, 1);
                grid.Children.Add(nameTxt);
                grid.Children.Add(pathTxt);

                listBoxItem.Content = grid;
                HistoryListBox.Items.Add(listBoxItem);
            }
        }

        // ==========================================
        // ✍️ 档案馆账本改写：追加或更新一条足迹
        // ==========================================
        private void AddToHistory(string filePath, string projectName)
        {
            var list = GetHistoryList();
            // 先揪出并剔除过往同物理路径的旧账（防止无限重复）
            list.RemoveAll(x => x.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));

            // 把最新的足迹插到队伍的最前面（第一顺位）
            list.Insert(0, new ProjectHistoryItem
            {
                ProjectName = projectName,
                FilePath = filePath,
                LastOpened = DateTime.Now
            });

            // 防爆仓防爆内存：最多只维护最近的 20 条记录
            if (list.Count > 20) list = list.Take(20).ToList();

            try
            {
                File.WriteAllText(GetHistoryFilePath(), JsonConvert.SerializeObject(list, Formatting.Indented));
            }
            catch { }
        }

        // ==========================================
        // 📡 历史记录点击响应：核心路径双向交叉安检
        // ==========================================
        private async void HistoryListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_projectOpening) return;
            if (HistoryListBox.SelectedItem is ListBoxItem selectedItem && selectedItem.Tag is ProjectHistoryItem historyItem)
            {
                // 🔥 关键防死锁：点完立刻清空选中状态，确保用户连续点击同一个失效文件时依然能疯狂弹窗！
                HistoryListBox.SelectedIndex = -1;

                // 🔍 【安检 A 面】：项目依然乖乖待在原有位置
                if (File.Exists(historyItem.FilePath))
                {
                    await OpenProjectAsync(historyItem.FilePath);
                }
                // 🛑 【安检 B 面】：纳尼？！项目玩失踪移形换位了！
                else
                {
                    // 强制弹出符合设计师设想的最终决断弹窗
                    var result = _dialogService.ShowYesNo(
                        $"抱歉主人！无法在原路径找到该工程文件：\n［{historyItem.FilePath}］\n\n该工程可能已被手动移动、重命名或无情删除。\n\n是否直接从历史档案馆中［删除记录］？",
                        "⚠️ 档案馆警报：检测到失效资产");

                    if (result)
                    {
                        // 🧹 物理擦除除尘！
                        var list = GetHistoryList();
                        list.RemoveAll(x => x.FilePath.Equals(historyItem.FilePath, StringComparison.OrdinalIgnoreCase));
                        try
                        {
                            File.WriteAllText(GetHistoryFilePath(), JsonConvert.SerializeObject(list, Formatting.Indented));
                        }
                        catch { }

                        LoadHistory(); // 瞬间重绘左侧黑板
                    }
                }
            }
        }

        // ==========================================
        // ✨ 右侧操作：新建项目文件
        // ==========================================
        private async void BtnCreateProject_Click(object sender, RoutedEventArgs e)
        {
            if (_projectOpening) return;
            var wizard = new Naziki_Editor.Views.Dialogs.NewProjectDialog
            {
                Owner = this
            };
            if (wizard.ShowDialog() == true && wizard.CreatedProject is { } created)
            {
                await OpenProjectAsync(
                    created.ProjectFilePath,
                    "项目初始化失败");
            }
        }

        // ==========================================
        // 📂 右侧操作：手动打开项目文件
        // ==========================================
        private async void BtnOpenProject_Click(object sender, RoutedEventArgs e)
        {
            if (_projectOpening) return;
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Title = "打开 Naziki 工程文件",
                Filter = "Naziki 工程文件 (*.nep)|*.nep"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                await OpenProjectAsync(openFileDialog.FileName);
            }
        }

        public async Task OpenProjectAsync(
            string projectFilePath,
            string failureTitle = "打开失败")
        {
            if (_projectOpening) return;

            if (!File.Exists(projectFilePath))
            {
                _dialogService.ShowErrorDialog(
                    $"无法找到项目文件：{projectFilePath}",
                    failureTitle);
                return;
            }

            try
            {
                _projectOpening = true;
                SetProjectActionsEnabled(false);
                using var loading = _loading.Begin(
                    this,
                    "请稍等，正在打开项目");
                await LaunchMainWindowAsync(projectFilePath);
            }
            catch (Exception ex)
            {
                _projectOpening = false;
                SetProjectActionsEnabled(true);
                _dialogService.ShowErrorDialog(
                    FormatLoadError(ex),
                    failureTitle,
                    ex.ToString());
            }
        }

        private async Task LaunchMainWindowAsync(string projectFilePath)
        {
            MainWindow? editorWindow = null;
            using var manualCancellation = new CancellationTokenSource();
            using var timeoutCancellation =
                new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var linkedCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    manualCancellation.Token, timeoutCancellation.Token);
            var stopwatch = Stopwatch.StartNew();
            _loading.SetCancellation(this, manualCancellation.Cancel);
            var progressEntries = new List<(ProjectLoadStage Stage, string Message)>();
            IProgress<ProjectLoadProgress> progress = new Progress<ProjectLoadProgress>(value =>
            {
                var index = progressEntries.FindIndex(item =>
                    item.Stage == value.Stage);
                var entry = (value.Stage, value.Message);
                if (index >= 0) progressEntries[index] = entry;
                else progressEntries.Add(entry);
                var steps = string.Join(
                    Environment.NewLine,
                    progressEntries.Select(item =>
                        $"  {GetStageName(item.Stage)}  {item.Message}"));
                _loading.Update(this,
                    $"正在准备编辑器，请稍候…  {stopwatch.Elapsed:mm\\:ss}" +
                    $"{Environment.NewLine}{Environment.NewLine}{steps}",
                    value.Percentage);
            });

            try
            {
                var prepared = await _projectLoader.PrepareAsync(
                    projectFilePath, progress, linkedCancellation.Token);
                linkedCancellation.Token.ThrowIfCancellationRequested();

                progress.Report(new(ProjectLoadStage.EditorSurface,
                    "正在创建编辑器控件…",
                    ProjectLoadPipeline.DataPreparationComplete,
                    ProjectLoadPipeline.TotalSteps));
                await Dispatcher.InvokeAsync(
                    () => { },
                    System.Windows.Threading.DispatcherPriority.Render);
                editorWindow = _serviceProvider.GetRequiredService<MainWindow>();

                await editorWindow.PrepareProjectAsync(
                    prepared, progress,
                    linkedCancellation.Token);
                linkedCancellation.Token.ThrowIfCancellationRequested();

                progress.Report(new(ProjectLoadStage.Commit,
                    "全部内容已加载，正在打开编辑器…",
                    ProjectLoadPipeline.Complete,
                    ProjectLoadPipeline.TotalSteps));
                AddToHistory(projectFilePath,
                    prepared.Context.ProjectData.ProjectName);
                Application.Current.MainWindow = editorWindow;
                editorWindow.CommitPreparedProject();
                editorWindow.Show();
                editorWindow.Activate();
                this.Close();
            }
            catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)
            {
                editorWindow?.Close();
                throw new TimeoutException(
                    "项目加载超过 60 秒，操作已终止。请检查最后显示的加载阶段后重试。");
            }
            catch
            {
                editorWindow?.Close();
                throw;
            }
            finally
            {
                _loading.SetCancellation(this, null);
            }
        }

        private static string GetStageName(ProjectLoadStage stage) => stage switch
        {
            ProjectLoadStage.ProjectConfiguration => "工程配置",
            ProjectLoadStage.ResourcePaths => "资源路径",
            ProjectLoadStage.Chart => "谱面数据",
            ProjectLoadStage.Storyboard => "故事板",
            ProjectLoadStage.Events => "事件列表",
            ProjectLoadStage.Notes => "音符列表",
            ProjectLoadStage.Assets => "素材资源",
            ProjectLoadStage.EditorSurface => "编辑器控件",
            ProjectLoadStage.Audio => "音频资源",
            ProjectLoadStage.Commit => "完成",
            _ => stage.ToString()
        };

        private static string FormatLoadError(Exception ex)
        {
            if (ex is ProjectLoadException load)
            {
                var path = string.IsNullOrWhiteSpace(load.ResourcePath)
                    ? string.Empty
                    : $"\n相关路径：{load.ResourcePath}";
                return $"项目加载在“{load.Stage}”阶段失败：{load.Message}{path}\n\n{load.InnerException?.Message}";
            }
            if (ex is OperationCanceledException)
                return "项目加载已取消，编辑器没有切换到未完成的工程。";
            return ex.Message;
        }

        private void SetProjectActionsEnabled(bool enabled)
        {
            BtnCreateProject.IsEnabled = enabled;
            BtnOpenProject.IsEnabled = enabled;
            HistoryListBox.IsEnabled = enabled;
        }
    }
}
