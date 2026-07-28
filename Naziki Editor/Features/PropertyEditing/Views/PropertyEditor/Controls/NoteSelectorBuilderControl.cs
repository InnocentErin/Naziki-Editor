using Naziki_Editor.Core;
using Naziki_Editor.Models;
using Naziki_Editor.State;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Naziki_Editor.Views.PropertyEditor
{
    // ==========================================================
    // 🎯 6. NoteSelectorBuilderControl (按需加载的高级音符雷达)
    // ==========================================================
    public class NoteSelectorBuilderControl : StackPanel
    {
        private readonly PropertyInfo _prop;
        private readonly object _state;
        private readonly ProjectDataContext _context;
        private NoteSelectorModel _currentSelector;

        public Action? OnModified { get; set; }

        // UI 部件
        private ComboBox _cmbMode;
        private TextBox _txtSingleId;
        private StackPanel _panelSelectorContent;
        private ComboBox _cmbAddFilter;
        private TextBlock _txtMatchResult;
        private StackPanel _panelFilters;

        private bool _isInternalUpdating = false;

        public NoteSelectorBuilderControl(PropertyInfo prop, object state, ProjectDataContext context)
        {
            _prop = prop;
            _state = state;
            _context = context;
            Orientation = Orientation.Vertical;

            // 1. 初始化数据容器（如果本身是单个数字，转成空的选择器）
            object currentVal = _prop.GetValue(_state);
            if (currentVal is NoteSelectorModel sel) { _currentSelector = sel; }
            else { _currentSelector = new NoteSelectorModel(); }

            if (currentVal != null && !(currentVal is NoteSelectorModel))
            {
                // 旧数据是一个单体 ID
                _txtSingleId = new TextBox { Text = currentVal.ToString() };
            }
            else { _txtSingleId = new TextBox { Text = "" }; }

            BuildLayout();
            LoadDataToUI();
            HookEvents();
            RefreshRadar();
        }

        private void BuildLayout()
        {
            // --- 顶部模式切换与结果显示 ---
            var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _cmbMode = new ComboBox { SelectedValuePath = "Tag", Height = 26, VerticalContentAlignment = VerticalAlignment.Center };
            _cmbMode.Items.Add(new ComboBoxItem { Content = "基础单选模式", Tag = "Single" });
            _cmbMode.Items.Add(new ComboBoxItem { Content = "高级选择器模式", Tag = "Selector" });
            Grid.SetColumn(_cmbMode, 0); headerGrid.Children.Add(_cmbMode);

            _txtMatchResult = new TextBlock { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 10, 0) };
            Grid.SetColumn(_txtMatchResult, 1); headerGrid.Children.Add(_txtMatchResult);
            Children.Add(headerGrid);

            // --- 基础单选模式面板 ---
            _txtSingleId.Padding = new Thickness(5);
            _txtSingleId.Margin = new Thickness(0, 0, 0, 10);
            _txtSingleId.ToolTip = "可以直接输入数字 ID，或者输入 $note 占位符哦！";
            Children.Add(_txtSingleId);

            // --- 高级选择器模式主容器 ---
            _panelSelectorContent = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 0, 10) };

            // 下拉添加过滤器
            _cmbAddFilter = new ComboBox { Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(5) };
            _cmbAddFilter.Items.Add(new ComboBoxItem { Content = "➕ 点击此处按需添加过滤条件..." });
            _cmbAddFilter.Items.Add(new ComboBoxItem { Content = "添加 [音符类型 Type] 限制", Tag = "Type" });
            _cmbAddFilter.Items.Add(new ComboBoxItem { Content = "添加 [ID 区间 Start/End] 限制", Tag = "IdRange" });
            _cmbAddFilter.Items.Add(new ComboBoxItem { Content = "添加 [扫线方向 Direction] 限制", Tag = "Direction" });
            _cmbAddFilter.Items.Add(new ComboBoxItem { Content = "添加 [空间X轴 MinX/MaxX] 限制", Tag = "XRange" });
            _cmbAddFilter.SelectedIndex = 0;
            _panelSelectorContent.Children.Add(_cmbAddFilter);

            _panelFilters = new StackPanel { Orientation = Orientation.Vertical };
            _panelSelectorContent.Children.Add(_panelFilters);
            Children.Add(_panelSelectorContent);
        }

        private void LoadDataToUI()
        {
            _isInternalUpdating = true;
            object val = _prop.GetValue(_state);

            // 判断当前模式
            if (val == null || (val is string s && string.IsNullOrEmpty(s)))
            {
                _cmbMode.SelectedValue = "Single";
            }
            else if (val is NoteSelectorModel || val.ToString().Trim().StartsWith("{"))
            {
                // 如果是 JSON 字符串，尝试反序列化
                if (val is string jsonStr)
                {
                    try { _currentSelector = Newtonsoft.Json.JsonConvert.DeserializeObject<NoteSelectorModel>(jsonStr) ?? new NoteSelectorModel(); }
                    catch { _currentSelector = new NoteSelectorModel(); }
                }
                _cmbMode.SelectedValue = "Selector";
                RebuildFilterUIFromData();
            }
            else
            {
                _cmbMode.SelectedValue = "Single";
                _txtSingleId.Text = val.ToString();
            }

            ToggleModeUI();
            _isInternalUpdating = false;
        }

        private void HookEvents()
        {
            _cmbMode.SelectionChanged += (s, e) =>
            {
                ToggleModeUI();
                SaveToMemory();
                RefreshRadar();
            };

            _txtSingleId.TextChanged += (s, e) => { if (!_isInternalUpdating) SaveToMemory(); };

            _cmbAddFilter.SelectionChanged += (s, e) =>
            {
                if (_cmbAddFilter.SelectedIndex == 0) return;
                var tag = (_cmbAddFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString();

                // 给底层数据赋初值，激活该属性！
                if (tag == "Type" && _currentSelector.Type == null) _currentSelector.Type = new List<int> { 0, 1, 2, 3, 5, 6 };
                if (tag == "IdRange") { if (_currentSelector.Start == null) _currentSelector.Start = 1; if (_currentSelector.End == null) _currentSelector.End = 100; }
                if (tag == "Direction" && _currentSelector.Direction == null) _currentSelector.Direction = 1;
                if (tag == "XRange") { if (_currentSelector.MinX == null) _currentSelector.MinX = 0f; if (_currentSelector.MaxX == null) _currentSelector.MaxX = 1f; }

                _cmbAddFilter.SelectedIndex = 0; // 重置下拉
                RebuildFilterUIFromData();
                SaveToMemory();
                RefreshRadar();
            };
        }

        private void ToggleModeUI()
        {
            bool isSingle = _cmbMode.SelectedValue?.ToString() == "Single";
            _txtSingleId.Visibility = isSingle ? Visibility.Visible : Visibility.Collapsed;
            _panelSelectorContent.Visibility = isSingle ? Visibility.Collapsed : Visibility.Visible;
            if (isSingle) _txtMatchResult.Text = ""; // 单选模式不显示雷达
        }

        // ==========================================
        // 🔮 动态渲染按需添加的过滤面板
        // ==========================================
        private void RebuildFilterUIFromData()
        {
            _panelFilters.Children.Clear();

            if (_currentSelector.Type != null) _panelFilters.Children.Add(CreateTypeFilterRow());
            if (_currentSelector.Start.HasValue || _currentSelector.End.HasValue) _panelFilters.Children.Add(CreateIdRangeRow());
            if (_currentSelector.Direction.HasValue) _panelFilters.Children.Add(CreateDirectionRow());
            if (_currentSelector.MinX.HasValue || _currentSelector.MaxX.HasValue) _panelFilters.Children.Add(CreateXRangeRow());
        }

        // 创建统一的面板边框与删除按钮
        private Border CreateFilterContainer(string title, UIElement content, Action onDelete)
        {
            var border = new Border { BorderBrush = Brushes.DimGray, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(10), Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)) };
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            headerGrid.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, Foreground = Brushes.LightSkyBlue });

            var btnDel = new Button { Content = "🗑️ 移除", Foreground = Brushes.IndianRed, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand };
            Grid.SetColumn(btnDel, 1);
            btnDel.Click += (s, e) => { onDelete?.Invoke(); RebuildFilterUIFromData(); SaveToMemory(); RefreshRadar(); };
            headerGrid.Children.Add(btnDel);

            Grid.SetRow(headerGrid, 0); grid.Children.Add(headerGrid);
            Grid.SetRow(content, 1); grid.Children.Add(content);
            border.Child = grid;
            return border;
        }

        private UIElement CreateTypeFilterRow()
        {
            var panel = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3 };
            int[] types = { 0, 1, 2, 3, 5, 6 };
            string[] names = { "Click(0)", "Hold(1)", "L-Hold(2)", "Drag(3)", "Flick(5)", "C-Drag(6)" };

            for (int i = 0; i < 6; i++)
            {
                int typeVal = types[i];
                var chk = new CheckBox { Content = names[i], Foreground = Brushes.White, Margin = new Thickness(0, 5, 0, 5) };
                chk.IsChecked = _currentSelector.Type.Contains(typeVal);

                chk.Click += (s, e) =>
                {
                    if (chk.IsChecked == true) { if (!_currentSelector.Type.Contains(typeVal)) _currentSelector.Type.Add(typeVal); }
                    else
                    {
                        if (_currentSelector.Type.Count <= 1)
                        {
                            BoundedSliderControl.DialogService?.ShowMessage("指挥官，不能全都不选哦！至少得保留一种音符类型呀！", "防呆拦截");
                            chk.IsChecked = true; return; // 拦截！
                        }
                        _currentSelector.Type.Remove(typeVal);
                    }
                    SaveToMemory(); RefreshRadar();
                };
                panel.Children.Add(chk);
            }
            return CreateFilterContainer("🧩 音符类型限制 (Type)", panel, () => { _currentSelector.Type = null; });
        }

        private UIElement CreateIdRangeRow()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var txtStart = new TextBox { Margin = new Thickness(5, 0, 5, 0), Padding = new Thickness(3) };
            var chkStartNoLimit = new CheckBox { Content = "无限制", Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center };
            var spStart = new StackPanel { Orientation = Orientation.Horizontal };
            spStart.Children.Add(new TextBlock { Text = "起点(Start):", VerticalAlignment = VerticalAlignment.Center });
            spStart.Children.Add(txtStart); spStart.Children.Add(chkStartNoLimit);
            Grid.SetColumn(spStart, 1); grid.Children.Add(spStart);

            var txtEnd = new TextBox { Margin = new Thickness(5, 0, 5, 0), Padding = new Thickness(3) };
            var chkEndNoLimit = new CheckBox { Content = "无限制", Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center };
            var spEnd = new StackPanel { Orientation = Orientation.Horizontal };
            spEnd.Children.Add(new TextBlock { Text = "终点(End):", VerticalAlignment = VerticalAlignment.Center });
            spEnd.Children.Add(txtEnd); spEnd.Children.Add(chkEndNoLimit);
            Grid.SetColumn(spEnd, 3); grid.Children.Add(spEnd);

            // 绑定初值
            if (!_currentSelector.Start.HasValue) { chkStartNoLimit.IsChecked = true; txtStart.IsEnabled = false; } else { txtStart.Text = _currentSelector.Start.Value.ToString(); }
            if (!_currentSelector.End.HasValue) { chkEndNoLimit.IsChecked = true; txtEnd.IsEnabled = false; } else { txtEnd.Text = _currentSelector.End.Value.ToString(); }

            // 事件联动
            chkStartNoLimit.Checked += (s, e) => { txtStart.IsEnabled = false; _currentSelector.Start = null; SaveToMemory(); RefreshRadar(); };
            chkStartNoLimit.Unchecked += (s, e) => { txtStart.IsEnabled = true; _currentSelector.Start = 1; txtStart.Text = "1"; SaveToMemory(); RefreshRadar(); };
            txtStart.TextChanged += (s, e) => { if (int.TryParse(txtStart.Text, out int v)) { _currentSelector.Start = v; SaveToMemory(); RefreshRadar(); } };

            chkEndNoLimit.Checked += (s, e) => { txtEnd.IsEnabled = false; _currentSelector.End = null; SaveToMemory(); RefreshRadar(); };
            chkEndNoLimit.Unchecked += (s, e) => { txtEnd.IsEnabled = true; _currentSelector.End = 100; txtEnd.Text = "100"; SaveToMemory(); RefreshRadar(); };
            txtEnd.TextChanged += (s, e) => { if (int.TryParse(txtEnd.Text, out int v)) { _currentSelector.End = v; SaveToMemory(); RefreshRadar(); } };

            return CreateFilterContainer("🔢 ID 区间界限 (Start/End)", grid, () => { _currentSelector.Start = null; _currentSelector.End = null; });
        }

        private UIElement CreateDirectionRow()
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            var chkUp = new CheckBox { Content = "向上扫描 (1)", Foreground = Brushes.White, Margin = new Thickness(0, 0, 15, 0) };
            var chkDown = new CheckBox { Content = "向下扫描 (-1)", Foreground = Brushes.White };

            chkUp.IsChecked = _currentSelector.Direction == 1 || _currentSelector.Direction == null;
            chkDown.IsChecked = _currentSelector.Direction == -1 || _currentSelector.Direction == null;

            RoutedEventHandler handleDirectionChange = (s, e) =>
            {
                if (chkUp.IsChecked == false && chkDown.IsChecked == false)
                {
                    BoundedSliderControl.DialogService?.ShowMessage("指挥官，不能全都不选哦！扫描线方向至少得保留一个呀！", "防呆拦截");
                    ((CheckBox)s).IsChecked = true; return; // 拦截！
                }

                if (chkUp.IsChecked == true && chkDown.IsChecked == true) _currentSelector.Direction = null; // null 意味着全部（不输出 JSON）
                else if (chkUp.IsChecked == true) _currentSelector.Direction = 1;
                else _currentSelector.Direction = -1;

                SaveToMemory(); RefreshRadar();
            };

            chkUp.Click += handleDirectionChange; chkDown.Click += handleDirectionChange;
            panel.Children.Add(chkUp); panel.Children.Add(chkDown);

            return CreateFilterContainer("↕️ 扫线方向限制 (Direction)", panel, () => { _currentSelector.Direction = null; });
        }

        private UIElement CreateXRangeRow()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Min
            var spMin = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 10, 0) };
            var chkMin = new CheckBox { Content = "无限制", FontSize = 10, Foreground = Brushes.Gray };
            var txtMin = new TextBox { Width = 50, Padding = new Thickness(3) };
            spMin.Children.Add(new TextBlock { Text = "MinX:", FontSize = 10 }); spMin.Children.Add(txtMin); spMin.Children.Add(chkMin);
            Grid.SetColumn(spMin, 0); grid.Children.Add(spMin);

            // Max
            var spMax = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(10, 0, 0, 0) };
            var chkMax = new CheckBox { Content = "无限制", FontSize = 10, Foreground = Brushes.Gray };
            var txtMax = new TextBox { Width = 50, Padding = new Thickness(3) };
            spMax.Children.Add(new TextBlock { Text = "MaxX:", FontSize = 10 }); spMax.Children.Add(txtMax); spMax.Children.Add(chkMax);
            Grid.SetColumn(spMax, 2); grid.Children.Add(spMax);

            // 滑块 (兜底只渲染 0~1 的线段防崩溃)
            var slider = new Slider { Minimum = 0, Maximum = 1, TickFrequency = 0.1, IsSnapToTickEnabled = false, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(slider, 1); grid.Children.Add(slider);

            // 绑定初值
            if (!_currentSelector.MinX.HasValue) { chkMin.IsChecked = true; txtMin.IsEnabled = false; slider.Value = 0; }
            else { txtMin.Text = _currentSelector.MinX.Value.ToString(); slider.Value = Math.Max(0, Math.Min(1, _currentSelector.MinX.Value)); }

            if (!_currentSelector.MaxX.HasValue) { chkMax.IsChecked = true; txtMax.IsEnabled = false; }
            else { txtMax.Text = _currentSelector.MaxX.Value.ToString(); }

            // 逻辑绑定
            chkMin.Checked += (s, e) => { txtMin.IsEnabled = false; _currentSelector.MinX = null; SaveToMemory(); RefreshRadar(); };
            chkMin.Unchecked += (s, e) => { txtMin.IsEnabled = true; _currentSelector.MinX = 0f; txtMin.Text = "0"; SaveToMemory(); RefreshRadar(); };
            txtMin.TextChanged += (s, e) => { if (float.TryParse(txtMin.Text, out float v)) { _currentSelector.MinX = v; slider.Value = Math.Max(0, Math.Min(1, v)); SaveToMemory(); RefreshRadar(); } };

            chkMax.Checked += (s, e) => { txtMax.IsEnabled = false; _currentSelector.MaxX = null; SaveToMemory(); RefreshRadar(); };
            chkMax.Unchecked += (s, e) => { txtMax.IsEnabled = true; _currentSelector.MaxX = 1f; txtMax.Text = "1"; SaveToMemory(); RefreshRadar(); };
            txtMax.TextChanged += (s, e) => { if (float.TryParse(txtMax.Text, out float v)) { _currentSelector.MaxX = v; SaveToMemory(); RefreshRadar(); } };

            // 限制：这里由于原生 WPF 没有双点滑块，我们借用单点 Slider 模拟 MinX 变更
            slider.ValueChanged += (s, e) =>
            {
                if (chkMin.IsChecked == false) { txtMin.Text = Math.Round(slider.Value, 3).ToString(); }
            };

            return CreateFilterContainer("📏 X轴空间雷达 (MinX/MaxX)", grid, () => { _currentSelector.MinX = null; _currentSelector.MaxX = null; });
        }

        // ==========================================
        // 💾 保存与雷达反馈系统
        // ==========================================
        private void SaveToMemory()
        {
            if (_isInternalUpdating) return;
            if (_cmbMode.SelectedValue?.ToString() == "Single")
            {
                string txt = _txtSingleId.Text.Trim();
                if (int.TryParse(txt, out int idVal)) _prop.SetValue(_state, idVal);
                else _prop.SetValue(_state, txt); // 可能是 $note 字符串
            }
            else
            {
                // 让大管家帮忙直接存下这个强类型的模型！
                _prop.SetValue(_state, _currentSelector);
            }

            // 惊醒大宇宙
            OnModified?.Invoke();
        }

        private void RefreshRadar()
        {
            if (_cmbMode.SelectedValue?.ToString() == "Single") return; // 单选不雷达

            // 必须确保谱面和页面列表都在，否则无法进行跨表查询
            if (_context?.Chart?.note_list == null || _context.Chart.page_list == null)
            {
                _txtMatchResult.Text = "⏸️ 请导入谱面启用雷达";
                _txtMatchResult.Foreground = Brushes.Gray;
                return;
            }

            int matchCount = 0;
            foreach (var note in _context.Chart.note_list)
            {
                // 🛡️ 抛弃危险的 dynamic，直接使用强类型 C2Note！
                int nid = note.id;
                int ntype = note.type;
                float nx = (float)note.x;

                // 🌟【官方跨表法则】：音符的方向必须去问它所在的 Page！
                int ndir = 1;
                if (note.page_index >= 0 && note.page_index < _context.Chart.page_list.Count)
                {
                    ndir = _context.Chart.page_list[note.page_index].scan_line_direction;
                }

                bool isMatch = true;

                if (_currentSelector.Type != null && !_currentSelector.Type.Contains(ntype)) isMatch = false;
                if (_currentSelector.Start.HasValue && nid < _currentSelector.Start.Value) isMatch = false;
                if (_currentSelector.End.HasValue && nid > _currentSelector.End.Value) isMatch = false;
                if (_currentSelector.Direction.HasValue && ndir != _currentSelector.Direction.Value) isMatch = false;
                if (_currentSelector.MinX.HasValue && nx < _currentSelector.MinX.Value) isMatch = false;
                if (_currentSelector.MaxX.HasValue && nx > _currentSelector.MaxX.Value) isMatch = false;

                if (isMatch) matchCount++;
            }

            if (matchCount > 0)
            {
                _txtMatchResult.Text = $"✅ 成功匹配 {matchCount} 个音符";
                _txtMatchResult.Foreground = Brushes.LightGreen;
            }
            else
            {
                _txtMatchResult.Text = "⚠️ 雷达扫空，此选择器无效！";
                _txtMatchResult.Foreground = Brushes.IndianRed;
            }
        }
    }
}
