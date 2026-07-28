using Naziki_Editor.State;
using Newtonsoft.Json.Linq;
using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Naziki_Editor.Views.PropertyEditor
{
    // ==========================================================
    // 🎯 7. LinePointsEditorControl (线条端点数组高阶编辑器)
    // ==========================================================
    public class LinePointsEditorControl : StackPanel
    {
        private readonly PropertyInfo _prop;
        private readonly object _state;
        private readonly ProjectDataContext _context;
        private JArray _pointsArray;
        private StackPanel _pointsContainer;
        private bool _isInternalUpdating = false;

        public Action? OnModified { get; set; }

        public LinePointsEditorControl(PropertyInfo prop, object state, ProjectDataContext context)
        {
            _prop = prop; _state = state; _context = context;
            Orientation = Orientation.Vertical;

            _pointsContainer = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 5, 0, 0) };
            Children.Add(_pointsContainer);

            Button btnAdd = new Button { Content = "➕ 新增端点", Margin = new Thickness(0, 5, 0, 0), Padding = new Thickness(5), Background = Brushes.MediumPurple, Foreground = Brushes.White, FontWeight = FontWeights.Bold, Cursor = System.Windows.Input.Cursors.Hand, BorderThickness = new Thickness(0) };
            btnAdd.Click += (s, e) => AddNewPoint();
            Children.Add(btnAdd);

            LoadData();
        }

        private void LoadData()
        {
            object val = _prop.GetValue(_state);
            if (val != null)
            {
                try { _pointsArray = JArray.FromObject(val); }
                catch { _pointsArray = null; }
            }

            // 防呆兜底：如果是新线条，自动给足保底的两个点 (默认 X 取 NoteX=1, Y 取 NoteY=2)
            if (_pointsArray == null || _pointsArray.Count < 2)
            {
                _pointsArray = new JArray(
                    new JObject { ["x"] = new JObject { ["Value"] = -100f, ["Unit"] = 1 }, ["y"] = new JObject { ["Value"] = 0f, ["Unit"] = 2 }, ["z"] = null },
                    new JObject { ["x"] = new JObject { ["Value"] = 100f, ["Unit"] = 1 }, ["y"] = new JObject { ["Value"] = 0f, ["Unit"] = 2 }, ["z"] = null }
                );
            }
            RenderUI();
        }

        private void RenderUI()
        {
            _isInternalUpdating = true;
            _pointsContainer.Children.Clear();
            int pointCount = _pointsArray.Count;

            for (int i = 0; i < pointCount; i++)
            {
                var ptObj = _pointsArray[i] as JObject;
                _pointsContainer.Children.Add(CreatePointRow(ptObj, pointCount));
            }
            _isInternalUpdating = false;
            SaveToMemory();
        }

        private void AddNewPoint()
        {
            _pointsArray.Add(new JObject
            {
                ["x"] = new JObject { ["Value"] = 0f, ["Unit"] = 1 },
                ["y"] = new JObject { ["Value"] = 0f, ["Unit"] = 2 },
                ["z"] = null
            });
            RenderUI();
        }

        private UIElement CreatePointRow(JObject pt, int totalCount)
        {
            var border = new Border { BorderBrush = Brushes.DimGray, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 0, 5), Padding = new Thickness(5, 5, 5, 5), Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)) };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // X
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Y
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Z
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 垃圾篓

            var pnlX = CreateAxisInput("X:", pt, "x", 1); Grid.SetColumn(pnlX, 0); grid.Children.Add(pnlX);
            var pnlY = CreateAxisInput("Y:", pt, "y", 2); Grid.SetColumn(pnlY, 1); grid.Children.Add(pnlY);
            var pnlZ = CreateAxisInput("Z:", pt, "z", 0); Grid.SetColumn(pnlZ, 2); grid.Children.Add(pnlZ);

            var btnDel = new Button { Content = "🗑", Margin = new Thickness(5, 0, 0, 0), Padding = new Thickness(5, 2, 5, 2), Background = Brushes.IndianRed, Foreground = Brushes.White, Cursor = System.Windows.Input.Cursors.Hand, BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Right };
            // 🛡️ 垃圾篓锁死逻辑：只要剩下两个点，彻底禁用删除！
            btnDel.IsEnabled = totalCount > 2;
            if (!btnDel.IsEnabled) btnDel.Background = Brushes.DimGray;

            btnDel.Click += (s, e) => { _pointsArray.Remove(pt); RenderUI(); };
            Grid.SetColumn(btnDel, 3); grid.Children.Add(btnDel);

            border.Child = grid;
            return border;
        }

        private UIElement CreateAxisInput(string label, JObject pt, string axis, int defaultUnit)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(new TextBlock { Text = label, Foreground = Brushes.LightSkyBlue, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 2, 0) });

            TextBox txtVal = new TextBox { Width = 35, Padding = new Thickness(2), VerticalContentAlignment = VerticalAlignment.Center };
            ComboBox cmbUnit = new ComboBox { Width = 55, Margin = new Thickness(2, 0, 0, 0), FontSize = 10 };
            cmbUnit.Items.Add(new ComboBoxItem { Content = "Wld", Tag = 0 });
            cmbUnit.Items.Add(new ComboBoxItem { Content = "NX", Tag = 1 });
            cmbUnit.Items.Add(new ComboBoxItem { Content = "NY", Tag = 2 });
            cmbUnit.Items.Add(new ComboBoxItem { Content = "SX", Tag = 3 });
            cmbUnit.Items.Add(new ComboBoxItem { Content = "SY", Tag = 4 });
            cmbUnit.Items.Add(new ComboBoxItem { Content = "CX", Tag = 5 });
            cmbUnit.Items.Add(new ComboBoxItem { Content = "CY", Tag = 6 });

            var axisToken = pt[axis];
            if (axisToken != null && axisToken.Type != JTokenType.Null)
            {
                txtVal.Text = axisToken["Value"]?.ToString();
                int u = axisToken["Unit"]?.Value<int>() ?? defaultUnit;
                foreach (ComboBoxItem item in cmbUnit.Items) { if ((int)item.Tag == u) { cmbUnit.SelectedItem = item; break; } }
            }
            else
            {
                txtVal.Text = ""; cmbUnit.SelectedIndex = 0; // 空值代表 Null (Z轴常见)
            }

            RoutedEventHandler updateLogic = (s, e) =>
            {
                if (_isInternalUpdating) return;
                string t = txtVal.Text.Trim();
                if (string.IsNullOrEmpty(t))
                {
                    pt[axis] = null; // 删空输入框自动变为 null
                }
                else if (float.TryParse(t, out float v))
                {
                    if (pt[axis] == null || pt[axis].Type == JTokenType.Null) pt[axis] = new JObject();
                    pt[axis]["Value"] = v;
                    pt[axis]["Unit"] = (int)((ComboBoxItem)cmbUnit.SelectedItem).Tag;
                }
                SaveToMemory();
            };

            txtVal.TextChanged += (s, e) => updateLogic(s, e);
            cmbUnit.SelectionChanged += (s, e) => updateLogic(s, e);

            panel.Children.Add(txtVal);
            panel.Children.Add(cmbUnit);
            return panel;
        }

        private void SaveToMemory()
        {
            if (_isInternalUpdating) return;
            try
            {
                // 反向序列化回底层模型真实需要的 List<Point> 类型
                var modelValue = _pointsArray.ToObject(_prop.PropertyType);
                _prop.SetValue(_state, modelValue);

                OnModified?.Invoke();
            }
            catch { }
        }
    }
}
