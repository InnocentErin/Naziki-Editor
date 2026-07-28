using Naziki_Editor.Core.Abstractions;
using Newtonsoft.Json;
using System.IO;

namespace Naziki_Editor.Features.EditorShell;

public interface IRecentProjectService
{
    IReadOnlyList<string> GetExisting();
    void Add(string path);
    void Remove(string path);
}

public sealed class RecentProjectService : IRecentProjectService
{
    private const string Key = "RecentProjects";
    private readonly ISettingsStore _settings;
    public RecentProjectService(ISettingsStore settings) => _settings = settings;

    public IReadOnlyList<string> GetExisting()
    {
        var items = JsonConvert.DeserializeObject<List<string>>(_settings.Get(Key, "[]")) ?? [];
        var existing = items.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray();
        if (existing.Length != items.Count) Save(existing);
        return existing;
    }

    public void Add(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var items = GetExisting().Where(item => !string.Equals(item, path, StringComparison.OrdinalIgnoreCase)).ToList();
        items.Insert(0, Path.GetFullPath(path));
        Save(items.Take(10));
    }

    public void Remove(string path) =>
        Save(GetExisting().Where(item => !string.Equals(item, path, StringComparison.OrdinalIgnoreCase)));

    private void Save(IEnumerable<string> items) =>
        _settings.Set(Key, JsonConvert.SerializeObject(items));
}
