using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Naziki_Editor.Models;

namespace Naziki_Editor.Views.PropertyEditor
{
    public partial class PropertyEditor_FrameDetails : UserControl
    {
        private object _currentState;

        public PropertyEditor_FrameDetails()
        {
            InitializeComponent();
        }

        // ==========================================
        // 📥 接口：接收选中的关键帧并渲染 UI
        // ==========================================
        public void LoadState(object stateReference, string frameTitle)
        {
            _currentState = stateReference;

            if (_currentState == null)
            {
                PanelDetails.Visibility = Visibility.Collapsed;
                TxtEmptyState.Visibility = Visibility.Visible;
                return;
            }

            PanelDetails.Visibility = Visibility.Visible;
            TxtEmptyState.Visibility = Visibility.Collapsed;
            TxtFrameTitle.Text = $"🎬 当前编辑: {frameTitle}";

            Type t = _currentState.GetType();

            // 1. 必有属性：Time
            TxtTime.Text = t.GetProperty("Time")?.GetValue(_currentState)?.ToString() ?? "0";

            // 2. 动态显示/隐藏：透明度
            SetupTextRow(RowOpacity, TxtOpacity, t.GetProperty("Opacity"));

            // 3. 动态显示/隐藏：旋转与 Z 轴
            SetupTextRow(RowRotZ, TxtRotZ, t.GetProperty("RotZ") ?? t.GetProperty("Rot_Z"));
            SetupTextRow(RowZ, TxtZ, t.GetProperty("Z"));

            // 4. 动态显示/隐藏：缩放 X 和 Y
            var propScaleX = t.GetProperty("ScaleX") ?? t.GetProperty("Scale_X");
            if (propScaleX != null)
            {
                RowScale.Visibility = Visibility.Visible;
                TxtScaleX.Text = propScaleX.GetValue(_currentState)?.ToString() ?? "1";
                TxtScaleY.Text = (t.GetProperty("ScaleY") ?? t.GetProperty("Scale_Y"))?.GetValue(_currentState)?.ToString() ?? "1";
            }
            else RowScale.Visibility = Visibility.Collapsed;

            // 5. 动态解析坐标 (UnitFloat)
            SetupUnitFloatRow(RowX, TxtValueX, CmbUnitX, t.GetProperty("X"));
            SetupUnitFloatRow(RowY, TxtValueY, CmbUnitY, t.GetProperty("Y"));

            // 6. 动态解析颜色与缓动
            var propColor = t.GetProperty("Color");
            if (propColor != null)
            {
                RowColor.Visibility = Visibility.Visible;
                // 暂时简单的映射到文本框，后续开发颜色选择器时会深化解析
                TxtColorHex.Text = propColor.GetValue(_currentState)?.ToString() ?? "#FFFFFF";
            }
            else RowColor.Visibility = Visibility.Collapsed;

            var propEasing = t.GetProperty("Easing");
            if (propEasing != null)
            {
                RowEasing.Visibility = Visibility.Visible;
                TxtEasing.Text = propEasing.GetValue(_currentState)?.ToString() ?? "linear";
            }
            else RowEasing.Visibility = Visibility.Collapsed;
        }

        // ==========================================
        // 📤 接口：将面板数据验证并保存回对象
        // ==========================================
        public bool ValidateAndSave()
        {
            if (_currentState == null) return true; // 空状态不需要保存，直接放行

            Type t = _currentState.GetType();
            try
            {
                SaveProperty(t.GetProperty("Time"), TxtTime.Text, isStringAllowed: true);
                SaveProperty(t.GetProperty("Opacity"), TxtOpacity.Text);
                SaveProperty(t.GetProperty("Z"), TxtZ.Text);
                SaveProperty(t.GetProperty("RotZ") ?? t.GetProperty("Rot_Z"), TxtRotZ.Text);

                if (RowScale.Visibility == Visibility.Visible)
                {
                    SaveProperty(t.GetProperty("ScaleX") ?? t.GetProperty("Scale_X"), TxtScaleX.Text);
                    SaveProperty(t.GetProperty("ScaleY") ?? t.GetProperty("Scale_Y"), TxtScaleY.Text);
                }

                if (RowX.Visibility == Visibility.Visible) SaveUnitFloat(t.GetProperty("X"), TxtValueX.Text, CmbUnitX);
                if (RowY.Visibility == Visibility.Visible) SaveUnitFloat(t.GetProperty("Y"), TxtValueY.Text, CmbUnitY);

                if (RowEasing.Visibility == Visibility.Visible)
                {
                    var prop = t.GetProperty("Easing");
                    if (prop != null) prop.SetValue(_currentState, TxtEasing.Text.Trim());
                }

                if (RowColor.Visibility == Visibility.Visible)
                {
                    // 预留给颜色解析保存逻辑
                    // t.GetProperty("Color")?.SetValue(_currentState, ParseCytoidColor(TxtColorHex.Text));
                }

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"参数保存失败 QAQ：{ex.Message}\n请检查您输入的格式是否正确！", "解析异常", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        // ==========================================
        // 👆 用户的梦幻弹窗接口预留
        // ==========================================
        private void BtnPickColor_Click(object sender, RoutedEventArgs e)
        {
            // 💡 这里是调用指挥官构思的【选色器弹窗】的入口！
            MessageBox.Show("即将召唤调色盘弹窗！(待实现)", "开发中");
        }

        private void BtnPickEasing_Click(object sender, RoutedEventArgs e)
        {
            // 💡 这里是调用指挥官构思的【缓动分类弹窗】的入口！
            MessageBox.Show("即将召唤缓动分类选择器！(待实现)", "开发中");
        }

        // ==========================================
        // 🛠️ 辅助法术模块 (动态UI控制)
        // ==========================================
        private void SetupTextRow(Grid row, TextBox txt, PropertyInfo prop)
        {
            if (prop != null)
            {
                row.Visibility = Visibility.Visible;
                txt.Text = prop.GetValue(_currentState)?.ToString() ?? "0";
            }
            else row.Visibility = Visibility.Collapsed;
        }

        private void SetupUnitFloatRow(Grid row, TextBox txtVal, ComboBox cmb, PropertyInfo prop)
        {
            if (prop != null)
            {
                row.Visibility = Visibility.Visible;
                UnitFloat uf = prop.GetValue(_currentState) as UnitFloat;
                if (uf != null)
                {
                    txtVal.Text = uf.Value.ToString();
                    SelectComboBoxByUnit(cmb, uf.Unit);
                }
                else { txtVal.Text = "0"; cmb.SelectedIndex = 0; }
            }
            else row.Visibility = Visibility.Collapsed;
        }

        private void SaveProperty(PropertyInfo prop, string input, bool isStringAllowed = false)
        {
            if (prop == null) return;
            string val = input.Trim();
            if (float.TryParse(val, out float fVal)) prop.SetValue(_currentState, fVal);
            else if (isStringAllowed) prop.SetValue(_currentState, val); // 允许 Time 填入 start:xxx 等锚点
        }

        private void SaveUnitFloat(PropertyInfo prop, string input, ComboBox cmb)
        {
            if (prop == null) return;
            if (float.TryParse(input.Trim(), out float val))
            {
                ReferenceUnit unit = ReferenceUnit.World;
                if (cmb.SelectedItem is ComboBoxItem item && Enum.TryParse(item.Tag.ToString(), out ReferenceUnit u)) unit = u;
                prop.SetValue(_currentState, new UnitFloat { Value = val, Unit = unit });
            }
        }

        private void SelectComboBoxByUnit(ComboBox cmb, ReferenceUnit unit)
        {
            string uStr = unit.ToString();
            foreach (ComboBoxItem item in cmb.Items)
                if (item.Tag.ToString() == uStr) { cmb.SelectedItem = item; return; }
            cmb.SelectedIndex = 0;
        }
    }
}