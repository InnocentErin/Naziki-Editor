using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Naziki_Editor.Views.PropertyEditor
{
    // ==========================================================
    // 🎢 5. EasingPickerControl (缓动触发器按钮 - 取代原有的干瘪文本框)
    // ==========================================================
    public class EasingPickerControl : Grid
    {
        private Button _btnEase;
        private PropertyInfo _prop;
        private object _state;

        public Action? OnModified { get; set; }

        // ==========================================================
        // 🔗 DependencyProperty 支持 (XAML/数据绑定)
        // ==========================================================
        public static readonly DependencyProperty EasingTypeProperty =
            DependencyProperty.Register(nameof(EasingType), typeof(int), typeof(EasingPickerControl),
                new PropertyMetadata(0, OnEasingTypeChanged));

        public int EasingType
        {
            get => (int)GetValue(EasingTypeProperty);
            set => SetValue(EasingTypeProperty, value);
        }

        private static void OnEasingTypeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is EasingPickerControl control && control._btnEase != null)
            {
                int newVal = (int)e.NewValue;
                control.UpdateBtnVisual(newVal.ToString());
            }
        }

        public EasingPickerControl() { }

        public EasingPickerControl(PropertyInfo prop, object state, Action<TextBox, string> attachProbeAction)
        {
            _prop = prop;
            _state = state;

            // 让按钮占满空间
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _btnEase = new Button
            {
                Height = 24,
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 50)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                BorderThickness = new Thickness(1),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 0, 0, 0)
            };

            Children.Add(_btnEase);

            // 📥 初始化读取内存数值
            object val = prop.GetValue(state);
            UpdateBtnVisual(val?.ToString());

            // 🔘 点击召唤视觉矩阵面板！
            _btnEase.Click += (s, e) =>
            {
                var currentVal = _prop.GetValue(_state)?.ToString();
                // 弹出小艾精心打造的曲线矩阵窗口
                var dialog = new EasingSelectionWindow(currentVal)
                {
                    Owner = Window.GetWindow(this),
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                if (dialog.ShowDialog() == true)
                {
                    // 确认选择后，写入内存并惊醒大管家！
                    string selectedEaseStr = dialog.SelectedEase.ToString();

                    // 兼容旧属性可能是 string 或者是 int 的情况
                    if (_prop.PropertyType == typeof(string)) _prop.SetValue(_state, selectedEaseStr);
                    else if (_prop.PropertyType == typeof(int)) _prop.SetValue(_state, (int)dialog.SelectedEase);
                    else _prop.SetValue(_state, Enum.Parse(_prop.PropertyType, selectedEaseStr));

                    UpdateBtnVisual(selectedEaseStr);

                    OnModified?.Invoke();
                }
            };
        }

        private void UpdateBtnVisual(string valStr)
        {
            if (string.IsNullOrEmpty(valStr)) valStr = "None";
            if (int.TryParse(valStr, out int easeInt)) valStr = ((Models.EasingFunction.Ease)easeInt).ToString();

            _btnEase.Content = $"📈 当前缓动：{valStr}";
        }
    }
}
