using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Chart;
using Naziki_Editor.Models;
using Naziki_Editor.State;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Naziki_Editor.Core.Commands
{
    public class AppCommands
    {
        private readonly IProjectService _projectService;
        private readonly IDialogService _dialogService;
        private readonly IMessageBroker _messageBroker;
        private readonly IHistoryService _historyService;
        private readonly ICompilationService _compilationService;

        public AppCommands(
            IProjectService projectService,
            IDialogService dialogService,
            IMessageBroker messageBroker,
            IHistoryService historyService,
            ICompilationService compilationService)
        {
            _projectService = projectService;
            _dialogService = dialogService;
            _messageBroker = messageBroker;
            _historyService = historyService;
            _compilationService = compilationService;
        }

        // ==========================================
        // ⚓ 公开港口入城式：先谱面→再故事板→通知UI
        // ==========================================
        public void DoLoadProject(string projectPath, NazikiProjectModel projectData, ProjectDataContext context)
        {
            if (projectData == null) return;

            context.ProjectFilePath = projectPath;
            context.ProjectData = projectData;

            // ==========================================
            // 🟢 【第一优先级】：强制加载谱面 (Chart)
            // ==========================================
            bool isChartLoaded = false;
            if (!string.IsNullOrEmpty(projectData.ChartFilePath) && File.Exists(projectData.ChartFilePath))
            {
                SilentImportChart(context, projectData.ChartFilePath);
                if (context.Chart != null) isChartLoaded = true;
            }

            // ==========================================
            // 🔵 【第二优先级】：在有谱面的前提下，才允许加载故事板 (Storyboard)
            // ==========================================
            if (!string.IsNullOrEmpty(projectData.StoryboardExportPath))
            {
                if (!isChartLoaded)
                {
                    // ❌ 致命拦截：没有谱面却尝试加载故事板
                    _dialogService.ShowMessage(
                        "🚨 加载中止：检测到工程内存在故事板文件，但未找到或无法加载对应的谱面文件！\n" +
                        "为了保证故事板中所有时间锚点（如 start:noteId）的正确解析，必须优先导入谱面。\n" +
                        "请检查 .nep 文件中的 ChartFilePath 路径是否正确。",
                        "强制顺序加载失败", DialogMessageType.Error);

                    // 清空故事板相关路径，防止后续渲染出错
                    context.StoryboardPath = null;
                    context.Storyboard = new StoryboardRoot();
                    context.StoryboardMeta = new StoryboardMeta();
                }
                else
                {
                    try
                    {
                        // 🟢 有谱面，正常解析故事板
                        var result = _projectService.LoadProjectStoryboard(projectData.StoryboardExportPath, projectData);

                        if (result.Storyboard != null)
                        {
                            context.StoryboardPath = projectData.StoryboardExportPath;
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
                        _dialogService.ShowMessage($"读取工程内关联的故事板文件失败 QAQ：\n{ex.Message}", "同步失败");
                        context.Storyboard = new StoryboardRoot();
                    }
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
                _dialogService.ShowMessage($"物理写入工程配置文件 (.nep) 失败 QAQ：\n{ex.Message}", "工程记账失败");
            }
        }

        // ==========================================
        // 💾 保存项目
        // ==========================================
        public async Task DoSaveProject(ProjectDataContext context)
        {
            if (string.IsNullOrEmpty(context.StoryboardPath))
            {
                string? savePath = _dialogService.ShowSaveFileDialog("选择保存位置", "Cytoid 故事板 (*.json)|*.json", "storyboard.json");
                if (savePath != null)
                {
                    context.StoryboardPath = savePath;
                    if (context.ProjectData != null)
                        context.ProjectData.StoryboardExportPath = context.StoryboardPath;
                }
                else return;
            }

            try
            {
                // 🧙‍♂️ 1/2. 影子分离、展平编译与模板元数据同步已下沉到 ICompilationService
                var shadowStoryboard = _compilationService.CompileForExport(context);
                _compilationService.SyncTemplateMetadata(context);

                // 💾 3. 谱面主文件物理落盘 (纯净无套娃官方格式)
                await _projectService.ExportCytoidStoryboardAsync(shadowStoryboard, context.StoryboardPath);

                // 📒 4. 写入元数据小账本
                await _projectService.SaveStoryboardMetaAsync(context, context.StoryboardPath);

                // 保存原本的工程配置文件 `.nep`
                SaveProjectNepFile(context);

                _dialogService.ShowMessage("故事板已完美展平，元数据小账本也已同步写入硬盘！(๑>ᴗ<๑)✧", "全盘保存成功");
            }
            catch (Exception ex)
            {
                _dialogService.ShowMessage("时空网关在写入磁盘时爆炸啦 QAQ：\n" + ex.Message, "物理写盘错误");
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
                    C2Chart? chart = _projectService.SilentImportChart(chartFile);
                    if (chart == null || chart.time_base == 0) return;

                    context.Chart = chart;
                    context.TimeEngine = new ChartTimeEngine(chart.tempo_list, chart.time_base);

                    if (context.ProjectData != null)
                    {
                        context.ProjectData.ChartFilePath = chartFile;
                        SaveProjectNepFile(context);
                    }

                    string bpmText = ChartLogic.GetBpmText(chart.tempo_list);
                    _dialogService.ShowMessage($"谱面加载成功！\n🎵 音符数：{chart.note_list.Count} 个\n📄 谱面页数：{chart.page_list.Count} 页\n⏱️ 歌曲 BPM：{bpmText}", "情报解析成功");

                    // 通知 UI 层刷新音符列表、事件锁定状态等
                    _messageBroker.Publish("ChartImported");
                }
                catch (Exception ex) { _dialogService.ShowMessage($"解析发生爆炸 QAQ：\n{ex.Message}"); }
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
                    _dialogService.ShowMessage($"解析 .nep 工程文件时发生爆炸 QAQ：\n{ex.Message}", "读取错误");
                }
            }
        }
    }
}