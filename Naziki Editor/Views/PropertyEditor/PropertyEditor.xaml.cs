using Naziki_Editor.Models;
using System.Windows;

namespace Naziki_Editor.Views
{
    public partial class PropertyEditor : Window
    {
        private StoryboardObject _editingObject;
        private StoryboardRoot _root;
        private string _originalId;

        public PropertyEditor(StoryboardObject targetObject, StoryboardRoot root)
        {
            InitializeComponent();
            _root = root;
            _originalId = targetObject.Id ?? "";

            // 1. 完美的克隆魔法
            string jsonClone = Newtonsoft.Json.JsonConvert.SerializeObject(targetObject);
            _editingObject = (StoryboardObject)Newtonsoft.Json.JsonConvert.DeserializeObject(jsonClone, targetObject.GetType());

            // 2. 将数据分发给四个子模块！(接口我们稍后去 UserControl 里写)
            // ModIdentity.LoadData(_editingObject, _root);
            // ModInitialState.LoadData(_editingObject);
            // ModFrameList.LoadData(_editingObject);
            // ModFrameDetails.LoadData(null); // 初始为空，等列表点击时再传

            BtnCancel.Click += (s, e) => { this.DialogResult = false; };
            BtnSave.Click += BtnSave_Click;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            // TODO: 让各个子模块校验自己的数据（比如查重名）
            // if (!ModIdentity.ValidateAndSave()) return;
            // ModInitialState.SaveData();
            // ...

            this.Tag = _editingObject;
            this.DialogResult = true;
        }
    }
}