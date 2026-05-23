using Naziki_Editor.Models;
using Naziki_Editor.State;
using Newtonsoft.Json;
using System.Windows;
using Naziki_Editor.Core;

namespace Naziki_Editor.Views.PropertyEditor
{
    public partial class PropertyEditorWindow : Window
    {
        private StoryboardObject _editingObject;
        private string _originalId;
        private ProjectDataContext _context;
        private StoryboardObject _originalObject;



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

            // ModInitialState.LoadData(_editingObject, _context);

            // 给帧列表模块通电！
            ModFrameList.LoadData(_editingObject, _context);

            // ✨ 致命修改 2：接通专线！只要列表选了帧，就强塞给模块四！
            // (注意：这里目前会报错说模块四没有 LoadState 方法，别急，那是咱们下一步要写的东西！)
            // ✨ 完美修复：让左边的接收端和右边的调用端都带上完整的 4 个参数！
            ModFrameList.OnFrameSelected += (state, title, rootState, isRoot) =>
                ModFrameDetails.LoadState(state, title, rootState, isRoot);


            BtnCancel.Click += (s, e) => { this.DialogResult = false; };
            BtnSave.Click += BtnSave_Click;
        }

        // ModInitialState.LoadData(_editingObject);
        // ModFrameList.LoadData(_editingObject);
        // ModFrameDetails.LoadData(null); // 初始为空，等列表点击时再传


        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            // 🛑 呼叫核心安检基站进行拦截
            var validationResult = Core.StoryboardValidator.ValidateStateConflicts(_editingObject);

            if (!validationResult.IsValid)
            {
                // 收到安检网的拦截情报，由 UI 层负责弹窗警告用户！
                MessageBox.Show(validationResult.ErrorMessage, "小艾的防呆纠察雷达", MessageBoxButton.OK, MessageBoxImage.Warning);
                return; // 阻止保存
            }

            // 🟢 安检通过！执行安全覆盖
            var settings = new JsonSerializerSettings
            {
                ObjectCreationHandling = ObjectCreationHandling.Replace
            };

            string updatedJson = JsonConvert.SerializeObject(_editingObject);
            JsonConvert.PopulateObject(updatedJson, _originalObject, settings);

            // 通知项目有未保存的修改
            _context.MarkAsModified();

            this.DialogResult = true;
            this.Close();
        }
    }
}