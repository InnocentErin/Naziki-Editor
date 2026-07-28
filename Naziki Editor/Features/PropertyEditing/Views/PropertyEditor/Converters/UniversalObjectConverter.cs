using System;
using System.Windows.Data;

namespace Naziki_Editor.Views.PropertyEditor
{
    // ==========================================
    // ✨ 专属翻译官 2 号：完美解析 System.Object 和 复杂数组！
    // ==========================================
    public class UniversalObjectConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value == null) return "";
            // 如果是普通的数字或纯字符串，直接打印
            if (value is string || value is int || value is float || value is double) return value.ToString();

            // 🎯【破解 System.Object】：如果是复杂的 JSON 选择器或 Pos 数组，把它序列化成漂亮的 JSON 字符串展示！
            try { return Newtonsoft.Json.JsonConvert.SerializeObject(value, Newtonsoft.Json.Formatting.None); }
            catch { return value.ToString(); }
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            string s = value?.ToString()?.Trim() ?? "";
            if (string.IsNullOrEmpty(s)) return null;

            // 如果用户手打了 JSON 括号，智能反序列化为对象！
            if (s.StartsWith("{") || s.StartsWith("["))
            {
                try { return Newtonsoft.Json.JsonConvert.DeserializeObject(s); } catch { return s; }
            }
            // 如果手打的是纯数字，智能转化回 int
            if (int.TryParse(s, out int iVal)) return iVal;

            // 兜底：纯字符串（例如 "$note"）
            return s;
        }
    }
}
