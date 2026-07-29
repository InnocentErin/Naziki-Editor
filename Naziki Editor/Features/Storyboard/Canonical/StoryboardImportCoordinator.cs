using System.Security.Cryptography;
using System.Text;
using System.IO;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Models;
using Naziki_Editor.State;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Naziki_Editor.Core.Storyboard.Canonical;

public sealed class StoryboardImportCoordinator :
    IStoryboardImportCoordinator
{
    private const string SourceRelativePath =
        ".naziki/storyboard.editor.json";
    private const string RuntimeRelativePath = "level/storyboard.json";

    private readonly IStoryboardImportService _importer;
    private readonly IStoryboardRuntimeExporter _exporter;
    private readonly IStoryboardSourceStore _sourceStore;
    private readonly IEditorStoryboardSerializer _serializer;
    private readonly IStoryboardDocumentReader _wireReader;
    private readonly IStoryboardCanonicalBridge _bridge;
    private readonly IMessageBroker _messages;

    public StoryboardImportCoordinator(
        IStoryboardImportService importer,
        IStoryboardRuntimeExporter exporter,
        IStoryboardSourceStore sourceStore,
        IEditorStoryboardSerializer serializer,
        IStoryboardDocumentReader wireReader,
        IStoryboardCanonicalBridge bridge,
        IMessageBroker messages)
    {
        _importer = importer;
        _exporter = exporter;
        _sourceStore = sourceStore;
        _serializer = serializer;
        _wireReader = wireReader;
        _bridge = bridge;
        _messages = messages;
    }

    public StoryboardImportCandidate Prepare(
        string json,
        C2Chart? chart = null,
        ITimeEngine? timeEngine = null,
        StoryboardMeta? legacyMeta = null,
        IReadOnlyDictionary<string, string>? controlBoardIds = null)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException(
                "故事板文件为空，无法建立规范故事板源。");

        var imported = _importer.Import(json, chart, legacyMeta,
            controlBoardIds);
        if (!imported.CanReplace || imported.Document is null)
            throw CreateImportException(imported.Issues);

        var document = imported.Document;
        var runtime = _exporter.Export(document, chart, timeEngine);
        var issues = imported.Issues.Concat(runtime.Issues).ToArray();
        if (!runtime.Success)
            throw CreateImportException(issues);

        var runtimeText = runtime.Json.ToString(Formatting.None);
        document.Metadata.LastExportHash = Hash(runtimeText);
        var sourceText = _serializer.Serialize(document);
        var projection = _wireReader.Read(runtimeText);
        return new StoryboardImportCandidate(
            document,
            runtime.Json,
            projection,
            issues,
            Hash(sourceText),
            document.Metadata.LastExportHash);
    }

    public async Task<StoryboardImportCommitResult> ImportAndCommitAsync(
        ProjectDataContext context,
        string externalStoryboardPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        EnsureProjectContext(context);
        if (string.IsNullOrWhiteSpace(externalStoryboardPath) ||
            !File.Exists(externalStoryboardPath))
            throw new FileNotFoundException(
                "待导入的故事板文件不存在。", externalStoryboardPath);

        var json = await File.ReadAllTextAsync(
            externalStoryboardPath, cancellationToken).ConfigureAwait(false);
        var candidate = Prepare(json, context.Chart, context.TimeEngine,
            context.StoryboardMeta,
            context.ProjectData.ControlBoardIdMaps);

        var projectPath = Path.GetFullPath(context.ProjectFilePath);
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var sourcePath = Path.Combine(projectDirectory,
            SourceRelativePath.Replace('/',
                Path.DirectorySeparatorChar));
        var runtimePath = Path.Combine(projectDirectory,
            RuntimeRelativePath.Replace('/',
                Path.DirectorySeparatorChar));
        var nextProject = CloneProject(context.ProjectData);
        nextProject.FormatVersion = 3;
        nextProject.StoryboardSourcePath = SourceRelativePath;
        nextProject.StoryboardExportPath = RuntimeRelativePath;
        nextProject.StoryboardSourceHash = candidate.SourceHash;
        nextProject.StoryboardExportHash = candidate.RuntimeHash;
        nextProject.LastModifiedTime = DateTime.Now;

        var writes = new[]
        {
            new PendingWrite(sourcePath,
                _serializer.Serialize(candidate.Document)),
            new PendingWrite(runtimePath,
                candidate.RuntimeJson.ToString(Formatting.Indented)),
            new PendingWrite(projectPath,
                JsonConvert.SerializeObject(nextProject,
                    Formatting.Indented))
        };
        CommitAtomically(writes);

        context.ProjectData = nextProject;
        context.StoryboardSourcePath = sourcePath;
        context.StoryboardPath = runtimePath;
        context.EditorStoryboard = candidate.Document;
#pragma warning disable CS0618
        context.Storyboard = candidate.LegacyProjection;
        context.LegacyStoryboardProjectionHash =
            _bridge.ComputeLegacyProjectionHash(
                candidate.LegacyProjection);
#pragma warning restore CS0618
        _messages.Publish("ProjectResourcesChanged",
            new Features.Project.Resources.ProjectResourceChanged(
                Features.Project.Resources.ProjectResourceKind.Storyboard,
                RuntimeRelativePath, runtimePath));

        return new StoryboardImportCommitResult(
            candidate.Document,
            candidate.LegacyProjection,
            sourcePath,
            runtimePath,
            candidate.RuntimeHash,
            candidate.Issues);
    }

    public Task<StoryboardImportCommitResult> CommitCurrentAsync(
        ProjectDataContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        EnsureProjectContext(context);
        cancellationToken.ThrowIfCancellationRequested();
        var runtime = _exporter.Export(context.EditorStoryboard,
            context.Chart, context.TimeEngine);
        if (!runtime.Success)
            throw CreateImportException(runtime.Issues);
        var runtimeText = runtime.Json.ToString(Formatting.None);
        context.EditorStoryboard.Metadata.LastExportHash =
            Hash(runtimeText);
        var candidate = new StoryboardImportCandidate(
            context.EditorStoryboard,
            runtime.Json,
            _wireReader.Read(runtimeText),
            runtime.Issues,
            Hash(_serializer.Serialize(
                context.EditorStoryboard)),
            context.EditorStoryboard.Metadata.LastExportHash);
        return Task.FromResult(CommitCandidate(context, candidate));
    }

    public EditorStoryboardDocument EnsureCanonicalSource(
        ProjectDataContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        EnsureProjectContext(context);
        if (context.ProjectData.FormatVersion != 3)
            throw new JsonSerializationException(
                "当前编辑器仅支持 format_version 明确为 3 的工程。");

        var projectPath = Path.GetFullPath(context.ProjectFilePath);
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var configuredSource =
            context.ProjectData.StoryboardSourcePath;
        var sourcePath = string.IsNullOrWhiteSpace(configuredSource)
            ? Path.Combine(projectDirectory, SourceRelativePath.Replace(
                '/', Path.DirectorySeparatorChar))
            : ResolveInsideProject(projectDirectory, configuredSource);

        try
        {
            var existing = _sourceStore.Load(sourcePath);
            context.StoryboardSourcePath = sourcePath;
            context.EditorStoryboard = existing;
            return existing;
        }
        catch (Exception ex) when (
            ex is FileNotFoundException or JsonException or
                InvalidDataException)
        {
            var runtimeConfigured =
                context.ProjectData.StoryboardExportPath;
            if (string.IsNullOrWhiteSpace(runtimeConfigured))
                throw new InvalidDataException(
                    "v3 工程缺少规范故事板源，且未配置可用于修复的运行故事板。",
                    ex);
            var runtimePath = ResolveInsideProject(projectDirectory,
                runtimeConfigured);
            if (!File.Exists(runtimePath))
                throw new InvalidDataException(
                    "v3 工程的规范故事板源和运行故事板均不存在，无法恢复。",
                    ex);

            var runtimeJson = File.ReadAllText(runtimePath);
            var candidate = Prepare(runtimeJson, context.Chart,
                context.TimeEngine, context.StoryboardMeta,
                context.ProjectData.ControlBoardIdMaps);
            candidate.Document.Metadata.LastExportHash = Hash(
                JToken.Parse(runtimeJson).ToString(Formatting.None));

            if (File.Exists(sourcePath))
                BackupCorruptSource(projectDirectory, sourcePath);

            var nextProject = CloneProject(context.ProjectData);
            nextProject.FormatVersion = 3;
            nextProject.StoryboardSourcePath = SourceRelativePath;
            nextProject.StoryboardSourceHash = Hash(
                _serializer.Serialize(candidate.Document));
            nextProject.StoryboardExportHash =
                candidate.Document.Metadata.LastExportHash;
            nextProject.LastModifiedTime = DateTime.Now;
            var defaultSourcePath = Path.Combine(projectDirectory,
                SourceRelativePath.Replace('/',
                    Path.DirectorySeparatorChar));
            CommitAtomically([
                new PendingWrite(defaultSourcePath,
                    _serializer.Serialize(candidate.Document)),
                new PendingWrite(projectPath,
                    JsonConvert.SerializeObject(nextProject,
                        Formatting.Indented))
            ]);

            context.ProjectData = nextProject;
            context.ProjectData.StoryboardSourceRecoveredDuringLoad =
                true;
            context.StoryboardSourcePath = defaultSourcePath;
            context.StoryboardPath = runtimePath;
            context.EditorStoryboard = candidate.Document;
#pragma warning disable CS0618
            context.Storyboard = candidate.LegacyProjection;
            context.LegacyStoryboardProjectionHash =
                _bridge.ComputeLegacyProjectionHash(
                    candidate.LegacyProjection);
#pragma warning restore CS0618
            _messages.Publish("StoryboardSourceRecovered",
                "规范故事板源已从运行故事板自动重建；模板绑定和逐 note 覆盖无法恢复。");
            return candidate.Document;
        }
    }

    private static void CommitAtomically(
        IReadOnlyList<PendingWrite> writes)
    {
        var snapshots = writes.Select(write =>
            new FileSnapshot(write.Path, File.Exists(write.Path),
                File.Exists(write.Path)
                    ? File.ReadAllBytes(write.Path)
                    : null)).ToArray();
        var temporaries = new List<(string Target, string Temporary)>();
        try
        {
            foreach (var write in writes)
            {
                var directory = Path.GetDirectoryName(write.Path)
                    ?? throw new IOException(
                        $"无法解析写入目录：{write.Path}");
                Directory.CreateDirectory(directory);
                var temporary = Path.Combine(directory,
                    $".{Path.GetFileName(write.Path)}.{Guid.NewGuid():N}.tmp");
                File.WriteAllText(temporary, write.Content,
                    new UTF8Encoding(false));
                temporaries.Add((write.Path, temporary));
            }

            // The caller orders .nep last, making it the commit marker.
            foreach (var item in temporaries)
                File.Move(item.Temporary, item.Target, true);
        }
        catch
        {
            foreach (var snapshot in snapshots.Reverse())
            {
                try
                {
                    if (snapshot.Existed)
                        File.WriteAllBytes(snapshot.Path,
                            snapshot.Content ?? []);
                    else if (File.Exists(snapshot.Path))
                        File.Delete(snapshot.Path);
                }
                catch
                {
                    // Preserve the original transaction error.
                }
            }
            throw;
        }
        finally
        {
            foreach (var item in temporaries)
            {
                try
                {
                    if (File.Exists(item.Temporary))
                        File.Delete(item.Temporary);
                }
                catch
                {
                    // Temporary cleanup is best effort.
                }
            }
        }
    }

    private StoryboardImportCommitResult CommitCandidate(
        ProjectDataContext context,
        StoryboardImportCandidate candidate)
    {
        var projectPath = Path.GetFullPath(context.ProjectFilePath);
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var sourcePath = Path.Combine(projectDirectory,
            SourceRelativePath.Replace('/',
                Path.DirectorySeparatorChar));
        var runtimePath = Path.Combine(projectDirectory,
            RuntimeRelativePath.Replace('/',
                Path.DirectorySeparatorChar));
        var nextProject = CloneProject(context.ProjectData);
        nextProject.FormatVersion = 3;
        nextProject.StoryboardSourcePath = SourceRelativePath;
        nextProject.StoryboardExportPath = RuntimeRelativePath;
        nextProject.StoryboardSourceHash = candidate.SourceHash;
        nextProject.StoryboardExportHash = candidate.RuntimeHash;
        nextProject.LastModifiedTime = DateTime.Now;
        CommitAtomically([
            new PendingWrite(sourcePath,
                _serializer.Serialize(candidate.Document)),
            new PendingWrite(runtimePath,
                candidate.RuntimeJson.ToString(Formatting.Indented)),
            new PendingWrite(projectPath,
                JsonConvert.SerializeObject(nextProject,
                    Formatting.Indented))
        ]);
        context.ProjectData = nextProject;
        context.StoryboardSourcePath = sourcePath;
        context.StoryboardPath = runtimePath;
        context.EditorStoryboard = candidate.Document;
#pragma warning disable CS0618
        context.Storyboard = candidate.LegacyProjection;
        context.LegacyStoryboardProjectionHash =
            _bridge.ComputeLegacyProjectionHash(
                candidate.LegacyProjection);
#pragma warning restore CS0618
        return new StoryboardImportCommitResult(
            candidate.Document,
            candidate.LegacyProjection,
            sourcePath,
            runtimePath,
            candidate.RuntimeHash,
            candidate.Issues);
    }

    private static void BackupCorruptSource(
        string projectDirectory, string sourcePath)
    {
        var recoveryDirectory = Path.Combine(projectDirectory,
            ".naziki", "recovery");
        Directory.CreateDirectory(recoveryDirectory);
        var backup = Path.Combine(recoveryDirectory,
            $"storyboard.editor.corrupt.{DateTime.UtcNow:yyyyMMddHHmmssfff}.json");
        File.Copy(sourcePath, backup, false);
    }

    private static NazikiProjectModel CloneProject(
        NazikiProjectModel project) =>
        JsonConvert.DeserializeObject<NazikiProjectModel>(
            JsonConvert.SerializeObject(project))
        ?? throw new JsonSerializationException(
            "无法复制 v3 工程清单。");

    private static string ResolveInsideProject(
        string projectDirectory, string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
            throw new InvalidDataException(
                "v3 工程资源路径必须是工程内相对路径。");
        var resolved = Path.GetFullPath(Path.Combine(projectDirectory,
            configuredPath.Replace('/',
                Path.DirectorySeparatorChar)));
        var root = projectDirectory.TrimEnd(
                       Path.DirectorySeparatorChar,
                       Path.AltDirectorySeparatorChar) +
                   Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(root,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "v3 工程资源路径越出了工程目录。");
        return resolved;
    }

    private static void EnsureProjectContext(
        ProjectDataContext context)
    {
        if (context.ProjectData is null ||
            string.IsNullOrWhiteSpace(context.ProjectFilePath))
            throw new InvalidOperationException(
                "必须先打开或创建 v3 工程。");
    }

    private static InvalidDataException CreateImportException(
        IEnumerable<StoryboardImportIssue> issues)
    {
        var details = issues.Where(issue =>
                issue.Severity == StoryboardDiagnosticSeverity.Error)
            .Take(20)
            .Select(issue => $"{issue.Path}: {issue.Message}")
            .ToArray();
        return new InvalidDataException(
            details.Length == 0
                ? "故事板无法规范化或生成有效运行文件。"
                : string.Join(Environment.NewLine, details));
    }

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(text)))
            .ToLowerInvariant();

    private sealed record PendingWrite(string Path, string Content);
    private sealed record FileSnapshot(
        string Path, bool Existed, byte[]? Content);
}
