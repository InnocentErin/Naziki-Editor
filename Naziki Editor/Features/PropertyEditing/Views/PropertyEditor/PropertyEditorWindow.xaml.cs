using Naziki_Editor.Core;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Common;
using Naziki_Editor.Core.Messaging;
using Naziki_Editor.Core.Storyboard;
using Naziki_Editor.Models;
using Naziki_Editor.State;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Windows;

namespace Naziki_Editor.Views.PropertyEditor
{
    public partial class PropertyEditorWindow : Window
    {
        private ProjectDataContext _context;
        private string _originalId;
        private bool _isTemplateMode = false;
        private string _templateName;
        private C2Template _editingTemplate;

        private IStoryboardEntity _mainObject;
        private IStoryboardEntity _currentActiveObject;

        private readonly IStoryboardRepository _storyboardRepository;
        private readonly IDialogService _dialogService;
        private readonly IPropertyEditorService _propertyEditorService;
        private readonly IMessageBroker _messageBroker;

        public PropertyEditorWindow(IDialogService dialogService, IStoryboardRepository storyboardRepository, IPropertyEditorService propertyEditorService, IMessageBroker messageBroker)
        {
            _dialogService = dialogService;
            _storyboardRepository = storyboardRepository;
            _propertyEditorService = propertyEditorService;
            _messageBroker = messageBroker;
        }

        // ==========================================
        // 🌟 构造函数一：适配普通事件对象的属性编辑
        // ==========================================
        public PropertyEditorWindow(IStoryboardEntity targetObject, ProjectDataContext context, IDialogService dialogService, IStoryboardRepository storyboardRepository, IPropertyEditorService propertyEditorService, IMessageBroker messageBroker)
            : this(dialogService, storyboardRepository, propertyEditorService, messageBroker)
        {
            InitializeComponent();
            ModFrameDetails.InitializeServices(_propertyEditorService, _messageBroker);
            _context = context;

            // 🌟 【小艾的智能命名系统】：根据对象基因自动赋予优雅的 ID！
            if (string.IsNullOrEmpty(targetObject.Id))
            {
                targetObject.Id = new Core.Common.EntityIdService().GenerateUniqueId(targetObject, _context.Storyboard);
            }

            _originalId = targetObject.Id;
            _isTemplateMode = false;

            // 1. 克隆魔法
            var snapshotSerializer = AppServices.GetService<IEditorSnapshotSerializer>();
            string jsonClone = snapshotSerializer.Serialize(targetObject);
            _mainObject = (IStoryboardEntity)snapshotSerializer.Deserialize(jsonClone, targetObject.GetType());

            // 2. 接通神经纽带：当子控件切换标签时，我们在外面重装数据源！
            ModControlBoards.OnActiveObjectSwitched += (activeObj) =>
            {
                _currentActiveObject = activeObj;
                ModFrameDetails.CurrentActiveObject = activeObj;
                ModIdentity.LoadData(activeObj, _context);
                ModFrameList.LoadData(activeObj, _context);

                var method = ModFrameDetails.GetType().GetMethod("LoadState");
                method?.Invoke(ModFrameDetails, new object[] { null, "", null, false, _context, _currentActiveObject });
            };

            // 3. 左侧点关键帧，右侧加载详情
            ModFrameList.OnFrameSelected += (state, title, bindingProps, isRoot) =>
            {
                ModFrameDetails.CurrentActiveObject = _currentActiveObject;
                ModFrameDetails.LoadState(state, title, bindingProps, isRoot, _context, _currentActiveObject);
            };

            // 4. 搜刮影子并交给子控件接管！
            var list = _storyboardRepository.GetListByType(_context.Storyboard, _mainObject.GetType());
            List<IStoryboardEntity> shadows = new List<IStoryboardEntity>();
            if (list != null)
            {
                foreach (IStoryboardEntity obj in list)
                {
                    if (obj.TargetId == _originalId && obj.Id != _originalId)
                    {
                        string cbJson = snapshotSerializer.Serialize(obj);
                        shadows.Add((IStoryboardEntity)snapshotSerializer.Deserialize(cbJson, obj.GetType()));
                    }
                }
            }
            // 移交大权
            ModControlBoards.Init(_mainObject, shadows);

            // 🌟【微创注入】：把大宇宙的全局模板字典死死焊进详情页，解除普通场景对象的“无模板封印”！
            ModFrameDetails.InitTemplates(_context.Storyboard.templates);
        }

        // ==========================================
        // 🌟 构造函数二：模板编辑专属通道
        // ==========================================
        public PropertyEditorWindow(string templateName, C2Template targetTemplate, ProjectDataContext context, IDialogService dialogService, IStoryboardRepository storyboardRepository, IPropertyEditorService propertyEditorService, IMessageBroker messageBroker)
            : this(dialogService, storyboardRepository, propertyEditorService, messageBroker)
        {
            InitializeComponent();
            ModFrameDetails.InitializeServices(_propertyEditorService, _messageBroker);
            _context = context;
            _isTemplateMode = true;
            _templateName = templateName;

            var snapshotSerializer = AppServices.GetService<IEditorSnapshotSerializer>();
            string jsonClone = snapshotSerializer.Serialize(targetTemplate);
            _editingTemplate = snapshotSerializer.Deserialize<C2Template>(jsonClone);

            // ✨ 修复 1：兜底防线。如果新建的模板没有肉体，强行给它注入灵魂！
            if (_editingTemplate.BaseState == null) _editingTemplate.BaseState = new TemplateState();
            if (_editingTemplate.Keyframes == null) _editingTemplate.Keyframes = new List<TemplateState>();

            ModFrameList.OnFrameSelected += (state, title, bindingProps, isRoot) =>
            {
                ModFrameDetails.LoadState(state, title, bindingProps, isRoot, _context);
            };

            // 模板模式禁用控制板
            ModControlBoards.Visibility = Visibility.Collapsed;

            ModIdentity.OnTemplateTypeChanged += (type) =>
            {
                ModFrameDetails.SetTemplateTypeLimit(type);
                // 实时记入新账本
                if (_context.StoryboardMeta?.TemplateMetas != null)
                {
                    string tName = ModIdentity.TxtObjectId.Text;
                    if (!_context.StoryboardMeta.TemplateMetas.ContainsKey(tName))
                        _context.StoryboardMeta.TemplateMetas[tName] = new EditorTemplateMeta();
                    _context.StoryboardMeta.TemplateMetas[tName].Type = type;
                }
            };
            ModIdentity.LoadTemplateData(_templateName, _context);

            ModFrameList.LoadTemplateData(_editingTemplate, _context);

            // 顺手同步初始门派，接通新字典
            if (_context.StoryboardMeta?.TemplateMetas != null && _context.StoryboardMeta.TemplateMetas.TryGetValue(_templateName, out var tMeta))
            {
                ModFrameDetails.SetTemplateTypeLimit(tMeta.Type);
            }
        }

        // ==========================================
        // 💾 终极落盘总线
        // ==========================================
        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (!ModIdentity.ValidateAndSave()) return;
            if (ModControlBoards != null && ModControlBoards.ControlBoards != null)
            {
                foreach (var cb in ModControlBoards.ControlBoards)
                {
                    System.Diagnostics.Debug.WriteLine($"[保存前夕审计] 控制板 ID: {cb.Id}, 它的 TargetId 是: '{cb.TargetId}'");
                }
            }


            if (_isTemplateMode)
            {
                string newName = ModIdentity.TxtObjectId.Text.Trim();
                if (string.IsNullOrEmpty(newName)) { _dialogService.ShowMessage("模板名称不能为空！", "拦截"); return; }

                if (newName != _templateName)
                {
                    new Core.TemplateManager().RenameTemplateGlobally(_context.Storyboard, _templateName, newName);
                    // ✨ 修复 4：拔除幽灵炸弹！更名后必须通过仓储把旧名字剔除！
                    _storyboardRepository.RemoveTemplate(_context.Storyboard, _templateName);
                }

                _storyboardRepository.AddTemplate(_context.Storyboard, newName, _editingTemplate);
                _context.MarkAsModified();
                this.DialogResult = true;
                this.Close();
                return;
            }

            // 🌟 洗盘法术：从我们的独立子控件里把修改后的控制板影子拿回来洗盘！
            if (string.IsNullOrEmpty(_mainObject.TargetId))
            {
                var list = _storyboardRepository.GetListByType(_context.Storyboard, _mainObject.GetType());
                if (list != null)
                {
                    for (int i = list.Count - 1; i >= 0; i--)
                    {
                        IStoryboardEntity entity = list[i] as IStoryboardEntity;
                        if (entity != null && entity.TargetId == _originalId && entity.Id != _originalId)
                        {
                            list.RemoveAt(i);
                        }
                    }
                    foreach (var cb in ModControlBoards.ControlBoards)
                    {
                        list.Add(cb);
                    }
                    
                }
            }
            
            this.Tag = _mainObject;
            this.DialogResult = true;
            this.Close();
        }



        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
