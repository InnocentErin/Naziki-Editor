using System.Globalization;
using Newtonsoft.Json.Linq;

namespace Naziki_Editor.Core.Serialization;

internal sealed record StoryboardColorDiagnostic(
    string Code,
    string Path,
    string Message);

/// <summary>
/// Converts color objects emitted by a deserialized Unity Player model back
/// to the hexadecimal strings accepted by the storyboard wire format.
/// </summary>
internal static class StoryboardColorCodec
{
    private static readonly HashSet<string> ColorFields =
        new(StringComparer.Ordinal)
        {
            "color", "fill_color", "scanline_color", "note_ring_color",
            "color_filter_color", "focus_color", "vignette_color"
        };

    public static IReadOnlyList<StoryboardColorDiagnostic>
        NormalizeWireValues(JToken token, string path = "$")
    {
        var diagnostics = new List<StoryboardColorDiagnostic>();
        Normalize(token, path, diagnostics);
        return diagnostics;
    }

    private static void Normalize(JToken token, string path,
        List<StoryboardColorDiagnostic> diagnostics)
    {
        if (token is JArray array)
        {
            for (var index = 0; index < array.Count; index++)
                Normalize(array[index], $"{path}[{index}]", diagnostics);
            return;
        }
        if (token is not JObject obj) return;

        foreach (var property in obj.Properties().ToArray())
        {
            var propertyPath = $"{path}.{property.Name}";
            if (ColorFields.Contains(property.Name) &&
                property.Value is JObject color)
            {
                if (TryConvert(color, out var hexadecimal,
                        out var message))
                    property.Value = hexadecimal;
                else
                    diagnostics.Add(new StoryboardColorDiagnostic(
                        "COLOR_OBJECT_INVALID", propertyPath, message));
                continue;
            }

            if (property.Name == "note_fill_colors" &&
                property.Value is JArray colors)
            {
                for (var index = 0; index < colors.Count; index++)
                {
                    if (colors[index] is not JObject arrayColor) continue;
                    if (TryConvert(arrayColor, out var hexadecimal,
                            out var message))
                        colors[index] = hexadecimal;
                    else
                        diagnostics.Add(new StoryboardColorDiagnostic(
                            "COLOR_OBJECT_INVALID",
                            $"{propertyPath}[{index}]", message));
                }
            }
            Normalize(property.Value, propertyPath, diagnostics);
        }
    }

    private static bool TryConvert(JObject color, out string hexadecimal,
        out string message)
    {
        if (!TryComponent(color["r"], "r", true, out var red,
                out message) ||
            !TryComponent(color["g"], "g", true, out var green,
                out message) ||
            !TryComponent(color["b"], "b", true, out var blue,
                out message) ||
            !TryComponent(color["a"], "a", false, out var alpha,
                out message))
        {
            hexadecimal = "";
            return false;
        }

        hexadecimal = alpha == byte.MaxValue
            ? $"#{red:X2}{green:X2}{blue:X2}"
            : $"#{red:X2}{green:X2}{blue:X2}{alpha:X2}";
        message = "";
        return true;
    }

    private static bool TryComponent(JToken? token, string name,
        bool required, out byte component, out string message)
    {
        if (token is null && !required)
        {
            component = byte.MaxValue;
            message = "";
            return true;
        }
        if (token?.Type is not
            (JTokenType.Integer or JTokenType.Float))
        {
            component = 0;
            message = required
                ? $"Color object requires numeric '{name}'."
                : $"Color component '{name}' must be numeric.";
            return false;
        }
        var value = token.Value<double>();
        if (!double.IsFinite(value) || value < 0 || value > 1)
        {
            component = 0;
            message =
                $"Color component '{name}' must be finite and between 0 and 1; got {value.ToString(CultureInfo.InvariantCulture)}.";
            return false;
        }
        component = (byte)Math.Round(value * byte.MaxValue,
            MidpointRounding.AwayFromZero);
        message = "";
        return true;
    }
}
