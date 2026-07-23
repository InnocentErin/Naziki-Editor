using System;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace Naziki_Editor.Core
{
    // ==========================================
    // ⚡ 极速反射引擎：编译 Delegate 缓存 + 完美防呆
    // ==========================================
    public static class FastReflectionHelper
    {
        // 缓存字典：TypeFullName.PropertyName -> Delegate
        private static readonly ConcurrentDictionary<string, Func<object, object>> _getters = new ConcurrentDictionary<string, Func<object, object>>();
        private static readonly ConcurrentDictionary<string, Action<object, object>> _setters = new ConcurrentDictionary<string, Action<object, object>>();

        private static string GetCacheKey(Type type, string propertyName) => $"{type.FullName}.{propertyName}";

        // 📥 安全快速读取
        public static bool TryGetValue(object obj, string propertyName, out object value)
        {
            value = null;
            if (obj == null || string.IsNullOrEmpty(propertyName)) return false;

            Type type = obj.GetType();
            string key = GetCacheKey(type, propertyName);

            try
            {
                var getter = _getters.GetOrAdd(key, k => CreateGetter(type, propertyName));
                if (getter == null) return false;

                value = getter(obj);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"💥 [FastReflection] 读取属性 {propertyName} 发生异常: {ex.Message}");
                return false;
            }
        }

        // 📤 安全快速写入
        public static bool TrySetValue(object obj, string propertyName, object value)
        {
            if (obj == null || string.IsNullOrEmpty(propertyName)) return false;

            Type type = obj.GetType();
            string key = GetCacheKey(type, propertyName);

            try
            {
                var setter = _setters.GetOrAdd(key, k => CreateSetter(type, propertyName));
                if (setter == null) return false;

                setter(obj, value);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"💥 [FastReflection] 写入属性 {propertyName} 发生异常: {ex.Message}");
                return false;
            }
        }

        // ⚙️ 核心法术：将 Get 操作编译为委托
        private static Func<object, object> CreateGetter(Type type, string propertyName)
        {
            PropertyInfo propInfo = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (propInfo == null || !propInfo.CanRead) return null;

            var instanceParam = Expression.Parameter(typeof(object), "instance");
            var castInstance = Expression.Convert(instanceParam, type);
            var propertyAccess = Expression.Property(castInstance, propInfo);
            var castResult = Expression.Convert(propertyAccess, typeof(object));

            return Expression.Lambda<Func<object, object>>(castResult, instanceParam).Compile();
        }

        // ⚙️ 核心法术：将 Set 操作编译为委托
        private static Action<object, object> CreateSetter(Type type, string propertyName)
        {
            PropertyInfo propInfo = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (propInfo == null || !propInfo.CanWrite) return null;

            var instanceParam = Expression.Parameter(typeof(object), "instance");
            var valueParam = Expression.Parameter(typeof(object), "value");

            var castInstance = Expression.Convert(instanceParam, type);
            var castValue = Expression.Convert(valueParam, propInfo.PropertyType);

            var propertyAccess = Expression.Property(castInstance, propInfo);
            var assign = Expression.Assign(propertyAccess, castValue);

            return Expression.Lambda<Action<object, object>>(assign, instanceParam, valueParam).Compile();
        }
    }
}