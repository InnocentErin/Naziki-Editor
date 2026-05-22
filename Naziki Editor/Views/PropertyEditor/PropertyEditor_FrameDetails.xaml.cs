using Naziki_Editor.Models;
using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

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
                return string.Join(", ", strList); // 完美将数组拼接成逗号分隔的字符串
            }
            return value?.ToString() ?? "";
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

        public PropertyEditor_FrameDetails()
        {
            InitializeComponent();
        }

        // ==========================================
        // 📥 接口：接收选中的关键帧并渲染 UI
        // ==========================================
        public void LoadState(object stateReference, string frameTitle, object rootState, bool isRoot)
        {
            _currentState = stateReference; _rootState = rootState; _isRoot = isRoot;
            if (_currentState == null) { PanelDetails.Visibility = Visibility.Collapsed; TxtEmptyState.Visibility = Visibility.Visible; return; }

            PanelDetails.Visibility = Visibility.Visible;
            TxtEmptyState.Visibility = Visibility.Collapsed;
            TxtFrameTitle.Text = $"当前选中 ➡️ {frameTitle}";

            // 🌟 修正 5：同样加上 State！
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
            Grid grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock lbl = new TextBlock { Text = prop.Name + ":", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Bold };
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);

            Button btnDel = new Button { Content = "🗑️", Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(5, 0, 0, 0), Foreground = System.Windows.Media.Brushes.Red };
            btnDel.Click += (s, e) => {
                // 防呆拦截：如果用户的模型里没有定义为可空类型（比如是 float 而不是 float?），它将无法被删除！
                if (prop.PropertyType.IsValueType && Nullable.GetUnderlyingType(prop.PropertyType) == null)
                {
                    MessageBox.Show($"指挥官，底层模型中的【{prop.Name}】不是可空类型 (Nullable)，无法被删除！\n请在您的 Models 代码中将其改为可空类型（如 float?）。", "底层限制");
                    return;
                }
                prop.SetValue(_currentState, null);
                LoadState(_currentState, _currentTitle, _rootState, _isRoot);// 刷新 UI
            };
            Grid.SetColumn(btnDel, 2);
            grid.Children.Add(btnDel);

            FrameworkElement inputCtrl = BuildBoundInputControl(prop, value);
            Grid.SetColumn(inputCtrl, 1);
            grid.Children.Add(inputCtrl);

            return grid;
        }

        // 核心：基于类型生成不同的双向绑定输入控件
        private FrameworkElement BuildBoundInputControl(PropertyInfo prop, object value)
        {
            Type pType = prop.PropertyType;
            Type uType = Nullable.GetUnderlyingType(pType) ?? pType;

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
                // 填充单位选项
                foreach (ReferenceUnit r in Enum.GetValues(typeof(ReferenceUnit)))
                {
                    cmb.Items.Add(new ComboBoxItem { Content = r.ToString(), Tag = r });
                }
                // 绑定当前单位值
                Binding bUnit = new Binding(prop.Name + ".Unit") { Source = _currentState, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged };
                cmb.SetBinding(ComboBox.SelectedValueProperty, bUnit);
                // 将下拉框放在第一列
                Grid.SetColumn(cmb, 0);
                g.Children.Add(cmb);
                // 再创建一个文本框输入数值
                TextBox txt = new TextBox { Padding = new Thickness(5) };
                // 绑定当前数值
                Binding bVal = new Binding(prop.Name + ".Value") { Source = _currentState, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged };
                txt.SetBinding(TextBox.TextProperty, bVal);
                // 将文本框放在第二列
                Grid.SetColumn(txt, 1);
                g.Children.Add(txt);

                return g;
            }
            // 其他类型默认使用 TextBox 输入
            else
            {
                TextBox txt = new TextBox { Padding = new Thickness(5) };
                Binding b = new Binding(prop.Name) { Source = _currentState, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged };
                txt.SetBinding(TextBox.TextProperty, b);
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
        // 🔮 辅助方法：属性名称智能分流分类器
        // ==========================================
        private int GetPropertyCategory(string name, object rootState)
        {
            string n = name.ToLower();
            if (rootState is ControllerState) // 👈 加上了 State！
            {
                if (n.Contains("chromatical") || n.Contains("bloom") || n.Contains("blur") || n.Contains("adjustment") || n.Contains("brightness") || n.Contains("saturation") || n.Contains("contrast") || n.Contains("noise") || n.Contains("sepia") || n.Contains("dream") || n.Contains("fisheye") || n.Contains("shockwave") || n.Contains("focus") || n.Contains("glitch") || n.Contains("arcade") || n == "tape" || n.Contains("filter")) return 5;
                if (n == "perspective" || n == "size" || n == "fov" || n == "x" || n == "y" || n == "z" || n.Contains("rot")) return 4;
                return 3;
            }
            else
            {
                if (n.Contains("color") || n.Contains("opacity") || n == "width" || n == "style" || n.Contains("direction")) return 2;
                return 1;
            }
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

            foreach (var prop in props)
            {
                if (prop.Name == "Time" || prop.Name == "Easing" || prop.Name == "AddTime" || prop.Name == "RelativeTime") continue;
                if (prop.Name == "Id" || prop.Name == "ParentId" || prop.Name == "TargetId" || prop.Name == "States") continue;

                if (IsStaticDnaProperty(prop.Name) && !_isRoot) continue;

                object val = prop.GetValue(_currentState);
                bool isActive = (val != null);
                int cat = GetPropertyCategory(prop.Name, _rootState);

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
                    if (!_isRoot && prop.Name != "Destroy")
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
        // 👆 三大卡片独立开启属性响应方法 (替代原本的单个点击方法)
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
                prop.SetValue(_currentState, defaultVal);
                LoadState(_currentState, TxtFrameTitle.Text.Replace("当前选中 ➡️ ", ""), _rootState, _isRoot);
            }
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