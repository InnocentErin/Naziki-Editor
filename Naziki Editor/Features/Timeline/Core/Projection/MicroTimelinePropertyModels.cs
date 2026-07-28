using System.Reflection;
using Naziki_Editor.Models;

namespace Naziki_Editor.Core.Timeline.Projection;

public enum PropertyTrackKind
{
    ContinuousNumeric,
    BooleanSegments,
    DiscreteSteps,
    ColorSteps,
    Composite
}

public sealed record PropertyTrackDescriptor(
    string PropertyName,
    string DisplayName,
    Type ValueType,
    PropertyTrackKind Kind,
    string? DependencyGroup = null,
    bool IsDependencySwitch = false);

public sealed record PropertyDependencyGroup(
    string SwitchProperty,
    string DisplayName,
    IReadOnlyList<string> DependentProperties);

public interface IPropertyMetadataCatalog
{
    IReadOnlyList<PropertyTrackDescriptor> Discover(IStoryboardEntity entity);
    IReadOnlyList<PropertyDependencyGroup> DependencyGroups { get; }
}

public sealed class PropertyMetadataCatalog : IPropertyMetadataCatalog
{
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    {
        "Time", "RelativeTime", "AddTime", "Easing", "Template", "Destroy",
        "States", "Id", "TargetId", "ParentId", "Layer", "Order",
        "UnknownProperties", "Diagnostics", "IsIdSynthetic"
    };

    public IReadOnlyList<PropertyDependencyGroup> DependencyGroups { get; } =
    [
        new("Chromatical", "色差", ["ChromaticalFade", "ChromaticalIntensity", "ChromaticalSpeed"]),
        new("Bloom", "辉光", ["BloomIntensity"]),
        new("RadialBlur", "径向模糊", ["RadialBlurIntensity"]),
        new("ColorAdjustment", "色彩调整", ["Brightness", "Saturation", "Contrast"]),
        new("GrayScale", "灰度", ["GrayScaleIntensity"]),
        new("Noise", "噪点", ["NoiseIntensity"]),
        new("Sepia", "棕褐色", ["SepiaIntensity"]),
        new("Dream", "梦境", ["DreamIntensity"]),
        new("Fisheye", "鱼眼", ["FisheyeIntensity"]),
        new("Shockwave", "冲击波", ["ShockwaveSpeed"]),
        new("Focus", "聚焦", ["FocusSize", "FocusColor", "FocusSpeed", "FocusIntensity"]),
        new("Glitch", "故障", ["GlitchIntensity"]),
        new("Arcade", "街机", ["ArcadeIntensity", "ArcadeInterferenceSize", "ArcadeInterferenceSpeed", "ArcadeContrast"]),
        new("Vignette", "暗角", ["VignetteColor", "VignetteStart", "VignetteEnd", "VignetteIntensity"])
    ];

    public IReadOnlyList<PropertyTrackDescriptor> Discover(IStoryboardEntity entity)
    {
        var populated = new HashSet<string>(StringComparer.Ordinal);
        var stateTypes = new HashSet<Type>();
        AddState(entity.GetBaseState());
        if (entity.GetKeyframes() != null)
            foreach (var state in entity.GetKeyframes())
                AddState(state);

        var groupByProperty = DependencyGroups
            .SelectMany(group => group.DependentProperties.Select(property => (property, group)))
            .ToDictionary(pair => pair.property, pair => pair.group, StringComparer.Ordinal);
        var switchByProperty = DependencyGroups
            .ToDictionary(group => group.SwitchProperty, StringComparer.Ordinal);

        var descriptors = new List<PropertyTrackDescriptor>();
        foreach (var type in stateTypes)
        {
            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!populated.Contains(property.Name) || Excluded.Contains(property.Name) ||
                    descriptors.Any(item => item.PropertyName == property.Name))
                    continue;
                var valueType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                var kind = Classify(property.Name, valueType);
                groupByProperty.TryGetValue(property.Name, out var group);
                descriptors.Add(new(
                    property.Name,
                    SplitPascalCase(property.Name),
                    valueType,
                    kind,
                    group?.SwitchProperty,
                    switchByProperty.ContainsKey(property.Name)));
            }
        }
        return descriptors.OrderBy(item => item.DependencyGroup).ThenBy(item => item.DisplayName).ToList();

        void AddState(object? state)
        {
            if (state == null) return;
            stateTypes.Add(state.GetType());
            foreach (var property in state.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!property.CanRead || property.GetIndexParameters().Length != 0) continue;
                try
                {
                    if (property.GetValue(state) != null)
                        populated.Add(property.Name);
                }
                catch
                {
                    // A malformed custom property must not prevent other tracks from loading.
                }
            }
        }
    }

    private static PropertyTrackKind Classify(string name, Type type)
    {
        if (type == typeof(bool)) return PropertyTrackKind.BooleanSegments;
        if (type.IsEnum || type == typeof(string))
            return name.Contains("Color", StringComparison.OrdinalIgnoreCase)
                ? PropertyTrackKind.ColorSteps
                : PropertyTrackKind.DiscreteSteps;
        if (type.IsPrimitive || type == typeof(decimal) || type.Name == "UnitFloat")
            return PropertyTrackKind.ContinuousNumeric;
        return PropertyTrackKind.Composite;
    }

    private static string SplitPascalCase(string value) =>
        System.Text.RegularExpressions.Regex.Replace(value, "(?<!^)([A-Z])", " $1");
}
