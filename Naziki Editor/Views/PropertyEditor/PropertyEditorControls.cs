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

            // 📡 挂载指挥官之前写好的严肃标红拦截探头
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
    // ⏱️ 4. StoryboardTimeControl (时空锚点动态智能交互面板)
    // ==========================================================
    public class StoryboardTimeControl : StackPanel
    {
        private readonly PropertyInfo _propTime;
        private readonly PropertyInfo _propRel;
        private readonly PropertyInfo _propAdd;
        private readonly object _state;
        private readonly bool _isRoot;
        private readonly State.ProjectDataContext _context;

        // 核心UI零部件
        private ComboBox _cmbMainMode;
        private StackPanel _panelTimeMode;
        private RadioButton _rbAbsolute;
        private RadioButton _rbRelative;
        private RadioButton _rbAdditive;
        private TextBox _txtTimeValue;

        private StackPanel _panelNoteMode;
        private TextBox _txtNoteId;
        private ComboBox _cmbNoteAnchor;
        private ComboBoxItem _itemIntro; // 专门用来动态隐藏的“淡入”项
        private RadioButton _rbOffset;
        private RadioButton _rbPercent;
        private TextBox _txtNoteParam;

        private bool _isInternalUpdating = false;

        public StoryboardTimeControl(object state, bool isRoot, State.ProjectDataContext context)
        {
            _state = state;
            _isRoot = isRoot;
            _context = context;
            this.Orientation = Orientation.Vertical;

            // 1. 🔍 抓取底层的互斥三剑客
            _propTime = state.GetType().GetProperty("Time");
            _propRel = state.GetType().GetProperty("RelativeTime");
            _propAdd = state.GetType().GetProperty("AddTime");

            BuildMainLayout();
            LoadCurrentData();
            HookEvents();
        }

        private void BuildMainLayout()
        {
            // 🌍 第一行：主模式选择下拉菜单
            var row1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };
            var lblMode = new TextBlock { Text = "时间模式: ", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray, FontSize = 11, Width = 65 };
            _cmbMainMode = new ComboBox { Width = 150, Padding = new Thickness(3) };
            _cmbMainMode.Items.Add(new ComboBoxItem { Content = "📅 基础时间轴", Tag = "Time" });
            _cmbMainMode.Items.Add(new ComboBoxItem { Content = "🎵 音符 ID 锚点", Tag = "Anchor" });
            row1.Children.Add(lblMode);
            row1.Children.Add(_cmbMainMode);
            this.Children.Add(row1);

            // 🕒 第二行 A 面：时间轴配置仓
            _panelTimeMode = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5), Visibility = Visibility.Collapsed };
            _rbAbsolute = new RadioButton { Content = "绝对", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            _rbRelative = new RadioButton { Content = "相对", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            _rbAdditive = new RadioButton { Content = "附加", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };

            // 如果是初始属性 (Root)，强制隐藏相对和附加单选按钮
            if (_isRoot) { _rbRelative.Visibility = Visibility.Collapsed; _rbAdditive.Visibility = Visibility.Collapsed; }

            _txtTimeValue = new TextBox { Width = 100, Padding = new Thickness(3), Margin = new Thickness(5, 0, 0, 0) };
            _panelTimeMode.Children.Add(_rbAbsolute);
            _panelTimeMode.Children.Add(_rbRelative);
            _panelTimeMode.Children.Add(_rbAdditive);
            _panelTimeMode.Children.Add(new TextBlock { Text = "值(秒):", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray, FontSize = 10, Margin = new Thickness(5, 0, 0, 0) });
            _panelTimeMode.Children.Add(_txtTimeValue);
            this.Children.Add(_panelTimeMode);

            // 🎵 第二行 B 面：音符雷达控制仓 (超豪华拼装件)
            _panelNoteMode = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5), Visibility = Visibility.Collapsed };
            _txtNoteId = new TextBox { Width = 50, Padding = new Thickness(3), ToolTip = "请输入谱面中的音符 ID" };

            _cmbNoteAnchor = new ComboBox { Width = 70, Padding = new Thickness(3), Margin = new Thickness(5, 0, 0, 0) };
            _cmbNoteAnchor.Items.Add(new ComboBoxItem { Content = "开始", Tag = "start" });
            _cmbNoteAnchor.Items.Add(new ComboBoxItem { Content = "结束", Tag = "end" });
            _itemIntro = new ComboBoxItem { Content = "淡入", Tag = "intro" };
            _cmbNoteAnchor.Items.Add(_itemIntro);
            _cmbNoteAnchor.SelectedIndex = 0;

            _rbOffset = new RadioButton { Content = "延迟(s)", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) };
            _rbPercent = new RadioButton { Content = "比例(%)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
            _txtNoteParam = new TextBox { Width = 60, Padding = new Thickness(3), Text = "0" };

            _panelNoteMode.Children.Add(new TextBlock { Text = "音符ID:", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray, FontSize = 10 });
            _panelNoteMode.Children.Add(_txtNoteId);
            _panelNoteMode.Children.Add(_cmbNoteAnchor);
            _panelNoteMode.Children.Add(_rbOffset);
            _panelNoteMode.Children.Add(_rbPercent);
            _panelNoteMode.Children.Add(_txtNoteParam);
            this.Children.Add(_panelNoteMode);
        }

        // 🧠 逆向工程：从底层乱七八糟的内存值或字符串中，拆解还原 UI 的摆放姿态
        private void LoadCurrentData()
        {
            _isInternalUpdating = true;

            string rawTime = _propTime?.GetValue(_state)?.ToString() ?? "";
            string rawRel = _propRel?.GetValue(_state)?.ToString() ?? "";
            string rawAdd = _propAdd?.GetValue(_state)?.ToString() ?? "";

            // 优先探测有没有好玩的“音符锚点字符串”冒充
            string activeAnchorStr = "";
            if (rawTime.Contains(":") || rawTime.Contains("$")) activeAnchorStr = rawTime;
            else if (rawRel.Contains(":") || rawRel.Contains("$")) activeAnchorStr = rawRel;
            else if (rawAdd.Contains(":") || rawAdd.Contains("$")) activeAnchorStr = rawAdd;

            if (!string.IsNullOrEmpty(activeAnchorStr))
            {
                // 🔮 触发音符锚点形态还原！
                _cmbMainMode.SelectedIndex = 1;
                _panelNoteMode.Visibility = Visibility.Visible;

                var parts = activeAnchorStr.Split(':');
                if (parts.Length >= 2)
                {
                    _txtNoteId.Text = parts[1];
                    foreach (ComboBoxItem item in _cmbNoteAnchor.Items)
                        if (item.Tag.ToString() == parts[0]) _cmbNoteAnchor.SelectedItem = item;

                    if (parts[0] == "at")
                    {
                        _rbPercent.IsChecked = true;
                        _txtNoteParam.Text = parts.Length > 2 ? parts[2] : "0.5";
                    }
                    else
                    {
                        _rbOffset.IsChecked = true;
                        _txtNoteParam.Text = parts.Length > 2 ? parts[2] : "0";
                    }
                }
                else if (activeAnchorStr.Contains("$note")) // 兼容特殊占位符
                {
                    _txtNoteId.Text = "$note";
                }
            }
            else
            {
                // 📅 触发传统时间轴形态还原！
                _cmbMainMode.SelectedIndex = 0;
                _panelTimeMode.Visibility = Visibility.Visible;

                if (!string.IsNullOrEmpty(rawAdd) && rawAdd != "0")
                {
                    _rbAdditive.IsChecked = true; _txtTimeValue.Text = rawAdd;
                }
                else if (!string.IsNullOrEmpty(rawRel) && rawRel != "0")
                {
                    _rbRelative.IsChecked = true; _txtTimeValue.Text = rawRel;
                }
                else
                {
                    _rbAbsolute.IsChecked = true;
                    // float.MaxValue 代表未启用，UI 展现为空白白
                    _txtTimeValue.Text = (rawTime.Contains("3.402823") || string.IsNullOrEmpty(rawTime)) ? "" : rawTime;
                }
            }

            _isInternalUpdating = false;
            TriggerNoteTypeRadar(); // 顺手开启雷达扫描一次
        }

        private void HookEvents()
        {
            // 主切换大闸
            _cmbMainMode.SelectionChanged += (s, e) =>
            {
                var tag = (_cmbMainMode.SelectedItem as ComboBoxItem)?.Tag?.ToString();
                _panelTimeMode.Visibility = tag == "Time" ? Visibility.Visible : Visibility.Collapsed;
                _panelNoteMode.Visibility = tag == "Anchor" ? Visibility.Visible : Visibility.Collapsed;
                SaveToMemory();
            };

            // 监听所有能导致数值变动的神经元
            RoutedEventHandler reSave = (s, e) => SaveToMemory();
            TextChangedEventHandler tcSave = (s, e) => SaveToMemory();

            _rbAbsolute.Checked += reSave; _rbRelative.Checked += reSave; _rbAdditive.Checked += reSave;
            _rbOffset.Checked += reSave; _rbPercent.Checked += reSave;
            _cmbNoteAnchor.SelectionChanged += (s, e) => SaveToMemory();

            _txtTimeValue.LostFocus += (s, e) => SaveToMemory();
            _txtNoteParam.LostFocus += (s, e) => SaveToMemory();

            // 📡 【核心绑定】：音符 ID 输入框实时雷达！
            _txtNoteId.TextChanged += (s, e) =>
            {
                TriggerNoteTypeRadar();
                SaveToMemory();
            };
        }

        // 🎵 【小艾的音符基因核验雷达】：查表断定音符门派，智能隐藏专属组件！
        private void TriggerNoteTypeRadar()
        {
            if (_context?.Chart?.note_list == null) return;
            string inputId = _txtNoteId.Text.Trim();

            if (int.TryParse(inputId, out int noteId))
            {
                // 去大管家的数据包里顺藤摸瓜找音符
                var targetNote = _context.Chart.note_list.Find(n => n.id == noteId);
                if (targetNote != null)
                {
                    // 依据 ChartLogic 和模型：type == 1(Hold) 或 2(LongHold) 才是持续性音符！
                    bool isContinuous = (targetNote.type == 1 || targetNote.type == 2);

                    if (!isContinuous)
                    {
                        // 🚨 触发铁腕隐身术：点类音符剥夺“淡入”和“百分比”权利！
                        if (_cmbNoteAnchor.SelectedItem == _itemIntro) _cmbNoteAnchor.SelectedIndex = 0;
                        _itemIntro.Visibility = Visibility.Collapsed;
                        _rbPercent.Visibility = Visibility.Collapsed;
                        _rbOffset.IsChecked = true; // 强制打回原形
                    }
                    else
                    {
                        // 🟢 恢复名誉：让持续音符的专属道具重新现身
                        _itemIntro.Visibility = Visibility.Visible;
                        _rbPercent.Visibility = Visibility.Visible;
                    }
                    return;
                }
            }
            // 混沌情况（比如手打 $note 时），全面放行保证最大包容度
            _itemIntro.Visibility = Visibility.Visible;
            _rbPercent.Visibility = Visibility.Visible;
        }

        // 🪄 【乐高拼装落盘引擎】：把 UI 状态打碎并严丝合缝地塞回底层 C# 模型
        private void SaveToMemory()
        {
            if (_isInternalUpdating) return;

            object finalTime = float.MaxValue; // 默认未激活状态
            object finalRel = null;
            object finalAdd = null;

            var mainTag = (_cmbMainMode.SelectedItem as ComboBoxItem)?.Tag?.ToString();

            if (mainTag == "Time")
            {
                // 📅 1. 传统时间轴拼装逻辑
                string valStr = _txtTimeValue.Text.Trim();
                float.TryParse(valStr, out float parsedVal);

                if (_rbAbsolute.IsChecked == true) finalTime = string.IsNullOrEmpty(valStr) ? float.MaxValue : parsedVal;
                else if (_rbRelative.IsChecked == true) { finalTime = 0f; finalRel = parsedVal; }
                else if (_rbAdditive.IsChecked == true) { finalTime = 0f; finalAdd = parsedVal; }
            }
            else
            {
                // 🎵 2. 音符锚点拼装逻辑
                string noteId = _txtNoteId.Text.Trim();
                if (!string.IsNullOrEmpty(noteId))
                {
                    string anchor = (_cmbNoteAnchor.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "start";
                    string param = _txtNoteParam.Text.Trim();
                    float.TryParse(param, out float pVal);

                    if (_rbPercent.IsChecked == true)
                    {
                        // 百分比格式强制拼装为 at:id:百分比
                        finalTime = $"at:{noteId}:{param}";
                    }
                    else
                    {
                        // 延迟偏移格式
                        if (pVal != 0f && !string.IsNullOrEmpty(param)) finalTime = $"{anchor}:{noteId}:{param}";
                        else finalTime = $"{anchor}:{noteId}";
                    }
                }
            }

            // ✍️ 轰入内存实体
            _propTime?.SetValue(_state, finalTime);
            _propRel?.SetValue(_state, finalRel);
            _propAdd?.SetValue(_state, finalAdd);

            // 📢 惊醒时光机记账
            _context?.MarkAsModified();
        }
    }





}