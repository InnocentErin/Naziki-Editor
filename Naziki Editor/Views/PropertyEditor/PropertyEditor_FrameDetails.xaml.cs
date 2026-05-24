using Naziki_Editor.Models;
using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Naziki_Editor.Views.PropertyEditor
{


    // ✨ 新增：专门解决 Time 数组被显示成 "System.Collections..." 的救星转换器！
    public class TimeBindingConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is System.Collections.IList list)
            {
                var strList = new System.Collections.Generic.List<string>();
                foreach (var item in list) strList.Add(item.ToString());
                return string.Join(", ", strList);
            }

            // ✨ 核心拦截：如果是 C# 的极限占位符，直接显示为空，不吓唬用户！
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
        // ✨ 新增：幽灵斩杀追踪器！记录哪些属性是玩家手动点的，没点过的0全部视为幽灵！
        private System.Collections.Generic.HashSet<string> _manuallyActivatedProps = new System.Collections.Generic.HashSet<string>();

        // ✨ 新增：存放从大本营传过来的全局模板图纸！
        private System.Collections.Generic.Dictionary<string, StoryboardTemplate> _globalTemplates;

        public void InitTemplates(System.Collections.Generic.Dictionary<string, StoryboardTemplate> templates)
        {
            _globalTemplates = templates;
        }


        public PropertyEditor_FrameDetails()
        {
            InitializeComponent();
        }




        // ==========================================
        // 📥 接口：接收选中的关键帧并渲染 UI
        // ==========================================
        public void LoadState(object stateReference, string frameTitle, object rootState, bool isRoot)
        {
            _currentState = stateReference; 
            _rootState = rootState; 
            _isRoot = isRoot;
            _currentTitle = frameTitle; // ✨ 必须保存当前标题，以便重载时使用


            if (_currentState == null) { PanelDetails.Visibility = Visibility.Collapsed; TxtEmptyState.Visibility = Visibility.Visible; return; }

            PanelDetails.Visibility = Visibility.Visible;
            TxtEmptyState.Visibility = Visibility.Collapsed;
            TxtFrameTitle.Text = $"当前选中 ➡️ {frameTitle}";
            PopulateTemplateDropdown(); // ✨ 刷新模板下拉框


            // ✨ 核心修正：如果是模板，全量开放所有功能卡片（因为模板可以控制所有参数）！
            bool isTemplate = _currentState is StoryboardTemplate || _rootState is StoryboardTemplate;
            // 同样加上 State！
            PanelSceneCards.Visibility = (_rootState is ControllerState) ? Visibility.Collapsed : Visibility.Visible;
            PanelControllerCards.Visibility = (_rootState is ControllerState) ? Visibility.Visible : Visibility.Collapsed;

            BindStaticProperty(TxtTime, "Time"); BindStaticProperty(TxtEasing, "Easing");
            PanelStateTimeOptions.Visibility = _isRoot ? Visibility.Collapsed : Visibility.Visible;
            if (!_isRoot) { BindStaticProperty(TxtAddTime, "AddTime"); BindStaticProperty(TxtRelativeTime, "RelativeTime"); }

            BuildDynamicPanel();
        }


        


        // 核心：绑定 Time 和 Easing 这两个固定属性的输入框
        private void BindStaticProperty(TextBox txt, string propName)
        {
            PropertyInfo prop = _currentState.GetType().GetProperty(propName);
            if (prop != null)
            {
                Binding b = new Binding(propName) { Source = _currentState, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged };
                // 🌟 修正 6：为 Time 挂载我们刚刚写的专属翻译官，杜绝乱码！
                if (propName == "Time" || propName == "AddTime" || propName == "RelativeTime") b.Converter = new TimeBindingConverter();
                txt.SetBinding(TextBox.TextProperty, b);
            }
        }



        // 核心：为每个已激活属性创建一行输入控件，包含标签、输入框和删除按钮
        private UIElement CreateDynamicRow(PropertyInfo prop, object value)
        {

            // ✨ 1. 先用透视法术查一下！
            bool isLocked = IsLockedByTemplate(prop.Name, out object templateValue);



            Grid grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // ✨ 2. 如果上锁了，标题旁边加个小锁头！
            TextBlock lbl = new TextBlock
            {
                Text = (isLocked ? "🔒 " : "") + prop.Name + ":",
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.Bold,
                Foreground = isLocked ? Brushes.Gray : Brushes.White
            };
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);

            Button btnDel = new Button { Content = "🗑️", Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(5, 0, 0, 0), Foreground = System.Windows.Media.Brushes.Red };
            // ✨ 3. 如果是模板锁定状态，绝对不允许用户删除它！把垃圾桶藏起来！
            if (isLocked) btnDel.Visibility = Visibility.Collapsed;

            btnDel.Click += (s, e) => {
                // 防呆拦截：如果用户的模型里没有定义为可空类型（比如是 float 而不是 float?），它将无法被删除！
                if (prop.PropertyType.IsValueType && Nullable.GetUnderlyingType(prop.PropertyType) == null)
                {
                    MessageBox.Show($"指挥官，底层模型中的【{prop.Name}】不是可空类型 (Nullable)，无法被删除！", "底层限制");
                    return;
                }
                prop.SetValue(_currentState, null);
                LoadState(_currentState, _currentTitle, _rootState, _isRoot);
            };
            Grid.SetColumn(btnDel, 2);
            grid.Children.Add(btnDel);

            // ✨ 4. 把上锁状态传给控件制造机
            FrameworkElement inputCtrl = BuildBoundInputControl(prop, value, isLocked, templateValue);
            Grid.SetColumn(inputCtrl, 1);
            grid.Children.Add(inputCtrl);

            return grid;
        }

        // 核心：基于类型生成不同的双向绑定输入控件
        private FrameworkElement BuildBoundInputControl(PropertyInfo prop, object value, bool isLocked, object templateValue)
        {
            Type pType = prop.PropertyType;
            Type uType = Nullable.GetUnderlyingType(pType) ?? pType;

            // ✨ 如果被模板锁死了，我们就斩断 TwoWay 绑定，只显示一个灰色的假只读控件！
            if (isLocked)
            {
                return new TextBox
                {
                    Text = templateValue.ToString(),
                    Padding = new Thickness(5),
                    IsEnabled = false,
                    Foreground = System.Windows.Media.Brushes.Gray,
                    ToolTip = "该属性已被模板接管。若需强制覆盖，请在当前状态解除模板印章。"
                };
            }




            // ✨ 魔法所在：全面采用 WPF 的数据绑定（TwoWay Binding），让 UI 与对象状态自动同步，无需手动读取输入框内容！
            if (uType == typeof(bool))
            {
                CheckBox chk = new CheckBox { VerticalAlignment = VerticalAlignment.Center };
                Binding b = new Binding(prop.Name) { Source = _currentState, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged };
                chk.SetBinding(CheckBox.IsCheckedProperty, b);
                return chk;
            }
            // 预留：如果未来需要支持颜色选择，可以在这里添加一个颜色选择控件的分支
            else if (uType == typeof(UnitFloat))
            {
                // 这个类型比较特殊，我们需要同时绑定它的 Value 和 Unit 两个属性，所以不能直接用一个 TextBox
                Grid g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // 先创建一个下拉框选择单位
                ComboBox cmb = new ComboBox { Margin = new Thickness(0, 0, 5, 0), SelectedValuePath = "Tag" };
                foreach (ReferenceUnit r in Enum.GetValues(typeof(ReferenceUnit))) cmb.Items.Add(new ComboBoxItem { Content = r.ToString(), Tag = r });
                cmb.SetBinding(ComboBox.SelectedValueProperty, new Binding(prop.Name + ".Unit") { Source = _currentState, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
                Grid.SetColumn(cmb, 0); g.Children.Add(cmb);

                // 再创建一个文本框输入数值
                TextBox txt = new TextBox { Padding = new Thickness(5) };
                txt.SetBinding(TextBox.TextProperty, new Binding(prop.Name + ".Value") { Source = _currentState, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
                Grid.SetColumn(txt, 1); g.Children.Add(txt);
                return g;
            }
            // 其他类型默认使用 TextBox 输入
            else
            {
                TextBox txt = new TextBox { Padding = new Thickness(5) };
                txt.SetBinding(TextBox.TextProperty, new Binding(prop.Name) { Source = _currentState, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
                return txt;
            }
        }


        // ==========================================
        // 🧬 DNA 鉴定器：判断哪些属性是不可变的静态属性或已固定的属性
        // ==========================================
        private bool IsStaticDnaProperty(string propName) =>
            propName == "Path" || propName == "Text" || propName == "TextContent" ||
            propName == "Pos" || propName == "Template" || propName == "Layer" ||
            propName == "Font" || propName == "Align" || propName == "Note";




        // ==========================================
        // 🔮 辅助方法：属性名称智能分流分类器 (Cytoid 官方全量归类版)
        // ==========================================
        private int GetPropertyCategory(string name)
        {
            string n = name.ToLower();

            // 📐 Cat 1: 空间坐标与尺寸控制 (Spatial)
            if (n == "x" || n == "y" || n == "z" || n == "width" || n == "height" || n.Contains("scale") || n.Contains("pivot") || n.Contains("rot"))
                return 1;

            // 🎥 Cat 4: 镜头与透视控制 (Camera)
            if (n == "fov" || n == "perspective")
                return 4;

            // ✨ Cat 5: 屏幕画面滤镜 (Effects)
            if (n.Contains("bloom") || n.Contains("vignette") || n.Contains("arcade") || n == "tape")
                return 5;

            // 🌌 Cat 3: 场景环境与音符乘区控制 (UI/Scene)
            if (n.Contains("dim") || n.Contains("opacity_multiplier") || n.Contains("multiplier") || n.Contains("offset") || n.Contains("override") || n == "videobgm" || n == "storyboardopacity" || n == "uiopacity")
                return 3;

            // 🎨 Cat 2: 外观颜色与表现控制 (Appearance - 剩下所有的都去外观)
            return 2;
        }

        // ==========================================
        // 🧠 核心方法：三路分流面板建立与血统继承控制
        // ==========================================
        private void BuildDynamicPanel()
        {
            PanelSpatialContainer.Children.Clear(); CmbSpatialProps.Items.Clear();
            PanelAppearanceContainer.Children.Clear(); CmbAppearanceProps.Items.Clear();
            PanelUiContainer.Children.Clear(); CmbUiProps.Items.Clear();
            PanelCameraContainer.Children.Clear(); CmbCameraProps.Items.Clear();
            PanelEffectsContainer.Children.Clear(); CmbEffectsProps.Items.Clear();

            PropertyInfo[] props = _currentState.GetType().GetProperties();
            bool isTemplate = _currentState is StoryboardTemplate || _rootState is StoryboardTemplate;

            foreach (var prop in props)
            {
                if (prop.Name == "Time" || prop.Name == "Easing" || prop.Name == "AddTime" || prop.Name == "RelativeTime") continue;
                if (prop.Name == "Id" || prop.Name == "ParentId" || prop.Name == "TargetId" || prop.Name == "States") continue;

                if (IsStaticDnaProperty(prop.Name) && !_isRoot) continue;

                object val = prop.GetValue(_currentState);
                bool isActive = (val != null);

                // 👻 ✨ 幽灵斩杀阵！如果是模板里的 0 World，且没有被追踪器记录，直接屏蔽！
                if (isActive && isTemplate && val is UnitFloat uf && uf.Value == 0 && uf.Unit == ReferenceUnit.World)
                {
                    if (!_manuallyActivatedProps.Contains(prop.Name)) isActive = false;
                }

                int cat = GetPropertyCategory(prop.Name); // 👈 呼叫全新精准分类器

                if (isActive)
                {
                    var row = CreateDynamicRow(prop, val);
                    if (cat == 1) PanelSpatialContainer.Children.Add(row);
                    else if (cat == 2) PanelAppearanceContainer.Children.Add(row);
                    else if (cat == 3) PanelUiContainer.Children.Add(row);
                    else if (cat == 4) PanelCameraContainer.Children.Add(row);
                    else if (cat == 5) PanelEffectsContainer.Children.Add(row);
                }
                else
                {
                    // 普通对象的子帧，如果 Root 没开就不进 ComboBox；但模板模式百无禁忌，全部塞进去让你选！
                    if (!_isRoot && prop.Name != "Destroy" && !isTemplate)
                    {
                        var rootProp = _rootState.GetType().GetProperty(prop.Name);
                        if (rootProp == null || rootProp.GetValue(_rootState) == null) continue;
                    }

                    var item = new ComboBoxItem { Content = prop.Name, Tag = prop };
                    if (cat == 1) CmbSpatialProps.Items.Add(item);
                    else if (cat == 2) CmbAppearanceProps.Items.Add(item);
                    else if (cat == 3) CmbUiProps.Items.Add(item);
                    else if (cat == 4) CmbCameraProps.Items.Add(item);
                    else if (cat == 5) CmbEffectsProps.Items.Add(item);
                }
            }

            if (CmbSpatialProps.Items.Count > 0) CmbSpatialProps.SelectedIndex = 0;
            if (CmbAppearanceProps.Items.Count > 0) CmbAppearanceProps.SelectedIndex = 0;
            if (CmbUiProps.Items.Count > 0) CmbUiProps.SelectedIndex = 0;
            if (CmbCameraProps.Items.Count > 0) CmbCameraProps.SelectedIndex = 0;
            if (CmbEffectsProps.Items.Count > 0) CmbEffectsProps.SelectedIndex = 0;
        }

        // ==========================================
        // 👆 三大卡片独立开启属性响应方法
        // ==========================================
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
                object defaultVal = uType == typeof(string) ? "" : (uType == typeof(UnitFloat) ? new UnitFloat { Value = 0, Unit = ReferenceUnit.World } : Activator.CreateInstance(uType));

                // ✨ 追踪器启动：打上玩家手动激活的印记，这样它即使是 0 也不会被当做幽灵屏蔽啦！
                _manuallyActivatedProps.Add(prop.Name);

                prop.SetValue(_currentState, defaultVal);
                LoadState(_currentState, TxtFrameTitle.Text.Replace("当前选中 ➡️ ", ""), _rootState, _isRoot);
            }
        }





        // ==========================================
        // ✨ 模板下拉框逻辑
        // ==========================================
        private void PopulateTemplateDropdown()
        {
            CmbTemplate.SelectionChanged -= CmbTemplate_SelectionChanged; // 关掉监听防死循环
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
                    LoadState(_currentState, _currentTitle, _rootState, _isRoot); // 立刻重载UI以上锁！
                }
            }
        }

        // ==========================================
        // 🔮 透视雷达：属性是否被模板接管（包含绝对豁免）
        // ==========================================
        private bool IsLockedByTemplate(string propName, out object templateValue)
        {
            templateValue = null;

            // 🛡️ 绝对豁免特权：时空参数永远归属当前帧本体！
            if (propName == "Time" || propName == "RelativeTime" || propName == "AddTime" || propName == "Template")
                return false;

            if (_currentState == null || _globalTemplates == null) return false;

            string currentTemplateName = _currentState.GetType().GetProperty("Template")?.GetValue(_currentState) as string;
            if (string.IsNullOrEmpty(currentTemplateName) || !_globalTemplates.ContainsKey(currentTemplateName))
                return false;

            var templateObj = _globalTemplates[currentTemplateName];
            var targetProp = templateObj.GetType().GetProperty(propName);

            if (targetProp != null)
            {
                object val = targetProp.GetValue(templateObj);
                if (val != null)
                {
                    templateValue = val;
                    return true;
                }
            }
            return false;
        }



        // ==========================================
        // 📤 接口：将面板数据验证并保存回对象
        // ==========================================
        public bool ValidateAndSave()
        {
            // ✨ 魔法所在：由于全面采用了 WPF 的数据绑定（TwoWay Binding）
            // 用户在界面上修改任何文字，对象都已经被自动修改了！不需要再费力去读取UI数据！
            return true;
        }
    }
}