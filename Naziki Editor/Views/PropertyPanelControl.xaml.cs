using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Naziki_Editor.Models;
using Naziki_Editor.State;

namespace Naziki_Editor.Views
{
    public partial class PropertyPanelControl : UserControl
    {
        public event Action<object> OnEditPropertiesRequested;
        public event Action<object> OnSaveAsMaterialRequested;
        public event Action OnApplyPropertiesRequested;
        public event Action OnDataModified;

        private object _currentObject;
        public ProjectDataContext Context { get; private set; }

        public void LoadContext(ProjectDataContext context)
        {
            Context = context;
        }

        public PropertyPanelControl()
        {
            InitializeComponent();
        }

        public void SetSelectedObject(object obj)
        {
            _currentObject = obj;
            RefreshPropertyDisplay();
        }

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

        private void AddPropertyRow(string label, string value)
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
                Margin = new Thickness(0, 12, 0, 4),
                FontSize = 13
            };
            PropertyContainer.Children.Add(header);
        }

        private void BuildSpriteForm(C2Sprite sprite)
        {
            AddSectionHeader("🖼️ 图片属性 (Sprite)");
            AddPropertyRow("唯一ID", sprite.Id ?? "（无）");
            var state = sprite.BaseState;
            if (state != null)
            {
                AddPropertyRow("素材路径", state.Path ?? "（未设置）");
                AddPropertyRow("不透明度", state.Opacity?.ToString() ?? "1.0");
                AddPropertyRow("图层(Layer)", state.Layer?.ToString() ?? "0");
                AddPropertyRow("排序(Order)", state.Order?.ToString() ?? "0");
                AddPropertyRow("X 坐标", FormatUnitFloat(state.X));
                AddPropertyRow("Y 坐标", FormatUnitFloat(state.Y));
                AddPropertyRow("Z 坐标", FormatUnitFloat(state.Z));
                AddPropertyRow("宽度 (W)", FormatUnitFloat(state.W));
                AddPropertyRow("高度 (H)", FormatUnitFloat(state.H));
                AddPropertyRow("保持宽高比", state.PreserveAspect?.ToString() ?? "未设置");
                AddPropertyRow("颜色覆写", state.Color ?? "默认");
            }
        }

        private void BuildTextForm(C2Text text)
        {
            AddSectionHeader("📝 文字属性 (Text)");
            AddPropertyRow("唯一ID", text.Id ?? "（无）");
            var state = text.BaseState;
            if (state != null)
            {
                AddPropertyRow("文本内容", state.TextContent ?? "（空）");
                AddPropertyRow("字号大小", state.Size?.ToString() ?? "默认");
                AddPropertyRow("字体种类", state.Font ?? "默认");
                AddPropertyRow("不透明度", state.Opacity?.ToString() ?? "1.0");
                AddPropertyRow("X 坐标", FormatUnitFloat(state.X));
                AddPropertyRow("Y 坐标", FormatUnitFloat(state.Y));
                AddPropertyRow("颜色", state.Color ?? "默认");
            }
        }

        private void BuildLineForm(C2Line line)
        {
            AddSectionHeader("〰️ 线条属性 (Line)");
            AddPropertyRow("唯一ID", line.Id ?? "（无）");
            var state = line.BaseState;
            if (state != null)
            {
                AddPropertyRow("线段宽度", state.Width?.ToString() ?? "默认");
                AddPropertyRow("不透明度", state.Opacity?.ToString() ?? "1.0");
                AddPropertyRow("线条颜色", state.Color ?? "默认");

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
            AddPropertyRow("唯一ID", video.Id ?? "（无）");
            var state = video.BaseState;
            if (state != null)
            {
                AddPropertyRow("视频路径", state.Path ?? "（未设置）");
                AddPropertyRow("播放速度", state.Speed?.ToString() ?? "1.0");
                AddPropertyRow("循环播放", state.Loop?.ToString() ?? "false");
                AddPropertyRow("不透明度", state.Opacity?.ToString() ?? "1.0");
                AddPropertyRow("宽度 (W)", FormatUnitFloat(state.W));
                AddPropertyRow("高度 (H)", FormatUnitFloat(state.H));
            }
        }

        private void BuildControllerForm(C2SceneController controller)
        {
            AddSectionHeader("🎛️ 场景控制器 (Scene)");
            AddPropertyRow("唯一ID", controller.Id ?? "（无）");
            var state = controller.BaseState;
            if (state != null)
            {
                AddPropertyRow("总板不透明度", state.StoryboardOpacity?.ToString() ?? "1.0");
                AddPropertyRow("核心UI不透明度", state.UiOpacity?.ToString() ?? "1.0");
                AddPropertyRow("扫描线不透明度", state.ScanlineOpacity?.ToString() ?? "1.0");
                AddPropertyRow("背景暗化遮罩", state.BackgroundDim?.ToString() ?? "0.85");
                AddPropertyRow("音符透明乘区", state.NoteOpacityMultiplier?.ToString() ?? "1.0");
                AddPropertyRow("3D相机的透视", state.Perspective?.ToString() ?? "true");
                AddPropertyRow("FOV视野角度", state.Fov?.ToString() ?? "53.2");
                AddPropertyRow("故障滤镜(Glitch)", state.Glitch?.ToString() ?? "false");
                AddPropertyRow("街机滤镜(Arcade)", state.Arcade?.ToString() ?? "false");
                AddPropertyRow("色差干扰(Chrom)", state.Chromatical?.ToString() ?? "false");
            }
        }

        private void BuildNoteControllerForm(C2NoteController noteCtrl)
        {
            AddSectionHeader("🎵 音符控制器 (Note)");
            AddPropertyRow("唯一ID", noteCtrl.Id ?? "（无）");
            var state = noteCtrl.BaseState;
            if (state != null)
            {
                AddPropertyRow("绑定音符ID", state.NoteTarget?.ToString() ?? "（未绑定）");
                AddPropertyRow("覆写 X 坐标", state.OverrideX?.ToString() ?? "false");
                AddPropertyRow("X 坐标轴", FormatUnitFloat(state.X));
                AddPropertyRow("覆写 Y 坐标", state.OverrideY?.ToString() ?? "false");
                AddPropertyRow("Y 坐标轴", FormatUnitFloat(state.Y));
                AddPropertyRow("大小缩放乘区", state.NoteSizeMultiplier?.ToString() ?? "1.0");
                AddPropertyRow("透明度缩放乘区", state.NoteOpacityMultiplier?.ToString() ?? "1.0");
            }
        }

        private void BuildTemplateForm(C2Template template)
        {
            AddSectionHeader("📦 动画印章模板 (Template)");
            AddPropertyRow("唯一ID", template.Id ?? "（无）");
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
                    AddPropertyRow(prop.Name, displayVal);
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
            if (Window.GetWindow(this) is MainWindow main)
            {
                // ✨ 采用 dynamic 幻影法术！彻底切断编译期硬核类型检查，让 PropertyPanelControl 顺利通关！
                if (_currentObject is IStoryboardEntity selectedObj)
                {
                    ((dynamic)main).OpenPropertyEditor(selectedObj);
                }
                else if (_currentObject is C2Template template)
                {
                    var targetEntry = main.Context.Storyboard.templates.FirstOrDefault(x => x.Value == template);
                    if (targetEntry.Key != null)
                    {
                        ((dynamic)main).OpenTemplatePropertyEditor(targetEntry.Key, template);
                    }
                }
            }
        }

        private void BtnSaveAsMaterial_Click(object sender, RoutedEventArgs e)
        {
            if (_currentObject != null)
                OnSaveAsMaterialRequested?.Invoke(_currentObject);
        }
    }
}