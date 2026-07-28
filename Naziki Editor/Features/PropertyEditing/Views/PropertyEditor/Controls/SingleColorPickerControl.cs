using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Naziki_Editor.Views.PropertyEditor
{
    // ==========================================================
    // 🎨 2. SingleColorPickerControl (高复用单体独立选色器组合)
    // ==========================================================
    public class SingleColorPickerControl : Grid
    {
        private Button _colorBtn;
        private TextBox _textBox;
        private PropertyInfo _prop;
        private object _state;
        private bool _isUpdatingLocal = false;

        public Action? OnModified { get; set; }

        public SingleColorPickerControl(PropertyInfo prop, object state, Action<TextBox, string> attachProbeAction)
        {
            _prop = prop;
            _state = state;

            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _colorBtn = new Button { Margin = new Thickness(0, 0, 5, 0), Cursor = System.Windows.Input.Cursors.Hand, BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray };
            _textBox = new TextBox { Padding = new Thickness(5), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(_textBox, 1);

            Children.Add(_colorBtn);
            Children.Add(_textBox);

            string hex = prop.GetValue(state) as string;
            _textBox.Text = hex ?? "";
            RefreshButtonColor(hex);

            attachProbeAction?.Invoke(_textBox, prop.Name);

            // 🔘 点击色块：召唤系统调色板
            _colorBtn.Click += (s, e) =>
            {
                var dialog = new System.Windows.Forms.ColorDialog();
                if (!string.IsNullOrEmpty(_textBox.Text))
                {
                    try
                    {
                        var c = (Color)ColorConverter.ConvertFromString(_textBox.Text);
                        dialog.Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B);
                    }
                    catch { }
                }

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    string newHex = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
                    _isUpdatingLocal = true;
                    _textBox.Text = newHex;
                    _isUpdatingLocal = false;
                    RefreshButtonColor(newHex);
                    SaveToMemory(newHex);
                }
            };

            // ✍️ 文本框打字改颜色
            _textBox.TextChanged += (s, e) =>
            {
                if (_isUpdatingLocal) return;
                string input = _textBox.Text.Trim();
                RefreshButtonColor(input);
                if (input.StartsWith("#") && (input.Length == 7 || input.Length == 9)) SaveToMemory(input);
            };

            _textBox.LostFocus += (s, e) =>
            {
                if (string.IsNullOrEmpty(_textBox.Text.Trim())) SaveToMemory(null);
            };
        }

        private void RefreshButtonColor(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex.ToLower() == "null")
            {
                _colorBtn.Background = new SolidColorBrush(Color.FromRgb(65, 65, 65));
                _colorBtn.Content = new TextBlock { Text = "默认", FontSize = 9, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            }
            else
            {
                try
                {
                    _colorBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                    _colorBtn.Content = null;
                }
                catch
                {
                    _colorBtn.Background = Brushes.Red;
                    _colorBtn.Content = new TextBlock { Text = "ERR", FontSize = 9, Foreground = Brushes.White };
                }
            }
        }

        private void SaveToMemory(string hex)
        {
            _prop.SetValue(_state, string.IsNullOrEmpty(hex) ? null : hex);
            OnModified?.Invoke();
        }
    }
}
