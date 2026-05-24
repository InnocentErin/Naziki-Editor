using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Naziki_Editor.Models;
using Naziki_Editor.State;

namespace Naziki_Editor.Views.PropertyEditor
{
    public partial class PropertyEditor_Identity : UserControl
    {
        private IStoryboardEntity _editingObject; // 🌟 核心升级：改用新世界树接口
        private string _originalId;
        private ProjectDataContext _context;

        public PropertyEditor_Identity()
        {
            InitializeComponent();
        }

        // ==========================================
        // 📥 接口升级：接收全新的 IStoryboardEntity 通用胶囊
        // ==========================================
        public void LoadData(IStoryboardEntity editingObj, ProjectDataContext context)
        {
            _editingObject = editingObj;
            _context = context;
            _originalId = editingObj.Id ?? "";

            TxtObjectId.Text = _editingObject.Id;
            TxtParentId.Text = _editingObject.ParentId;

            // 如果有谱面绑定逻辑，这里可以根据需要继续保留或留空
            if (_context != null && _context.HasChart && _context.Chart.note_list != null && _context.Chart.note_list.Count > 0)
            {
                BtnBindNote.IsEnabled = true;
            }
        }

        public bool ValidateAndSave()
        {
            string newId = TxtObjectId.Text.Trim();
            if (string.IsNullOrEmpty(newId))
            {
                TxtIdWarning.Text = "⚠️ 指挥官，ID绝对不能为空哦！";
                TxtIdWarning.Visibility = Visibility.Visible;
                return false;
            }

            if (newId != _originalId && IsIdConflict(newId))
            {
                TxtIdWarning.Text = $"⚠️ ID '{newId}' 已经被别人占领啦，请换一个！";
                TxtIdWarning.Visibility = Visibility.Visible;
                return false;
            }

            TxtIdWarning.Visibility = Visibility.Collapsed;
            _editingObject.Id = newId;
            _editingObject.ParentId = string.IsNullOrWhiteSpace(TxtParentId.Text) ? null : TxtParentId.Text.Trim();

            return true;
        }

        private bool IsIdConflict(string id)
        {
            if (_context == null || !_context.HasStoryboard) return false;
            var root = _context.Storyboard;

            // 🌟 完美契合重构后的新实体库查重
            if (root.sprites?.Any(x => x.Id == id) == true) return true;
            if (root.texts?.Any(x => x.Id == id) == true) return true;
            if (root.lines?.Any(x => x.Id == id) == true) return true;
            if (root.videos?.Any(x => x.Id == id) == true) return true;
            if (root.controllers?.Any(x => x.Id == id) == true) return true;
            if (root.note_controllers?.Any(x => x.Id == id) == true) return true;

            return false;
        }
    }
}