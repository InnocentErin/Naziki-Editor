using System.Globalization;
using Naziki_Editor.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Naziki_Editor.Core.Serialization;

public sealed class StoryboardUnitValueJsonConverter : JsonConverter<UnitFloat>
{
    public override void WriteJson(JsonWriter writer, UnitFloat? value, JsonSerializer serializer)
    {
        if (value is null) { writer.WriteNull(); return; }
        if (value.Unit == ReferenceUnit.World) { writer.WriteValue(value.Value); return; }
        var prefix = value.Unit switch
        {
            ReferenceUnit.NoteX => "noteX",
            ReferenceUnit.NoteY => "noteY",
            ReferenceUnit.StageX => "stageX",
            ReferenceUnit.StageY => "stageY",
            ReferenceUnit.CameraX => "cameraX",
            ReferenceUnit.CameraY => "cameraY",
            _ => throw new JsonSerializationException($"Unsupported reference unit: {value.Unit}")
        };
        writer.WriteValue($"{prefix}:{value.Value.ToString("R", CultureInfo.InvariantCulture)}");
    }

    public override UnitFloat? ReadJson(JsonReader reader, Type objectType, UnitFloat? existingValue,
        bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null) return null;
        if (reader.TokenType is JsonToken.Integer or JsonToken.Float)
            return new UnitFloat { Value = Convert.ToSingle(reader.Value, CultureInfo.InvariantCulture) };
        if (reader.TokenType != JsonToken.String)
            throw new JsonSerializationException($"Expected number or unit string, got {reader.TokenType}.");

        var raw = ((string?)reader.Value)?.Trim() ?? "";
        var separator = raw.IndexOf(':');
        if (separator <= 0 ||
            !float.TryParse(raw[(separator + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            throw new JsonSerializationException($"Invalid unit value '{raw}'.");
        var unit = raw[..separator].ToLowerInvariant() switch
        {
            "notex" => ReferenceUnit.NoteX,
            "notey" => ReferenceUnit.NoteY,
            "stagex" => ReferenceUnit.StageX,
            "stagey" => ReferenceUnit.StageY,
            "camerax" => ReferenceUnit.CameraX,
            "cameray" => ReferenceUnit.CameraY,
            _ => throw new JsonSerializationException($"Unknown reference unit in '{raw}'.")
        };
        return new UnitFloat { Value = number, Unit = unit };
    }
}

public sealed class StoryboardEntityJsonConverter : JsonConverter
{
    public override bool CanConvert(Type objectType) =>
        typeof(IStoryboardEntity).IsAssignableFrom(objectType);

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        var entity = (IStoryboardEntity?)value;
        if (entity is null) { writer.WriteNull(); return; }
        var json = new JObject();
        if (!entity.IsIdSynthetic && !string.IsNullOrWhiteSpace(entity.Id)) json["id"] = entity.Id;
        if (!string.IsNullOrWhiteSpace(entity.TargetId)) json["target_id"] = entity.TargetId;
        if (!string.IsNullOrWhiteSpace(entity.ParentId)) json["parent_id"] = entity.ParentId;

        if (entity.GetBaseState() is { } baseState)
            Merge(json, JObject.FromObject(baseState, serializer));
        if (entity.GetKeyframes() is { Count: > 0 } states)
            json["states"] = JArray.FromObject(states, serializer);
        foreach (var property in entity.UnknownProperties)
            if (json[property.Key] is null) json[property.Key] = property.Value.DeepClone();
        json.WriteTo(writer);
    }

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue,
        JsonSerializer serializer)
    {
        var json = JObject.Load(reader);
        var entity = (IStoryboardEntity)(existingValue ?? Activator.CreateInstance(objectType)
            ?? throw new JsonSerializationException($"Cannot create {objectType}."));
        entity.Id = json.Value<string>("id");
        entity.IsIdSynthetic = false;
        entity.TargetId = json.Value<string>("target_id");
        entity.ParentId = json.Value<string>("parent_id");

        var baseJson = (JObject)json.DeepClone();
        baseJson.Remove("id"); baseJson.Remove("target_id"); baseJson.Remove("parent_id"); baseJson.Remove("states");
        using (var stateReader = baseJson.CreateReader())
            serializer.Populate(stateReader, entity.GetBaseState());

        if (json["states"] is JArray states)
        {
            var list = entity.GetKeyframes();
            list.Clear();
            var stateType = entity.GetBaseState().GetType();
            foreach (var token in states)
            {
                var state = token.ToObject(stateType, serializer);
                if (state is not null) list.Add(state);
            }
        }
        return entity;
    }

    private static void Merge(JObject destination, JObject source)
    {
        foreach (var property in source.Properties())
            destination[property.Name] = property.Value;
    }
}
