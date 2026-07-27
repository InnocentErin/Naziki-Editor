using System;
using System.Windows.Data;

namespace Naziki_Editor.Views.PropertyEditor
{
    // ==========================================
    // ✨ 专属翻译官：防止 Time 数组或空值转义成乱码
    // ==========================================
    public class TimeBindingConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is System.Collections.IList list)
            {
                var strList = new System.Collections.Generic.List<string>();
                foreach (var item in list) strList.Add(item.ToString());
                return string.Join(", ", strList);
            }
            string strVal = value?.ToString() ?? "";
            if (strVal.Contains("3.402823")) return "";
            return strVal;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            string s = value?.ToString() ?? "";
            if (s.Contains(","))
            {
                var parts = s.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                var list = new System.Collections.Generic.List<object>();
                foreach (var p in parts)
                {
                    string t = p.Trim();
                    if (float.TryParse(t, out float f)) list.Add(f); else list.Add(t);
                }
                return list;
            }
            if (float.TryParse(s, out float fSingle)) return fSingle;
            return s;
        }
    }
}
