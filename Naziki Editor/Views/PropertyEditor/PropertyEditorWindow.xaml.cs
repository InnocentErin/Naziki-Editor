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
        // ✨ 核心升级：将原有的旧基类全部替换成我们全新的时空分离型接口与新模板实体！
        private IStoryboardEntity _editingObject;
        private string _originalId;
        private ProjectDataContext _context;

        private bool _isTemplateMode = false;
        private string _templateName;
        private C2Template _editingTemplate;

        // 🌟 构造函数一：适配普通事件对象的属性编辑
        public PropertyEditorWindow(IStoryboardEntity targetObject, ProjectDataContext context)
        {
            InitializeComponent();

            _context = context;
            _originalId = targetObject.Id ?? "";

            // 1. 完美的克隆魔法（不带自定义转换器，纯净保留内存中的 BaseState 和 Keyframes）
            string jsonClone = Core.StoryboardSerializer.ToJson(targetObject);
            _editingObject = (IStoryboardEntity)JsonConvert.DeserializeObject(jsonClone, targetObject.GetType(), Core.StoryboardSerializer.GetSettings());

            // 2. 将数据分发给各个子模块！
            ModIdentity.LoadData(_editingObject, _context);
            ModFrameList.LoadData(_editingObject, _context);

            // ⚡ 强绑定专线：只要列表选了帧，就塞给详情编辑面板
            ModFrameList.OnFrameSelected += (state, title, rootState, isRoot) =>
                ModFrameDetails.LoadState(state, title, rootState, isRoot, _context);

            BtnCancel.Click += (s, e) => { this.DialogResult = false; };
            BtnSave.Click += BtnSave_Click;
        }

        // 🌟 构造函数二：模板专属的特化重载构造函数 (双轨制)
        public PropertyEditorWindow(string templateName, C2Template targetTemplate, ProjectDataContext context)
        {
            InitializeComponent();
            _context = context;
            _isTemplateMode = true;
            _templateName = templateName;

            // ==========================================================
            // 🧵【完美的完全体缝合代码】（既消灭了Bug，又接通了新功能！）
            // ==========================================================

            // 1. 完美的克隆魔法：确保用户点取消时绝对不污染原件
            string jsonClone = Core.StoryboardSerializer.ToJson(targetTemplate);
            _editingTemplate = JsonConvert.DeserializeObject<C2Template>(jsonClone, Core.StoryboardSerializer.GetSettings());

            // 🌟【新线 A】：从项目账本里读取类型，如果新模板没记录，就启动雷达智能推断！
            TemplateType tType = TemplateType.Generic;
            if (_context.ProjectData.TemplateTypes != null && _context.ProjectData.TemplateTypes.ContainsKey(templateName))
                tType = _context.ProjectData.TemplateTypes[templateName];
            else
                tType = Core.TemplateManager.InferTemplateType(_editingTemplate);

            // 2. 改造身份面板：把原来的 TxtObjectId.Text 升级为调用我们的新初始化方法！
            ModIdentity.LoadTemplateData(templateName, tType);

            // （保留你原本的隐藏 Parent 控件的防呆逻辑，非常棒！）
            ModIdentity.TxtParentId.Visibility = Visibility.Collapsed;
            if (ModIdentity.TxtParentId.Parent is Grid parentGrid) parentGrid.Visibility = Visibility.Collapsed;

            // 🌟【新线 B】：把推断出来的门派类型喂给右侧详情面板，开启属性白名单拦截！
            ModFrameDetails.SetTemplateTypeLimit(tType);

            // 🌟【新线 C】：死死栓住身份面板的下拉菜单！只要用户切换门派，立刻同步记账并刷新右侧面板！
            ModIdentity.OnTemplateTypeChanged += (newType) =>
            {
                if (_context.ProjectData.TemplateTypes == null)
                    _context.ProjectData.TemplateTypes = new System.Collections.Generic.Dictionary<string, TemplateType>();

                _context.ProjectData.TemplateTypes[templateName] = newType; // 永久记入小本本
                ModFrameDetails.SetTemplateTypeLimit(newType); // 强行刷新右侧
            };

            // 3. 核心解封：把单帧详情面板喂给模板全局字典，开启上锁雷达
            ModFrameDetails.InitTemplates(_context.Storyboard.templates);

            // 4. 时空对接：让关键帧列表模块以“模板特化模式”加载
            ModFrameList.OnFrameSelected += (state, title, rootState, isRoot) =>
                ModFrameDetails.LoadState(state, title, rootState, isRoot, _context);

            // 🚀 激活列表：专门传入模板和状态数组
            ModFrameList.LoadTemplateData(_editingTemplate, _context);

            BtnCancel.Click += (s, e) => { this.DialogResult = false; };
            BtnSave.Click += BtnSave_Click;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            // ✨ 模板模式下的级联保存与更名拦截
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