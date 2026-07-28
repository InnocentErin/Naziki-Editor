using Newtonsoft.Json;

namespace Naziki_Editor.Models;

public sealed class StoryboardPropertyCatalog
{
    [JsonProperty("catalog_version")] public int CatalogVersion { get; set; }
    [JsonProperty("root_collections")] public List<string> RootCollections { get; set; } = new();
    [JsonProperty("known_properties")] public List<string> KnownProperties { get; set; } = new();
    [JsonProperty("properties")] public List<StoryboardPropertyDefinition> Properties { get; set; } = new();
    [JsonProperty("aliases")] public Dictionary<string, string> Aliases { get; set; } = new(StringComparer.Ordinal);
}

public sealed class StoryboardPropertyDefinition
{
    [JsonProperty("json_name")] public string JsonName { get; set; } = "";
    [JsonProperty("levels")] public List<string> Levels { get; set; } = new();
    [JsonProperty("value_type")] public string ValueType { get; set; } = "";
    [JsonProperty("ui")] public string Ui { get; set; } = "";
    [JsonProperty("timeline")] public string Timeline { get; set; } = "";
    [JsonProperty("min")] public double? Min { get; set; }
    [JsonProperty("max")] public double? Max { get; set; }
    [JsonProperty("default")] public Newtonsoft.Json.Linq.JToken? Default { get; set; }
    [JsonProperty("array_semantics")]
    public string ArraySemantics { get; set; } = "value";
}
