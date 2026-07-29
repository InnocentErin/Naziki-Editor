using Naziki_Editor.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Naziki_Editor.Core.Serialization;

public sealed class StoryboardUnitValueJsonConverter : JsonConverter<UnitFloat>
{
    public override void WriteJson(JsonWriter writer, UnitFloat? value, JsonSerializer serializer)
    {
        if (value is null) { writer.WriteNull(); return; }
        if (value.Unit == ReferenceUnit.World && !value.HasExplicitUnit)
        {
            writer.WriteValue(value.Value);
            return;
        }
        var prefix = StoryboardUnitCodec.FromModelUnit(value.Unit);
        writer.WriteValue($"{prefix}:{value.Value.ToString("R",
            System.Globalization.CultureInfo.InvariantCulture)}");
    }

    public override UnitFloat? ReadJson(JsonReader reader, Type objectType, UnitFloat? existingValue,
        bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null) return null;
        var token = JToken.Load(reader);
        if (!StoryboardUnitCodec.TryDecodeWireValue(token, out var decoded,
                out var code, out var message))
            throw new JsonSerializationException(
                $"{code ?? "UNIT_OBJECT_INVALID"}: {message ?? $"Unsupported unit token {token.Type}."}");
        return new UnitFloat
        {
            Value = checked((float)decoded.Value),
            Unit = decoded.Explicit
                ? StoryboardUnitCodec.ToModelUnit(decoded.Unit)
                : ReferenceUnit.World,
            HasExplicitUnit = decoded.Explicit
        };
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
