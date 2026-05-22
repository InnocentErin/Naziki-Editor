using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Naziki_Editor.Models;
using Naziki_Editor.State;

namespace Naziki_Editor.Views.PropertyEditor
{
    public partial class PropertyEditor_InitialState : UserControl
    {
        private StoryboardObject _editingObject; // ✨ 这个就是我们克隆出来的那个对象，我们要在它身上检查和修改 States[0]，但不动原件！等到最后保存的时候再把它放回去！
        private ProjectDataContext _context; // ✨ 可能会用到的项目数据上下文，先预留着
        private object _firstStateReference; // ✨ 用来保存抓取到的 States[0] 引用

        public PropertyEditor_InitialState()
        {
            InitializeComponent();
            BtnCreateInitial.Click += BtnCreateInitial_Click;
        }

        // ==========================================
        // 📥 接口 1：接收数据并判定是否有 States[0]
        // ==========================================
        public void LoadData(StoryboardObject editingObj, ProjectDataContext context)
        {
            _editingObject = editingObj;
            _context = context;
            RefreshUI();
        }

        private void RefreshUI()
        {
            _firstStateReference = null;

            // 检查克隆体肚子里有没有第 0 帧
            if (_editingObject is Sprite s && s.States?.Count > 0) _firstStateReference = s.States[0];
            else if (_editingObject is Text t && t.States?.Count > 0) _firstStateReference = t.States[0];
            else if (_editingObject is Line l && l.States?.Count > 0) _firstStateReference = l.States[0];
            else if (_editingObject is Video v && v.States?.Count > 0) _firstStateReference = v.States[0];
            else if (_editingObject is Controller c && c.States?.Count > 0) _firstStateReference = c.States[0];
            else if (_editingObject is NoteController nc && nc.States?.Count > 0) _firstStateReference = nc.States[0];

            if (_firstStateReference != null)
            {
                PanelUninitialized.Visibility = Visibility.Collapsed;
                PanelProperties.Visibility = Visibility.Visible;
                LoadFirstFrameToUI(); // 呼叫打印机
            }
            else
            {
                PanelUninitialized.Visibility = Visibility.Visible;
                PanelProperties.Visibility = Visibility.Collapsed;
            }
        }

        // ==========================================
        // ➕ 生成初始状态法术
        // ==========================================
        private void BtnCreateInitial_Click(object sender, RoutedEventArgs e)
        {
            // 根据不同的物种，强行给它们塞入一个默认的第 0 帧！
            if (_editingObject is Sprite s)
            {
                s.States = s.States ?? new List<SpriteState>();
                s.States.Add(new SpriteState { Time = 0.0f, Opacity = 1.0f });
            }
            else if (_editingObject is Text t)
            {
                t.States = t.States ?? new List<TextState>();
                t.States.Add(new TextState { Time = 0.0f, Opacity = 1.0f });
            }
            else if (_editingObject is Line l)
            {
                l.States = l.States ?? new List<LineState>();
                l.States.Add(new LineState { Time = 0.0f, Opacity = 1.0f });
            }
            else if (_editingObject is Video v)
            {
                v.States = v.States ?? new List<VideoState>();
                v.States.Add(new VideoState { Time = 0.0f });
            }
            else if (_editingObject is Controller c)
            {
                c.States = c.States ?? new List<ControllerState>();
                c.States.Add(new ControllerState { Time = 0.0f });
            }
            else if (_editingObject is NoteController nc)
            {
                nc.States = nc.States ?? new List<NoteControllerState>();
                nc.States.Add(new NoteControllerState { Time = 0.0f });
            }

            RefreshUI(); // 刷新界面，撤下警告网！
        }

        // ==========================================
        // 🖨️ 打印机：提取第 0 帧的数据放到文本框里
        // ==========================================
        private void LoadFirstFrameToUI()
        {
            if (_firstStateReference == null) return;
            Type stateType = _firstStateReference.GetType();

            // 1. 读取时间 (所有人都有 Time)
            var propTime = stateType.GetProperty("Time");
            if (propTime != null)
            {
                object timeObj = propTime.GetValue(_firstStateReference);
                TxtTime.Text = timeObj?.ToString() ?? "0";
            }

            // 2. 读取透明度 (有的对象没透明度)
            var propOpacity = stateType.GetProperty("Opacity");
            if (propOpacity != null)
            {
                RowOpacity.Visibility = Visibility.Visible;
                object op = propOpacity.GetValue(_firstStateReference);
                TxtOpacity.Text = op?.ToString() ?? "1";
            }
            else RowOpacity.Visibility = Visibility.Collapsed;

            // 3. 读取 X 坐标 (解析 UnitFloat)
            var propX = stateType.GetProperty("X");
            if (propX != null)
            {
                RowX.Visibility = Visibility.Visible;
                UnitFloat xObj = propX.GetValue(_firstStateReference) as UnitFloat;
                if (xObj != null)
                {
                    TxtValueX.Text = xObj.Value.ToString();
                    SelectComboBoxByUnit(CmbUnitX, xObj.Unit);
                }
                else TxtValueX.Text = "0"; // 空的防呆
            }
            else RowX.Visibility = Visibility.Collapsed;

            // 4. 读取 Y 坐标 (解析 UnitFloat)
            var propY = stateType.GetProperty("Y");
            if (propY != null)
            {
                RowY.Visibility = Visibility.Visible;
                UnitFloat yObj = propY.GetValue(_firstStateReference) as UnitFloat;
                if (yObj != null)
                {
                    TxtValueY.Text = yObj.Value.ToString();
                    SelectComboBoxByUnit(CmbUnitY, yObj.Unit);
                }
                else TxtValueY.Text = "0";
            }
            else RowY.Visibility = Visibility.Collapsed;
        }

        // ==========================================
        // 📤 接口 2：终极防呆拦截网
        // ==========================================
        public bool ValidateAndSave()
        {
            if (PanelUninitialized.Visibility == Visibility.Visible)
            {
                MessageBox.Show("指挥官！当前对象还没有初始属性呢！\n请点击【➕ 立即生成初始属性】后再保存哦！", "防呆拦截", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (_firstStateReference == null) return false;
            Type stateType = _firstStateReference.GetType();

            try
            {
                // 保存 Time (支持数字和字符串锚点)
                var propTime = stateType.GetProperty("Time");
                if (propTime != null)
                {
                    string timeInput = TxtTime.Text.Trim();
                    if (float.TryParse(timeInput, out float fTime)) propTime.SetValue(_firstStateReference, fTime);
                    else propTime.SetValue(_firstStateReference, timeInput); // 字符串锚点，比如 start:841
                }

                // 保存 Opacity
                var propOpacity = stateType.GetProperty("Opacity");
                if (propOpacity != null && RowOpacity.Visibility == Visibility.Visible)
                {
                    if (float.TryParse(TxtOpacity.Text, out float op))
                        propOpacity.SetValue(_firstStateReference, op);
                }

                // 保存 X 坐标
                var propX = stateType.GetProperty("X");
                if (propX != null && RowX.Visibility == Visibility.Visible)
                {
                    if (float.TryParse(TxtValueX.Text, out float xVal))
                    {
                        ReferenceUnit xUnit = GetUnitFromComboBox(CmbUnitX);
                        propX.SetValue(_firstStateReference, new UnitFloat { Value = xVal, Unit = xUnit });
                    }
                }

                // 保存 Y 坐标
                var propY = stateType.GetProperty("Y");
                if (propY != null && RowY.Visibility == Visibility.Visible)
                {
                    if (float.TryParse(TxtValueY.Text, out float yVal))
                    {
                        ReferenceUnit yUnit = GetUnitFromComboBox(CmbUnitY);
                        propY.SetValue(_firstStateReference, new UnitFloat { Value = yVal, Unit = yUnit });
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存初始参数时发生异常 QAQ：{ex.Message}\n请检查您输入的数字格式是否正确！", "解析错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            return true;
        }








        // 🛠️ 辅助工具：反向查找下拉菜单
        private void SelectComboBoxByUnit(ComboBox cmb, ReferenceUnit unit)
        {
            string unitStr = unit.ToString();
            foreach (ComboBoxItem item in cmb.Items)
            {
                if (item.Tag.ToString() == unitStr)
                {
                    cmb.SelectedItem = item;
                    return;
                }
            }
            cmb.SelectedIndex = 0; // 找不到就默认 World
        }

        // 🛠️ 辅助工具：提取下拉菜单的枚举
        private ReferenceUnit GetUnitFromComboBox(ComboBox cmb)
        {
            if (cmb.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                if (Enum.TryParse(item.Tag.ToString(), out ReferenceUnit unit))
                    return unit;
            }
            return ReferenceUnit.World;
        }
    }
}