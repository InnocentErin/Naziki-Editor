using Naziki_Editor.Models;
using System.Windows;
using Naziki_Editor.Models;
using System.Windows;
using Naziki_Editor.State;

namespace Naziki_Editor.Views.PropertyEditor
{
    public partial class PropertyEditorWindow : Window
    {
        private StoryboardObject _editingObject;
        private string _originalId;
        private ProjectDataContext _context;


        // 🌟 这个窗口的职责就是：克隆一份数据，给四个模块分发，等四个模块都说 OK 了再放行保存！如果有一个模块说不 OK 就立刻停下来不保存！
        public PropertyEditorWindow(StoryboardObject targetObject, ProjectDataContext context)
        {
            InitializeComponent();

            _context = context;
            _originalId = targetObject.Id ?? "";

            // 1. 完美的克隆魔法
            string jsonClone = Newtonsoft.Json.JsonConvert.SerializeObject(targetObject);
            _editingObject = (StoryboardObject)Newtonsoft.Json.JsonConvert.DeserializeObject(jsonClone, targetObject.GetType());

            // 2. 将数据分发给四个子模块！(接口我们稍后去 UserControl 里写)

            // ✨ 激活第一模块！把克隆体和字典交给它！
            ModIdentity.LoadData(_editingObject, _context);
            ModInitialState.LoadData(_editingObject, _context);

            BtnCancel.Click += (s, e) => { this.DialogResult = false; };
            BtnSave.Click += BtnSave_Click;
        }

        // ModInitialState.LoadData(_editingObject);
        // ModFrameList.LoadData(_editingObject);
        // ModFrameDetails.LoadData(null); // 初始为空，等列表点击时再传


        private void BtnSave_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            // ✨ 查岗：让身份模块自己检查有没有错，如果返回 false，直接停止保存并留在这个窗口！
            if (!ModIdentity.ValidateAndSave()) return;
            if (!ModInitialState.ValidateAndSave()) return;

            // 如果全都顺利通过，再放行
            this.Tag = _editingObject;
            this.DialogResult = true;
        }
    }
}