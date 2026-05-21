using Naziki_Editor.Core;
using Naziki_Editor.Models;
using Newtonsoft.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Linq;

namespace Naziki_Editor.Views
{
    public partial class PropertyEditor : Window
    {
        // 🔮 正在编辑的克隆体对象（绝不直接修改传入的原件）
        private StoryboardObject _editingObject;
        // ⏱️ 弹窗专属的“局部时光机”
        private UndoRedoManager _localTimeMachine = new UndoRedoManager();
        // ✨ 新增：保存整个宇宙的引用和最初的身份证号
        private StoryboardRoot _root;
        private string _originalId;

        // 🌟 预留：如果需要在编辑器里动态展示可用的轨道蓝图列表，可以在这里存一份当前的快照，随时刷新它
        private List<Core.TrackBlueprint> _currentAvailableBlueprints;

        public PropertyEditor(StoryboardObject targetObject, StoryboardRoot root)
        {
            InitializeComponent();

            _root = root;
            _originalId = targetObject.Id ?? ""; // 记住原来的名字

            // 1. 深拷贝：把传进来的对象序列化再反序列化，彻底斩断灵魂连结！
            // 🌟 完美多态克隆：让 Json 解析器按照目标最真实的类型（比如 Sprite）去复原肉体！
            string jsonClone = JsonConvert.SerializeObject(targetObject);
            _editingObject = (StoryboardObject)JsonConvert.DeserializeObject(jsonClone, targetObject.GetType());

            // 2. 拍下最初的快照，作为时光机的起点
            _localTimeMachine.RecordSnapshot(_editingObject);

            // 3. 部署“全局雷达”：监听所有 UI 元素的失焦事件
            this.AddHandler(UIElement.LostFocusEvent, new RoutedEventHandler(OnAnyControlLostFocus));

            // 4. 监听键盘快捷键 (Ctrl+Z / Ctrl+Y)
            this.KeyDown += PropertyEditor_KeyDown;

            // 5. 初始化专属面板显示
            SetupIdentityPanel();

            // 绑定下方按钮事件
            BtnSave.Click += BtnSave_Click;
            BtnCancel.Click += BtnCancel_Click;



            BtnActivateTrack.Click += (s, e) =>
            {
                if (BtnActivateTrack.ContextMenu != null)
                {
                    BtnActivateTrack.ContextMenu.PlacementTarget = BtnActivateTrack;
                    BtnActivateTrack.ContextMenu.IsOpen = true;
                }
            };

        }

        // ==========================================
        // 📡 全局雷达：自动拦截并存档
        // ==========================================
        private void OnAnyControlLostFocus(object sender, RoutedEventArgs e)
        {
            // 过滤：只记录输入类控件的失焦（比如文本框、下拉框、勾选框等）
            if (e.OriginalSource is TextBox || e.OriginalSource is ComboBox || e.OriginalSource is ToggleButton)
            {
                // ✨ 触发局部存档！
                _localTimeMachine.RecordSnapshot(_editingObject);
            }
        }

        // ==========================================
        // ⌨️ 弹窗专属快捷键 (撤销与重做)
        // ==========================================
        private void PropertyEditor_KeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (e.Key == Key.Z) // ⏪ 撤销
                {
                    // 强制夺取焦点，确保正在输入的数据能先保存下来
                    Keyboard.ClearFocus();

                    var prevState = _localTimeMachine.Undo(_editingObject, out bool success);
                    if (success && prevState != null)
                    {
                        _editingObject = prevState;
                        RefreshAllUI(); // 刷新整个界面的数据绑定
                    }
                    e.Handled = true;
                }
                else if (e.Key == Key.Y) // ⏩ 重做
                {
                    Keyboard.ClearFocus();

                    var nextState = _localTimeMachine.Redo(_editingObject, out bool success);
                    if (success && nextState != null)
                    {
                        _editingObject = nextState;
                        RefreshAllUI();
                    }
                    e.Handled = true;
                }

            }


        }

        // ==========================================
        // 🎭 动态拼图法术与 UI 刷新
        // ==========================================
        private void SetupIdentityPanel()
        {
            TxtObjectId.Text = _editingObject.Id;

            // 根据类型展示不同的身份面板
            if (_editingObject is Sprite || _editingObject is Text || _editingObject is Line || _editingObject is Video)
            {
                PanelRenderObject.Visibility = Visibility.Visible;
                PanelController.Visibility = Visibility.Collapsed;
                PanelNoteController.Visibility = Visibility.Collapsed;
                Title = $"属性编辑器 - [场景演员: {_editingObject.GetType().Name}]";

                // ✨ 载入场景对象的蓝图辞典！
                _currentAvailableBlueprints = Core.TrackBlueprintManager.RenderObjectBlueprints;
            }
            else if (_editingObject is Controller)
            {
                PanelController.Visibility = Visibility.Visible;
                PanelRenderObject.Visibility = Visibility.Collapsed;
                PanelNoteController.Visibility = Visibility.Collapsed;
                Title = "属性编辑器 - [全局控制器]";

                // ✨ 载入场景控制器的蓝图辞典！
                _currentAvailableBlueprints = Core.TrackBlueprintManager.ControllerBlueprints;
            }
            else if (_editingObject is NoteController)
            {
                PanelNoteController.Visibility = Visibility.Visible;
                PanelRenderObject.Visibility = Visibility.Collapsed;
                PanelController.Visibility = Visibility.Collapsed;
                Title = "属性编辑器 - [音符控制器]";

                // ✨ 载入音符控制器的蓝图辞典！
                _currentAvailableBlueprints = Core.TrackBlueprintManager.NoteControllerBlueprints;
            }
        }

        private void RefreshAllUI()
        {
            // TODO: 当触发撤销/重做时，调用这个方法把 _editingObject 的最新数据重新填到界面上
            SetupIdentityPanel();
        }

        // ==========================================
        // 💾 保存与取消
        // ==========================================
        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            Keyboard.ClearFocus();
            FocusManager.SetFocusedElement(this, this);

            string newId = TxtObjectId.Text.Trim();

            // 🛡️ 防线一：不许为空！
            if (string.IsNullOrEmpty(newId))
            {
                TxtIdWarning.Text = "⚠️ ID 绝对不能为空哦！";
                TxtIdWarning.Visibility = Visibility.Visible;
                return; // 拦截！不让窗口关闭！
            }

            // 🛡️ 防线二：不许重名！(且要排除自己原来的名字)
            if (newId != _originalId && IsIdConflict(newId))
            {
                TxtIdWarning.Text = $"⚠️ ID '{newId}' 已经被占用啦，请换一个名称！";
                TxtIdWarning.Visibility = Visibility.Visible;
                return; // 拦截！
            }

            // 警报解除
            TxtIdWarning.Visibility = Visibility.Collapsed;

            // 保存并放行
            _editingObject.Id = newId;
            this.Tag = _editingObject;
            this.DialogResult = true;
        }

        // 🔍 查字典：遍历整个宇宙，看看有没有人叫这个名字
        private bool IsIdConflict(string id)
        {
            if (_root == null) return false;

            if (_root.sprites?.Any(x => x.Id == id) == true) return true;
            if (_root.texts?.Any(x => x.Id == id) == true) return true;
            if (_root.lines?.Any(x => x.Id == id) == true) return true;
            if (_root.videos?.Any(x => x.Id == id) == true) return true;
            if (_root.controllers?.Any(x => x.Id == id) == true) return true;
            if (_root.note_controllers?.Any(x => x.Id == id) == true) return true;

            return false;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }



        // ==========================================
        // 🔮 智能反射引擎：打通 JSON 蛇形名与 C# 驼峰名
        // ==========================================
        private System.Reflection.MemberInfo FindMemberIgnoreCase(object target, string jsonName)
        {
            string cleanName = jsonName.Replace("_", "").ToLower(); // 例如 ui_opacity 变成 uiopacity

            // 找字段 (Field)
            foreach (var field in target.GetType().GetFields())
                if (field.Name.ToLower() == cleanName) return field;

            // 找属性 (Property)
            foreach (var prop in target.GetType().GetProperties())
                if (prop.Name.ToLower() == cleanName) return prop;

            return null;
        }

        private object GetPropertyValue(object target, string jsonName)
        {
            var member = FindMemberIgnoreCase(target, jsonName);
            if (member is System.Reflection.FieldInfo f) return f.GetValue(target);
            if (member is System.Reflection.PropertyInfo p) return p.GetValue(target);
            return null;
        }

        private void SetPropertyValue(object target, string jsonName, object value)
        {
            var member = FindMemberIgnoreCase(target, jsonName);
            try
            {
                if (member is System.Reflection.FieldInfo f)
                {
                    // 处理 Cytoid 特有的 UnitFloat
                    if (f.FieldType.Name == "UnitFloat" && value is float floatVal)
                    {
                        var unitFloat = Activator.CreateInstance(f.FieldType);
                        f.FieldType.GetField("Value").SetValue(unitFloat, floatVal);
                        f.SetValue(target, unitFloat);
                    }
                    // 处理普通的 bool?, float? 等
                    else
                    {
                        object safeValue = value == null ? null : Convert.ChangeType(value, Nullable.GetUnderlyingType(f.FieldType) ?? f.FieldType);
                        f.SetValue(target, safeValue);
                    }
                }
            }
            catch (Exception ex) { System.Windows.MessageBox.Show($"设置属性 {jsonName} 失败: {ex.Message}"); }
        }




        // ==========================================
        // 🖨️ UI 魔法打印机：根据 State 动态生成轨道
        // ==========================================
        private void RefreshDynamicTracks(object currentState)
        {
            if (currentState == null || _currentAvailableBlueprints == null) return;

            ActiveTracksContainer.Children.Clear();

            // 制作下拉菜单的字典，用来给属性分组
            var menuGroups = new Dictionary<string, MenuItem>();
            ContextMenu addTrackMenu = new ContextMenu();

            foreach (var bp in _currentAvailableBlueprints)
            {
                object currentValue = GetPropertyValue(currentState, bp.JsonName);

                // 🌟 核心判断：如果值不是 null，说明轨道已激活，打印 UI！
                if (currentValue != null)
                {
                    UIElement trackUI = CreateTrackControl(bp, currentState, currentValue);
                    ActiveTracksContainer.Children.Add(trackUI);
                }
                // 🌟 如果值是 null，说明没激活，放进➕号菜单里让用户选！
                else
                {
                    if (!menuGroups.ContainsKey(bp.GroupName))
                    {
                        var groupItem = new MenuItem { Header = bp.GroupName, FontWeight = FontWeights.Bold };
                        menuGroups[bp.GroupName] = groupItem;
                        addTrackMenu.Items.Add(groupItem);
                    }

                    var addItem = new MenuItem { Header = bp.DisplayName };
                    addItem.Click += (s, e) =>
                    {
                        // 玩家点击激活：注入默认值，刷新 UI，存入时光机！
                        SetPropertyValue(currentState, bp.JsonName, bp.DefaultValue);
                        RefreshDynamicTracks(currentState);
                        _localTimeMachine.RecordSnapshot(_editingObject);
                    };
                    menuGroups[bp.GroupName].Items.Add(addItem);
                }
            }

            // 把做好的菜单绑定到按钮上
            BtnActivateTrack.ContextMenu = addTrackMenu;
        }



        // 🌟 核心方法：根据蓝图和当前状态，生成一个轨道的 UI 控件
        private UIElement CreateTrackControl(Core.TrackBlueprint bp, object currentState, object initialValue)
        {
            // 外层的大容器：包含名字、输入框和删除按钮
            Grid trackRow = new Grid { Margin = new Thickness(0, 5, 0, 5) };
            trackRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            trackRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            trackRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 左侧的名字标签
            TextBlock label = new TextBlock { Text = bp.DisplayName, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(label, 0);
            trackRow.Children.Add(label);

            // ❌ 删除轨道按钮（即把数据重新变回 null）
            Button btnDelete = new Button { Content = "❌", Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand, Margin = new Thickness(5, 0, 0, 0) };
            btnDelete.Click += (s, e) =>
            {
                SetPropertyValue(currentState, bp.JsonName, null);
                RefreshDynamicTracks(currentState);
                _localTimeMachine.RecordSnapshot(_editingObject);
            };
            Grid.SetColumn(btnDelete, 2);
            trackRow.Children.Add(btnDelete);

            // 中间的动态输入区
            if (bp.DataType == Core.TrackDataType.Float || bp.DataType == Core.TrackDataType.String)
            {
                // 如果是 UnitFloat，要拆出里面的 Value 显示
                string displayString = initialValue.ToString();
                if (initialValue.GetType().Name == "UnitFloat")
                {
                    var valField = initialValue.GetType().GetField("Value");
                    displayString = valField?.GetValue(initialValue)?.ToString() ?? "0";
                }

                TextBox textBox = new TextBox { Text = displayString, Padding = new Thickness(2) };

                // ✨ 核心绑定：失焦时写回数据，利用“全局雷达”触发存档！
                textBox.LostFocus += (s, e) =>
                {
                    if (float.TryParse(textBox.Text, out float floatVal))
                    {
                        SetPropertyValue(currentState, bp.JsonName, floatVal);
                    }
                };
                Grid.SetColumn(textBox, 1);
                trackRow.Children.Add(textBox);
            }
            else if (bp.DataType == Core.TrackDataType.Boolean)
            {
                CheckBox chk = new CheckBox { IsChecked = (bool)initialValue, VerticalAlignment = VerticalAlignment.Center };
                chk.Checked += (s, e) => { SetPropertyValue(currentState, bp.JsonName, true); _localTimeMachine.RecordSnapshot(_editingObject); };
                chk.Unchecked += (s, e) => { SetPropertyValue(currentState, bp.JsonName, false); _localTimeMachine.RecordSnapshot(_editingObject); };
                Grid.SetColumn(chk, 1);
                trackRow.Children.Add(chk);
            }
            // TODO: Color 和 NoteColorArray 可以先放一个占位 TextBox，后续我们再接上拾色器窗口！

            return trackRow;
        }

    }
}