using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Naziki_Editor.Core;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.ErrorHandling;
using Naziki_Editor.Core.Messaging;
using Naziki_Editor.Models;
using Naziki_Editor.State;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Naziki_Editor.Core.Project
{
    /// <summary>
    /// 项目文件服务，负责工程 (.nep)、故事板及元数据文件的加载与保存。
    /// 不包含任何 UI 依赖，所有异常均向调用方抛出，同时通过 IErrorHandler 记录标准化日志。
    /// </summary>
    public class ProjectService : IProjectService
    {
        private readonly IMessageBroker _messageBroker;
        private readonly IErrorHandler _errorHandler;
        private readonly IStoryboardParser _storyboardParser;
        private readonly IStoryboardDocumentReader _storyboardReader;
        private readonly IStoryboardDocumentWriter _storyboardWriter;
        private readonly IStoryboardDocumentValidator _storyboardValidator;

        public ProjectService(
            IMessageBroker messageBroker,
            IErrorHandler errorHandler,
            IStoryboardParser storyboardParser,
            IStoryboardDocumentReader storyboardReader,
            IStoryboardDocumentWriter storyboardWriter,
            IStoryboardDocumentValidator storyboardValidator)
        {
            _messageBroker = messageBroker;
            _errorHandler = errorHandler;
            _storyboardParser = storyboardParser;
            _storyboardReader = storyboardReader;
            _storyboardWriter = storyboardWriter;
            _storyboardValidator = storyboardValidator;
        }

        public Task<ProjectDataContext?> LoadProjectAsync(string filePath)
        {
            var context = LoadProjectData(filePath);
            return Task.FromResult<ProjectDataContext?>(context);
        }

        public Task SaveProjectAsync(ProjectDataContext context, string filePath)
        {
            SaveProjectNepFile(context, filePath);
            return Task.CompletedTask;
        }

        public Task ExportCytoidStoryboardAsync(
            StoryboardRoot storyboard,
            string outputPath,
            ProjectDataContext? context = null)
        {
            if (storyboard == null) throw new ArgumentNullException(nameof(storyboard));
            if (string.IsNullOrEmpty(outputPath)) throw new ArgumentException("输出路径不能为空", nameof(outputPath));

            try
            {
                var diagnostics = _storyboardValidator.Validate(storyboard, context);
                var errors = diagnostics.Where(d => d.Severity == StoryboardDiagnosticSeverity.Error).ToArray();
                if (errors.Length > 0)
                    throw new JsonSerializationException(string.Join(Environment.NewLine,
                        errors.Select(error => $"{error.Path}: {error.Message}")));
                WriteAllTextAtomic(outputPath, _storyboardWriter.Write(storyboard));
            }
            catch (JsonException ex)
            {
                _errorHandler.HandleException(ex, ErrorSeverity.Error, "DataValidation",
                    "故事板导出序列化失败", "ProjectService.ExportCytoidStoryboardAsync",
                    $"OutputPath: {outputPath}");
                throw;
            }
            catch (IOException ex)
            {
                _errorHandler.HandleException(ex, ErrorSeverity.Critical, "FileIO",
                    "写入导出故事板文件时发生 I/O 错误", "ProjectService.ExportCytoidStoryboardAsync",
                    $"OutputPath: {outputPath}");
                throw;
            }

            return Task.CompletedTask;
        }

        public Task SaveStoryboardMetaAsync(ProjectDataContext context, string storyboardPath)
        {
            SaveStoryboardMeta(context, storyboardPath);
            return Task.CompletedTask;
        }

        public Task SaveProjectNepFileAsync(ProjectDataContext context, string? filePath = null)
        {
            SaveProjectNepFile(context, filePath);
            return Task.CompletedTask;
        }

        /// <summary>
        /// 从 .nep 工程文件加载工程数据并构建上下文。
        /// </summary>
        public ProjectDataContext? LoadProjectData(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentException("文件路径不能为空", nameof(filePath));
            if (!File.Exists(filePath)) throw new FileNotFoundException("工程文件不存在", filePath);

            try
            {
                string jsonText = File.ReadAllText(filePath);
                var projectData = JsonConvert.DeserializeObject<NazikiProjectModel>(jsonText);
                if (projectData == null)
                {
                    _errorHandler.HandleException(
                        new InvalidOperationException("工程文件解析结果为空"),
                        ErrorSeverity.Error, "DataValidation",
                        "工程文件 (.nep) 反序列化后为 null", "ProjectService.LoadProjectData",
                        $"FilePath: {filePath}");
                    return null;
                }
                if (projectData.FormatVersion != 2)
                    throw new JsonSerializationException(
                        $"不支持的项目格式版本 {projectData.FormatVersion}；当前版本仅支持 .nep v2。");

                var context = new ProjectDataContext(_messageBroker)
                {
                    ProjectFilePath = filePath,
                    ProjectData = projectData
                };

                return context;
            }
            catch (JsonException ex)
            {
                _errorHandler.HandleException(ex, ErrorSeverity.Error, "DataValidation",
                    "工程文件 (.nep) JSON 格式错误", "ProjectService.LoadProjectData",
                    $"FilePath: {filePath}");
                throw;
            }
            catch (IOException ex)
            {
                _errorHandler.HandleException(ex, ErrorSeverity.Error, "FileIO",
                    "读取工程文件 (.nep) 时发生 I/O 错误", "ProjectService.LoadProjectData",
                    $"FilePath: {filePath}");
                throw;
            }
        }

        /// <summary>
        /// 保存 .nep 工程文件。
        /// </summary>
        public void SaveProjectNepFile(ProjectDataContext context, string? filePath = null)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            string targetPath = filePath ?? context.ProjectFilePath;
            if (string.IsNullOrEmpty(targetPath)) throw new InvalidOperationException("工程文件路径未设置");
            if (context.ProjectData == null) throw new InvalidOperationException("工程数据为空");

            try
            {
                context.ProjectData.LastModifiedTime = DateTime.Now;
                string json = JsonConvert.SerializeObject(context.ProjectData, Formatting.Indented);
                WriteAllTextAtomic(targetPath, json);
            }
            catch (JsonException ex)
            {
                _errorHandler.HandleException(ex, ErrorSeverity.Error, "DataValidation",
                    "工程数据序列化失败", "ProjectService.SaveProjectNepFile",
                    $"FilePath: {targetPath}");
                throw;
            }
            catch (IOException ex)
            {
                _errorHandler.HandleException(ex, ErrorSeverity.Critical, "FileIO",
                    "写入工程文件 (.nep) 时发生 I/O 错误", "ProjectService.SaveProjectNepFile",
                    $"FilePath: {targetPath}");
                throw;
            }
        }

        /// <summary>
        /// 将单个实体保存为素材胶囊 (.nem) 文件，返回写入的完整路径。
        /// </summary>
        public string SaveAssetCapsule(ProjectDataContext context, IStoryboardEntity entity, string materialType)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (context.ProjectFilePath == null) throw new InvalidOperationException("工程文件路径未设置");
            if (context.ProjectData == null) throw new InvalidOperationException("工程数据为空");

            string projectDir = Path.GetDirectoryName(context.ProjectFilePath)!;
            string materialsDir = Path.Combine(projectDir, context.ProjectData.MaterialFolderPath);
            if (!Directory.Exists(materialsDir)) Directory.CreateDirectory(materialsDir);

            string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"{materialType}_Preset_{timeStamp}.nem";
            string filePath = Path.Combine(materialsDir, fileName);

            var miniRoot = new StoryboardRoot();
            if (entity is C2Sprite s) miniRoot.sprites = new List<C2Sprite> { s };
            else if (entity is C2Text t) miniRoot.texts = new List<C2Text> { t };
            else if (entity is C2Line l) miniRoot.lines = new List<C2Line> { l };
            else if (entity is C2Video v) miniRoot.videos = new List<C2Video> { v };
            else if (entity is C2SceneController c) miniRoot.controllers = new List<C2SceneController> { c };
            else if (entity is C2NoteController nc) miniRoot.note_controllers = new List<C2NoteController> { nc };
            else throw new InvalidOperationException($"不支持的实体类型：{entity.GetType().Name}");

            var capsule = new NemDocument
            {
                MaterialType = materialType,
                MaterialName = Path.GetFileNameWithoutExtension(fileName),
                Payload = miniRoot
            };
            var capsuleJson = JObject.FromObject(capsule);
            capsuleJson["payload"] = JObject.Parse(_storyboardWriter.Write(miniRoot));
            string pureJson = capsuleJson.ToString(Formatting.Indented);
            try
            {
                WriteAllTextAtomic(filePath, pureJson);
            }
            catch (IOException ex)
            {
                _errorHandler.HandleException(ex, ErrorSeverity.Error, "FileIO",
                    "保存素材胶囊 (.nem) 时发生 I/O 错误", "ProjectService.SaveAssetCapsule",
                    $"FilePath: {filePath}");
                throw;
            }

            return filePath;
        }

        /// <summary>
        /// 从文件路径加载故事板 JSON。
        /// </summary>
        public StoryboardRoot LoadStoryboard(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentException("文件路径不能为空", nameof(filePath));
            if (!File.Exists(filePath)) throw new FileNotFoundException("故事板文件不存在", filePath);

            try
            {
                string jsonText = File.ReadAllText(filePath);
                var storyboard = _storyboardReader.Read(jsonText);
                if (storyboard == null)
                {
                    _errorHandler.HandleException(
                        new InvalidOperationException("故事板文件解析结果为空"),
                        ErrorSeverity.Error, "DataValidation",
                        "故事板 JSON 反序列化后为 null", "ProjectService.LoadStoryboard",
                        $"FilePath: {filePath}");
                    throw new InvalidOperationException("故事板文件解析结果为空");
                }
                _storyboardValidator.Validate(storyboard);
                return storyboard;
            }
            catch (JsonException ex)
            {
                _errorHandler.HandleException(ex, ErrorSeverity.Error, "DataValidation",
                    "故事板文件 JSON 格式错误", "ProjectService.LoadStoryboard",
                    $"FilePath: {filePath}");
                throw;
            }
            catch (IOException ex)
            {
                _errorHandler.HandleException(ex, ErrorSeverity.Error, "FileIO",
                    "读取故事板文件时发生 I/O 错误", "ProjectService.LoadStoryboard",
                    $"FilePath: {filePath}");
                throw;
            }
        }

        /// <summary>
        /// 尝试加载故事板元数据文件；若不存在或损坏则返回空元数据对象。
        /// </summary>
        public StoryboardMeta LoadStoryboardMeta(string storyboardPath)
        {
            if (string.IsNullOrEmpty(storyboardPath)) return new StoryboardMeta();

            string metaPath = storyboardPath + "_meta.json";
            if (!File.Exists(metaPath)) return new StoryboardMeta();

            string metaContent = File.ReadAllText(metaPath);
            return JsonConvert.DeserializeObject<StoryboardMeta>(metaContent) ?? new StoryboardMeta();
        }

        /// <summary>
        /// 导入故事板文件：加载 JSON、标准化 ID、读取元数据，返回故事板与元数据。
        /// </summary>
        public (StoryboardRoot Storyboard, StoryboardMeta Meta) ImportStoryboard(string storyboardPath, NazikiProjectModel? projectData)
        {
            if (string.IsNullOrEmpty(storyboardPath)) throw new ArgumentException("故事板路径不能为空", nameof(storyboardPath));

            var storyboard = LoadStoryboard(storyboardPath);
            _storyboardParser.StandardizeStoryboardIds(storyboard, projectData);

            var meta = LoadStoryboardMeta(storyboardPath);
            return (storyboard, meta);
        }

        /// <summary>
        /// 加载项目关联的故事板：校验文件、反序列化、标准化 ID、同步控制板映射并读取元数据。
        /// 若文件不存在或路径为空，则返回空故事板与空元数据，不抛出异常。
        /// </summary>
        public (StoryboardRoot? Storyboard, StoryboardMeta Meta) LoadProjectStoryboard(string storyboardPath, NazikiProjectModel projectData)
        {
            if (string.IsNullOrEmpty(storyboardPath) || !File.Exists(storyboardPath))
                return (null, new StoryboardMeta());

            var storyboard = LoadStoryboard(storyboardPath);
            _storyboardParser.StandardizeStoryboardIds(storyboard, projectData);
            _storyboardParser.SyncControlBoardIdMaps(storyboard, projectData);

            var meta = LoadStoryboardMeta(storyboardPath);
            return (storyboard, meta);
        }

        /// <summary>
        /// 保存故事板元数据小账本。
        /// </summary>
        public void SaveStoryboardMeta(ProjectDataContext context, string storyboardPath)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrEmpty(storyboardPath)) throw new ArgumentException("故事板路径不能为空", nameof(storyboardPath));

            if (context.StoryboardMeta == null) context.StoryboardMeta = new StoryboardMeta();

            string metaPath = storyboardPath + "_meta.json";
            string metaJson = JsonConvert.SerializeObject(context.StoryboardMeta, Formatting.Indented);
            WriteAllTextAtomic(metaPath, metaJson);
        }

        /// <summary>
        /// 静默导入谱面文件，返回解析后的 C2Chart。
        /// </summary>
        public C2Chart? SilentImportChart(string chartPath)
        {
            if (string.IsNullOrEmpty(chartPath)) throw new ArgumentException("谱面路径不能为空", nameof(chartPath));
            if (!File.Exists(chartPath)) throw new FileNotFoundException("谱面文件不存在", chartPath);

            try
            {
                string jsonText = File.ReadAllText(chartPath);
                var chart = JsonConvert.DeserializeObject<C2Chart>(jsonText);
                if (chart == null)
                {
                    _errorHandler.HandleException(
                        new InvalidOperationException("谱面文件解析结果为空"),
                        ErrorSeverity.Error, "DataValidation",
                        "谱面 JSON 反序列化后为 null", "ProjectService.SilentImportChart",
                        $"FilePath: {chartPath}");
                }
                return chart;
            }
            catch (JsonException ex)
            {
                _errorHandler.HandleException(ex, ErrorSeverity.Error, "DataValidation",
                    "谱面文件 JSON 格式错误", "ProjectService.SilentImportChart",
                    $"FilePath: {chartPath}");
                return null;
            }
            catch (IOException ex)
            {
                _errorHandler.HandleException(ex, ErrorSeverity.Error, "FileIO",
                    "读取谱面文件时发生 I/O 错误", "ProjectService.SilentImportChart",
                    $"FilePath: {chartPath}");
                return null;
            }
        }

        private static void WriteAllTextAtomic(string targetPath, string content)
        {
            var fullPath = Path.GetFullPath(targetPath);
            var directory = Path.GetDirectoryName(fullPath)
                ?? throw new IOException($"Cannot resolve output directory for '{targetPath}'.");
            Directory.CreateDirectory(directory);
            var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(temporaryPath, content, new System.Text.UTF8Encoding(false));
                File.Move(temporaryPath, fullPath, true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }
    }
}
