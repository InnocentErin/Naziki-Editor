using Naziki_Editor.Core;
using Naziki_Editor.Models;
using Naziki_Editor.State;
using Newtonsoft.Json;
using System.Windows;
using System.Windows.Controls;

namespace Naziki_Editor.Views.PropertyEditor
{
    public partial class PropertyEditorWindow : Window
    {
        private StoryboardObject _editingObject;
        private string _originalId;
        private ProjectDataContext _context;


        private bool _isTemplateMode = false;
        private string _templateName;
        private StoryboardTemplate _editingTemplate;









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




        // ==========================================
        // ✨ 新增：模板专属的特化重载构造函数 (双轨制)
        // ==========================================
        public PropertyEditorWindow(string templateName, StoryboardTemplate targetTemplate, ProjectDataContext context)
        {
            InitializeComponent();
            _context = context;
            _isTemplateMode = true;
            _templateName = templateName;

            // 1. 完美的克隆魔法：确保用户点取消时绝对不污染原件
            string jsonClone = Newtonsoft.Json.JsonConvert.SerializeObject(targetTemplate);
            _editingTemplate = Newtonsoft.Json.JsonConvert.DeserializeObject<StoryboardTemplate>(jsonClone);

            // 2. 改造身份面板：模板不需要“跟随目标(Parent)”，把下面藏起来，只留名字编辑！
            ModIdentity.TxtObjectId.Text = templateName;
            ModIdentity.TxtParentId.Visibility = Visibility.Collapsed; // 隐藏无用控件
            if (ModIdentity.TxtParentId.Parent is Grid parentGrid) parentGrid.Visibility = Visibility.Collapsed;

            // 3. 核心解封：把单帧详情面板喂给模板全局字典，开启上锁雷达
            ModFrameDetails.InitTemplates(_context.Storyboard.templates);

            // 4. 时空对接：让关键帧列表模块以“模板特化模式”加载它的 states 数组！
            // 完美绑定：只要列表里选了帧（或者选了虚拟根节点），就强塞给详情面板
            ModFrameList.OnFrameSelected += (state, title, rootState, isRoot) =>
                ModFrameDetails.LoadState(state, title, rootState, isRoot);

            // 🚀 激活列表：专门传入模板和状态数组
            ModFrameList.LoadTemplateData(_editingTemplate, _context);

            BtnCancel.Click += (s, e) => { this.DialogResult = false; };
            BtnSave.Click += BtnSave_Click;
        }






        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            // ✨ 新增：模板模式下的级联保存与更名拦截
            if (_isTemplateMode)
            {
                string newName = ModIdentity.TxtObjectId.Text.Trim();
                if (string.IsNullOrEmpty(newName))
                {
                    MessageBox.Show("指挥官，模板名称绝对不能为空哦！", "保存被拦截");
                    return;
                }

                // 如果改名了，呼叫大管家级联更新全宇宙的引用！
                if (newName != _templateName)
                {
                    Core.TemplateManager.RenameTemplateGlobally(_context.Storyboard, _templateName, newName);
                }

                // 把修改后的完全体克隆实体塞回大本营字典
                _context.Storyboard.templates[newName] = _editingTemplate;

                _context.MarkAsModified(); // 触发未保存标记
                this.DialogResult = true;
                this.Close();
                return;
            }



            // 🛑 呼叫核心安检基站进行拦截
            var validationResult = Core.StoryboardValidator.ValidateStateConflicts(_editingObject);

            if (!validationResult.IsValid)
            {
                MessageBox.Show(validationResult.ErrorMessage, "小艾的防呆纠察雷达", MessageBoxButton.OK, MessageBoxImage.Warning);
                return; // 阻止保存
            }

            // 🟢 安检通过！把克隆好并修改过的最终数据装进 Tag 胶囊里！
            this.Tag = _editingObject;

            // 通知项目有未保存的修改
            _context.MarkAsModified();

            this.DialogResult = true;
            this.Close();
        }





    }
}