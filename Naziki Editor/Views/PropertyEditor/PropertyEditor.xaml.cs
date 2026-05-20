using Naziki_Editor.Models;
using System.Windows;

namespace Naziki_Editor.Views
{
    public partial class PropertyEditor : Window
    {
        // 存储正在编辑的对象的克隆体（防止点取消时污染原数据）
        private StoryboardObject _editingObject;

        // 构造函数：要求呼叫弹窗时，必须交出一个对象！
        public PropertyEditor(StoryboardObject targetObject)
        {
            InitializeComponent();

            // TODO: 这里需要做一个深拷贝(Deep Clone)，以免用户修改一半点取消
            _editingObject = targetObject;

            // 🌟 核心魔法：根据对象类型，动态展现专属身份区
            SetupIdentityPanel();
        }

        // 🎭 动态拼图法术
        private void SetupIdentityPanel()
        {
            // 1. 填入通用 ID
            TxtObjectId.Text = _editingObject.Id;

            // 2. 类型分支判定
            if (_editingObject is Sprite || _editingObject is Text || _editingObject is Line || _editingObject is Video)
            {
                // 场景演员组：显示挂靠和资源，隐藏施法目标
                PanelRenderObject.Visibility = Visibility.Visible;
                PanelController.Visibility = Visibility.Collapsed;
                PanelNoteController.Visibility = Visibility.Collapsed;

                Title = $"属性编辑器 - [场景演员: {_editingObject.GetType().Name}]";
            }
            else if (_editingObject is Controller)
            {
                // 幕后控制器组：显示施法目标，隐藏其他
                PanelController.Visibility = Visibility.Visible;
                PanelRenderObject.Visibility = Visibility.Collapsed;
                PanelNoteController.Visibility = Visibility.Collapsed;

                Title = "属性编辑器 - [全局控制器]";
            }
            else if (_editingObject is NoteController)
            {
                // 音符控制器组：显示音符目标，隐藏其他
                PanelNoteController.Visibility = Visibility.Visible;
                PanelRenderObject.Visibility = Visibility.Collapsed;
                PanelController.Visibility = Visibility.Collapsed;

                Title = "属性编辑器 - [音符控制器]";
            }
        }
    }
}