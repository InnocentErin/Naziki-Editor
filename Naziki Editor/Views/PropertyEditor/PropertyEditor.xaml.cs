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
            }
            else if (_editingObject is Controller)
            {
                PanelController.Visibility = Visibility.Visible;
                PanelRenderObject.Visibility = Visibility.Collapsed;
                PanelNoteController.Visibility = Visibility.Collapsed;
                Title = "属性编辑器 - [全局控制器]";
            }
            else if (_editingObject is NoteController)
            {
                PanelNoteController.Visibility = Visibility.Visible;
                PanelRenderObject.Visibility = Visibility.Collapsed;
                PanelController.Visibility = Visibility.Collapsed;
                Title = "属性编辑器 - [音符控制器]";
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
    }
}