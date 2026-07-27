using Naziki_Editor.Core;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Common;
using Naziki_Editor.Models;
using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Naziki_Editor.Views.PropertyEditor
{
    // ==========================================================
    // 🎚️ 1. BoundedSliderControl (离散落盘滑块 + 输入框 + 探头联动)
    // ==========================================================
    public class BoundedSliderControl : Grid
    {
        internal static IDialogService? DialogService;

        public static void Initialize(IDialogService dialogService) { DialogService = dialogService; }
        private static readonly IPropertyEditorService _propertyEditorService = new PropertyEditorService();

        private Slider _slider;
        private TextBox _textBox;
        private PropertyInfo _prop;
        private object _state;
        private bool _isUpdatingLocal = false;

        public Action? OnModified { get; set; }

        // ==========================================================
        // 🔗 DependencyProperty 支持 (XAML/数据绑定)
        // ==========================================================
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(double), typeof(BoundedSliderControl),
                new PropertyMetadata(0.0, OnValueChanged));

        public static readonly DependencyProperty PropertyNameProperty =
            DependencyProperty.Register(nameof(PropertyName), typeof(string), typeof(BoundedSliderControl),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty MinimumProperty =
            DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(BoundedSliderControl),
                new PropertyMetadata(0.0));

        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(BoundedSliderControl),
                new PropertyMetadata(1.0));

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public string PropertyName
        {
            get => (string)GetValue(PropertyNameProperty);
            set => SetValue(PropertyNameProperty, value);
        }

        public double Minimum
        {
            get => (double)GetValue(MinimumProperty);
            set => SetValue(MinimumProperty, value);
        }

        public double Maximum
        {
            get => (double)GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is BoundedSliderControl control && control._slider != null && !control._isUpdatingLocal)
            {
                control._isUpdatingLocal = true;
                double newVal = (double)e.NewValue;
                control._slider.Value = newVal;
                control._textBox.Text = Math.Round(newVal, 3).ToString();
                control._isUpdatingLocal = false;
            }
        }

        public BoundedSliderControl() { }

        public BoundedSliderControl(PropertyInfo prop, object state, Action<TextBox, string> attachProbeAction)
        {
            _prop = prop;
            _state = state;

            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

            var rule = _propertyEditorService.GetConstraint(prop.Name);

            _slider = new Slider
            {
                Minimum = rule.Min == float.MinValue ? 0 : rule.Min,
                Maximum = rule.Max == float.MaxValue ? 1 : rule.Max,
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            _textBox = new TextBox
            {
                Padding = new Thickness(5),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_textBox, 1);

            Children.Add(_slider);
            Children.Add(_textBox);

            // 📥 填入初始内存数值
            object val = prop.GetValue(state);
            if (val != null)
            {
                float currentVal = Convert.ToSingle(val);
                _textBox.Text = currentVal.ToString();
                _slider.Value = currentVal;
            }

            // 📡 挂载设计师之前写好的严肃标红拦截探头
            attachProbeAction?.Invoke(_textBox, prop.Name);

            // 🔄 拖拽滑块时：仅实时高频更新文本框的“视觉数字”，绝不写入内存污染时光机！
            _slider.ValueChanged += (s, e) =>
            {
                if (_isUpdatingLocal) return;
                _isUpdatingLocal = true;
                _textBox.Text = Math.Round(_slider.Value, 3).ToString();
                _isUpdatingLocal = false;
            };

            // 🎯 【核心性能漏洞修复】：只有当松开鼠标左键的那一刹那，才一次性轰进内存并记账！
            _slider.PreviewMouseLeftButtonUp += (s, e) => WriteToMemory();

            // 🔢 手动敲键盘输入时：同步回拨滑块刻度
            _textBox.TextChanged += (s, e) =>
            {
                if (_isUpdatingLocal) return;
                if (float.TryParse(_textBox.Text.Trim(), out float res))
                {
                    _isUpdatingLocal = true;
                    if (res >= _slider.Minimum && res <= _slider.Maximum) _slider.Value = res;
                    _isUpdatingLocal = false;
                }
            };

            // 🚪 文本框失去焦点时：一次性写入内存落盘！
            _textBox.LostFocus += (s, e) => WriteToMemory();
        }

        private void WriteToMemory()
        {
            if (float.TryParse(_textBox.Text.Trim(), out float res))
            {
                var rule = _propertyEditorService.GetConstraint(_prop.Name);
                if (res >= rule.Min && res <= rule.Max)
                {
                    _propertyEditorService.TrySetValue(_state, _prop.Name, res);

                    // 📢 惊醒大宇宙时光机记账
                    NotifyModification();
                }
            }
        }

        private void NotifyModification()
        {
            OnModified?.Invoke();
        }
    }
}
