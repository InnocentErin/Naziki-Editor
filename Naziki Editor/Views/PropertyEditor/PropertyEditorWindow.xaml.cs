using Naziki_Editor.Models;
using System.Windows;
using Naziki_Editor.Models;
using System.Windows;

namespace Naziki_Editor.Views.PropertyEditor
{
    public partial class PropertyEditorWindow : Window
    {
        private StoryboardObject _editingObject;
        private StoryboardRoot _root;
        private string _originalId;
        private C2Chart _chart;

        public PropertyEditorWindow(StoryboardObject targetObject, StoryboardRoot root, C2Chart chart)
        {
            InitializeComponent();
            _root = root;
            _chart = chart;
            _originalId = targetObject.Id ?? "";

            // 1. 完美的克隆魔法
            string jsonClone = Newtonsoft.Json.JsonConvert.SerializeObject(targetObject);
            _editingObject = (StoryboardObject)Newtonsoft.Json.JsonConvert.DeserializeObject(jsonClone, targetObject.GetType());

            // 2. 将数据分发给四个子模块！(接口我们稍后去 UserControl 里写)

            // ✨ 激活第一模块！把克隆体和字典交给它！
            ModIdentity.LoadData(_editingObject, _root, _chart);

            BtnCancel.Click += (s, e) => { this.DialogResult = false; };
            BtnSave.Click += BtnSave_Click;

            // ModInitialState.LoadData(_editingObject);
            // ModFrameList.LoadData(_editingObject);
            // ModFrameDetails.LoadData(null); // 初始为空，等列表点击时再传

            BtnCancel.Click += (s, e) => { this.DialogResult = false; };
            BtnSave.Click += BtnSave_Click;
        }

        private void BtnSave_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            // ✨ 查岗：让身份模块自己检查有没有错，如果返回 false，直接停止保存并留在这个窗口！
            if (!ModIdentity.ValidateAndSave())
            {
                return;
            }

            // 如果全都顺利通过，再放行
            this.Tag = _editingObject;
            this.DialogResult = true;
        }
    }
}