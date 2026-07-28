using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Naziki_Editor.Views.PropertyEditor
{
    // ==========================================================
    // 🌈 3. TwelveColorPickerControl (十二色矩阵面板 - 自给自足零漏洞)
    // ==========================================================
    public class TwelveColorPickerControl : Border
    {
        public Action? OnModified { get; set; }

        public TwelveColorPickerControl(PropertyInfo prop, object state)
        {
            BorderBrush = Brushes.DimGray; BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(4);
            Padding = new Thickness(5); Background = new SolidColorBrush(Color.FromRgb(40, 40, 40));

            var colorList = prop.GetValue(state) as List<string>;
            if (colorList == null || colorList.Count < 12)
            {
                colorList = new List<string>(new string[12]);
                prop.SetValue(state, colorList);
            }

            var grid = new UniformGrid { Rows = 2, Columns = 6 };
            string[] noteNames = { "Click1外", "Click2内", "Drag1外", "Drag2内", "Hold1外", "Hold2内", "L-Hold1外", "L-Hold2内", "Flick1外", "Flick2内", "C-Drag1外", "C-Drag2内" };

            for (int i = 0; i < 12; i++)
            {
                int index = i;
                var block = new StackPanel { Margin = new Thickness(3) };
                var btn = new Button { Height = 30, Cursor = System.Windows.Input.Cursors.Hand, BorderThickness = new Thickness(1), BorderBrush = Brushes.DarkGray };

                Action refreshSkin = () =>
                {
                    string hex = colorList[index];
                    if (string.IsNullOrEmpty(hex) || hex.ToLower() == "null")
                    {
                        btn.Background = new SolidColorBrush(Color.FromRgb(65, 65, 65));
                        btn.Content = new TextBlock { Text = "默认", FontSize = 9, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    }
                    else
                    {
                        try { btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); btn.Content = null; }
                        catch { btn.Background = Brushes.Red; }
                    }
                };
                refreshSkin();

                btn.Click += (s, e) =>
                {
                    var dialog = new System.Windows.Forms.ColorDialog();
                    if (!string.IsNullOrEmpty(colorList[index]))
                    {
                        try { var c = (Color)ColorConverter.ConvertFromString(colorList[index]); dialog.Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B); } catch { }
                    }

                    if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        colorList[index] = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
                    }
                    else
                    {
                        if (BoundedSliderControl.DialogService?.ShowYesNo("是否恢复为游戏默认配色？", "提示") == true) colorList[index] = null;
                    }
                    refreshSkin();

                    OnModified?.Invoke();
                };

                var txt = new TextBlock { Text = noteNames[index], FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0), Foreground = Brushes.Gray };
                block.Children.Add(btn); block.Children.Add(txt); grid.Children.Add(block);
            }
            Child = grid;
        }
    }
}
