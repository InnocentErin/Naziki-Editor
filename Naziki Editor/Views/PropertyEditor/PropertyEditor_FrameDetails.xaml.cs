using Naziki_Editor.Models;
using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Naziki_Editor.Views.PropertyEditor
{
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
            _currentState = stateReference;
            _rootState = rootState;
            _isRoot = isRoot;
            // ... 绑定固定属性 (Time, Easing) ...
            BuildDynamicPanel();
        }

        // 核心：绑定 Time 和 Easing 这两个固定属性的输入框
        private void BindStaticProperty(TextBox txt, string propName)
        {
            PropertyInfo prop = _currentState.GetType().GetProperty(propName);
            if (prop != null)
            {
                Binding b = new Binding(propName) { Source = _currentState, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged };
                txt.SetBinding(TextBox.TextProperty, b);
            }
        }

        // 核心：扫描当前状态对象的属性，动态生成输入行或添加到可选列表
        private void BuildDynamicPanel()
        {
            PanelDynamicProperties.Children.Clear();
            CmbAvailableProperties.Items.Clear();

            PropertyInfo[] props = _currentState.GetType().GetProperties();

            foreach (var prop in props)
            {
                // 过滤掉不可动的基础 DNA (Path, Text 等) 和自带的 Time/Easing
                if (IsStaticDnaProperty(prop.Name)) continue;

                object val = prop.GetValue(_currentState);
                bool isActive = (val != null);

                if (isActive)
                {
                    // 如果已激活，直接渲染输入框
                    PanelDynamicProperties.Children.Add(CreateDynamicRow(prop, val));
                }
                else
                {
                    // 🛑 核心防呆：如果当前【不是】初始帧，进行血统审查！
                    if (!_isRoot)
                    {
                        object rootVal = prop.GetValue(_rootState);
                        // 只有当这个属性在初始帧里被激活了（不为 null），它才有资格出现在子帧的下拉菜单里！
                        if (rootVal == null) continue;
                    }

                    // 添加到下拉菜单
                    CmbAvailableProperties.Items.Add(new ComboBoxItem { Content = prop.Name, Tag = prop });
                }
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
        // 核心：当用户从下拉列表选择一个未激活的属性并点击添加时，将其初始化并刷新面板
        private void BtnAddProperty_Click(object sender, RoutedEventArgs e)
        {
            // 防呆拦截：如果用户的模型里没有定义为可空类型（比如是 float 而不是 float?），它将无法被添加！
            if (CmbAvailableProperties.SelectedItem is ComboBoxItem item && item.Tag is PropertyInfo prop)
            {
                Type pType = prop.PropertyType;
                Type uType = Nullable.GetUnderlyingType(pType) ?? pType;

                object defaultVal = null;
                if (uType == typeof(string)) defaultVal = "";
                // 预留：如果未来需要支持颜色选择，可以在这里添加一个颜色类型的默认值（如 Colors.Transparent）
                else if (uType == typeof(UnitFloat)) defaultVal = new UnitFloat { Value = 0, Unit = ReferenceUnit.World };
                else if (uType.IsValueType) defaultVal = Activator.CreateInstance(uType); // 例如 0 或 false

                prop.SetValue(_currentState, defaultVal);
                LoadState(_currentState, _currentTitle, _rootState, _isRoot); // 刷新面板
            }
        }

        // ==========================================
        // 🧬 DNA 鉴定器：判断哪些属性是不可变的静态属性或已固定的属性
        // ==========================================
        private bool IsStaticDnaProperty(string propName)
        {
            return propName == "Id" || propName == "TargetId" || propName == "ParentId" ||
                   propName == "Path" || propName == "Text" || propName == "Pos" ||
                   propName == "Template" || propName == "Time" || propName == "Easing";
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