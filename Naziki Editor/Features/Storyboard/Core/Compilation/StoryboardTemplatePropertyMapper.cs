using System.Reflection;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Serialization;
using Naziki_Editor.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Naziki_Editor.Core.Compilation;

public sealed class StoryboardTemplatePropertyMapper : IStoryboardTemplatePropertyMapper
{
    private static readonly HashSet<string> TimeControlProperties =
        new(StringComparer.Ordinal)
        {
            "time", "relative_time", "add_time", "template"
        };

    private static readonly SnakeCaseNamingStrategy NamingStrategy = new();
    private readonly JsonSerializer _serializer =
        JsonSerializer.Create(StoryboardJsonSettings.Create());

    public IReadOnlyList<StoryboardTemplatePropertyIssue> Apply(
        ObjectState target,
        TemplateState source,
        StoryboardTemplateApplyMode mode,
        string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        var issues = AnalyzeAndApply(target.GetType(), target, source, mode, sourcePath);
        MergeUnknownProperties(target, source, mode);
        return issues;
    }

    public IReadOnlyList<StoryboardTemplatePropertyIssue> Analyze(
        Type targetStateType,
        TemplateState source,
        string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(targetStateType);
        ArgumentNullException.ThrowIfNull(source);
        if (!typeof(ObjectState).IsAssignableFrom(targetStateType))
            throw new ArgumentException("Target type must be a storyboard state.", nameof(targetStateType));
        return AnalyzeAndApply(targetStateType, null, source,
            StoryboardTemplateApplyMode.Override, sourcePath);
    }

    internal static bool IsExportableProperty(
        PropertyInfo property,
        bool requireWrite = false)
    {
        if (!property.CanRead || requireWrite && !property.CanWrite ||
            property.GetIndexParameters().Length != 0)
            return false;
        if (property.GetCustomAttribute<JsonIgnoreAttribute>() is not null ||
            property.GetCustomAttribute<JsonExtensionDataAttribute>() is not null)
            return false;
        return property.Name is not nameof(ObjectState.Diagnostics)
            and not nameof(ObjectState.UnknownProperties);
    }

    internal static string JsonName(PropertyInfo property)
    {
        var declared = property.GetCustomAttribute<JsonPropertyAttribute>()?.PropertyName;
        return string.IsNullOrWhiteSpace(declared)
            ? NamingStrategy.GetPropertyName(property.Name, false)
            : declared;
    }

    private IReadOnlyList<StoryboardTemplatePropertyIssue> AnalyzeAndApply(
        Type targetStateType,
        ObjectState? target,
        TemplateState source,
        StoryboardTemplateApplyMode mode,
        string sourcePath)
    {
        var targetProperties = PropertiesByJsonName(targetStateType, requireWrite: true);
        var issues = new List<StoryboardTemplatePropertyIssue>();
        foreach (var sourceProperty in typeof(TemplateState)
                     .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .Where(property => IsExportableProperty(property)))
        {
            var jsonName = JsonName(sourceProperty);
            if (TimeControlProperties.Contains(jsonName)) continue;
            var sourceValue = sourceProperty.GetValue(source);
            if (sourceValue is null) continue;

            if (!targetProperties.TryGetValue(jsonName, out var targetProperty))
            {
                issues.Add(new StoryboardTemplatePropertyIssue(
                    "TEMPLATE_PROPERTY_IGNORED",
                    jsonName,
                    $"{sourcePath}.{jsonName}",
                    $"Template property '{jsonName}' is not supported by {targetStateType.Name} and will be ignored.",
                    StoryboardDiagnosticSeverity.Warning));
                continue;
            }

            try
            {
                var converted = ConvertValue(sourceValue, targetProperty.PropertyType);
                if (target is not null &&
                    (mode == StoryboardTemplateApplyMode.Override ||
                     targetProperty.GetValue(target) is null))
                    targetProperty.SetValue(target, converted);
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidCastException
                                       or FormatException or OverflowException)
            {
                issues.Add(new StoryboardTemplatePropertyIssue(
                    "TEMPLATE_PROPERTY_TYPE_INVALID",
                    jsonName,
                    $"{sourcePath}.{jsonName}",
                    $"Template property '{jsonName}' cannot be converted to " +
                    $"{targetStateType.Name}.{targetProperty.Name} ({targetProperty.PropertyType.Name}): {ex.Message}",
                    StoryboardDiagnosticSeverity.Error));
            }
        }
        return issues;
    }

    private object? ConvertValue(object sourceValue, Type targetType)
    {
        var token = JToken.FromObject(sourceValue, _serializer);
        return token.ToObject(targetType, _serializer);
    }

    private static Dictionary<string, PropertyInfo> PropertiesByJsonName(
        Type type,
        bool requireWrite)
    {
        return type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => IsExportableProperty(property, requireWrite))
            .GroupBy(JsonName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(property =>
                    InheritanceDistance(type, property.DeclaringType)).First(),
                StringComparer.Ordinal);
    }

    private static int InheritanceDistance(Type type, Type? declaringType)
    {
        var distance = 0;
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current == declaringType) return distance;
            distance++;
        }
        return int.MaxValue;
    }

    private static void MergeUnknownProperties(
        ObjectState target,
        TemplateState source,
        StoryboardTemplateApplyMode mode)
    {
        foreach (var property in source.UnknownProperties)
        {
            if (mode == StoryboardTemplateApplyMode.FillMissing &&
                target.UnknownProperties.ContainsKey(property.Key))
                continue;
            target.UnknownProperties[property.Key] = property.Value.DeepClone();
        }
    }
}
