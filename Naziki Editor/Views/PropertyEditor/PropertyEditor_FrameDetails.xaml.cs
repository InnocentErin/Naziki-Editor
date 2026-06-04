using Naziki_Editor.Core;
using Naziki_Editor.Models;
using Naziki_Editor.State;
using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using static Naziki_Editor.Views.PropertyEditor.TimeBindingConverter;

namespace Naziki_Editor.Views.PropertyEditor
{





    // ==========================================
    // ✨ 专属翻译官：防止 Time 数组或空值转义成乱码
    // ==========================================
    public class TimeBindingConverter : IValueConverter
    {
        // ==========================================
        // ✨ 专属翻译官 2 号：完美解析 System.Object 和 复杂数组！
        // ==========================================
        public class UniversalObjectConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                if (value == null) return "";
                // 如果是普通的数字或纯字符串，直接打印
                if (value is string || value is int || value is float || value is double) return value.ToString();

                // 🎯【破解 System.Object】：如果是复杂的 JSON 选择器或 Pos 数组，把它序列化成漂亮的 JSON 字符串展示！
                try { return Newtonsoft.Json.JsonConvert.SerializeObject(value, Newtonsoft.Json.Formatting.None); }
                catch { return value.ToString(); }
            }

            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                string s = value?.ToString()?.Trim() ?? "";
                if (string.IsNullOrEmpty(s)) return null;

                // 如果用户手打了 JSON 括号，智能反序列化为对象！
                if (s.StartsWith("{") || s.StartsWith("["))
                {
                    try { return Newtonsoft.Json.JsonConvert.DeserializeObject(s); } catch { return s; }
                }
                // 如果手打的是纯数字，智能转化回 int
                if (int.TryParse(s, out int iVal)) return iVal;

                // 兜底：纯字符串（例如 "$note"）
                return s;
            }
        }




        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is System.Collections.IList list)
            {
                var strList = new System.Collections.Generic.List<string>();
                foreach (var item in list) strList.Add(item.ToString());
                return string.Join(", ", strList);
            }
            string strVal = value?.ToString() ?? "";
            if (strVal.Contains("3.402823")) return "";
            return strVal;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            string s = value?.ToString() ?? "";
            if (s.Contains(","))
            {
                var parts = s.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                var list = new System.Collections.Generic.List<object>();
                foreach (var p in parts)
                {
                    string t = p.Trim();
                    if (float.TryParse(t, out float f)) list.Add(f); else list.Add(t);
                }
                return list;
            }
            if (float.TryParse(s, out float fSingle)) return fSingle;
            return s;
        }
    }

    public partial class PropertyEditor_FrameDetails : UserControl
    {
        private object _currentState;
        private string _currentTitle;
        private object _rootState;
        private bool _isRoot;
        private bool _isControlBoard = false; // ✨ 新增：高阶属性控制板检测锁

        private Naziki_Editor.State.ProjectDataContext _context;

        private System.Collections.Generic.Dictionary<string, C2Template> _globalTemplates;
        private HashSet<string> _invalidProperties = new HashSet<string>();

        public void InitTemplates(System.Collections.Generic.Dictionary<string, C2Template> templates)
        {
            _globalTemplates = templates;
        }

        public PropertyEditor_FrameDetails()
        {
            InitializeComponent();
        }

        // ==========================================
        // 📥 终极修正入口：精准对接新世界时空
        // ==========================================
        public void LoadState(object stateReference, string frameTitle, object rootState, bool isRoot, ProjectDataContext context)
        {
            _context = context;
            _currentState = stateReference;
            _rootState = rootState;
            _isRoot = isRoot;
            _currentTitle = frameTitle;

            if (_currentState == null)
            {
                PanelDetails.Visibility = Visibility.Collapsed;
                TxtEmptyState.Visibility = Visibility.Visible;
                return;
            }

            PanelDetails.Visibility = Visibility.Visible;
            TxtEmptyState.Visibility = Visibility.Collapsed;
            TxtFrameTitle.Text = $"当前选中 ➡️ {frameTitle}";
            // PopulateTemplateDropdown(); 顶部已隐身，不再重复刷新它

            // 🚀 核心强打通：彻底废除一刀切的物理屏蔽屏障！
            // 因为 TemplateState 作为上帝类，必须能同时渲染 Scene 和 Controller 两大面板。
            // 且就算是普通的 Controller，也需要用到 Scene 面板里的 X、Y、Z 坐标，统统放开权限！
            PanelSceneCards.Visibility = Visibility.Visible;
            PanelControllerCards.Visibility = Visibility.Visible;

            // ✨ 核心强打通：绑定常驻时间轴参数
            // BindStaticProperty(TxtEasing, "Easing"); // 🚫 彻底抛弃旧的文本框数据绑定！

            // 1. 让原先写死在 XAML 里的旧输入框全部隐身退场，防止双向数据打架！
            TxtTime.Visibility = Visibility.Collapsed;
            PanelStateTimeOptions.Visibility = Visibility.Collapsed;
            TxtEasing.Visibility = Visibility.Collapsed; // 🌟 让原有的干瘪缓动输入框永久隐身！

            // 🌟 1.5【魔法降临】：挂载全新的“缓动视觉矩阵触发按钮”！
            // 先拔掉上一次残留的老按钮，防止切换关键帧时无限套娃
            UIElement oldEasingPicker = null;
            foreach (UIElement child in RowEasing.Children)
            {
                if (child is EasingPickerControl) { oldEasingPicker = child; break; }
            }
            if (oldEasingPicker != null) RowEasing.Children.Remove(oldEasingPicker);

            // 用反射抓取当前帧的 Easing 属性，生成魔法按钮并塞进 RowEasing 容器！
            var easingProp = _currentState.GetType().GetProperty("Easing");
            if (easingProp != null)
            {
                // 🌟 架构级数据分流：若为后续帧，绑定目标切换为初始属性 (_rootState) 以实现“纯显示继承”！
                object targetState = _isRoot ? _currentState : _rootState;
                var targetProp = targetState?.GetType().GetProperty("Easing") ?? easingProp;

                var easingPicker = new EasingPickerControl(targetProp, targetState, AttachValidationProbe);

                // 🔒 置灰控制：只有初始核心属性允许编辑，后续状态一律变灰锁死，绝对不写盘！
                easingPicker.IsEnabled = _isRoot;

                // 🏗️ 建立纵向排版包厢，确保小提示能够完美地顶在按钮的正下方
                StackPanel easingLayoutGroup = new StackPanel { Orientation = Orientation.Vertical };
                easingLayoutGroup.Children.Add(easingPicker);

                if (!_isRoot)
                {
                    easingLayoutGroup.Children.Add(new TextBlock
                    {
                        Text = "⚠ 仅初始属性才可编辑缓动类型，状态内不允许编辑！",
                        Foreground = Brushes.Gray,
                        FontSize = 10,
                        Margin = new Thickness(0, 4, 0, 0),
                        FontStyle = FontStyles.Italic,
                        HorizontalAlignment = HorizontalAlignment.Left
                    });
                }

                Grid.SetColumn(easingLayoutGroup, 1); // 放在容器的右侧列
                RowEasing.Children.Add(easingLayoutGroup);
            }

            // 2. 拔掉上一次残留的老控件，防止切换帧时在界面上无限堆叠套娃
            UIElement expiredCtrl = null;
            foreach (UIElement child in RowTime.Children)
            {
                if (child is StoryboardTimeControl) { expiredCtrl = child; break; }
            }
            if (expiredCtrl != null) RowTime.Children.Remove(expiredCtrl);

            // 3. 动态降临最新科技：完全体时间锚点控制中心！
            // 传递当前帧 _currentState、是否为首帧 _isRoot、以及全局大管家 _context！
            var timeCenterCtrl = new StoryboardTimeControl(_currentState, _isRoot, _context);

            // 塞进原有的 RowTime 网格的右侧大阵营（Grid.Column=1）！
            Grid.SetColumn(timeCenterCtrl, 1);
            RowTime.Children.Add(timeCenterCtrl);

            // 🌟 VIP 常驻特区数据接线 (Layer / Order)
            if (_currentState.GetType().GetProperty("Layer") != null)
            {
                Binding bLayer = new Binding("Layer") { Source = _currentState, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged };
                TxtLayer.SetBinding(TextBox.TextProperty, bLayer);
            }

            if (_currentState.GetType().GetProperty("Order") != null)
            {
                Binding bOrder = new Binding("Order") { Source = _currentState, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged };
                TxtOrder.SetBinding(TextBox.TextProperty, bOrder);
            }

            // 🔒 结界生效：如果不是主干初始帧，绝不允许修改图层秩序！(时空禁入规则)
            TxtLayer.IsEnabled = _isRoot;
            TxtOrder.IsEnabled = _isRoot;


            BuildDynamicPanel();





            // 4. 🧲 基因属性大挪移：寻找时间轴容器，清理历史残留
            StackPanel timePanel = RowTime.Parent as StackPanel;
            if (timePanel != null)
            {
                UIElement oldFixedContainer = null;
                foreach (UIElement child in timePanel.Children)
                {
                    if (child is StackPanel sp && sp.Name == "PanelRootFixedProps") { oldFixedContainer = child; break; }
                }
                if (oldFixedContainer != null) timePanel.Children.Remove(oldFixedContainer);

                // 📌 核心逻辑：只有在初始状态 (_isRoot == true) 时，才把这几个固定 DNA 属性展示出来！
                if (_isRoot)
                {
                    StackPanel fixedPropsContainer = new StackPanel { Name = "PanelRootFixedProps", Margin = new Thickness(0, 10, 0, 0) };

                    // 🌟 1. 【黑魔法】：从父窗口偷看当前对象到底是不是“控制板”！
                    _isControlBoard = false;
                    var parentWin = Window.GetWindow(this);
                    if (parentWin != null)
                    {
                        var field = parentWin.GetType().GetField("_currentActiveObject", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (field != null)
                        {
                            var activeObj = field.GetValue(parentWin) as IStoryboardEntity;
                            if (activeObj != null && !string.IsNullOrEmpty(activeObj.TargetId))
                            {
                                _isControlBoard = true;
                            }
                        }
                    }

                    // 🌟 1.5 【神圣雷达】：嗅探当前是否处于模板编辑模式！
                    bool isTemplateMode = _currentState != null && _currentState.GetType().Name == "TemplateState";

                    // 🌟 2. 给予用户贴心的提示，替换掉原有的输入框
                    if (_isControlBoard)
                    {
                        fixedPropsContainer.Children.Add(new TextBlock
                        {
                            Text = "👻 控制板隐身模式：此对象仅传递动画，初始视觉核心（Path/Text/Pos）已禁用。",
                            Foreground = Brushes.Gray,
                            Margin = new Thickness(0, 0, 0, 10),
                            FontStyle = FontStyles.Italic
                        });
                    }
                    else if (isTemplateMode)
                    {
                        // 模板模式专属高雅提示
                        fixedPropsContainer.Children.Add(new TextBlock
                        {
                            Text = "🪄 模板纯净模式：模板仅用于存储动画状态。为了防止基因污染，初始实体属性已被屏蔽。",
                            Foreground = Brushes.Gold,
                            Margin = new Thickness(0, 0, 0, 10),
                            FontStyle = FontStyles.Italic
                        });
                    }

                    PropertyInfo[] props = _currentState.GetType().GetProperties();
                    foreach (var prop in props)
                    {
                        // 筛选需要置顶的初始固定属性 (联动增加 Text 判定)
                        if (prop.Name == "Path" || prop.Name == "Text" || prop.Name == "TextContent" || prop.Name == "NoteTarget" || prop.Name == "Pos")
                        {
                            // 🛑 【终极拦截】：如果是控制板，直接跳过生成，统统失去实体！
                            if (_isControlBoard) continue;

                            // 🛑 【神圣隔离】：如果是模板模式，绝对禁止暴露实体 DNA，直接跳过！
                            if (isTemplateMode) continue;

                            var row = CreateFixedPropertyRow(prop);
                            fixedPropsContainer.Children.Add(row);
                        }
                    }
                    timePanel.Children.Add(fixedPropsContainer);
                }
            }

        }

        private void BindStaticProperty(TextBox txt, string propName)
        {
            if (_currentState == null) return;
            PropertyInfo prop = _currentState.GetType().GetProperty(propName);
            if (prop != null)
            {
                Binding b = new Binding(propName) { Source = _currentState, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged };
                if (propName == "Time" || propName == "AddTime" || propName == "RelativeTime") b.Converter = new TimeBindingConverter();
                txt.SetBinding(TextBox.TextProperty, b);
            }
        }

        private UIElement CreateDynamicRow(PropertyInfo prop, object value)
        {
            bool isLocked = IsLockedByTemplate(prop.Name, out object templateValue);

            Grid grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock lbl = new TextBlock
            {
                Text = (isLocked ? "🔒 " : "") + prop.Name + ":",
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.Bold,
                Foreground = isLocked ? Brushes.Gray : Brushes.White
            };
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);

            Button btnDel = new Button { Content = "🗑️", Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(5, 0, 0, 0), Foreground = Brushes.Red };
            if (isLocked) btnDel.Visibility = Visibility.Collapsed;

            btnDel.Click += (s, e) => {
                if (prop.PropertyType.IsValueType && Nullable.GetUnderlyingType(prop.PropertyType) == null)
                {
                    MessageBox.Show($"设计师，底层模型中的【{prop.Name}】不是可空类型，无法被删除！", "底层限制");
                    return;
                }
                prop.SetValue(_currentState, null);
                LoadState(_currentState, _currentTitle, _rootState, _isRoot, _context);
            };
            Grid.SetColumn(btnDel, 2);
            grid.Children.Add(btnDel);

            FrameworkElement inputCtrl = BuildBoundInputControl(prop, value, isLocked, templateValue);
            Grid.SetColumn(inputCtrl, 1);
            grid.Children.Add(inputCtrl);

            return grid;
        }

        private FrameworkElement BuildBoundInputControl(PropertyInfo prop, object value, bool isLocked, object templateValue)
        {
            // 🌟【高阶魔法】：如果用户在关键帧中开启了 Template 属性，动态为它空降一个模板专属选择下拉框！
            if (prop.Name == "Template")
            {
                ComboBox cmbTemplateBox = new ComboBox { Padding = new Thickness(5) };
                cmbTemplateBox.Items.Add(new ComboBoxItem { Content = "🚫 不使用模板", Tag = null });

                if (_globalTemplates != null)
                {
                    foreach (var kvp in _globalTemplates)
                    {
                        cmbTemplateBox.Items.Add(new ComboBoxItem { Content = $"✨ {kvp.Key}", Tag = kvp.Key });
                    }
                }

                // 反查当前帧被赋予的模板值并挂载初值
                string currentTemplateVal = prop.GetValue(_currentState) as string;
                cmbTemplateBox.SelectedIndex = 0;
                if (!string.IsNullOrEmpty(currentTemplateVal))
                {
                    for (int idx = 1; idx < cmbTemplateBox.Items.Count; idx++)
                    {
                        if ((cmbTemplateBox.Items[idx] as ComboBoxItem)?.Tag?.ToString() == currentTemplateVal)
                        {
                            cmbTemplateBox.SelectedIndex = idx;
                            break;
                        }
                    }
                }

                // 当谱师在关键帧里换了模板印章，立刻重刷右侧，让模板的“锁死/变灰”防御性逻辑实时刷新联动！
                cmbTemplateBox.SelectionChanged += (s, e) =>
                {
                    if (cmbTemplateBox.SelectedItem is ComboBoxItem selectItem)
                    {
                        prop.SetValue(_currentState, selectItem.Tag as string);
                        // 时空同步：重新走一遍 LoadState 刷洗变灰状态！
                        LoadState(_currentState, _currentTitle, _rootState, _isRoot, _context);
                    }
                };
                return cmbTemplateBox;
            }





            Type pType = prop.PropertyType;
            Type uType = Nullable.GetUnderlyingType(pType) ?? pType;

            // 1. ⚖️ 询问《参数限制大管家》，获取当前属性的至高展现形态
            var constraint = Core.PropertyConstraintManager.GetConstraint(prop.Name);

            // 🔒 2. 模板死锁机制
            if (isLocked)
            {
                return new TextBox { Text = templateValue?.ToString() ?? "", Padding = new Thickness(5), IsEnabled = false, Foreground = Brushes.Gray };
            }

            // 🎚️ 3. 【核心进化一】：如果是滑块形态属性 (如 Opacity)，一键降临离散滑块组件！
            if (constraint.UIType == Core.PropertyUIType.Slider)
            {
                return new BoundedSliderControl(prop, _currentState, AttachValidationProbe);
            }

            // 🎨 4. 【核心进化二】：如果是单体选色器属性 (如 ScanlineColor)，一键召唤单体选色框！
            if (constraint.UIType == Core.PropertyUIType.ColorPicker)
            {
                return new SingleColorPickerControl(prop, _currentState, AttachValidationProbe);
            }

            // 🔘 5. 常规布尔值开关
            if (uType == typeof(bool))
            {
                CheckBox chk = new CheckBox { VerticalAlignment = VerticalAlignment.Center };
                Binding b = new Binding(prop.Name) { Source = _currentState, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged };
                chk.SetBinding(CheckBox.IsCheckedProperty, b);
                return chk;
            }
            // 📏 6. 坐标参考系复合框 (UnitFloat)
            else if (uType == typeof(UnitFloat))
            {
                Grid g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                ComboBox cmb = new ComboBox { Margin = new Thickness(0, 0, 5, 0), SelectedValuePath = "Tag" };
                foreach (ReferenceUnit r in Enum.GetValues(typeof(ReferenceUnit))) cmb.Items.Add(new ComboBoxItem { Content = r.ToString(), Tag = r });
                cmb.SetBinding(ComboBox.SelectedValueProperty, new Binding(prop.Name + ".Unit") { Source = _currentState, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
                Grid.SetColumn(cmb, 0); g.Children.Add(cmb);

                TextBox txt = new TextBox { Padding = new Thickness(5) };
                txt.SetBinding(TextBox.TextProperty, new Binding(prop.Name + ".Value") { Source = _currentState, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });

                AttachValidationProbe(txt, prop.Name); // 挂载探头

                Grid.SetColumn(txt, 1); g.Children.Add(txt);
                return g;
            }
            // 🌈 7. 【核心进化三】：如果是12色列表阵列 (List<string>)，一键铺开 2x6 矩阵调色盘！
            else if (uType == typeof(System.Collections.Generic.List<string>))
            {
                return new TwelveColorPickerControl(prop, _currentState);
            }
            // 📝 8. 其余普通的纯文本/数值属性 (兜底保留原生双向 Binding)
            else
            {
                TextBox txt = new TextBox { Padding = new Thickness(5) };
                txt.SetBinding(TextBox.TextProperty, new Binding(prop.Name) { Source = _currentState, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });

                AttachValidationProbe(txt, prop.Name); // 挂载探头

                return txt;
            }
        }

        // 
        private bool IsStaticDnaProperty(string propName) =>
            propName == "Path" || propName == "Text" || propName == "TextContent" ||
            propName == "Pos" || propName == "Layer" ||
            propName == "Font" || propName == "Align" || propName == "Note";


        // 1. 在类的顶部加入记录当前模板类型的变量
        private TemplateType _currentTemplateType = TemplateType.Generic;

        // 2. 增加接收类型变更的方法
        public void SetTemplateTypeLimit(TemplateType type)
        {
            _currentTemplateType = type;
            if (_currentState != null)
            {
                BuildDynamicPanel(); // 门派变了，立刻重新粉刷右侧的界面！
            }
        }
        private void BuildDynamicPanel()
        {
            PanelSpatialContainer.Children.Clear(); CmbSpatialProps.Items.Clear();
            PanelAppearanceContainer.Children.Clear(); CmbAppearanceProps.Items.Clear();
            PanelUiContainer.Children.Clear(); CmbUiProps.Items.Clear();
            PanelCameraContainer.Children.Clear(); CmbCameraProps.Items.Clear();
            PanelEffectsContainer.Children.Clear(); CmbEffectsProps.Items.Clear();

            PropertyInfo[] props = _currentState.GetType().GetProperties();

            foreach (var prop in props)
            {
                if (prop.Name == "Layer" || prop.Name == "Order") continue;
                // ✨ 新增：VIP 常驻特权属性绝对不允许在下方的动态列表里二次生成！
                if (prop.Name == "Time" || prop.Name == "Easing" || prop.Name == "AddTime" || prop.Name == "RelativeTime") continue;
                if (prop.Name == "Id" || prop.Name == "ParentId" || prop.Name == "TargetId" || prop.Name == "States" || prop.Name == "Keyframes") continue;
                // 只要是这几个不可做动画的固定 DNA 属性，全宇宙无条件隐身！绝对不进动态编辑面板！
                if (prop.Name == "Path" || prop.Name == "TextContent" || prop.Name == "NoteTarget" || prop.Name == "Pos") continue;
                //  
                if (IsStaticDnaProperty(prop.Name) && !_isRoot) continue;
                // ✨【控制板防御拦截】：如果是控制板叠加对象，其余任何诸如 Layer, Font, Align 等静态非动画属性一律不准在面板里露脸！
                if (_isControlBoard && IsStaticDnaProperty(prop.Name)) continue;

                // 🌟 【小艾的终极防线】：如果处于模板编辑模式，且该属性不属于此门派，直接蒸发！
                if (_rootState != null && _rootState.GetType().Name == "TemplateState") // 只要是模板类
                {
                    if (!Core.TemplateManager.IsPropertyAllowed(prop.Name, _currentTemplateType))
                    {
                        continue; // 不允许显示的属性，看都不给看，直接跳过！
                    }
                }

                object val = prop.GetValue(_currentState);
                // ==========================================
                // 🚀 核心修复：给 Template 发放永久 VIP 通行证！
                // 因为默认它是 null，会被系统无情地扔进“未激活”的下拉菜单里。
                // 我们直接拦截它，把它强行置顶钉死在界面最上方！
                // ==========================================
                if (prop.Name == "Template")
                {
                    if (_isRoot) continue;
                    var tplRow = CreateDynamicRow(prop, val);
                    PanelSpatialContainer.Children.Insert(0, tplRow); // 强行插在空间(Spatial)面板的最顶部！
                    continue; // 处理完毕，跳过后续的普通分拣
                }

                bool isActive = (val != null);



                // ==========================================
                // 🌟 小艾的完美分拣法术：确保没有任何一个属性流浪！
                // ==========================================
                // 🌟【解耦对接】：呼叫我们在公共 Core 文件夹里造出来的公共分拣大脑！
                var category = Core.PropertyClassifier.GetCategory(prop.Name);
                StackPanel targetPanel = null;
                ComboBox targetComboBox = null;

                switch (category)
                {
                    case Core.PropertyCategory.Spatial:
                        targetPanel = PanelSpatialContainer; targetComboBox = CmbSpatialProps; break;
                    case Core.PropertyCategory.UiControl:
                        targetPanel = PanelUiContainer; targetComboBox = CmbUiProps; break;
                    case Core.PropertyCategory.Camera:
                        targetPanel = PanelCameraContainer; targetComboBox = CmbCameraProps; break;
                    case Core.PropertyCategory.Effects:
                        targetPanel = PanelEffectsContainer; targetComboBox = CmbEffectsProps; break;
                    default:
                        targetPanel = PanelAppearanceContainer; targetComboBox = CmbAppearanceProps; break;
                }

                if (isActive)
                {
                    var row = CreateDynamicRow(prop, val);
                    targetPanel.Children.Add(row);
                }
                else
                {
                    var item = new ComboBoxItem { Content = prop.Name, Tag = prop };
                    targetComboBox.Items.Add(item);
                }
            }

            if (CmbSpatialProps.Items.Count > 0) CmbSpatialProps.SelectedIndex = 0;
            if (CmbAppearanceProps.Items.Count > 0) CmbAppearanceProps.SelectedIndex = 0;
            if (CmbUiProps.Items.Count > 0) CmbUiProps.SelectedIndex = 0;
            if (CmbCameraProps.Items.Count > 0) CmbCameraProps.SelectedIndex = 0;
            if (CmbEffectsProps.Items.Count > 0) CmbEffectsProps.SelectedIndex = 0;
        }

        private void BtnAddSpatial_Click(object sender, RoutedEventArgs e) => ExecutePropertyActivation(CmbSpatialProps);
        private void BtnAddAppearance_Click(object sender, RoutedEventArgs e) => ExecutePropertyActivation(CmbAppearanceProps);
        private void BtnAddUiProp_Click(object sender, RoutedEventArgs e) => ExecutePropertyActivation(CmbUiProps);
        private void BtnAddCameraProp_Click(object sender, RoutedEventArgs e) => ExecutePropertyActivation(CmbCameraProps);
        private void BtnAddEffectsProp_Click(object sender, RoutedEventArgs e) => ExecutePropertyActivation(CmbEffectsProps);

        private void ExecutePropertyActivation(ComboBox cmb)
        {
            if (cmb.SelectedItem is ComboBoxItem item && item.Tag is PropertyInfo prop)
            {
                Type uType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                object defaultVal = uType == typeof(string) ? "" : (uType == typeof(UnitFloat) ? new UnitFloat { Value = 0, Unit = ReferenceUnit.World } : (uType == typeof(System.Collections.Generic.List<string>) ? new System.Collections.Generic.List<string>(new string[12]) : Activator.CreateInstance(uType)));

                prop.SetValue(_currentState, defaultVal);
                LoadState(_currentState, _currentTitle, _rootState, _isRoot, _context);
            }
        }

        private void PopulateTemplateDropdown()
        {
            CmbTemplate.SelectionChanged -= CmbTemplate_SelectionChanged;
            CmbTemplate.Items.Clear();
            CmbTemplate.Items.Add(new ComboBoxItem { Content = "🚫 不使用模板", Tag = null });

            string currentTemplate = _currentState.GetType().GetProperty("Template")?.GetValue(_currentState) as string;
            int selectedIndex = 0;

            if (_globalTemplates != null)
            {
                int index = 1;
                foreach (var kvp in _globalTemplates)
                {
                    CmbTemplate.Items.Add(new ComboBoxItem { Content = $"✨ {kvp.Key}", Tag = kvp.Key });
                    if (kvp.Key == currentTemplate) selectedIndex = index;
                    index++;
                }
            }
            CmbTemplate.SelectedIndex = selectedIndex;
            CmbTemplate.SelectionChanged += CmbTemplate_SelectionChanged;
        }

        private void CmbTemplate_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbTemplate.SelectedItem is ComboBoxItem item)
            {
                string newTemplate = item.Tag as string;
                var prop = _currentState.GetType().GetProperty("Template");
                if (prop != null)
                {
                    prop.SetValue(_currentState, newTemplate);
                    LoadState(_currentState, _currentTitle, _rootState, _isRoot, _context);
                }
            }
        }

        private bool IsLockedByTemplate(string propName, out object templateValue)
        {
            templateValue = null;
            if (propName == "Time" || propName == "RelativeTime" || propName == "AddTime" || propName == "Template") return false;
            if (_currentState == null || _globalTemplates == null) return false;

            if (!FastReflectionHelper.TryGetValue(_currentState, "Template", out object currentTemplateNameObj) ||
                !(currentTemplateNameObj is string currentTemplateName) ||
                string.IsNullOrEmpty(currentTemplateName) ||
                !_globalTemplates.ContainsKey(currentTemplateName))
            {
                return false;
            }

            var templateObj = _globalTemplates[currentTemplateName];
            if (templateObj?.BaseState != null && FastReflectionHelper.TryGetValue(templateObj.BaseState, propName, out object val) && val != null)
            {
                templateValue = val;
                return true;
            }
            return false;
        }

        public bool ValidateAndSave()
        {
            if (_invalidProperties.Count > 0)
            {
                string errorList = string.Join(", ", _invalidProperties);
                MessageBox.Show($"设计师，发现 {_invalidProperties.Count} 个参数填写错误哦！\n\n" +
                                $"【异常参数】: {errorList}\n\n" +
                                $"带有红框的输入框必须修改正确，否则达咩（绝对不行）！请修改后再保存吧！",
                                "小艾的严肃拦截", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            return true;
        }


        // ==========================================
        // 🎨 探头辅助工具箱（只管视觉，不管赋值！）
        // ==========================================

        /// <summary>
        /// 🔴 触发红色警报：把框框标红，记录案犯属性
        /// </summary>
        private void SetError(TextBox txt, string propName, string tooltipMsg)
        {
            txt.BorderBrush = System.Windows.Media.Brushes.Red;
            txt.BorderThickness = new Thickness(2);
            // 换上淡淡的红色背景，超显眼！
            txt.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(20, 255, 0, 0));
            txt.ToolTip = tooltipMsg;

            _invalidProperties.Add(propName); // 丢进收容所，大门锁死！
        }

        /// <summary>
        /// 🟢 解除警报：恢复原样，从黑名单中踢出
        /// </summary>
        private void ClearError(TextBox txt, string propName)
        {
            txt.ClearValue(TextBox.BorderBrushProperty);
            txt.ClearValue(TextBox.BorderThicknessProperty);
            txt.ClearValue(TextBox.BackgroundProperty);
            txt.ToolTip = null;

            _invalidProperties.Remove(propName); // 释放属性，允许保存！
        }

        // ==========================================
        // 📡 独立监控基站：专门给输入框加装审查探头
        // ==========================================
        private void AttachValidationProbe(TextBox txtValue, string propName)
        {
            if (txtValue == null) return;

            // 1. ⚖️ 提前向《属性宪法》大管家拿到该参数的合法规则
            var rule = Core.PropertyConstraintManager.GetConstraint(propName);

            // 2. ⚡ 挂载 TextChanged 实时审查视线
            txtValue.TextChanged += (s, e) =>
            {
                string input = txtValue.Text.Trim();

                // 🚨 铁腕规则一：空值绝对拦截！强制要求重填
                if (string.IsNullOrEmpty(input))
                {
                    SetError(txtValue, propName, "⚠️ 这里的参数不能为空哦！如果不想要它，请使用侧面的属性关闭功能把它移除。");
                    return; // 标红后直接拦截，不往下执行
                }

                bool isError = false;
                string errorMsg = "";

                // 🔬 铁腕规则二：根据门派和规则开始对输入的文本进行严苛审查
                if (rule.UIType == Core.PropertyUIType.Slider || rule.UIType == Core.PropertyUIType.FloatBox || rule.UIType == Core.PropertyUIType.UnitBox)
                {
                    if (float.TryParse(input, out float fVal))
                    {
                        if (fVal < rule.Min || fVal > rule.Max)
                        {
                            isError = true;
                            errorMsg = $"⚠️ 数值越界啦！设计师，这里只能填入 {rule.Min} 到 {rule.Max} 之间的数字哦！";
                        }
                    }
                    else
                    {
                        isError = true;
                        errorMsg = "⚠️ 格式不正确！这里必须填入合法的数字（支持小数）哦！";
                    }
                }
                else if (rule.UIType == Core.PropertyUIType.IntBox)
                {
                    if (int.TryParse(input, out int iVal))
                    {
                        if (iVal < rule.Min || iVal > rule.Max)
                        {
                            isError = true;
                            errorMsg = $"⚠️ 越界达咩！这里只能填入 {rule.Min} 到 {rule.Max} 之间的整数哦！";
                        }
                    }
                    else
                    {
                        isError = true;
                        errorMsg = "⚠️ 这里只能填入纯整数（不能有小数点）哦！";
                    }
                }
                else if (rule.UIType == Core.PropertyUIType.ColorPicker)
                {
                    // 简单的十六进制颜色防呆审查
                    if (!input.StartsWith("#") || (input.Length != 7 && input.Length != 9))
                    {
                        isError = true;
                        errorMsg = "⚠️ 颜色代码格式错啦！必须以 # 开头，例如 #FFFFFF（6位）或 #FF4568DC（8位）哦！";
                    }
                }

                // 🎨 审判结果宣判：只控红框和名单，绝对不执行 SetValue 干扰 Binding！
                if (isError)
                {
                    SetError(txtValue, propName, errorMsg);
                }
                else
                {
                    ClearError(txtValue, propName);
                }
            };
        }

        // 📦 专门为置顶的“固定属性”制造带有专属翻译官的 UI 排版行
        private UIElement CreateFixedPropertyRow(PropertyInfo prop)
        {
            Grid grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            string icon = "📌";
            if (prop.Name == "Path") icon = "🖼️";
            else if (prop.Name == "TextContent") icon = "📝";
            else if (prop.Name == "NoteTarget") icon = "🎯";
            else if (prop.Name == "Pos") icon = "〰️";

            TextBlock lbl = new TextBlock
            {
                Text = $"{icon} 初始核心 ({prop.Name}):",
                VerticalAlignment = prop.Name == "NoteTarget" ? VerticalAlignment.Top : VerticalAlignment.Center, // 雷达太高了，标题顶对齐
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)Application.Current.Resources["HighlightBorderColor"] ?? Brushes.LightSkyBlue,
                Margin = new Thickness(0, 5, 0, 0)
            };
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);

            // ==========================================================
            // 🚀 【超级法术接线】：如果是 NoteTarget，拒绝使用普通文本框，一键降临超级雷达！
            // ==========================================================
            if (prop.Name == "NoteTarget")
            {
                var selectorCtrl = new NoteSelectorBuilderControl(prop, _currentState, _context);
                Grid.SetColumn(selectorCtrl, 1);
                grid.Children.Add(selectorCtrl);
            }
            else if (prop.Name == "Pos") // ✨ 新增：拦截 Pos，降临线条端点矩阵！
            {
                var lineCtrl = new LinePointsEditorControl(prop, _currentState, _context);
                Grid.SetColumn(lineCtrl, 1);
                grid.Children.Add(lineCtrl);
            }
            else
            {
                // 其他常规固定属性 (Path, TextContent) 保持不变
                TextBox txt = new TextBox { Padding = new Thickness(5) };
                Binding b = new Binding(prop.Name) { Source = _currentState, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged };

                if (prop.Name == "Pos")
                    b.Converter = new UniversalObjectConverter();

                txt.SetBinding(TextBox.TextProperty, b);
                Grid.SetColumn(txt, 1);
                grid.Children.Add(txt);
            }

            return grid;
        }




    }
}