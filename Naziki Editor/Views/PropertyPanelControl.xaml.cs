using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Naziki_Editor.Models;

namespace Naziki_Editor.Views
{
    public partial class PropertyPanelControl : UserControl
    {
        // 事件：请求编辑属性（携带当前选中对象）
        public event Action<object> OnEditPropertiesRequested;
        // 事件：请求另存为素材
        public event Action<object> OnSaveAsMaterialRequested;
        // 新增：请求直接应用属性修改（不需要弹出对话框）
        public event Action OnApplyPropertiesRequested;
        // 新增：数据被修改（属性值发生变化）时触发
        public event Action OnDataModified;

        private object _currentObject;

        public PropertyPanelControl()
        {
            InitializeComponent();
        }

        // 外部调用：设置要显示的对象
        public void SetSelectedObject(object obj)
        {
            _currentObject = obj;
            RefreshPropertyDisplay();
        }

        // 刷新属性表单
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

            // 根据对象类型调用不同的构建方法
            switch (_currentObject)
            {
                case Sprite sprite:
                    BuildSpriteForm(sprite);
                    break;
                case Text text:
                    BuildTextForm(text);
                    break;
                case Line line:
                    BuildLineForm(line);
                    break;
                case Video video:
                    BuildVideoForm(video);
                    break;
                case Controller controller:
                    BuildControllerForm(controller);
                    break;
                case NoteController noteCtrl:
                    BuildNoteControllerForm(noteCtrl);
                    break;
                case StoryboardTemplate template:
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

        // ========== 辅助方法 ==========
        private void AddPropertyRow(string label, string value)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Margin = new Thickness(0, 5, 0, 5);

            var labelBlock = new TextBlock
            {
                Text = label,
                Foreground = (Brush)FindResource("SecTextColor"),
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.Bold
            };
            Grid.SetColumn(labelBlock, 0);
            grid.Children.Add(labelBlock);

            var valueBlock = new TextBlock
            {
                Text = value,
                Foreground = (Brush)FindResource("MainTextColor"),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(valueBlock, 1);
            grid.Children.Add(valueBlock);

            PropertyContainer.Children.Add(grid);
        }

        private void AddSectionHeader(string title)
        {
            var header = new TextBlock
            {
                Text = title,
                Foreground = (Brush)FindResource("HighlightBorderColor"),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 15, 0, 5),
                FontSize = 13
            };
            PropertyContainer.Children.Add(header);
        }

        // ========== 各对象表单构建 ==========
        private void BuildSpriteForm(Sprite sprite)
        {
            AddSectionHeader("🖼️ 图片属性");
            AddPropertyRow("ID", sprite.Id ?? "（无）");
            if (sprite.States != null && sprite.States.Count > 0)
            {
                var state = sprite.States[0];
                AddPropertyRow("素材路径", state.Path ?? "（未设置）");
                AddPropertyRow("出现时间", state.Time?.ToString() ?? "0");
                AddPropertyRow("不透明度", state.Opacity?.ToString() ?? "1");
                AddPropertyRow("X坐标", FormatUnitFloat(state.X));
                AddPropertyRow("Y坐标", FormatUnitFloat(state.Y));
                AddPropertyRow("Z层级", state.Z?.ToString() ?? "0");
                AddPropertyRow("X缩放", state.ScaleX?.ToString() ?? "1");
                AddPropertyRow("Y缩放", state.ScaleY?.ToString() ?? "1");
                AddPropertyRow("X旋转", state.RotX?.ToString() ?? "0");
                AddPropertyRow("Y旋转", state.RotY?.ToString() ?? "0");
                AddPropertyRow("Z旋转", state.RotZ?.ToString() ?? "0");
                AddPropertyRow("X锚点", state.PivotX?.ToString() ?? "0");
                AddPropertyRow("Y锚点", state.PivotY?.ToString() ?? "0");
                AddPropertyRow("颜色", FormatColor(state.Color));
            }
            else
            {
                AddPropertyRow("提示", "无状态数据");
            }
        }

        private void BuildTextForm(Text text)
        {
            AddSectionHeader("📝 文字属性");
            AddPropertyRow("ID", text.Id ?? "（无）");
            if (text.States != null && text.States.Count > 0)
            {
                var state = text.States[0];
                AddPropertyRow("文字内容", state.Text ?? "（空）");
                AddPropertyRow("出现时间", state.Time?.ToString() ?? "0");
                AddPropertyRow("不透明度", state.Opacity?.ToString() ?? "1");
                AddPropertyRow("字号", state.Size?.ToString() ?? "默认");
                AddPropertyRow("对齐", state.Align ?? "Center");
                AddPropertyRow("X坐标", FormatUnitFloat(state.X));
                AddPropertyRow("Y坐标", FormatUnitFloat(state.Y));
                AddPropertyRow("Z层级", state.Z?.ToString() ?? "0");
                AddPropertyRow("X缩放", state.ScaleX?.ToString() ?? "1");
                AddPropertyRow("Y缩放", state.ScaleY?.ToString() ?? "1");
                AddPropertyRow("X旋转", state.RotX?.ToString() ?? "0");
                AddPropertyRow("Y旋转", state.RotY?.ToString() ?? "0");
                AddPropertyRow("Z旋转", state.RotZ?.ToString() ?? "0");
                AddPropertyRow("X锚点", state.PivotX?.ToString() ?? "0");
                AddPropertyRow("Y锚点", state.PivotY?.ToString() ?? "0");
                AddPropertyRow("颜色", FormatColor(state.Color));
            }
            else
            {
                AddPropertyRow("提示", "无状态数据");
            }
        }

        private void BuildLineForm(Line line)
        {
            AddSectionHeader("〰️ 线条属性");
            AddPropertyRow("ID", line.Id ?? "（无）");
            if (line.States != null && line.States.Count > 0)
            {
                var state = line.States[0];
                AddPropertyRow("出现时间", state.Time?.ToString() ?? "0");
                AddPropertyRow("线宽", FormatUnitFloat(state.Width));
                AddPropertyRow("不透明度", state.Opacity?.ToString() ?? "1");
                AddPropertyRow("颜色", FormatColor(state.Color));
                AddPropertyRow("X坐标", FormatUnitFloat(state.X));
                AddPropertyRow("Y坐标", FormatUnitFloat(state.Y));
                AddPropertyRow("Z层级", state.Z?.ToString() ?? "0");
                AddPropertyRow("端点数量", state.Pos?.Count.ToString() ?? "0");
            }
            else
            {
                AddPropertyRow("提示", "无状态数据");
            }
        }

        private void BuildVideoForm(Video video)
        {
            AddSectionHeader("🎬 视频属性");
            AddPropertyRow("ID", video.Id ?? "（无）");
            if (video.States != null && video.States.Count > 0)
            {
                var state = video.States[0];
                AddPropertyRow("视频路径", state.Path ?? "（未设置）");
                AddPropertyRow("出现时间", state.Time?.ToString() ?? "0");
                AddPropertyRow("不透明度", state.Opacity?.ToString() ?? "1");
                AddPropertyRow("X坐标", FormatUnitFloat(state.X));
                AddPropertyRow("Y坐标", FormatUnitFloat(state.Y));
                AddPropertyRow("Z层级", state.Z?.ToString() ?? "0");
                AddPropertyRow("宽度", FormatUnitFloat(state.Width));
                AddPropertyRow("高度", FormatUnitFloat(state.Height));
                AddPropertyRow("颜色", FormatColor(state.Color));
            }
            else
            {
                AddPropertyRow("提示", "无状态数据");
            }
        }

        private void BuildControllerForm(Controller controller)
        {
            AddSectionHeader("🎛️ 场景控制器");
            AddPropertyRow("ID", controller.Id ?? "（无）");
            if (controller.States != null && controller.States.Count > 0)
            {
                var state = controller.States[0];
                AddPropertyRow("触发时间", state.Time?.ToString() ?? "0");
                AddPropertyRow("Arcade模式", state.Arcade?.ToString() ?? "未设置");
                AddPropertyRow("背景暗化", state.BackgroundDim?.ToString() ?? "未设置");
                AddPropertyRow("UI透明度", state.UiOpacity?.ToString() ?? "未设置");
                AddPropertyRow("故事板透明度", state.StoryboardOpacity?.ToString() ?? "未设置");
            }
            else
            {
                AddPropertyRow("提示", "无状态数据");
            }
        }

        private void BuildNoteControllerForm(NoteController noteCtrl)
        {
            AddSectionHeader("🎵 音符控制器");
            AddPropertyRow("ID", noteCtrl.Id ?? "（无）");
            AddPropertyRow("绑定目标", noteCtrl.NoteTarget?.ToString() ?? "（未绑定）");
            if (noteCtrl.States != null && noteCtrl.States.Count > 0)
            {
                var state = noteCtrl.States[0];
                AddPropertyRow("触发时间", state.Time?.ToString() ?? "0");
                AddPropertyRow("覆盖X坐标", state.OverrideX?.ToString() ?? "未设置");
                AddPropertyRow("X坐标", FormatUnitFloat(state.X));
                AddPropertyRow("X偏移", state.XOffset?.ToString() ?? "0");
                AddPropertyRow("X倍率", state.XMultiplier?.ToString() ?? "1");
                AddPropertyRow("大小倍率", state.SizeMultiplier?.ToString() ?? "1");
                AddPropertyRow("不透明度倍率", state.OpacityMultiplier?.ToString() ?? "1");
            }
            else
            {
                AddPropertyRow("提示", "无状态数据");
            }
        }

        private void BuildTemplateForm(StoryboardTemplate template)
        {
            AddSectionHeader("📦 模板组");
            AddPropertyRow("图片数量", template.sprites?.Count.ToString() ?? "0");
            AddPropertyRow("文字数量", template.texts?.Count.ToString() ?? "0");
            AddPropertyRow("线条数量", template.lines?.Count.ToString() ?? "0");
            AddPropertyRow("视频数量", template.videos?.Count.ToString() ?? "0");
            AddPropertyRow("场景控制数量", template.controllers?.Count.ToString() ?? "0");
            AddPropertyRow("音符控制数量", template.note_controllers?.Count.ToString() ?? "0");
        }

        // ========== 格式化辅助方法 ==========
        private string FormatUnitFloat(UnitFloat uf)
        {
            if (uf == null) return "0 (World)";
            string unit = uf.Unit == ReferenceUnit.World ? "World" : uf.Unit.ToString();
            return $"{uf.Value} ({unit})";
        }

        private string FormatColor(CytoidColor color)
        {
            if (color == null) return "默认白色";
            return $"R:{color.R} G:{color.G} B:{color.B} A:{color.A}";
        }

        // ========== 按钮事件 ==========
        private void BtnEditProperties_Click(object sender, RoutedEventArgs e)
        {
            if (_currentObject is StoryboardObject selectedObj)
            {
                if (Application.Current.MainWindow is MainWindow main)
                {
                    main.OpenPropertyEditor(selectedObj);
                }
            }
        }

        private void BtnSaveAsMaterial_Click(object sender, RoutedEventArgs e)
        {
            if (_currentObject != null)
                OnSaveAsMaterialRequested?.Invoke(_currentObject);
        }

        // 假设面板里有一个私有变量 _currentObject 存着当前显示的对象

        // 假设面板里有一个私有变量 _currentObject 存着当前显示的对象
        
    }
}