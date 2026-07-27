using Naziki_Editor.Core;
using Naziki_Editor.Models;
using Naziki_Editor.State;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Naziki_Editor.Views.PropertyEditor
{
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
        private readonly ChartTimeEngine _engine;
        private readonly C2Chart _chart;
        private readonly Action _onChangedCallback;

        // 🌟 物理手柄
        public Border DragHandle { get; private set; }
        public Button BtnDelete { get; private set; }

        public StoryboardTimeRow(object initialValue, bool isRoot, ProjectDataContext context, Action onChangedCallback)
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
}
