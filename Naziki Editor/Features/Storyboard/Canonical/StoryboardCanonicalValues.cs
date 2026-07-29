using Naziki_Editor.Core.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Naziki_Editor.Core.Storyboard.Canonical;

/// <summary>
/// Canonical scalar encoding facade. The codec in Core.Serialization is the
/// single authority for unit names, Unity enum mapping and wire conversion.
/// </summary>
internal static class StoryboardCanonicalValues
{
    public static void NormalizeUnits(JToken token)
    {
        var diagnostics = StoryboardUnitCodec.NormalizeWireValues(token);
        var error = diagnostics.FirstOrDefault(item => !item.Migrated);
        if (error is not null)
            throw new JsonSerializationException(
                $"{error.Code} at {error.Path}: {error.Message}");
    }

    public static IReadOnlyList<StoryboardUnitDiagnostic>
        NormalizeWireUnits(JToken token, string path = "$") =>
        StoryboardUnitCodec.NormalizeWireValues(token, path);

    public static IReadOnlyList<StoryboardUnitDiagnostic>
        NormalizeCanonicalUnits(JToken token, string path = "$") =>
        StoryboardUnitCodec.NormalizeCanonicalValues(token, path);

    public static JObject ToWireObject(JObject canonical)
    {
        var clone = (JObject)canonical.DeepClone();
        StoryboardUnitCodec.DenormalizeCanonicalValues(clone);
        return clone;
    }

    public static bool TryReadUnit(JToken? token, out double value,
        out string? unit)
    {
        if (StoryboardUnitCodec.TryRead(token, out var decoded))
        {
            value = decoded.Value;
            unit = decoded.Explicit ? decoded.Unit : null;
            return true;
        }
        value = 0;
        unit = null;
        return false;
    }

    public static JObject Unit(double value, string unit) =>
        StoryboardUnitCodec.Canonical(value, unit);

    public static bool IsUnitToken(JToken token) =>
        StoryboardUnitCodec.IsCanonicalUnit(token);
}
