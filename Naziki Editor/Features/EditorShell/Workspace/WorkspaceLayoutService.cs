using Naziki_Editor.Core.Abstractions;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using System.IO;

namespace Naziki_Editor.Features.EditorShell.Workspace;

public enum WorkspacePaneId
{
    Canvas,
    Events,
    Notes,
    Assets,
    Properties,
    MainTimeline
}

public sealed record WorkspacePaneViewModel(WorkspacePaneId Id, string Title, string ContentId);
public sealed record WorkspaceLayoutDescriptor(string Id, string Name, bool IsBuiltIn);

public interface IWorkspaceLayoutService
{
    IReadOnlyList<WorkspaceLayoutDescriptor> ListLayouts();
    WorkspaceLayoutDescriptor SaveAs(string name, string layoutXml);
    void Rename(string id, string name);
    string? Activate(string id);
    void Delete(string id);
    string? RestoreLastActive();
    void ResetToDefault();
}

public sealed class WorkspaceLayoutService : IWorkspaceLayoutService
{
    public const string DefaultLayoutId = "default";
    private const string MetadataKey = "Workspace.Layouts";
    private const string ActiveKey = "Workspace.ActiveLayout";
    private static readonly Regex InvalidName = new(@"[\\/:*?""<>|]", RegexOptions.Compiled);
    private readonly ISettingsStore _settings;
    private readonly string _layoutDirectory;

    public WorkspaceLayoutService(ISettingsStore settings)
    {
        _settings = settings;
        _layoutDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NazikiEditor", "Layouts");
    }

    public IReadOnlyList<WorkspaceLayoutDescriptor> ListLayouts()
    {
        var user = JsonConvert.DeserializeObject<List<WorkspaceLayoutDescriptor>>(
            _settings.Get(MetadataKey, "[]")) ?? [];
        return new[] { new WorkspaceLayoutDescriptor(DefaultLayoutId, "Default", true) }
            .Concat(user.Where(item => !item.IsBuiltIn && File.Exists(PathFor(item.Id))))
            .ToArray();
    }

    public WorkspaceLayoutDescriptor SaveAs(string name, string layoutXml)
    {
        ValidateName(name);
        Directory.CreateDirectory(_layoutDirectory);
        var descriptors = ListLayouts().Where(item => !item.IsBuiltIn).ToList();
        if (descriptors.Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"工作区布局“{name}”已存在。");
        var descriptor = new WorkspaceLayoutDescriptor(Guid.NewGuid().ToString("N"), name.Trim(), false);
        File.WriteAllText(PathFor(descriptor.Id), layoutXml);
        descriptors.Add(descriptor);
        SaveMetadata(descriptors);
        _settings.Set(ActiveKey, descriptor.Id);
        return descriptor;
    }

    public void Rename(string id, string name)
    {
        ValidateMutable(id);
        ValidateName(name);
        var descriptors = ListLayouts().Where(item => !item.IsBuiltIn).ToList();
        if (descriptors.Any(item => item.Id != id && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"工作区布局“{name}”已存在。");
        var index = descriptors.FindIndex(item => item.Id == id);
        if (index < 0) throw new FileNotFoundException("工作区布局不存在。");
        descriptors[index] = descriptors[index] with { Name = name.Trim() };
        SaveMetadata(descriptors);
    }

    public string? Activate(string id)
    {
        if (id == DefaultLayoutId)
        {
            _settings.Set(ActiveKey, DefaultLayoutId);
            return null;
        }
        ValidateMutable(id);
        var path = PathFor(id);
        if (!File.Exists(path)) throw new FileNotFoundException("工作区布局不存在。", path);
        try
        {
            var xml = File.ReadAllText(path);
            System.Xml.Linq.XDocument.Parse(xml);
            _settings.Set(ActiveKey, id);
            return xml;
        }
        catch
        {
            var corruptPath = path + $".corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";
            File.Move(path, corruptPath, true);
            _settings.Set(ActiveKey, DefaultLayoutId);
            return null;
        }
    }

    public void Delete(string id)
    {
        ValidateMutable(id);
        var path = PathFor(id);
        if (File.Exists(path)) File.Delete(path);
        SaveMetadata(ListLayouts().Where(item => !item.IsBuiltIn && item.Id != id));
        if (_settings.Get(ActiveKey, DefaultLayoutId) == id) ResetToDefault();
    }

    public string? RestoreLastActive() => Activate(_settings.Get(ActiveKey, DefaultLayoutId));
    public void ResetToDefault() => _settings.Set(ActiveKey, DefaultLayoutId);

    private string PathFor(string id) => Path.Combine(_layoutDirectory, id + ".xml");
    private void SaveMetadata(IEnumerable<WorkspaceLayoutDescriptor> descriptors) =>
        _settings.Set(MetadataKey, JsonConvert.SerializeObject(descriptors));
    private static void ValidateMutable(string id)
    {
        if (id == DefaultLayoutId) throw new InvalidOperationException("默认布局不可修改。");
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("布局标识无效。", nameof(id));
    }
    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 64 || InvalidName.IsMatch(name))
            throw new ArgumentException("布局名称为空、过长或包含非法字符。", nameof(name));
    }
}
