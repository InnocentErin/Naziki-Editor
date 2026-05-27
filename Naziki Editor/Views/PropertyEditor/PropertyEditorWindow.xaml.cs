using Naziki_Editor.Core;
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

        // ==========================================
        // 🌟 构造函数一：适配普通事件对象的属性编辑
        // ==========================================
        public PropertyEditorWindow(IStoryboardEntity targetObject, ProjectDataContext context)
        {
            InitializeComponent();
            _context = context;

            // 🌟 【小艾的智能命名系统】：根据对象基因自动赋予优雅的 ID！
            if (string.IsNullOrEmpty(targetObject.Id))
            {
                targetObject.Id = GenerateSmartId(targetObject, _context);
            }

            _originalId = targetObject.Id;
            _isTemplateMode = false;

            // 1. 克隆魔法
            string jsonClone = Core.StoryboardSerializer.ToJson(targetObject);
            _mainObject = (IStoryboardEntity)JsonConvert.DeserializeObject(jsonClone, targetObject.GetType(), Core.StoryboardSerializer.GetSettings());

            // 2. 接通神经纽带：当子控件切换标签时，我们在外面重装数据源！
            ModControlBoards.OnActiveObjectSwitched += (activeObj) =>
            {
                _currentActiveObject = activeObj;
                ModIdentity.LoadData(activeObj, _context);
                ModFrameList.LoadData(activeObj, _context);

                var method = ModFrameDetails.GetType().GetMethod("LoadState");
                method?.Invoke(ModFrameDetails, new object[] { null, "", null, false, _context });
            };

            // 3. 左侧点关键帧，右侧加载详情
            ModFrameList.OnFrameSelected += (state, title, bindingProps, isRoot) =>
            {
                ModFrameDetails.LoadState(state, title, bindingProps, isRoot, _context);
            };

            // 4. 搜刮影子并交给子控件接管！
            var list = GetTargetListByType(_context.Storyboard, _mainObject.GetType());
            List<IStoryboardEntity> shadows = new List<IStoryboardEntity>();
            if (list != null)
            {
                foreach (IStoryboardEntity obj in list)
                {
                    if (obj.TargetId == _originalId && obj.Id != _originalId)
                    {
                        string cbJson = Core.StoryboardSerializer.ToJson(obj);
                        shadows.Add((IStoryboardEntity)JsonConvert.DeserializeObject(cbJson, obj.GetType(), Core.StoryboardSerializer.GetSettings()));
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
        public PropertyEditorWindow(string templateName, C2Template targetTemplate, ProjectDataContext context)
        {
            InitializeComponent();
            _context = context;
            _isTemplateMode = true;
            _templateName = templateName;

            string jsonClone = JsonConvert.SerializeObject(targetTemplate, Core.StoryboardSerializer.GetSettings());
            _editingTemplate = JsonConvert.DeserializeObject<C2Template>(jsonClone, Core.StoryboardSerializer.GetSettings());

            ModFrameList.OnFrameSelected += (state, title, bindingProps, isRoot) =>
            {
                ModFrameDetails.LoadState(state, title, bindingProps, isRoot, _context);
            };

            // 模板模式禁用控制板
            ModControlBoards.Visibility = Visibility.Collapsed;

            ModIdentity.TxtObjectId.Text = _templateName;
            ModFrameList.LoadTemplateData(_editingTemplate, _context);
        }

        private IList GetTargetListByType(StoryboardRoot root, Type t)
        {
            if (t == typeof(C2Sprite)) return root.sprites;
            if (t == typeof(C2Text)) return root.texts;
            if (t == typeof(C2Line)) return root.lines;
            if (t == typeof(C2Video)) return root.videos;
            if (t == typeof(C2SceneController)) return root.controllers;
            if (t == typeof(C2NoteController)) return root.note_controllers;
            return null;
        }

        // ==========================================
        // 💾 终极落盘总线
        // ==========================================
        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (_isTemplateMode)
            {
                string newName = ModIdentity.TxtObjectId.Text.Trim();
                if (string.IsNullOrEmpty(newName)) { MessageBox.Show("模板名称不能为空！", "拦截"); return; }
                if (newName != _templateName) Core.TemplateManager.RenameTemplateGlobally(_context.Storyboard, _templateName, newName);

                _context.Storyboard.templates[newName] = _editingTemplate;
                _context.MarkAsModified();
                this.DialogResult = true;
                this.Close();
                return;
            }

            var validationResult = Core.StoryboardValidator.ValidateStateConflicts(_mainObject);
            if (!validationResult.IsValid)
            {
                MessageBox.Show(validationResult.ErrorMessage, "主体对象防呆纠察", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 🌟 洗盘法术：从我们的独立子控件里把修改后的控制板影子拿回来洗盘！
            if (string.IsNullOrEmpty(_mainObject.TargetId))
            {
                var list = GetTargetListByType(_context.Storyboard, _mainObject.GetType());
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



        // ==========================================
        // 🧠 智能命名中枢：根据对象基因自动生成优雅的 ID！
        // ==========================================
        private string GenerateSmartId(IStoryboardEntity obj, ProjectDataContext context)
        {
            string typeName = "obj";
            string coreValue = "";

            // 🧬 1. 测绘基因，提取“初始核心”！
            if (obj is C2Sprite s) { typeName = "sprite"; coreValue = s.BaseState?.Path; }
            else if (obj is C2Text t) { typeName = "text"; coreValue = t.BaseState?.TextContent; }
            else if (obj is C2Video v) { typeName = "video"; coreValue = v.BaseState?.Path; }
            else if (obj is C2Line l) { typeName = "line"; coreValue = "pos"; } // 数组太长，用 pos 代替
            else if (obj is C2SceneController) { typeName = "controller"; coreValue = "scene"; }
            else if (obj is C2NoteController nc)
            {
                typeName = "note";
                if (nc.BaseState?.NoteTarget != null)
                {
                    string sVal = nc.BaseState.NoteTarget.ToString();
                    coreValue = sVal.StartsWith("{") ? "selector" : sVal; // 识别出是选择器还是单独音符
                }
            }

            // 🧹 2. 净化文字：去杂质、去后缀、防越界
            if (string.IsNullOrEmpty(coreValue)) coreValue = "new";
            else
            {
                try
                {
                    // 剃掉后缀名（如果是从素材库拉进来的图片路径）
                    coreValue = System.IO.Path.GetFileNameWithoutExtension(coreValue);
                    // 仅保留中英文和数字，其他杂质全换成下划线
                    coreValue = System.Text.RegularExpressions.Regex.Replace(coreValue, @"[^a-zA-Z0-9\u4e00-\u9fa5]", "_");
                    // 压缩多余的下划线并掐头去尾
                    coreValue = System.Text.RegularExpressions.Regex.Replace(coreValue, @"_+", "_").Trim('_');
                    // 限制长度防爆，过长就截断
                    if (coreValue.Length > 15) coreValue = coreValue.Substring(0, 15);

                    if (string.IsNullOrEmpty(coreValue)) coreValue = "item";
                }
                catch { coreValue = "item"; }
            }

            string baseId = $"{typeName}_{coreValue}".ToLower();
            string finalId = baseId;
            int index = 1;

            // 🛡️ 3. 查户口：如果大本营里已经有人叫这个名字了，就不断自增数字后缀！
            while (IsIdExists(finalId, context))
            {
                finalId = $"{baseId}_{index}";
                index++;
            }

            return finalId;
        }

        // 📡 配套辅助雷达：快速全量扫盘检测重名
        private bool IsIdExists(string id, ProjectDataContext context)
        {
            var root = context?.Storyboard;
            if (root == null) return false;

            bool exists = false;
            if (root.sprites != null) exists |= root.sprites.Exists(x => x.Id == id);
            if (root.texts != null) exists |= root.texts.Exists(x => x.Id == id);
            if (root.videos != null) exists |= root.videos.Exists(x => x.Id == id);
            if (root.lines != null) exists |= root.lines.Exists(x => x.Id == id);
            if (root.controllers != null) exists |= root.controllers.Exists(x => x.Id == id);
            if (root.note_controllers != null) exists |= root.note_controllers.Exists(x => x.Id == id);

            return exists;
        }





        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}