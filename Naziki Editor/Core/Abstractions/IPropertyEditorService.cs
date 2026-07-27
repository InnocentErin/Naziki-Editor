using System;
using System.Collections.Generic;
using System.Reflection;
using Naziki_Editor.Core;
using Naziki_Editor.Models;

namespace Naziki_Editor.Core.Abstractions
{
    /// <summary>
    /// 属性编辑器服务抽象，负责反射读写、属性分类与约束生成。
    /// </summary>
    public interface IPropertyEditorService
    {
        bool TryGetValue(object obj, string propertyName, out object? value);
        bool TrySetValue(object obj, string propertyName, object? value);
        PropertyCategory GetCategory(string propertyName);
        PropertyConstraint GetConstraint(string propertyName);
        IReadOnlyCollection<string> GetAllowedPropertiesForType(TemplateType type);
        bool IsEditableProperty(PropertyInfo property);
    }
}
