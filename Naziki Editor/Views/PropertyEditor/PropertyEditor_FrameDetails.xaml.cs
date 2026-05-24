using Naziki_Editor.Models;
using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Naziki_Editor.Core;

namespace Naziki_Editor.Views.PropertyEditor
{
    // ==========================================
    // ✨ 专属翻译官：防止 Time 数组或空值转义成乱码
    // ==========================================
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

        private System.Collections.Generic.HashSet<string> _manuallyActivatedProps = new System.Collections.Generic.HashSet<string>();
        private System.Collections.Generic.Dictionary<string, C2Template> _globalTemplates;

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
        public void LoadState(object stateReference, string frameTitle, object rootState, bool isRoot)
        {

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
            PopulateTemplateDropdown();

            // ✨ 核心修正：不管是普通对象还是状态类，只要属于场景控制器，全部分流
            bool isController = (_rootState is ControllerState || _currentState is ControllerState);
            PanelSceneCards.Visibility = isController ? Visibility.Collapsed : Visibility.Visible;
            PanelControllerCards.Visibility = isController ? Visibility.Visible : Visibility.Collapsed;

            // ✨ 核心强打通：绑定常驻时间轴参数
            BindStaticProperty(TxtTime, "Time");
            BindStaticProperty(TxtEasing, "Easing");

            PanelStateTimeOptions.Visibility = _isRoot ? Visibility.Collapsed : Visibility.Visible;
            if (!_isRoot)
            {
                BindStaticProperty(TxtAddTime, "AddTime");
                BindStaticProperty(TxtRelativeTime, "RelativeTime");
            }

            BuildDynamicPanel();
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
                    MessageBox.Show($"指挥官，底层模型中的【{prop.Name}】不是可空类型，无法被删除！", "底层限制");
                    return;
                }
                prop.SetValue(_currentState, null);
                LoadState(_currentState, _currentTitle, _rootState, _isRoot);
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
            Type pType = prop.PropertyType;
            Type uType = Nullable.GetUnderlyingType(pType) ?? pType;

            if (isLocked)
            {
                return new TextBox { Text = templateValue?.ToString() ?? "", Padding = new Thickness(5), IsEnabled = false, Foreground = Brushes.Gray };
            }

            if (uType == typeof(bool))
            {
                CheckBox chk = new CheckBox { VerticalAlignment = VerticalAlignment.Center };
                Binding b = new Binding(prop.Name) { Source = _currentState, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged };
                chk.SetBinding(CheckBox.IsCheckedProperty, b);
                return chk;
            }
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
                Grid.SetColumn(txt, 1); g.Children.Add(txt);
                return g;
            }
            else if (uType == typeof(System.Collections.Generic.List<string>))
            {
                // 12色特殊阵列定制 TextBox
                TextBox txt = new TextBox { Padding = new Thickness(5) };
                txt.SetBinding(TextBox.TextProperty, new Binding(prop.Name) { Source = _currentState, Mode = BindingMode.TwoWay, Converter = new TimeBindingConverter(), UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
                return txt;
            }
            else
            {
                TextBox txt = new TextBox { Padding = new Thickness(5) };
                txt.SetBinding(TextBox.TextProperty, new Binding(prop.Name) { Source = _currentState, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
                return txt;
            }
        }

        private bool IsStaticDnaProperty(string propName) =>
            propName == "Path" || propName == "Text" || propName == "TextContent" ||
            propName == "Pos" || propName == "Template" || propName == "Layer" ||
            propName == "Font" || propName == "Align" || propName == "Note";

        private int GetPropertyCategory(string name)
        {
            string n = name.ToLower();
            if (n == "x" || n == "y" || n == "z" || n == "width" || n == "height" || n.Contains("scale") || n.Contains("pivot") || n.Contains("rot") || n.StartsWith("x1") || n.StartsWith("x2") || n.StartsWith("y1") || n.StartsWith("y2") || n.Contains("pos"))
                return 1;
            if (n == "fov" || n == "perspective" || n == "size")
                return 4;
            if (n.Contains("bloom") || n.Contains("vignette") || n.Contains("arcade") || n == "tape" || n.Contains("chromatical") || n.Contains("blur") || n.Contains("filter") || n.Contains("adjustment") || n == "glitch" || n.Contains("noise") || n.Contains("focus") || n.Contains("shockwave") || n.Contains("sepia") || n.Contains("dream") || n.Contains("fisheye") || n.Contains("gray_scale"))
                return 5;
            if (n.Contains("dim") || n.Contains("multiplier") || n.Contains("offset") || n.Contains("override") || n.Contains("opacity") || n.Contains("color"))
                return 3;
            return 2;
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
                if (prop.Name == "Time" || prop.Name == "Easing" || prop.Name == "AddTime" || prop.Name == "RelativeTime") continue;
                if (prop.Name == "Id" || prop.Name == "ParentId" || prop.Name == "TargetId" || prop.Name == "States" || prop.Name == "Keyframes") continue;

                if (IsStaticDnaProperty(prop.Name) && !_isRoot) continue;

                object val = prop.GetValue(_currentState);
                bool isActive = (val != null);

                // ✨ 核心重修：修正可空 UnitFloat 初始默认判断
                if (isActive && val is UnitFloat uf && uf.Value == 0 && uf.Unit == ReferenceUnit.World)
                {
                    if (!_manuallyActivatedProps.Contains(prop.Name)) isActive = false;
                }

                int cat = GetPropertyCategory(prop.Name);

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

                _manuallyActivatedProps.Add(prop.Name);
                prop.SetValue(_currentState, defaultVal);
                LoadState(_currentState, _currentTitle, _rootState, _isRoot);
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
                    LoadState(_currentState, _currentTitle, _rootState, _isRoot);
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

        public bool ValidateAndSave() => true;
    }
}