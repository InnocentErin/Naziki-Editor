using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Naziki_Editor.Core;
using Naziki_Editor.Models;

namespace Naziki_Editor.Views.PropertyEditor
{
    // ==========================================================
    // 🎚️ 1. BoundedSliderControl (离散落盘滑块 + 输入框 + 探头联动)
    // ==========================================================
    public class BoundedSliderControl : Grid
    {
        private Slider _slider;
        private TextBox _textBox;
        private PropertyInfo _prop;
        private object _state;
        private bool _isUpdatingLocal = false;

        public BoundedSliderControl(PropertyInfo prop, object state, Action<TextBox, string> attachProbeAction)
        {
            _prop = prop;
            _state = state;

            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

            var rule = Core.PropertyConstraintManager.GetConstraint(prop.Name);

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
                var rule = Core.PropertyConstraintManager.GetConstraint(_prop.Name);
                if (res >= rule.Min && res <= rule.Max)
                {
                    Type targetType = Nullable.GetUnderlyingType(_prop.PropertyType) ?? _prop.PropertyType;
                    _prop.SetValue(_state, Convert.ChangeType(res, targetType));

                    // 📢 惊醒大宇宙时光机记账
                    NotifyModification();
                }
            }
        }

        private void NotifyModification()
        {
            if (Window.GetWindow(this) is PropertyEditorWindow parentWin)
            {
                var ctxProp = parentWin.GetType().GetProperty("_context", BindingFlags.NonPublic | BindingFlags.Instance);
                if (ctxProp != null)
                {
                    dynamic ctx = ctxProp.GetValue(parentWin);
                    ctx?.MarkAsModified();
                }
            }
        }
    }

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
            if (Window.GetWindow(this) is PropertyEditorWindow parentWin)
            {
                var ctxProp = parentWin.GetType().GetProperty("_context", BindingFlags.NonPublic | BindingFlags.Instance);
                if (ctxProp != null) { dynamic ctx = ctxProp.GetValue(parentWin); ctx?.MarkAsModified(); }
            }
        }
    }

    // ==========================================================
    // 🌈 3. TwelveColorPickerControl (十二色矩阵面板 - 自给自足零漏洞)
    // ==========================================================
    public class TwelveColorPickerControl : Border
    {
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
                        if (MessageBox.Show("是否恢复为游戏默认配色？", "提示", MessageBoxButton.YesNo) == MessageBoxResult.Yes) colorList[index] = null;
                    }
                    refreshSkin();

                    if (Window.GetWindow(this) is PropertyEditorWindow parentWin)
                    {
                        var ctxProp = parentWin.GetType().GetProperty("_context", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (ctxProp != null) { dynamic ctx = ctxProp.GetValue(parentWin); ctx?.MarkAsModified(); }
                    }
                };

                var txt = new TextBlock { Text = noteNames[index], FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0), Foreground = Brushes.Gray };
                block.Children.Add(btn); block.Children.Add(txt); grid.Children.Add(block);
            }
            Child = grid;
        }
    }


    // ==========================================================
    // 🎢 5. EasingPickerControl (缓动触发器按钮 - 取代原有的干瘪文本框)
    // ==========================================================
    public class EasingPickerControl : Grid
    {
        private Button _btnEase;
        private PropertyInfo _prop;
        private object _state;

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

                    if (Window.GetWindow(this) is PropertyEditorWindow parentWin)
                    {
                        var ctxProp = parentWin.GetType().GetProperty("_context", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (ctxProp != null) { dynamic ctx = ctxProp.GetValue(parentWin); ctx?.MarkAsModified(); }
                    }
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

    // ==========================================================
    // 🌌 6. EasingSelectionWindow (史诗级视觉曲线矩阵面板 - 纯代码实时算力绘图！)
    // ==========================================================
    public class EasingSelectionWindow : Window
    {
        public Models.EasingFunction.Ease SelectedEase { get; private set; }

        public EasingSelectionWindow(string initialEaseStr)
        {
            Title = "选择缓动魔法 (Easing Selector)";
            Width = 720; Height = 560;
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
            WindowStyle = WindowStyle.ToolWindow;

            if (Enum.TryParse(initialEaseStr, true, out Models.EasingFunction.Ease parsedEase)) SelectedEase = parsedEase;
            else if (int.TryParse(initialEaseStr, out int easeInt)) SelectedEase = (Models.EasingFunction.Ease)easeInt;
            else SelectedEase = Models.EasingFunction.Ease.None;

            var scrollViewer = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var wrapPanel = new System.Windows.Controls.Primitives.UniformGrid { Columns = 5, Margin = new Thickness(10) };
            scrollViewer.Content = wrapPanel;
            Content = scrollViewer;

            // 遍历所有 34 种缓动，动态生成视觉方块！
            foreach (Models.EasingFunction.Ease ease in Enum.GetValues(typeof(Models.EasingFunction.Ease)))
            {
                var card = CreateEaseCard(ease);
                wrapPanel.Children.Add(card);
            }
        }

        private Border CreateEaseCard(Models.EasingFunction.Ease ease)
        {
            bool isSelected = (ease == SelectedEase);

            var border = new Border
            {
                Width = 120,
                Height = 100,
                Margin = new Thickness(8),
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                BorderBrush = isSelected ? Brushes.DeepSkyBlue : Brushes.DimGray,
                BorderThickness = new Thickness(isSelected ? 2 : 1),
                CornerRadius = new CornerRadius(6),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            var grid = new Grid { Margin = new Thickness(5) };
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 🎨 核心：绘制实时函数曲线的画布
            var canvas = new Canvas { ClipToBounds = true, Margin = new Thickness(5, 10, 5, 5) };
            var polyline = new System.Windows.Shapes.Polyline
            {
                Stroke = isSelected ? Brushes.DeepSkyBlue : Brushes.MediumSpringGreen,
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round
            };

            // 利用算法生成 30 个点，连成平滑曲线
            for (int i = 0; i <= 30; i++)
            {
                double t = i / 30.0;
                double v = GetApproximatedEaseValue(ease, t);

                // 坐标映射：画布宽100，高60，留出一点溢出空间展示 Bounce 和 Back
                double x = t * 100;
                double y = 60 - (v * 40 + 10); // 基础区间在中间 40 像素，上下各留 10 像素用于弹跳溢出
                polyline.Points.Add(new Point(x, y));
            }
            canvas.Children.Add(polyline);
            grid.Children.Add(canvas);

            // 文字标签
            var txt = new TextBlock
            {
                Text = $"{(int)ease} - {ease}",
                Foreground = isSelected ? Brushes.White : Brushes.LightGray,
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetRow(txt, 1);
            grid.Children.Add(txt);

            border.Child = grid;

            // 🖱️ 交互动画与点击
            border.MouseEnter += (s, e) => { if (!isSelected) border.BorderBrush = Brushes.LightGray; };
            border.MouseLeave += (s, e) => { if (!isSelected) border.BorderBrush = Brushes.DimGray; };
            border.MouseLeftButtonUp += (s, e) => { SelectedEase = ease; this.DialogResult = true; this.Close(); };

            return border;
        }

        // 🧠 小艾的超算中心：提供用于视觉预览的函数插值算法！(仅供绘图展示，极其轻量)
        private double GetApproximatedEaseValue(Models.EasingFunction.Ease ease, double t)
        {
            switch (ease)
            {
                case Models.EasingFunction.Ease.Linear: case Models.EasingFunction.Ease.None: return t;
                case Models.EasingFunction.Ease.EaseInQuad: return t * t;
                case Models.EasingFunction.Ease.EaseOutQuad: return t * (2 - t);
                case Models.EasingFunction.Ease.EaseInOutQuad: return t < 0.5 ? 2 * t * t : -1 + (4 - 2 * t) * t;
                case Models.EasingFunction.Ease.EaseInCubic: return t * t * t;
                case Models.EasingFunction.Ease.EaseOutCubic: return (--t) * t * t + 1;
                case Models.EasingFunction.Ease.EaseInSine: return 1 - Math.Cos(t * Math.PI / 2);
                case Models.EasingFunction.Ease.EaseOutSine: return Math.Sin(t * Math.PI / 2);
                case Models.EasingFunction.Ease.EaseInOutSine: return -(Math.Cos(Math.PI * t) - 1) / 2;
                case Models.EasingFunction.Ease.EaseInExpo: return t == 0 ? 0 : Math.Pow(2, 10 * (t - 1));
                case Models.EasingFunction.Ease.EaseOutExpo: return t == 1 ? 1 : 1 - Math.Pow(2, -10 * t);
                case Models.EasingFunction.Ease.EaseInBack: { double s = 1.70158; return t * t * ((s + 1) * t - s); }
                case Models.EasingFunction.Ease.EaseOutBack: { double s = 1.70158; t--; return (t * t * ((s + 1) * t + s) + 1); }
                case Models.EasingFunction.Ease.Blink: return t < 0.5 ? 0 : 1;
                // 其他未精细化书写的复杂缓动，默认用一条稍微带点弧度的通用曲线平替，防止卡死
                default:
                    if (ease.ToString().Contains("Out")) return Math.Sin(t * Math.PI / 2);
                    if (ease.ToString().Contains("InOut")) return -(Math.Cos(Math.PI * t) - 1) / 2;
                    return t * t;
            }
        }
    }












    // ==========================================================
    // 🎚️ 4. StoryboardTimeRow (时空锚点单体功能细胞行 - 逻辑严密对齐版)
    // ==========================================================
    public class StoryboardTimeRow : Grid
    {
        private ComboBox _cmbMainMode;
        private StackPanel _panelTimeMode;
        private RadioButton _rbAbsolute;
        private RadioButton _rbRelative;
        private RadioButton _rbAdditive;
        private TextBox _txtTimeValue;

        private StackPanel _panelNoteMode;
        private TextBox _txtNoteId;
        private ComboBox _cmbNoteAnchor;
        private ComboBoxItem _itemStart;
        private ComboBoxItem _itemEnd;
        private ComboBoxItem _itemIntro;
        private ComboBoxItem _itemAt;      // 🌟 “在 (at)” 核心时刻
        private TextBlock _lblParamHint;   // 🌟 动态切换 “延(s):” 或 “比(%):” 的提示标签
        private TextBox _txtNoteParam;

        private bool _isInternalUpdating = false;
        private readonly bool _isRoot;
        private readonly Core.ChartTimeEngine _engine;
        private readonly Models.C2Chart _chart;
        private readonly Action _onChangedCallback;

        // 🌟 物理手柄
        public Border DragHandle { get; private set; }
        public Button BtnDelete { get; private set; }

        public StoryboardTimeRow(object initialValue, bool isRoot, State.ProjectDataContext context, Action onChangedCallback)
        {
            _isRoot = isRoot;
            _engine = context?.TimeEngine;
            _chart = context?.Chart;
            _onChangedCallback = onChangedCallback;

            Margin = new Thickness(0, 2, 0, 4);

            // 布局切分：[ ☰ 拖拽(30) ] + [ 🔮 核心控制中心(*) ] + [ 🗑️ 销毁(35) ]
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(35) });

            BuildRowLayout();
            HookRowEvents();
            SetValue(initialValue);
        }

        private void BuildRowLayout()
        {
            // 1. 🎽 左侧神圣拖拽手柄
            DragHandle = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(55, 55, 55)),
                CornerRadius = new CornerRadius(3, 0, 0, 3),
                Cursor = System.Windows.Input.Cursors.SizeNS,
                ToolTip = "按住左键上下拖动，可任意重排时空顺序哦！",
                Child = new TextBlock { Text = "⣿", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brushes.Gray, FontSize = 12 }
            };
            Grid.SetColumn(DragHandle, 0);
            Children.Add(DragHandle);

            // 2. 🔮 中间控制卡片容器（完美的流线型横向排列单行长廊）
            var centerStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Background = new SolidColorBrush(Color.FromRgb(43, 43, 43)) };
            Border centerBorder = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(65, 65, 65)), BorderThickness = new Thickness(0, 1, 0, 1), Padding = new Thickness(8, 3, 8, 3), Child = centerStack };
            Grid.SetColumn(centerBorder, 1);
            Children.Add(centerBorder);

            // 🏷️ 门派主模式选择框
            var rowMode = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            _cmbMainMode = new ComboBox { Width = 100, Height = 22, FontSize = 10, Padding = new Thickness(2) };
            _cmbMainMode.Items.Add(new ComboBoxItem { Content = "📅 基础时空", Tag = "Time" });
            _cmbMainMode.Items.Add(new ComboBoxItem { Content = "🎵 音符锚点", Tag = "Anchor" });
            rowMode.Children.Add(new TextBlock { Text = "模式: ", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.DarkGray, FontSize = 10, Margin = new Thickness(0, 0, 4, 0) });
            rowMode.Children.Add(_cmbMainMode);
            centerStack.Children.Add(rowMode);

            // A 面：基础时空轴配置仓
            _panelTimeMode = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _rbAbsolute = new RadioButton { Content = "绝对", IsChecked = true, FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            _rbRelative = new RadioButton { Content = "相对", FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            _rbAdditive = new RadioButton { Content = "附加", FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            if (_isRoot) { _rbRelative.Visibility = Visibility.Collapsed; _rbAdditive.Visibility = Visibility.Collapsed; }
            _txtTimeValue = new TextBox { Width = 65, Height = 20, FontSize = 10, Padding = new Thickness(1), Margin = new Thickness(4, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center };

            _panelTimeMode.Children.Add(_rbAbsolute); _panelTimeMode.Children.Add(_rbRelative); _panelTimeMode.Children.Add(_rbAdditive);
            _panelTimeMode.Children.Add(new TextBlock { Text = " ⏳ 秒数:", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.DarkGray, FontSize = 10, Margin = new Thickness(4, 0, 2, 0) });
            _panelTimeMode.Children.Add(_txtTimeValue);
            centerStack.Children.Add(_panelTimeMode);

            // B 面：音符锚点控制仓（遵照设计师大宪法重塑后的无缝面板）
            _panelNoteMode = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _txtNoteId = new TextBox { Width = 45, Height = 20, FontSize = 10, Padding = new Thickness(1), VerticalContentAlignment = VerticalAlignment.Center };

            _cmbNoteAnchor = new ComboBox { Width = 75, Height = 20, FontSize = 10, Margin = new Thickness(4, 0, 4, 0) };
            _itemStart = new ComboBoxItem { Content = "开始(start)", Tag = "start" };
            _itemEnd = new ComboBoxItem { Content = "结束(end)", Tag = "end" };
            _itemIntro = new ComboBoxItem { Content = "淡入(intro)", Tag = "intro" };
            _itemAt = new ComboBoxItem { Content = "在(at)", Tag = "at" }; // 🌟 迎回 at 王者
            _cmbNoteAnchor.Items.Add(_itemStart); _cmbNoteAnchor.Items.Add(_itemEnd); _cmbNoteAnchor.Items.Add(_itemIntro); _cmbNoteAnchor.Items.Add(_itemAt);
            _cmbNoteAnchor.SelectedIndex = 0;

            // 🌟 动态提示语，初始默认为延迟
            _lblParamHint = new TextBlock { Text = " 延(s):", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.DarkGray, FontSize = 10, Margin = new Thickness(4, 0, 2, 0) };
            _txtNoteParam = new TextBox { Width = 45, Height = 20, FontSize = 10, Padding = new Thickness(1), VerticalContentAlignment = VerticalAlignment.Center, Text = "0" };

            _panelNoteMode.Children.Add(new TextBlock { Text = "ID:", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.DarkGray, FontSize = 10, Margin = new Thickness(0, 0, 2, 0) });
            _panelNoteMode.Children.Add(_txtNoteId);
            _panelNoteMode.Children.Add(_cmbNoteAnchor);
            _panelNoteMode.Children.Add(_lblParamHint);
            _panelNoteMode.Children.Add(_txtNoteParam);
            centerStack.Children.Add(_panelNoteMode);

            // 3. 🗑️ 右侧销毁按钮
            BtnDelete = new Button
            {
                Content = "✕",
                Foreground = Brushes.IndianRed,
                Background = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "将此条时间轴锚点抹杀！"
            };
            Grid.SetColumn(BtnDelete, 2);
            Children.Add(BtnDelete);

            if (_isRoot)
            {
                DragHandle.Visibility = Visibility.Collapsed;
                BtnDelete.Visibility = Visibility.Collapsed;

                // 顺便把左右两边用来装手柄和删除键的废弃列宽彻底压扁，
                // 让控制面板在初始属性里向左右延展占满，视觉更清爽！
                this.ColumnDefinitions[0].Width = new GridLength(0);
                this.ColumnDefinitions[2].Width = new GridLength(0);
            }

        }

        private void HookRowEvents()
        {
            _cmbMainMode.SelectionChanged += (s, e) =>
            {
                var tag = (_cmbMainMode.SelectedItem as ComboBoxItem)?.Tag?.ToString();
                _panelTimeMode.Visibility = tag == "Time" ? Visibility.Visible : Visibility.Collapsed;
                _panelNoteMode.Visibility = tag == "Anchor" ? Visibility.Visible : Visibility.Collapsed;
                if (!_isInternalUpdating) _onChangedCallback?.Invoke();
            };

            RoutedEventHandler reTrigger = (s, e) => { if (!_isInternalUpdating) _onChangedCallback?.Invoke(); };
            _rbAbsolute.Checked += reTrigger; _rbRelative.Checked += reTrigger; _rbAdditive.Checked += reTrigger;

            // 🌟【宪法防御一】：实时调整后缀提示语，杜绝 at 与延迟、非 at 与百分比发生越界踩踏
            _cmbNoteAnchor.SelectionChanged += (s, e) =>
            {
                if (_cmbNoteAnchor.SelectedItem is ComboBoxItem item && _lblParamHint != null)
                {
                    string anchorTag = item.Tag.ToString();
                    _lblParamHint.Text = (anchorTag == "at") ? " 比(%):" : " 延(s):";
                }
                if (!_isInternalUpdating) _onChangedCallback?.Invoke();
            };

            _txtTimeValue.LostFocus += (s, e) => _onChangedCallback?.Invoke();
            _txtNoteParam.LostFocus += (s, e) => _onChangedCallback?.Invoke();
            _txtNoteId.TextChanged += (s, e) => { TriggerNoteTypeRadar(); _onChangedCallback?.Invoke(); };
        }

        private void TriggerNoteTypeRadar()
        {
            if (_chart?.note_list == null) return;
            string inputId = _txtNoteId.Text.Trim();
            if (int.TryParse(inputId, out int noteId))
            {
                var targetNote = _chart.note_list.Find(n => n.id == noteId);
                if (targetNote != null)
                {
                    bool isContinuous = (targetNote.type == 1 || targetNote.type == 2); // 1:Hold, 2:LHold
                    if (!isContinuous)
                    {
                        // 🚨【宪法防御二】：点类音符绝对不允许染指 intro 和 at！一见发现，铁腕遣返归为 start 形态！
                        var currentTag = (_cmbNoteAnchor.SelectedItem as ComboBoxItem)?.Tag?.ToString();
                        if (currentTag == "intro" || currentTag == "at") _cmbNoteAnchor.SelectedItem = _itemStart;

                        _itemIntro.Visibility = Visibility.Collapsed;
                        _itemAt.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        _itemIntro.Visibility = Visibility.Visible;
                        _itemAt.Visibility = Visibility.Visible;
                    }
                    return;
                }
            }
            _itemIntro.Visibility = Visibility.Visible;
            _itemAt.Visibility = Visibility.Visible;
        }

        public void SetArrayModeRestrictions(bool isArray)
        {
            if (_isRoot) return;
            if (isArray)
            {
                if (_rbRelative.IsChecked == true || _rbAdditive.IsChecked == true) _rbAbsolute.IsChecked = true;
                _rbRelative.Visibility = Visibility.Collapsed;
                _rbAdditive.Visibility = Visibility.Collapsed;
            }
            else
            {
                _rbRelative.Visibility = Visibility.Visible;
                _rbAdditive.Visibility = Visibility.Visible;
            }
        }

        public bool IsModifierSelected()
        {
            if ((_cmbMainMode.SelectedItem as ComboBoxItem)?.Tag?.ToString() != "Time") return false;
            return _rbRelative.IsChecked == true || _rbAdditive.IsChecked == true;
        }

        public string GetMainMode() => (_cmbMainMode.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        public string GetTimeSubMode() => _rbAbsolute.IsChecked == true ? "Absolute" : (_rbRelative.IsChecked == true ? "Relative" : "Additive");
        public string GetTimeValue() => _txtTimeValue.Text.Trim();
        public string GetNoteId() => _txtNoteId.Text.Trim();
        public string GetNoteAnchor() => (_cmbNoteAnchor.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "start";
        public bool IsPercent() => (_cmbNoteAnchor.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "at";
        public string GetNoteParam() => _txtNoteParam.Text.Trim();

        public void SetValue(object value)
        {
            _isInternalUpdating = true;
            string raw = value?.ToString() ?? "";

            if (raw.StartsWith("relative:"))
            {
                _cmbMainMode.SelectedIndex = 0; _panelTimeMode.Visibility = Visibility.Visible;
                _rbRelative.IsChecked = true; _txtTimeValue.Text = raw.Replace("relative:", "");
            }
            else if (raw.StartsWith("additive:"))
            {
                _cmbMainMode.SelectedIndex = 0; _panelTimeMode.Visibility = Visibility.Visible;
                _rbAdditive.IsChecked = true; _txtTimeValue.Text = raw.Replace("additive:", "");
            }
            else if (raw.Contains(":") || raw.Contains("$"))
            {
                _cmbMainMode.SelectedIndex = 1; _panelNoteMode.Visibility = Visibility.Visible;
                var parts = raw.Split(':');
                if (parts.Length >= 2)
                {
                    _txtNoteId.Text = parts[1];

                    // 🔮 智能反向读取机制：当探测到 at 前缀时，精准咬合 at 菜单项并切换为百分比视图
                    if (parts[0] == "at")
                    {
                        _cmbNoteAnchor.SelectedItem = _itemAt;
                        _lblParamHint.Text = " 比(%):";
                        _txtNoteParam.Text = parts.Length > 2 ? parts[2] : "0.5";
                    }
                    else
                    {
                        foreach (ComboBoxItem item in _cmbNoteAnchor.Items) if (item.Tag.ToString() == parts[0]) _cmbNoteAnchor.SelectedItem = item;
                        _lblParamHint.Text = " 延(s):";
                        _txtNoteParam.Text = parts.Length > 2 ? parts[2] : "0";
                    }
                }
                else if (raw.Contains("$note")) _txtNoteId.Text = "$note";
            }
            else
            {
                _cmbMainMode.SelectedIndex = 0; _panelTimeMode.Visibility = Visibility.Visible;
                _rbAbsolute.IsChecked = true;
                _txtTimeValue.Text = (raw.Contains("3.402823") || string.IsNullOrEmpty(raw)) ? "" : raw;
            }
            _isInternalUpdating = false;
            TriggerNoteTypeRadar();
        }

        public object GetValue()
        {
            var mainTag = (_cmbMainMode.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            if (mainTag == "Time")
            {
                string valStr = _txtTimeValue.Text.Trim();
                if (string.IsNullOrEmpty(valStr)) return float.MaxValue;
                float.TryParse(valStr, out float parsedVal);
                return parsedVal;
            }
            else
            {
                string noteId = _txtNoteId.Text.Trim();
                if (string.IsNullOrEmpty(noteId)) return float.MaxValue;
                string anchor = (_cmbNoteAnchor.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "start";
                string param = _txtNoteParam.Text.Trim();
                float.TryParse(param, out float pVal);

                // 🚀【数据落盘核心】：当处于 at 时刻，由于其不支持延迟参数，无条件输出标准的 at:id:百分比 字符串！
                if (anchor == "at")
                {
                    return $"at:{noteId}:{(string.IsNullOrEmpty(param) ? "0.5" : param)}";
                }
                // 🚀 当处于 start, end, intro 时，完全对应延迟后缀。如果没填或填0则智能合并兜底
                return (pVal != 0f && !string.IsNullOrEmpty(param)) ? $"{anchor}:{noteId}:{param}" : $"{anchor}:{noteId}";
            }
        }
    }

    // ==========================================================
    // 🌍 5. StoryboardTimeControl (时空锚点调度管理母仓 - 完全体拖拽矩阵版)
    // ==========================================================
    public class StoryboardTimeControl : StackPanel
    {
        private readonly PropertyInfo _propTime;
        private readonly PropertyInfo _propRel;
        private readonly PropertyInfo _propAdd;
        private readonly object _state;
        private readonly bool _isRoot;
        private readonly State.ProjectDataContext _context;

        private StackPanel _rowsContainer;
        private Button _btnAddRow;

        private bool _isDragging = false;
        private StoryboardTimeRow _draggedRow = null;
        private bool _isInternalUpdating = false;

        public StoryboardTimeControl(object state, bool isRoot, State.ProjectDataContext context)
        {
            _state = state;
            _isRoot = isRoot;
            _context = context;
            this.Orientation = Orientation.Vertical;

            _propTime = state.GetType().GetProperty("Time");
            _propRel = state.GetType().GetProperty("RelativeTime");
            _propAdd = state.GetType().GetProperty("AddTime");

            _rowsContainer = new StackPanel { Orientation = Orientation.Vertical };
            this.Children.Add(_rowsContainer);

            _btnAddRow = new Button
            {
                Content = "➕ 添加多重时间轴轴心锚点 (Add Time Row)",
                Margin = new Thickness(0, 5, 0, 5),
                Padding = new Thickness(10, 6, 10, 6),
                Background = new SolidColorBrush(Color.FromRgb(45, 90, 45)),
                Foreground = Brushes.LightGreen,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            _btnAddRow.Click += (s, e) => { AddNewTimeRow(float.MaxValue); UpdateUiRestrictionsAndSave(); };

            if (!_isRoot) this.Children.Add(_btnAddRow);

            LoadCurrentData();
        }

        private void LoadCurrentData()
        {
            _isInternalUpdating = true;
            _rowsContainer.Children.Clear();

            object rawTimeObj = _propTime?.GetValue(_state);
            string rawRel = _propRel?.GetValue(_state)?.ToString() ?? "";
            string rawAdd = _propAdd?.GetValue(_state)?.ToString() ?? "";

            if (rawTimeObj is System.Collections.IList list)
            {
                foreach (var item in list) AddNewTimeRow(item);
            }
            else if (!string.IsNullOrEmpty(rawRel) && rawRel != "0" && !_isRoot)
            {
                AddNewTimeRow($"relative:{rawRel}");
            }
            else if (!string.IsNullOrEmpty(rawAdd) && rawAdd != "0" && !_isRoot)
            {
                AddNewTimeRow($"additive:{rawAdd}");
            }
            else
            {
                AddNewTimeRow(rawTimeObj);
            }

            _isInternalUpdating = false;
            UpdateUiRestrictionsAndSave();
        }

        private void AddNewTimeRow(object val)
        {
            var row = new StoryboardTimeRow(val, _isRoot, _context, UpdateUiRestrictionsAndSave);

            row.BtnDelete.Click += (s, e) =>
            {
                _rowsContainer.Children.Remove(row);
                UpdateUiRestrictionsAndSave();
            };

            row.DragHandle.PreviewMouseLeftButtonDown += (s, e) =>
            {
                _isDragging = true; _draggedRow = row;
                row.DragHandle.CaptureMouse(); e.Handled = true;
            };

            row.DragHandle.PreviewMouseMove += (s, e) =>
            {
                if (!_isDragging || _draggedRow != row) return;

                Point mousePos = e.GetPosition(_rowsContainer);
                int targetIndex = -1; double heightAccumulator = 0;

                for (int i = 0; i < _rowsContainer.Children.Count; i++)
                {
                    var child = _rowsContainer.Children[i] as FrameworkElement;
                    heightAccumulator += child.ActualHeight;
                    if (mousePos.Y < heightAccumulator) { targetIndex = i; break; }
                }
                if (targetIndex == -1) targetIndex = _rowsContainer.Children.Count - 1;

                int currentIndex = _rowsContainer.Children.IndexOf(_draggedRow);
                if (currentIndex != targetIndex && targetIndex >= 0)
                {
                    _rowsContainer.Children.Remove(_draggedRow);
                    _rowsContainer.Children.Insert(targetIndex, _draggedRow);
                }
            };

            row.DragHandle.PreviewMouseLeftButtonUp += (s, e) =>
            {
                if (_isDragging && _draggedRow == row)
                {
                    row.DragHandle.ReleaseMouseCapture(); _isDragging = false; _draggedRow = null;
                    UpdateUiRestrictionsAndSave();
                }
            };

            _rowsContainer.Children.Add(row);
        }

        private void UpdateUiRestrictionsAndSave()
        {
            if (_isInternalUpdating) return;

            int count = _rowsContainer.Children.Count;
            bool isArrayMode = count > 1;

            foreach (StoryboardTimeRow row in _rowsContainer.Children)
            {
                row.SetArrayModeRestrictions(isArrayMode);
            }

            if (count == 1)
            {
                var firstRow = _rowsContainer.Children[0] as StoryboardTimeRow;
                if (firstRow != null && firstRow.IsModifierSelected())
                {
                    _btnAddRow.IsEnabled = false; _btnAddRow.Opacity = 0.4;
                    _btnAddRow.ToolTip = "⚠️ 提示：当第一行被指定为相对(Relative)或附加(Additive)时间时，无法再点击添加平行复数矩阵行哦！";
                }
                else
                {
                    _btnAddRow.IsEnabled = true; _btnAddRow.Opacity = 1.0; _btnAddRow.ToolTip = null;
                }
            }
            else
            {
                _btnAddRow.IsEnabled = true; _btnAddRow.Opacity = 1.0; _btnAddRow.ToolTip = null;
            }

            SaveToMemory();
        }

        private void SaveToMemory()
        {
            if (_isInternalUpdating) return;

            object finalTime = float.MaxValue;
            object finalRel = null;
            object finalAdd = null;

            int count = _rowsContainer.Children.Count;
            if (count == 0)
            {
                _propTime?.SetValue(_state, float.MaxValue);
                _propRel?.SetValue(_state, null); _propAdd?.SetValue(_state, null);
                _context?.MarkAsModified(); return;
            }

            if (count == 1)
            {
                var row = _rowsContainer.Children[0] as StoryboardTimeRow;
                string mainMode = row.GetMainMode();

                if (mainMode == "Time")
                {
                    string subMode = row.GetTimeSubMode(); string valStr = row.GetTimeValue();
                    float.TryParse(valStr, out float parsedVal);

                    if (subMode == "Absolute") finalTime = string.IsNullOrEmpty(valStr) ? float.MaxValue : (object)parsedVal;
                    else if (subMode == "Relative") { finalTime = 0f; finalRel = parsedVal; }
                    else if (subMode == "Additive") { finalTime = 0f; finalAdd = parsedVal; }
                }
                else
                {
                    finalTime = row.GetValue();
                }
            }
            else
            {
                var finalList = new List<object>();
                foreach (StoryboardTimeRow row in _rowsContainer.Children)
                {
                    var val = row.GetValue();
                    if (val != null && !val.ToString().Contains("3.402823"))
                    {
                        finalList.Add(val);
                    }
                }
                finalTime = finalList.Count > 0 ? finalList : (object)float.MaxValue;
            }

            _propTime?.SetValue(_state, finalTime);
            _propRel?.SetValue(_state, finalRel);
            _propAdd?.SetValue(_state, finalAdd);

            _context?.MarkAsModified();
        }
    }




    // ==========================================================
    // 🎯 6. NoteSelectorBuilderControl (按需加载的高级音符雷达)
    // ==========================================================
    public class NoteSelectorBuilderControl : StackPanel
    {
        private readonly PropertyInfo _prop;
        private readonly object _state;
        private readonly State.ProjectDataContext _context;
        private NoteSelectorModel _currentSelector;

        // UI 部件
        private ComboBox _cmbMode;
        private TextBox _txtSingleId;
        private StackPanel _panelSelectorContent;
        private ComboBox _cmbAddFilter;
        private TextBlock _txtMatchResult;
        private StackPanel _panelFilters;

        private bool _isInternalUpdating = false;

        public NoteSelectorBuilderControl(PropertyInfo prop, object state, State.ProjectDataContext context)
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
                            MessageBox.Show("指挥官，不能全都不选哦！至少得保留一种音符类型呀！", "防呆拦截");
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
                    MessageBox.Show("指挥官，不能全都不选哦！扫描线方向至少得保留一个呀！", "防呆拦截");
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
            if (Window.GetWindow(this) is PropertyEditorWindow parentWin)
            {
                var ctxProp = parentWin.GetType().GetProperty("_context", BindingFlags.NonPublic | BindingFlags.Instance);
                if (ctxProp != null) { dynamic ctx = ctxProp.GetValue(parentWin); ctx?.MarkAsModified(); }
            }
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