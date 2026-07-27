using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Naziki_Editor.Core.Messaging;
using Naziki_Editor.Models;
using Naziki_Editor.State;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Shortcuts;

namespace Naziki_Editor.Views
{
    public partial class PropertyPanelControl : UserControl, IShortcutAware
    {
        public ShortcutContext ShortcutContext => ShortcutContext.PropertyPanel;
        public bool OnShortcutFocusGained() => true;
        public void OnShortcutFocusLost() { }

        public event Action<object> OnEditPropertiesRequested;
        public event Action<object> OnSaveAsMaterialRequested;
        public event Action OnApplyPropertiesRequested;

        private object _currentObject;
        private readonly IMessageBroker _messageBroker;
        public ProjectDataContext Context { get; private set; }

        public void LoadContext(ProjectDataContext context)
        {
            Context = context;
        }

        public PropertyPanelControl()
        {
            InitializeComponent();
        }

        public PropertyPanelControl(IMessageBroker messageBroker) : this()
        {
            _messageBroker = messageBroker;
        }

        public void SetSelectedObject(object obj)
        {
            _currentObject = obj;
            RefreshPropertyDisplay();
        }

        /// <summary>
        /// 获取当前属性面板中选中的对象（供快捷键系统调用）。
        /// </summary>
        public object GetSelectedObject() => _currentObject;

        private void RefreshPropertyDisplay()
        {
            PropertyContainer.Children.Clear();

            if (_currentObject == null)
            {
                PropertyContainer.Children.Add(new TextBlock
                {
                    Text = "未选中任何对象",
                    Foreground = (Brush)FindResource("TipsColor"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 20, 0, 0)
                });
                return;
            }

            // ✨ 核心升级：分支判定全部对接 C2 实体家族！
            switch (_currentObject)
            {
                case C2Sprite sprite:
                    BuildSpriteForm(sprite);
                    break;
                case C2Text text:
                    BuildTextForm(text);
                    break;
                case C2Line line:
                    BuildLineForm(line);
                    break;
                case C2Video video:
                    BuildVideoForm(video);
                    break;
                case C2SceneController controller:
                    BuildControllerForm(controller);
                    break;
                case C2NoteController noteCtrl:
                    BuildNoteControllerForm(noteCtrl);
                    break;
                case C2Template template:
                    BuildTemplateForm(template);
                    break;
                default:
                    PropertyContainer.Children.Add(new TextBlock
                    {
                        Text = $"不支持的类型：{_currentObject.GetType().Name}",
                        Foreground = Brushes.Red
                    });
                    break;
            }
        }

        private void AddPropertyRow(string label, string value, object source = null, string propertyName = null)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Margin = new Thickness(0, 4, 0, 4);

            var labelBlock = new TextBlock
            {
                Text = label + ":",
                Foreground = (Brush)FindResource("SecTextColor"),
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.Bold
            };
            Grid.SetColumn(labelBlock, 0);
            grid.Children.Add(labelBlock);

            // Determine if this property is editable via reflection
            bool isEditable = source != null && propertyName != null;
            System.Reflection.PropertyInfo propInfo = null;
            if (isEditable)
            {
                propInfo = source.GetType().GetProperty(propertyName);
                isEditable = propInfo != null && propInfo.CanWrite;
            }

            if (!isEditable)
            {
                // Read-only display (original behavior)
                var valueBlock = new TextBlock
                {
                    Text = value,
                    Foreground = (Brush)FindResource("MainTextColor"),
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(valueBlock, 1);
                grid.Children.Add(valueBlock);
            }
            else
            {
                // Editable: Border + TextBlock combo, click to edit
                var border = new Border
                {
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Padding = new Thickness(2, 1, 2, 1)
                };
                Grid.SetColumn(border, 1);

                var valueBlock = new TextBlock
                {
                    Text = value,
                    Foreground = (Brush)FindResource("MainTextColor"),
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center
                };
                border.Child = valueBlock;

                var editCtx = new PropertyEditContext
                {
                    Source = source,
                    PropertyInfo = propInfo,
                    OriginalValue = value,
                    DisplayTextBlock = valueBlock,
                    ContainerBorder = border
                };
                border.Tag = editCtx;

                border.MouseLeftButtonDown += EditableValue_Click;
                border.MouseEnter += (s, e) => border.Background = new SolidColorBrush(Color.FromRgb(60, 60, 70));
                border.MouseLeave += (s, e) => border.Background = Brushes.Transparent;

                grid.Children.Add(border);
            }

            PropertyContainer.Children.Add(grid);
        }

        // ==========================================
        // Lightweight Property Editing Support
        // ==========================================

        private class PropertyEditContext
        {
            public object Source { get; set; }
            public System.Reflection.PropertyInfo PropertyInfo { get; set; }
            public string OriginalValue { get; set; }
            public TextBlock DisplayTextBlock { get; set; }
            public Border ContainerBorder { get; set; }
            public FrameworkElement ActiveEditor { get; set; }
        }

        private void EditableValue_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var border = sender as Border;
            if (border == null || border.Tag is not PropertyEditContext ctx) return;
            if (ctx.ActiveEditor != null) return;

            // Hide TextBlock, show editor
            ctx.DisplayTextBlock.Visibility = Visibility.Collapsed;

            var editor = CreateEditorForProperty(ctx);
            ctx.ActiveEditor = editor;
            border.Child = editor;

            editor.Focus();
            if (editor is TextBox tb)
            {
                tb.SelectAll();
            }

            e.Handled = true;
        }

        private FrameworkElement CreateEditorForProperty(PropertyEditContext ctx)
        {
            var propType = ctx.PropertyInfo.PropertyType;
            var currentValue = ctx.PropertyInfo.GetValue(ctx.Source);

            if (propType == typeof(bool) || propType == typeof(bool?))
            {
                var combo = new ComboBox
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = new SolidColorBrush(Color.FromRgb(45, 45, 55)),
                    Foreground = (Brush)FindResource("MainTextColor"),
                    BorderBrush = (Brush)FindResource("HighlightBorderColor")
                };
                combo.Items.Add("true");
                combo.Items.Add("false");
                combo.Items.Add("（未设置）");

                if (currentValue is bool b)
                    combo.SelectedItem = b ? "true" : "false";
                else
                    combo.SelectedItem = "（未设置）";

                combo.LostFocus += Editor_LostFocus;
                combo.KeyDown += Editor_KeyDown;
                combo.Tag = ctx;
                return combo;
            }
            else if (propType == typeof(UnitFloat))
            {
                var uf = currentValue as UnitFloat;
                string editText = uf != null ? uf.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0";

                var tb = new TextBox
                {
                    Text = editText,
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = new SolidColorBrush(Color.FromRgb(45, 45, 55)),
                    Foreground = (Brush)FindResource("MainTextColor"),
                    BorderBrush = (Brush)FindResource("HighlightBorderColor"),
                    CaretBrush = (Brush)FindResource("MainTextColor")
                };
                tb.LostFocus += Editor_LostFocus;
                tb.KeyDown += Editor_KeyDown;
                tb.Tag = ctx;
                return tb;
            }
            else
            {
                // Default: TextBox for string, float, int, double, etc.
                string editText;
                if (currentValue is float f)
                    editText = f.ToString(System.Globalization.CultureInfo.InvariantCulture);
                else if (currentValue is double d)
                    editText = d.ToString(System.Globalization.CultureInfo.InvariantCulture);
                else if (currentValue is int i)
                    editText = i.ToString();
                else
                    editText = currentValue?.ToString() ?? "";

                var tb = new TextBox
                {
                    Text = editText,
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = new SolidColorBrush(Color.FromRgb(45, 45, 55)),
                    Foreground = (Brush)FindResource("MainTextColor"),
                    BorderBrush = (Brush)FindResource("HighlightBorderColor"),
                    CaretBrush = (Brush)FindResource("MainTextColor")
                };
                tb.LostFocus += Editor_LostFocus;
                tb.KeyDown += Editor_KeyDown;
                tb.Tag = ctx;
                return tb;
            }
        }

        private void Editor_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                CommitEdit(sender as FrameworkElement);
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                CancelEdit(sender as FrameworkElement);
                e.Handled = true;
            }
        }

        private void Editor_LostFocus(object sender, RoutedEventArgs e)
        {
            var editor = sender as FrameworkElement;
            if (editor == null) return;

            // Skip if the ComboBox dropdown is still open
            if (editor is ComboBox combo && combo.IsDropDownOpen)
                return;

            // Only commit if the editor is still visible (not already handled by KeyDown)
            if (editor.IsVisible)
            {
                CommitEdit(editor);
            }
        }

        private void CommitEdit(FrameworkElement editor)
        {
            if (editor == null || editor.Tag is not PropertyEditContext ctx) return;
            if (ctx.ActiveEditor == null) return;

            string newText = "";
            if (editor is TextBox tb)
                newText = tb.Text;
            else if (editor is ComboBox combo)
                newText = combo.SelectedItem?.ToString() ?? "";

            try
            {
                SetPropertyValue(ctx, newText);
            }
            catch
            {
                // If parsing fails, silently restore the original value
            }

            RestoreDisplay(ctx);
        }

        private void CancelEdit(FrameworkElement editor)
        {
            if (editor == null || editor.Tag is not PropertyEditContext ctx) return;
            RestoreDisplay(ctx);
        }

        private void RestoreDisplay(PropertyEditContext ctx)
        {
            ctx.ActiveEditor = null;
            ctx.DisplayTextBlock.Visibility = Visibility.Visible;
            ctx.ContainerBorder.Child = ctx.DisplayTextBlock;

            // Refresh the display text with the current property value
            var currentValue = ctx.PropertyInfo.GetValue(ctx.Source);
            if (currentValue is UnitFloat uf)
            {
                string unit = uf.Unit == ReferenceUnit.World ? "World" : uf.Unit.ToString();
                ctx.DisplayTextBlock.Text = $"{uf.Value} ({unit})";
            }
            else
            {
                ctx.DisplayTextBlock.Text = currentValue?.ToString() ?? "";
            }
        }

        private void SetPropertyValue(PropertyEditContext ctx, string text)
        {
            var propType = ctx.PropertyInfo.PropertyType;

            if (propType == typeof(bool) || propType == typeof(bool?))
            {
                if (text == "true")
                    ctx.PropertyInfo.SetValue(ctx.Source, true);
                else if (text == "false")
                    ctx.PropertyInfo.SetValue(ctx.Source, false);
                else
                    ctx.PropertyInfo.SetValue(ctx.Source, null);
            }
            else if (propType == typeof(UnitFloat))
            {
                if (float.TryParse(text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float fVal))
                {
                    var existing = ctx.PropertyInfo.GetValue(ctx.Source) as UnitFloat;
                    if (existing != null)
                    {
                        existing.Value = fVal;
                    }
                    else
                    {
                        ctx.PropertyInfo.SetValue(ctx.Source, new UnitFloat { Value = fVal });
                    }
                }
            }
            else if (propType == typeof(int) || propType == typeof(int?))
            {
                if (string.IsNullOrWhiteSpace(text))
                    ctx.PropertyInfo.SetValue(ctx.Source, null);
                else if (int.TryParse(text, out int iVal))
                    ctx.PropertyInfo.SetValue(ctx.Source, iVal);
            }
            else if (propType == typeof(float) || propType == typeof(float?))
            {
                if (string.IsNullOrWhiteSpace(text))
                    ctx.PropertyInfo.SetValue(ctx.Source, null);
                else if (float.TryParse(text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float fVal))
                    ctx.PropertyInfo.SetValue(ctx.Source, fVal);
            }
            else if (propType == typeof(double) || propType == typeof(double?))
            {
                if (string.IsNullOrWhiteSpace(text))
                    ctx.PropertyInfo.SetValue(ctx.Source, null);
                else if (double.TryParse(text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double dVal))
                    ctx.PropertyInfo.SetValue(ctx.Source, dVal);
            }
            else if (propType == typeof(string))
            {
                ctx.PropertyInfo.SetValue(ctx.Source, text);
            }
            else
            {
                // Fallback: set as string
                ctx.PropertyInfo.SetValue(ctx.Source, text);
            }
        }

        private void AddSectionHeader(string title)
        {
            var header = new TextBlock
            {
                Text = title,
                Foreground = (Brush)FindResource("HighlightBorderColor"),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 12, 0, 4),
                FontSize = 13
            };
            PropertyContainer.Children.Add(header);
        }

        private void BuildSpriteForm(C2Sprite sprite)
        {
            AddSectionHeader("🖼️ 图片属性 (Sprite)");
            AddPropertyRow("唯一ID", sprite.Id ?? "（无）", sprite, "Id");
            var state = sprite.BaseState;
            if (state != null)
            {
                AddPropertyRow("素材路径", state.Path ?? "（未设置）", state, "Path");
                AddPropertyRow("不透明度", state.Opacity?.ToString() ?? "1.0", state, "Opacity");
                AddPropertyRow("图层(Layer)", state.Layer?.ToString() ?? "0", state, "Layer");
                AddPropertyRow("排序(Order)", state.Order?.ToString() ?? "0", state, "Order");
                AddPropertyRow("X 坐标", FormatUnitFloat(state.X), state, "X");
                AddPropertyRow("Y 坐标", FormatUnitFloat(state.Y), state, "Y");
                AddPropertyRow("Z 坐标", FormatUnitFloat(state.Z), state, "Z");
                AddPropertyRow("宽度 (W)", FormatUnitFloat(state.W), state, "W");
                AddPropertyRow("高度 (H)", FormatUnitFloat(state.H), state, "H");
                AddPropertyRow("保持宽高比", state.PreserveAspect?.ToString() ?? "未设置", state, "PreserveAspect");
                AddPropertyRow("颜色覆写", state.Color ?? "默认", state, "Color");
            }
        }

        private void BuildTextForm(C2Text text)
        {
            AddSectionHeader("📝 文字属性 (Text)");
            AddPropertyRow("唯一ID", text.Id ?? "（无）", text, "Id");
            var state = text.BaseState;
            if (state != null)
            {
                AddPropertyRow("文本内容", state.TextContent ?? "（空）", state, "TextContent");
                AddPropertyRow("字号大小", state.Size?.ToString() ?? "默认", state, "Size");
                AddPropertyRow("字体种类", state.Font ?? "默认", state, "Font");
                AddPropertyRow("不透明度", state.Opacity?.ToString() ?? "1.0", state, "Opacity");
                AddPropertyRow("X 坐标", FormatUnitFloat(state.X), state, "X");
                AddPropertyRow("Y 坐标", FormatUnitFloat(state.Y), state, "Y");
                AddPropertyRow("颜色", state.Color ?? "默认", state, "Color");
            }
        }

        private void BuildLineForm(C2Line line)
        {
            AddSectionHeader("〰️ 线条属性 (Line)");
            AddPropertyRow("唯一ID", line.Id ?? "（无）", line, "Id");
            var state = line.BaseState;
            if (state != null)
            {
                AddPropertyRow("线段宽度", state.Width?.ToString() ?? "默认", state, "Width");
                AddPropertyRow("不透明度", state.Opacity?.ToString() ?? "1.0", state, "Opacity");
                AddPropertyRow("线条颜色", state.Color ?? "默认", state, "Color");

                // 多端点全自动点兵雷达（完美消灭编译报错！）

                if (state.Pos != null && state.Pos.Count > 0)
                {
                    for (int i = 0; i < state.Pos.Count; i++)
                    {
                        var point = state.Pos[i];

                        // 自动为每一个顶点编上卡哇伊的序号，如“顶点 1 X”、“顶点 2 Y”
                        AddPropertyRow($"顶点 {i + 1} X", FormatUnitFloat(point.X));
                        AddPropertyRow($"顶点 {i + 1} Y", FormatUnitFloat(point.Y));

                        // 🛡️ 防灾机制：如果谱面开启了3D深度，有 Z 轴数据才显示，否则默默隐藏
                        if (point.Z != null && point.Z.Value != 0)
                        {
                            AddPropertyRow($"顶点 {i + 1} Z", FormatUnitFloat(point.Z));
                        }
                    }
                }
                else
                {
                    AddPropertyRow("〰️ 线条状态", "当前未包含任何有效顶点坐标");
                }
            }
        }

        private void BuildVideoForm(C2Video video)
        {
            AddSectionHeader("🎬 视频属性 (Video)");
            AddPropertyRow("唯一ID", video.Id ?? "（无）", video, "Id");
            var state = video.BaseState;
            if (state != null)
            {
                AddPropertyRow("视频路径", state.Path ?? "（未设置）", state, "Path");
                AddPropertyRow("播放速度", state.Speed?.ToString() ?? "1.0", state, "Speed");
                AddPropertyRow("循环播放", state.Loop?.ToString() ?? "false", state, "Loop");
                AddPropertyRow("不透明度", state.Opacity?.ToString() ?? "1.0", state, "Opacity");
                AddPropertyRow("宽度 (W)", FormatUnitFloat(state.W), state, "W");
                AddPropertyRow("高度 (H)", FormatUnitFloat(state.H), state, "H");
            }
        }

        private void BuildControllerForm(C2SceneController controller)
        {
            AddSectionHeader("🎛️ 场景控制器 (Scene)");
            AddPropertyRow("唯一ID", controller.Id ?? "（无）", controller, "Id");
            var state = controller.BaseState;
            if (state != null)
            {
                AddPropertyRow("总板不透明度", state.StoryboardOpacity?.ToString() ?? "1.0", state, "StoryboardOpacity");
                AddPropertyRow("核心UI不透明度", state.UiOpacity?.ToString() ?? "1.0", state, "UiOpacity");
                AddPropertyRow("扫描线不透明度", state.ScanlineOpacity?.ToString() ?? "1.0", state, "ScanlineOpacity");
                AddPropertyRow("背景暗化遮罩", state.BackgroundDim?.ToString() ?? "0.85", state, "BackgroundDim");
                AddPropertyRow("音符透明乘区", state.NoteOpacityMultiplier?.ToString() ?? "1.0", state, "NoteOpacityMultiplier");
                AddPropertyRow("3D相机的透视", state.Perspective?.ToString() ?? "true", state, "Perspective");
                AddPropertyRow("FOV视野角度", state.Fov?.ToString() ?? "53.2", state, "Fov");
                AddPropertyRow("故障滤镜(Glitch)", state.Glitch?.ToString() ?? "false", state, "Glitch");
                AddPropertyRow("街机滤镜(Arcade)", state.Arcade?.ToString() ?? "false", state, "Arcade");
                AddPropertyRow("色差干扰(Chrom)", state.Chromatical?.ToString() ?? "false", state, "Chromatical");
            }
        }

        private void BuildNoteControllerForm(C2NoteController noteCtrl)
        {
            AddSectionHeader("🎵 音符控制器 (Note)");
            AddPropertyRow("唯一ID", noteCtrl.Id ?? "（无）", noteCtrl, "Id");
            var state = noteCtrl.BaseState;
            if (state != null)
            {
                AddPropertyRow("绑定音符ID", state.NoteTarget?.ToString() ?? "（未绑定）", state, "NoteTarget");
                AddPropertyRow("覆写 X 坐标", state.OverrideX?.ToString() ?? "false", state, "OverrideX");
                AddPropertyRow("X 坐标轴", FormatUnitFloat(state.X), state, "X");
                AddPropertyRow("覆写 Y 坐标", state.OverrideY?.ToString() ?? "false", state, "OverrideY");
                AddPropertyRow("Y 坐标轴", FormatUnitFloat(state.Y), state, "Y");
                AddPropertyRow("大小缩放乘区", state.NoteSizeMultiplier?.ToString() ?? "1.0", state, "NoteSizeMultiplier");
                AddPropertyRow("透明度缩放乘区", state.NoteOpacityMultiplier?.ToString() ?? "1.0", state, "NoteOpacityMultiplier");
            }
        }

        private void BuildTemplateForm(C2Template template)
        {
            AddSectionHeader("📦 动画印章模板 (Template)");
            AddPropertyRow("唯一ID", template.Id ?? "（无）", template, "Id");
            AddPropertyRow("子关键帧数量", template.Keyframes?.Count.ToString() ?? "0");

            var state = template.BaseState;
            if (state == null) return;

            var props = state.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            foreach (var prop in props)
            {
                if (prop.Name == "Time" || prop.Name == "Easing" || prop.Name == "Template") continue;

                object val = prop.GetValue(state);
                if (val != null)
                {
                    string displayVal = val is UnitFloat uf ? FormatUnitFloat(uf) : val.ToString();
                    AddPropertyRow(prop.Name, displayVal, state, prop.Name);
                }
            }
        }

        private string FormatUnitFloat(UnitFloat uf)
        {
            if (uf == null) return "0 (World)";
            string unit = uf.Unit == ReferenceUnit.World ? "World" : uf.Unit.ToString();
            return $"{uf.Value} ({unit})";
        }

        private void BtnEditProperties_Click(object sender, RoutedEventArgs e)
        {
            if (_currentObject != null)
            {
                // 📢 呼叫主战舰！我这里有个对象需要打开高级属性编辑器啦！
                // 无论是普通对象还是模板，主窗口的频道 2 都已经写好了自动分拣逻辑，直接把对象丢过去就行！
                _messageBroker.Publish("RequestOpenPropertyEditor", _currentObject);
            }
        }

        private void BtnSaveAsMaterial_Click(object sender, RoutedEventArgs e)
        {
            if (_currentObject != null)
                OnSaveAsMaterialRequested?.Invoke(_currentObject);
        }
    }
}