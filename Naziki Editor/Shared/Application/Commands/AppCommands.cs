using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Models;
using Naziki_Editor.State;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Security.Cryptography;
using System.Text;
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
        private readonly IStoryboardSourceStore _storyboardSourceStore;
        private readonly IStoryboardCanonicalBridge _storyboardBridge;
        private readonly IStoryboardImportCoordinator _storyboardImportCoordinator;

        public AppCommands(
            IProjectService projectService,
            IDialogService dialogService,
            IMessageBroker messageBroker,
            IHistoryService historyService,
            ICompilationService compilationService,
            INotificationService notificationService,
            IStoryboardDocumentValidator storyboardValidator,
            IProjectResourceService projectResources,
            IStoryboardSourceStore storyboardSourceStore,
            IStoryboardCanonicalBridge storyboardBridge,
            IStoryboardImportCoordinator storyboardImportCoordinator)
        {
            _projectService = projectService;
            _dialogService = dialogService;
            _messageBroker = messageBroker;
            _historyService = historyService;
            _compilationService = compilationService;
            _notificationService = notificationService;
            _storyboardValidator = storyboardValidator;
            _projectResources = projectResources;
            _storyboardSourceStore = storyboardSourceStore;
            _storyboardBridge = storyboardBridge;
            _storyboardImportCoordinator = storyboardImportCoordinator;
        }

        // ==========================================
        // ⚓ 公开港口入城式：先谱面→再故事板→通知UI
        // ==========================================
        public void DoLoadProject(
            string projectPath,
            NazikiProjectModel projectData,
            ProjectDataContext context,
            bool publishLoaded = true,
            bool strict = false)
        {
            if (projectData == null) return;

            context.ProjectFilePath = projectPath;
            context.ProjectData = projectData;
            context.ChartDocument = null;
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
                SilentImportChart(context, chartPath, strict);
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
                    if (strict)
                        throw new InvalidDataException(
                            $"读取工程故事板失败：{storyboardPath}", ex);
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

            if (projectData.FormatVersion == 3)
            {
                context.EditorStoryboard =
                    _storyboardImportCoordinator.EnsureCanonicalSource(
                        context);
            }
            else
            {
                throw new InvalidDataException(
                    "当前编辑器仅支持 format_version: 3 的工程。");
            }
#pragma warning disable CS0618
            context.LegacyStoryboardProjectionHash =
                _storyboardBridge.ComputeLegacyProjectionHash(context.Storyboard);
#pragma warning restore CS0618
            if (context.ProjectData
                    .StoryboardSourceRecoveredDuringLoad)
            {
                _notificationService.ShowWarning(
                    "规范故事板源已从运行故事板自动重建。模板绑定和逐 note 覆盖等编辑器专属信息无法恢复。",
                    8000);
                context.ProjectData
                    .StoryboardSourceRecoveredDuringLoad = false;
            }

            // ==========================================
            // 🔴 【第三优先级】：加载完成后，通知 UI 刷新
            // ==========================================
            if (publishLoaded)
                _messageBroker.Publish("ProjectLoaded");

            string? ResolveConfigured(ProjectResourceKind kind)
            {
                try { return _projectResources.ResolvePath(context, kind); }
                catch
                {
                    if (strict) throw;
                    return null;
                }
            }
        }

        private void SilentImportChart(
            ProjectDataContext context,
            string chartPath,
            bool strict)
        {
            try
            {
                var document =
                    _projectService.LoadChartDocument(chartPath);
                context.ChartDocument = document;
                context.Chart = document.Projection;
                context.TimeEngine = new ChartTimeEngine(
                    context.Chart.tempo_list,
                    context.Chart.time_base);
            }
            catch
            {
                if (strict) throw;
            }
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

            IReadOnlyList<FileSnapshot> snapshots = [];
            var exportHashBeforeSave =
                context.EditorStoryboard.Metadata.LastExportHash;
            try
            {
                if (HasExternalStoryboardConflict(context))
                {
                    var choice = _dialogService.ShowConfirm(
                        "检测到运行导出 JSON 已被外部程序修改。\n\n" +
                        "“是”：重新导入外部 JSON 并替换规范源（模板绑定和逐 note 覆盖可能丢失）；\n" +
                        "“否”：保留规范源并覆盖外部运行 JSON；\n" +
                        "“取消”：中止本次保存。",
                        "故事板外部修改冲突",
                        DialogMessageType.Warning);
                    if (choice == ConfirmResult.Cancel) return;
                    if (choice == ConfirmResult.Yes)
                        ReimportExternalStoryboard(context);
                }

                snapshots = CaptureSaveTargets(context);
                var runtime = _storyboardBridge.Export(context);
                var sourceErrors = runtime.Issues
                    .Where(item => item.Severity ==
                                   StoryboardDiagnosticSeverity.Error)
                    .ToArray();
                _messageBroker.Publish("RefreshStoryboardDiagnostics");
                if (sourceErrors.Length > 0)
                    throw new JsonSerializationException(
                        "故事板源文档验证失败，未执行编译或写盘：" + Environment.NewLine +
                        string.Join(Environment.NewLine, sourceErrors.Take(12)
                            .Select(error => $"{error.Path}: {error.Message}")));

                // 🧙‍♂️ 1/2. 影子分离、展平编译与模板元数据同步已下沉到 ICompilationService
                // The canonical bridge materialized and validated the runtime
                // document above. Saving must not invoke the legacy flattener.

                // 💾 3. 谱面主文件物理落盘 (纯净无套娃官方格式)
                await _projectService.ExportCytoidStoryboardJsonAsync(
                    runtime.Json.ToString(Formatting.Indented),
                    context.StoryboardPath);
                context.EditorStoryboard.Metadata.LastExportHash = Hash(
                    runtime.Json.ToString(Formatting.None));

                // 📒 4. 写入元数据小账本
                _compilationService.SyncTemplateMetadata(context);
                context.EditorStoryboard.Metadata.LegacyMeta =
                    JObject.FromObject(context.StoryboardMeta);
                context.EditorStoryboard.Metadata.ControlBoardIdMaps =
                    new Dictionary<string, string>(
                        context.ProjectData?.ControlBoardIdMaps ??
                        new Dictionary<string, string>(),
                        StringComparer.Ordinal);

                // 保存原本的工程配置文件 `.nep`
                _projectService.SaveProjectNepFile(context, context.ProjectFilePath);

                _notificationService.ShowSuccess("故事板已完美展平，元数据小账本也已同步写入硬盘！(๑>ᴗ<๑)✧");
            }
            catch (Exception ex)
            {
                context.EditorStoryboard.Metadata.LastExportHash =
                    exportHashBeforeSave;
                RestoreSnapshots(snapshots);
                _dialogService.ShowErrorDialog("时空网关在写入磁盘时爆炸啦 QAQ：\n" + ex.Message, "物理写盘错误", ex.ToString());
            }
        }

        private bool HasExternalStoryboardConflict(ProjectDataContext context)
        {
            var expected = context.EditorStoryboard.Metadata.LastExportHash;
            if (string.IsNullOrWhiteSpace(expected) ||
                string.IsNullOrWhiteSpace(context.StoryboardPath) ||
                !File.Exists(context.StoryboardPath))
                return false;
            try
            {
                var normalized = JToken.Parse(
                    File.ReadAllText(context.StoryboardPath))
                    .ToString(Formatting.None);
                return !string.Equals(expected, Hash(normalized),
                    StringComparison.Ordinal);
            }
            catch (JsonException)
            {
                return true;
            }
        }

        private void ReimportExternalStoryboard(ProjectDataContext context)
        {
            var path = context.StoryboardPath ??
                       throw new InvalidOperationException(
                           "运行导出路径未设置。");
            var json = File.ReadAllText(path);
            var imported = AppServices.GetService<IStoryboardImportService>()
                .Import(json, context.Chart, context.StoryboardMeta,
                    context.ProjectData?.ControlBoardIdMaps);
            if (!imported.CanReplace)
                throw new JsonSerializationException(string.Join(
                    Environment.NewLine,
                    imported.Issues.Where(issue =>
                            issue.Severity ==
                            StoryboardDiagnosticSeverity.Error)
                        .Select(issue =>
                        $"{issue.Path}: {issue.Message}")));
            context.EditorStoryboard = imported.Document;
#pragma warning disable CS0618
            context.Storyboard = AppServices
                .GetService<IStoryboardDocumentReader>().Read(json);
            context.LegacyStoryboardProjectionHash =
                _storyboardBridge.ComputeLegacyProjectionHash(
                    context.Storyboard);
#pragma warning restore CS0618
        }

        private static string Hash(string text) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))
                .ToLowerInvariant();

        private IReadOnlyList<FileSnapshot> CaptureSaveTargets(
            ProjectDataContext context)
        {
            var sourcePath = string.IsNullOrWhiteSpace(
                context.StoryboardSourcePath)
                ? _storyboardSourceStore.GetDefaultSourcePath(
                    context.ProjectFilePath)
                : context.StoryboardSourcePath;
            return new[]
                {
                    context.StoryboardPath,
                    sourcePath,
                    context.ProjectFilePath
                }
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => File.Exists(path)
                    ? new FileSnapshot(path!, true,
                        File.ReadAllBytes(path!))
                    : new FileSnapshot(path!, false, null))
                .ToArray();
        }

        private static void RestoreSnapshots(
            IEnumerable<FileSnapshot> snapshots)
        {
            foreach (var snapshot in snapshots.Reverse())
            {
                try
                {
                    if (snapshot.Existed)
                    {
                        Directory.CreateDirectory(
                            Path.GetDirectoryName(snapshot.Path)!);
                        File.WriteAllBytes(snapshot.Path,
                            snapshot.Contents ?? []);
                    }
                    else if (File.Exists(snapshot.Path))
                    {
                        File.Delete(snapshot.Path);
                    }
                }
                catch
                {
                    // Keep reporting the original save failure.
                }
            }
        }

        private sealed record FileSnapshot(
            string Path, bool Existed, byte[]? Contents);

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
                    var document =
                        _projectService.LoadChartDocument(managedChart);
                    var chart = document.Projection;
                    context.ChartDocument = document;
                    context.Chart = chart;
                    context.TimeEngine = new ChartTimeEngine(
                        chart.tempo_list, chart.time_base);

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
