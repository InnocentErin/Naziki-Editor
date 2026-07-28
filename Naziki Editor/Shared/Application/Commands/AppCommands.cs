using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Chart;
using Naziki_Editor.Models;
using Naziki_Editor.State;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Naziki_Editor.Features.Project.Resources;

namespace Naziki_Editor.Core.Commands
{
    public class AppCommands
    {
        private readonly IProjectService _projectService;
        private readonly IDialogService _dialogService;
        private readonly IMessageBroker _messageBroker;
        private readonly IHistoryService _historyService;
        private readonly ICompilationService _compilationService;
        private readonly INotificationService _notificationService;
        private readonly IStoryboardDocumentValidator _storyboardValidator;
        private readonly IProjectResourceService _projectResources;

        public AppCommands(
            IProjectService projectService,
            IDialogService dialogService,
            IMessageBroker messageBroker,
            IHistoryService historyService,
            ICompilationService compilationService,
            INotificationService notificationService,
            IStoryboardDocumentValidator storyboardValidator,
            IProjectResourceService projectResources)
        {
            _projectService = projectService;
            _dialogService = dialogService;
            _messageBroker = messageBroker;
            _historyService = historyService;
            _compilationService = compilationService;
            _notificationService = notificationService;
            _storyboardValidator = storyboardValidator;
            _projectResources = projectResources;
        }

        // ==========================================
        // ⚓ 公开港口入城式：先谱面→再故事板→通知UI
        // ==========================================
        public void DoLoadProject(string projectPath, NazikiProjectModel projectData, ProjectDataContext context)
        {
            if (projectData == null) return;

            context.ProjectFilePath = projectPath;
            context.ProjectData = projectData;
            context.Chart = null;
            context.TimeEngine = null;
            context.StoryboardPath = null;
            context.Storyboard = new StoryboardRoot();
            context.StoryboardMeta = new StoryboardMeta();

            // ==========================================
            // 🟢 【第一优先级】：强制加载谱面 (Chart)
            // ==========================================
            var chartPath = ResolveConfigured(ProjectResourceKind.Chart);
            if (!string.IsNullOrEmpty(chartPath) && File.Exists(chartPath))
            {
                SilentImportChart(context, chartPath);
            }

            // ==========================================
            // 🔵 【第二优先级】：在有谱面的前提下，才允许加载故事板 (Storyboard)
            // ==========================================
            var storyboardPath = ResolveConfigured(ProjectResourceKind.Storyboard);
            if (!string.IsNullOrEmpty(projectData.StoryboardExportPath))
            {
                try
                {
                    // 故事板本身可独立查看；缺谱面时仅锁定 Note Controller 等依赖功能。
                    var result = _projectService.LoadProjectStoryboard(storyboardPath ?? string.Empty, projectData);

                    if (result.Storyboard != null)
                    {
                        context.StoryboardPath = storyboardPath;
                        context.Storyboard = result.Storyboard;
                        context.StoryboardMeta = result.Meta;

                        _historyService.Reset();
                        _historyService.RecordSnapshot(context.Storyboard);
                    }
                    else
                    {
                        context.Storyboard = new StoryboardRoot();
                        _historyService.Reset();
                        _historyService.RecordSnapshot(context.Storyboard);
                    }
                }
                catch (Exception ex)
                {
                    _dialogService.ShowErrorDialog($"读取工程内关联的故事板文件失败 QAQ：\n{ex.Message}", "同步失败", ex.ToString());
                    context.Storyboard = new StoryboardRoot();
                }
            }
            else
            {
                // 无故事板路径，重置历史
                _historyService.Reset();
                _historyService.RecordSnapshot(context.Storyboard);
            }

            // ==========================================
            // 🔴 【第三优先级】：加载完成后，通知 UI 刷新
            // ==========================================
            _messageBroker.Publish("ProjectLoaded");

            string? ResolveConfigured(ProjectResourceKind kind)
            {
                try { return _projectResources.ResolvePath(context, kind); }
                catch { return null; }
            }
        }

        private void SilentImportChart(ProjectDataContext context, string chartPath)
        {
            try
            {
                C2Chart? chart = _projectService.SilentImportChart(chartPath);
                if (chart != null)
                {
                    context.Chart = chart;
                    context.TimeEngine = new ChartTimeEngine(chart.tempo_list, chart.time_base);
                }
            }
            catch { }
        }

        // ==========================================
        // 💾 核心加装：.nep 工程物理存盘记账引擎
        // ==========================================
        private void SaveProjectNepFile(ProjectDataContext context)
        {
            try
            {
                _projectService.SaveProjectNepFile(context, context.ProjectFilePath);
            }
            catch (Exception ex)
            {
                _dialogService.ShowErrorDialog($"物理写入工程配置文件 (.nep) 失败 QAQ：\n{ex.Message}", "工程记账失败", ex.ToString());
            }
        }

        // ==========================================
        // 💾 保存项目
        // ==========================================
        public async Task DoSaveProject(ProjectDataContext context)
        {
            if (string.IsNullOrEmpty(context.StoryboardPath))
            {
                if (context.ProjectData != null && !string.IsNullOrWhiteSpace(context.ProjectFilePath))
                {
                    await _projectResources.EnsureStoryboardAsync(context);
                    context.StoryboardPath =
                        _projectResources.ResolvePath(context, ProjectResourceKind.Storyboard);
                }
                else
                {
                    string? savePath = _dialogService.ShowSaveFileDialog(
                        "选择保存位置",
                        "Cytoid 故事板 (*.json)|*.json",
                        "storyboard.json");
                    if (savePath == null) return;
                    context.StoryboardPath = savePath;
                }
            }

            try
            {
                var sourceErrors = _storyboardValidator
                    .Validate(context.Storyboard, context)
                    .Where(item => item.Severity == StoryboardDiagnosticSeverity.Error)
                    .ToArray();
                _messageBroker.Publish("RefreshStoryboardDiagnostics");
                if (sourceErrors.Length > 0)
                    throw new JsonSerializationException(
                        "故事板源文档验证失败，未执行编译或写盘：" + Environment.NewLine +
                        string.Join(Environment.NewLine, sourceErrors.Take(12)
                            .Select(error => $"{error.Path}: {error.Message}")));

                // 🧙‍♂️ 1/2. 影子分离、展平编译与模板元数据同步已下沉到 ICompilationService
                var shadowStoryboard = _compilationService.CompileForExport(context);

                // 💾 3. 谱面主文件物理落盘 (纯净无套娃官方格式)
                await _projectService.ExportCytoidStoryboardAsync(
                    shadowStoryboard, context.StoryboardPath, context);

                // 📒 4. 写入元数据小账本
                _compilationService.SyncTemplateMetadata(context);
                await _projectService.SaveStoryboardMetaAsync(context, context.StoryboardPath);

                // 保存原本的工程配置文件 `.nep`
                _projectService.SaveProjectNepFile(context, context.ProjectFilePath);

                _notificationService.ShowSuccess("故事板已完美展平，元数据小账本也已同步写入硬盘！(๑>ᴗ<๑)✧");
            }
            catch (Exception ex)
            {
                _dialogService.ShowErrorDialog("时空网关在写入磁盘时爆炸啦 QAQ：\n" + ex.Message, "物理写盘错误", ex.ToString());
            }
        }

        // ==========================================
        // 📥 导入谱面
        // ==========================================
        public async Task DoImportChart(ProjectDataContext context)
        {
            string? chartFile = _dialogService.ShowOpenFileDialog("请选择你的谱面文件", "Cytus II 谱面 (*.json)|*.json");
            if (chartFile != null)
            {
                try
                {
                    await _projectResources.ImportAsync(
                        context,
                        ProjectResourceKind.Chart,
                        chartFile);
                    var managedChart = _projectResources.ResolvePath(context, ProjectResourceKind.Chart)
                        ?? throw new InvalidDataException("无法解析导入后的谱面路径。");
                    C2Chart? chart = _projectService.SilentImportChart(managedChart);
                    if (chart == null || chart.time_base == 0) return;

                    context.Chart = chart;
                    context.TimeEngine = new ChartTimeEngine(chart.tempo_list, chart.time_base);

                    if (context.ProjectData != null)
                    {
                        SaveProjectNepFile(context);
                    }

                    string bpmText = ChartLogic.GetBpmText(chart.tempo_list);
                    _notificationService.ShowSuccess($"谱面加载成功！🎵 音符数：{chart.note_list.Count} 个 | 📄 谱面页数：{chart.page_list.Count} 页 | ⏱️ 歌曲 BPM：{bpmText}");

                    // 通知 UI 层刷新音符列表、事件锁定状态等
                    _messageBroker.Publish("ChartImported");
                }
                catch (Exception ex) { _dialogService.ShowErrorDialog($"解析发生爆炸 QAQ：\n{ex.Message}", "解析发生爆炸", ex.ToString()); }
            }
        }

        // ==========================================
        // 📂 打开 .nep 核心工程文件
        // ==========================================
        public async Task DoOpenProject(Action<ProjectDataContext> onProjectLoaded)
        {
            // 1. 🪄 召唤文件选择魔法阵，专门只抓取 .nep 后缀的工程账本
            string? projectFile = _dialogService.ShowOpenFileDialog("请选择你要打开的工程宇宙", "Naziki 工程文件 (*.nep)|*.nep");

            if (projectFile != null)
            {
                try
                {
                    // 2. 📖 读取物理文件，并用 Newtonsoft 还原出工程模型基因
                    var loadedContext = _projectService.LoadProjectData(projectFile);

                    if (loadedContext?.ProjectData != null)
                    {
                        // 3. 🚀 完美闭环：呼叫主战舰早已备好的港口入城式法术！
                        loadedContext.ProjectFilePath = projectFile;
                        onProjectLoaded(loadedContext);
                    }
                    else
                    {
                        _dialogService.ShowMessage("这个工程文件似乎是个空壳子哦！", "解析失败");
                    }
                }
                catch (Exception ex)
                {
                    _dialogService.ShowErrorDialog($"解析 .nep 工程文件时发生爆炸 QAQ：\n{ex.Message}", "读取错误", ex.ToString());
                }
            }
        }
    }
}
