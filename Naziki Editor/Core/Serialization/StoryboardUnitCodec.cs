using System.Globalization;
using Naziki_Editor.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Naziki_Editor.Core.Serialization;

internal sealed record StoryboardUnitDiagnostic(
    string Code,
    string Path,
    string Message,
    bool Migrated = false);

internal sealed record StoryboardDecodedUnit(
    double Value,
    string Unit,
    bool Explicit);

internal static class StoryboardUnitCodec
{
    public const string TypeProperty = "$naziki_type";
    public const string UnitProperty = "unit";
    public const string ValueProperty = "value";
    public const string TypeName = "unit_float";

    private static readonly HashSet<string> UnitFields =
        new(StringComparer.Ordinal)
        {
            "x", "y", "z", "width", "height", "w", "h",
            "scanline_pos", "pos"
        };

    public static IReadOnlyList<StoryboardUnitDiagnostic>
        NormalizeWireValues(JToken token, string path = "$")
    {
        var diagnostics = new List<StoryboardUnitDiagnostic>();
        NormalizeWire(token, path, diagnostics);
        return diagnostics;
    }

    public static IReadOnlyList<StoryboardUnitDiagnostic>
        NormalizeCanonicalValues(JToken token, string path = "$")
    {
        var diagnostics = new List<StoryboardUnitDiagnostic>();
        NormalizeCanonical(token, path, diagnostics);
        return diagnostics;
    }

    public static void DenormalizeCanonicalValues(JToken token)
    {
        if (token is JObject obj)
        {
            foreach (var property in obj.Properties().ToArray())
            {
                if (IsCanonicalUnit(property.Value))
                {
                    if (!TryDecodeCanonical(property.Value, out var decoded,
                            out var code, out var message))
                        throw new JsonSerializationException(
                            $"{code}: {message}");
                    property.Value = decoded.Explicit
                        ? $"{decoded.Unit}:{decoded.Value.ToString("R",
                            CultureInfo.InvariantCulture)}"
                        : JToken.FromObject(decoded.Value);
                }
                else
                    DenormalizeCanonicalValues(property.Value);
            }
        }
        else if (token is JArray array)
            foreach (var item in array)
                DenormalizeCanonicalValues(item);
    }

    public static bool TryRead(JToken? token, out StoryboardDecodedUnit value)
    {
        if (token is null)
        {
            value = new(0, "", false);
            return false;
        }
        if (token.Type is JTokenType.Integer or JTokenType.Float)
        {
            var number = token.Value<double>();
            value = new(number, "", false);
            return double.IsFinite(number);
        }
        if (IsCanonicalUnit(token))
            return TryDecodeCanonical(token, out value, out _, out _);
        if (token.Type == JTokenType.String)
            return TryDecodeString(token.Value<string>() ?? "", out value,
                out _, out _);
        value = new(0, "", false);
        return false;
    }

    public static bool TryDecodeWireValue(JToken token,
        out StoryboardDecodedUnit decoded, out string? code,
        out string? message) =>
        TryDecodeWire(token, out decoded, out code, out message);

    public static bool IsCanonicalUnit(JToken token) =>
        token is JObject obj &&
        obj.Value<string>(TypeProperty) == TypeName;

    public static JObject Canonical(double value, string unit) => new()
    {
        [TypeProperty] = TypeName,
        [UnitProperty] = NormalizeUnitName(unit),
        [ValueProperty] = value
    };

    public static ReferenceUnit ToModelUnit(string unit) =>
        NormalizeUnitName(unit) switch
        {
            "world" => ReferenceUnit.World,
            "stageX" => ReferenceUnit.StageX,
            "stageY" => ReferenceUnit.StageY,
            "noteX" => ReferenceUnit.NoteX,
            "noteY" => ReferenceUnit.NoteY,
            "cameraX" => ReferenceUnit.CameraX,
            "cameraY" => ReferenceUnit.CameraY,
            _ => throw new JsonSerializationException(
                $"Unknown reference unit '{unit}'.")
        };

    public static string FromModelUnit(ReferenceUnit unit) => unit switch
    {
        ReferenceUnit.World => "world",
        ReferenceUnit.StageX => "stageX",
        ReferenceUnit.StageY => "stageY",
        ReferenceUnit.NoteX => "noteX",
        ReferenceUnit.NoteY => "noteY",
        ReferenceUnit.CameraX => "cameraX",
        ReferenceUnit.CameraY => "cameraY",
        _ => throw new JsonSerializationException(
            $"Unsupported reference unit: {unit}")
    };

    private static void NormalizeWire(JToken token, string path,
        List<StoryboardUnitDiagnostic> diagnostics)
    {
        if (token is JArray array)
        {
            for (var index = 0; index < array.Count; index++)
                NormalizeWire(array[index], $"{path}[{index}]", diagnostics);
            return;
        }
        if (token is not JObject obj) return;
        foreach (var property in obj.Properties().ToArray())
        {
            var propertyPath = $"{path}.{property.Name}";
            if (UnitFields.Contains(property.Name))
            {
                if (TryDecodeWire(property.Value, out var decoded,
                        out var code, out var message))
                {
                    property.Value = decoded.Explicit
                        ? Canonical(decoded.Value, decoded.Unit)
                        : JToken.FromObject(decoded.Value);
                    continue;
                }
                if (code is not null)
                {
                    diagnostics.Add(new(code, propertyPath, message!));
                    continue;
                }
            }
            NormalizeWire(property.Value, propertyPath, diagnostics);
        }
    }

    private static void NormalizeCanonical(JToken token, string path,
        List<StoryboardUnitDiagnostic> diagnostics)
    {
        if (token is JArray array)
        {
            for (var index = 0; index < array.Count; index++)
                NormalizeCanonical(array[index], $"{path}[{index}]",
                    diagnostics);
            return;
        }
        if (token is not JObject obj) return;
        if (IsCanonicalUnit(obj))
        {
            var unitToken = obj[UnitProperty];
            if (unitToken?.Type == JTokenType.String &&
                string.IsNullOrWhiteSpace(unitToken.Value<string>()))
            {
                obj[UnitProperty] = "world";
                diagnostics.Add(new(
                    "CANONICAL_UNIT_WORLD_MIGRATED", path,
                    "Empty canonical unit was migrated to explicit 'world'.",
                    true));
            }
            if (!TryDecodeCanonical(obj, out var decoded, out var code,
                    out var message))
            {
                diagnostics.Add(new(code!, path, message!));
                return;
            }
            obj[UnitProperty] = decoded.Unit;
            obj[ValueProperty] = decoded.Value;
            return;
        }
        foreach (var property in obj.Properties())
            NormalizeCanonical(property.Value,
                $"{path}.{property.Name}", diagnostics);
    }

    private static bool TryDecodeWire(JToken token,
        out StoryboardDecodedUnit decoded, out string? code,
        out string? message)
    {
        code = null;
        message = null;
        if (token.Type is JTokenType.Integer or JTokenType.Float)
        {
            var value = token.Value<double>();
            if (!double.IsFinite(value))
                return Fail("UNIT_VALUE_INVALID",
                    "Unit value must be finite.", out decoded,
                    out code, out message);
            decoded = new(value, "", false);
            return true;
        }
        if (token.Type == JTokenType.String)
            return TryDecodeString(token.Value<string>() ?? "", out decoded,
                out code, out message);
        if (token is not JObject obj)
        {
            decoded = new(0, "", false);
            return false;
        }
        if (IsCanonicalUnit(obj))
            return TryDecodeCanonical(obj, out decoded, out code, out message);

        var valueToken = obj[ValueProperty] ?? obj["Value"];
        var unitToken = obj[UnitProperty] ?? obj["Unit"];
        if (valueToken is null || unitToken is null)
            return Fail("UNIT_OBJECT_INVALID",
                "Unit object requires both Value and Unit.",
                out decoded, out code, out message);
        if (valueToken.Type is not
            (JTokenType.Integer or JTokenType.Float) ||
            !double.IsFinite(valueToken.Value<double>()))
            return Fail("UNIT_VALUE_INVALID",
                "Unit object Value must be a finite number.",
                out decoded, out code, out message);
        if (!TryDecodeUnityUnit(unitToken, out var unit, out code,
                out message))
        {
            decoded = new(0, "", false);
            return false;
        }
        decoded = new(valueToken.Value<double>(), unit, true);
        return true;
    }

    private static bool TryDecodeCanonical(JToken token,
        out StoryboardDecodedUnit decoded, out string? code,
        out string? message)
    {
        var obj = (JObject)token;
        var value = obj[ValueProperty];
        var unit = obj[UnitProperty];
        if (value?.Type is not
            (JTokenType.Integer or JTokenType.Float) ||
            !double.IsFinite(value.Value<double>()))
            return Fail("CANONICAL_UNIT_INVALID",
                "Canonical unit requires a finite numeric value.",
                out decoded, out code, out message);
        if (unit?.Type != JTokenType.String ||
            !TryNormalizeUnitName(unit.Value<string>() ?? "",
                out var canonical))
            return Fail("CANONICAL_UNIT_INVALID",
                "Canonical unit requires a recognized non-empty unit.",
                out decoded, out code, out message);
        decoded = new(value.Value<double>(), canonical, true);
        code = null;
        message = null;
        return true;
    }

    private static bool TryDecodeString(string raw,
        out StoryboardDecodedUnit decoded, out string? code,
        out string? message)
    {
        raw = raw.Trim();
        var separator = raw.IndexOf(':');
        if (separator < 0)
        {
            if (double.TryParse(raw, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var scalar) &&
                double.IsFinite(scalar))
            {
                decoded = new(scalar, "", false);
                code = null;
                message = null;
                return true;
            }
            return Fail("UNIT_VALUE_INVALID",
                $"Invalid numeric unit value '{raw}'.",
                out decoded, out code, out message);
        }
        if (!TryNormalizeUnitName(raw[..separator], out var unit))
            return Fail("UNIT_NAME_UNKNOWN",
                $"Unknown reference unit '{raw[..separator]}'.",
                out decoded, out code, out message);
        if (!double.TryParse(raw[(separator + 1)..], NumberStyles.Float,
                CultureInfo.InvariantCulture, out var value) ||
            !double.IsFinite(value))
            return Fail("UNIT_VALUE_INVALID",
                $"Invalid unit value '{raw}'.",
                out decoded, out code, out message);
        decoded = new(value, unit, true);
        code = null;
        message = null;
        return true;
    }

    private static bool TryDecodeUnityUnit(JToken token, out string unit,
        out string? code, out string? message)
    {
        if (token.Type is JTokenType.Integer or JTokenType.Float)
        {
            var numeric = token.Value<double>();
            if (!double.IsFinite(numeric) ||
                numeric != Math.Truncate(numeric) ||
                numeric < int.MinValue || numeric > int.MaxValue)
            {
                unit = "";
                code = "UNIT_ENUM_UNKNOWN";
                message =
                    $"Unity reference unit must be an integer from 0 to 6, got '{token}'.";
                return false;
            }
            unit = (int)numeric switch
            {
                0 => "world",
                1 => "stageX",
                2 => "stageY",
                3 => "noteX",
                4 => "noteY",
                5 => "cameraX",
                6 => "cameraY",
                _ => ""
            };
            if (unit.Length > 0)
            {
                code = null;
                message = null;
                return true;
            }
            code = "UNIT_ENUM_UNKNOWN";
            message = $"Unknown Unity reference unit value '{token}'.";
            return false;
        }
        if (token.Type == JTokenType.String &&
            TryNormalizeUnitName(token.Value<string>() ?? "", out unit))
        {
            code = null;
            message = null;
            return true;
        }
        unit = "";
        code = token.Type == JTokenType.String
            ? "UNIT_NAME_UNKNOWN"
            : "UNIT_OBJECT_INVALID";
        message = token.Type == JTokenType.String
            ? $"Unknown reference unit '{token}'."
            : "Unit must be a Unity enum integer or unit name.";
        return false;
    }

    private static string NormalizeUnitName(string unit) =>
        TryNormalizeUnitName(unit, out var canonical)
            ? canonical
            : throw new JsonSerializationException(
                $"Unknown reference unit '{unit}'.");

    private static bool TryNormalizeUnitName(string unit,
        out string canonical)
    {
        canonical = unit.Trim().ToLowerInvariant() switch
        {
            "world" => "world",
            "stagex" => "stageX",
            "stagey" => "stageY",
            "notex" => "noteX",
            "notey" => "noteY",
            "camerax" => "cameraX",
            "cameray" => "cameraY",
            _ => ""
        };
        return canonical.Length > 0;
    }

    private static bool Fail(string failureCode, string failureMessage,
        out StoryboardDecodedUnit decoded, out string? code,
        out string? message)
    {
        decoded = new(0, "", false);
        code = failureCode;
        message = failureMessage;
        return false;
    }
}
