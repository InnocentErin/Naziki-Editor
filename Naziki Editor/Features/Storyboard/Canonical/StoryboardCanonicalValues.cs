using System.Globalization;
using Newtonsoft.Json.Linq;

namespace Naziki_Editor.Core.Storyboard.Canonical;

/// <summary>
/// Canonical scalar encoding. Unit expressions are not stored as ambiguous
/// strings in the editor source; the wire spelling is restored only during
/// runtime export.
/// </summary>
internal static class StoryboardCanonicalValues
{
    private const string TypeProperty = "$naziki_type";
    private const string UnitProperty = "unit";
    private const string ValueProperty = "value";
    private static readonly HashSet<string> UnitFields =
        new(StringComparer.Ordinal)
        {
            "x", "y", "z", "width", "height", "w", "h",
            "scanline_pos"
        };

    public static void NormalizeUnits(JToken token)
    {
        if (token is JObject obj)
        {
            foreach (var property in obj.Properties().ToArray())
            {
                if (UnitFields.Contains(property.Name) &&
                    TryParseWireUnit(property.Value, out var typed))
                    property.Value = typed;
                else
                    NormalizeUnits(property.Value);
            }
        }
        else if (token is JArray array)
        {
            foreach (var item in array) NormalizeUnits(item);
        }
    }

    public static JObject ToWireObject(JObject canonical)
    {
        var clone = (JObject)canonical.DeepClone();
        DenormalizeUnits(clone);
        return clone;
    }

    public static bool TryReadUnit(JToken? token, out double value,
        out string? unit)
    {
        if (token is JObject typed &&
            typed.Value<string>(TypeProperty) == "unit_float")
        {
            value = typed.Value<double>(ValueProperty);
            unit = typed.Value<string>(UnitProperty);
            return true;
        }
        if (token?.Type is JTokenType.Integer or JTokenType.Float)
        {
            value = token.Value<double>();
            unit = null;
            return true;
        }
        if (token?.Type == JTokenType.String)
        {
            var raw = token.Value<string>() ?? "";
            var separator = raw.IndexOf(':');
            var numberText = separator >= 0 ? raw[(separator + 1)..] : raw;
            if (double.TryParse(numberText, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out value))
            {
                unit = separator >= 0 ? raw[..separator] : null;
                return true;
            }
        }
        value = 0;
        unit = null;
        return false;
    }

    public static JObject Unit(double value, string unit) => new()
    {
        [TypeProperty] = "unit_float",
        [UnitProperty] = unit,
        [ValueProperty] = value
    };

    public static bool IsUnitToken(JToken token) =>
        token is JObject obj &&
        obj.Value<string>(TypeProperty) == "unit_float";

    private static bool TryParseWireUnit(JToken token, out JObject typed)
    {
        typed = new JObject();
        if (token.Type != JTokenType.String) return false;
        var raw = token.Value<string>() ?? "";
        var separator = raw.IndexOf(':');
        if (separator <= 0) return false;
        var unit = raw[..separator];
        if (unit.ToLowerInvariant() is not
            ("notex" or "notey" or "stagex" or "stagey" or
             "camerax" or "cameray"))
            return false;
        if (!double.TryParse(raw[(separator + 1)..], NumberStyles.Float,
                CultureInfo.InvariantCulture, out var value))
            return false;
        typed = Unit(value, unit);
        return true;
    }

    private static void DenormalizeUnits(JToken token)
    {
        if (token is JObject obj)
        {
            foreach (var property in obj.Properties().ToArray())
            {
                if (property.Value is JObject typed &&
                    typed.Value<string>(TypeProperty) == "unit_float")
                {
                    var unit = typed.Value<string>(UnitProperty) ?? "";
                    var value = typed.Value<double>(ValueProperty);
                    property.Value =
                        $"{unit}:{value.ToString("R", CultureInfo.InvariantCulture)}";
                }
                else
                    DenormalizeUnits(property.Value);
            }
        }
        else if (token is JArray array)
        {
            foreach (var item in array) DenormalizeUnits(item);
        }
    }
}

