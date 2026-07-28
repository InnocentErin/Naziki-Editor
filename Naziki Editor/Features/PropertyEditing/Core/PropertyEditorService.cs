using System.Collections.Generic;
using System.Reflection;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Models;
using Newtonsoft.Json;

namespace Naziki_Editor.Core.Common
{
    /// <summary>
    /// 属性编辑器服务实现，统一封装反射读写、属性分类、约束生成与模板属性白名单。
    /// </summary>
    public class PropertyEditorService : IPropertyEditorService
    {
        public bool TryGetValue(object obj, string propertyName, out object? value)
        {
            return FastReflectionHelper.TryGetValue(obj, propertyName, out value);
        }

        public bool TrySetValue(object obj, string propertyName, object? value)
        {
            if (obj == null || string.IsNullOrEmpty(propertyName)) return false;

            try
            {
                var prop = obj.GetType().GetProperty(propertyName);
                if (prop == null) return false;

                var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                if (value != null && !targetType.IsInstanceOfType(value))
                {
                    value = Convert.ChangeType(value, targetType, System.Globalization.CultureInfo.InvariantCulture);
                }

                return FastReflectionHelper.TrySetValue(obj, propertyName, value);
            }
            catch
            {
                return false;
            }
        }

        public PropertyCategory GetCategory(string propertyName)
        {
            return PropertyClassifier.GetCategory(propertyName);
        }

        public PropertyConstraint GetConstraint(string propertyName)
        {
            return PropertyConstraintManager.GetConstraint(propertyName);
        }

        public IReadOnlyCollection<string> GetAllowedPropertiesForType(TemplateType type)
        {
            return new TemplateManager().GetAllowedPropertiesForType(type);
        }

        public bool IsEditableProperty(PropertyInfo property)
        {
            if (property == null || !property.CanRead || !property.CanWrite) return false;
            if (property.GetIndexParameters().Length != 0) return false;
            if (property.GetCustomAttribute<JsonIgnoreAttribute>() != null) return false;
            return property.Name is not nameof(IExtensibleStoryboardNode.Diagnostics)
                and not nameof(IExtensibleStoryboardNode.UnknownProperties)
                and not nameof(IStoryboardEntity.IsIdSynthetic);
        }
    }
}
