using System.Text;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Models;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Naziki_Editor.Core.Serialization;

public sealed class StoryboardJsonNormalizer : IStoryboardJsonNormalizer
{
    private readonly Dictionary<string, string> _canonicalNames;
    private readonly Dictionary<string, string> _aliases;
    private readonly SnakeCaseNamingStrategy _snakeCase = new();

    public StoryboardJsonNormalizer(IStoryboardPropertyCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var source = catalog.Catalog;
        _canonicalNames = source.RootCollections
            .Concat(source.KnownProperties)
            .Concat(source.Properties.Select(property => property.JsonName))
            .Concat(StructuralProperties)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(Compact, name => name, StringComparer.Ordinal);
        _aliases = source.Aliases.ToDictionary(
            pair => Compact(pair.Key),
            pair => pair.Value,
            StringComparer.Ordinal);
    }

    public StoryboardJsonNormalizationResult Normalize(JToken source)
        => Normalize(source, new Dictionary<string, string>(
            StringComparer.Ordinal));

    public StoryboardJsonNormalizationResult Normalize(
        JToken source,
        IReadOnlyDictionary<string, string> conflictSelections)
    {
        ArgumentNullException.ThrowIfNull(source);
        var clone = source.DeepClone();
        var changes = new List<StoryboardPropertyNameChange>();
        var conflicts = new List<StoryboardPropertyNameConflict>();
        NormalizeToken(clone, "$", false, conflictSelections, changes,
            conflicts);
        return new StoryboardJsonNormalizationResult(clone, changes, conflicts);
    }

    private void NormalizeToken(
        JToken token,
        string path,
        bool preserveObjectKeys,
        IReadOnlyDictionary<string, string> conflictSelections,
        List<StoryboardPropertyNameChange> changes,
        List<StoryboardPropertyNameConflict> conflicts)
    {
        if (token is JArray array)
        {
            for (var index = 0; index < array.Count; index++)
                NormalizeToken(array[index], $"{path}[{index}]", false,
                    conflictSelections, changes, conflicts);
            return;
        }

        if (token is not JObject obj) return;
        if (preserveObjectKeys)
        {
            foreach (var property in obj.Properties().ToArray())
                NormalizeToken(property.Value,
                    $"{path}.{Escape(property.Name)}", false,
                    conflictSelections, changes, conflicts);
            return;
        }

        var properties = obj.Properties().ToArray();
        var groups = properties.GroupBy(
            property => Canonicalize(property.Name),
            StringComparer.Ordinal);
        foreach (var group in groups)
        {
            var entries = group.ToArray();
            var canonical = group.Key;
            if (entries.Length > 1)
            {
                var valuesEqual = entries.Skip(1).All(property =>
                    JToken.DeepEquals(entries[0].Value, property.Value));
                if (!valuesEqual)
                {
                    var conflictKey = ConflictKey(path, canonical);
                    if (conflictSelections.TryGetValue(conflictKey,
                            out var selectedName) &&
                        entries.FirstOrDefault(property =>
                            property.Name == selectedName) is { } selected)
                    {
                        foreach (var duplicate in entries.Where(property =>
                                     !ReferenceEquals(property, selected)))
                            duplicate.Remove();
                        changes.Add(new StoryboardPropertyNameChange(
                            path, selected.Name, canonical, true));
                        Rename(selected, canonical);
                        continue;
                    }
                    conflicts.Add(new StoryboardPropertyNameConflict(
                        path, canonical,
                        entries.Select(property =>
                            new StoryboardPropertyConflictCandidate(
                                property.Name,
                                property.Value.DeepClone())).ToArray()));
                    continue;
                }

                var keeper = entries.FirstOrDefault(property =>
                                 property.Name == canonical) ?? entries[0];
                foreach (var duplicate in entries.Where(property =>
                             !ReferenceEquals(property, keeper)))
                    duplicate.Remove();
                changes.Add(new StoryboardPropertyNameChange(
                    path, string.Join(", ", entries.Select(item => item.Name)),
                    canonical, true));
                Rename(keeper, canonical);
            }
            else if (entries[0].Name != canonical)
            {
                changes.Add(new StoryboardPropertyNameChange(
                    path, entries[0].Name, canonical, false));
                Rename(entries[0], canonical);
            }
        }

        foreach (var property in obj.Properties().ToArray())
        {
            var preserveChildren = property.Name == "templates" &&
                                   property.Value is JObject;
            NormalizeToken(property.Value,
                $"{path}.{Escape(property.Name)}", preserveChildren,
                conflictSelections, changes, conflicts);
        }
    }

    private string Canonicalize(string name)
    {
        var compact = Compact(name);
        if (_aliases.TryGetValue(compact, out var alias)) return alias;
        if (_canonicalNames.TryGetValue(compact, out var canonical))
            return canonical;
        return _snakeCase.GetPropertyName(name, false).ToLowerInvariant();
    }

    private static string Compact(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
            if (character != '_' && character != '-' &&
                !char.IsWhiteSpace(character))
                builder.Append(char.ToLowerInvariant(character));
        return builder.ToString();
    }

    private static void Rename(JProperty property, string name)
    {
        if (property.Name == name) return;
        property.Replace(new JProperty(name, property.Value));
    }

    private static string Escape(string name) =>
        name.All(character => char.IsLetterOrDigit(character) ||
                              character is '_' or '-')
            ? name
            : $"['{name.Replace("'", "\\'", StringComparison.Ordinal)}']";

    public static string ConflictKey(string path, string canonicalName) =>
        $"{path}|{canonicalName}";

    private static readonly string[] StructuralProperties =
    [
        "id", "target_id", "parent_id", "states", "time", "relative_time",
        "add_time", "template", "reset", "note", "easing", "destroy",
        "spawn", "notes", "type", "path", "text", "layer", "order",
        "width", "height", "scale_to_canvas", "preserve_aspect", "value",
        "unit", "span"
    ];
}
