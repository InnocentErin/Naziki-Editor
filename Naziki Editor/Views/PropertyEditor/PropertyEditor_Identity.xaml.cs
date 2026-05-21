using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Naziki_Editor.Models;

namespace Naziki_Editor.Views.PropertyEditor
{
    public partial class PropertyEditor_Identity : UserControl
    {
        private StoryboardObject _editingObject;
        private StoryboardRoot _root;
        private string _originalId; 
        private C2Chart _chart;

        public PropertyEditor_Identity()
        {
            InitializeComponent();
            SetupEventBindingMenu();// ✨ 启动事件菜单装配机
            SetupNoteBindingMenu(); // ✨ 启动音符菜单装配机
        }

        // ==========================================
        // 📥 接口 1：主窗口呼叫这里，把数据塞进来
        // ==========================================
        public void LoadData(StoryboardObject editingObj, StoryboardRoot root, C2Chart chart)
        {
            _editingObject = editingObj;
            _root = root;
            _chart = chart;
            _originalId = editingObj.Id ?? "";

            // 渲染 UI
            TxtObjectId.Text = _editingObject.Id;
            TxtParentId.Text = _editingObject.ParentId;

            // ✨ 解锁判定：如果有谱面，并且里面有音符，就点亮按钮并去掉灰色提示！
            if (_chart != null && _chart.note_list != null && _chart.note_list.Count > 0)
            {
                BtnBindNote.IsEnabled = true;
                BtnBindNote.ToolTip = "点击选择要绑定的音符ID";
            }
        }


        // ==========================================
        // 🪄 核心逻辑：组装下拉事件列表
        // ==========================================
        private void SetupEventBindingMenu()
        {
            BtnBindEvent.Click += (s, e) =>
            {
                MenuEventList.Items.Clear();
                var allIds = new List<string>();

                // 雷达扫描大宇宙里的所有生命体
                if (_root != null)
                {
                    if (_root.sprites != null) allIds.AddRange(_root.sprites.Select(x => x.Id));
                    if (_root.texts != null) allIds.AddRange(_root.texts.Select(x => x.Id));
                    if (_root.lines != null) allIds.AddRange(_root.lines.Select(x => x.Id));
                    if (_root.videos != null) allIds.AddRange(_root.videos.Select(x => x.Id));
                    if (_root.controllers != null) allIds.AddRange(_root.controllers.Select(x => x.Id));
                    if (_root.note_controllers != null) allIds.AddRange(_root.note_controllers.Select(x => x.Id));
                }

                // 防呆：不能认自己当爸爸！
                if (_editingObject != null && allIds.Contains(_editingObject.Id))
                {
                    allIds.Remove(_editingObject.Id);
                }

                if (allIds.Count == 0)
                {
                    MenuEventList.Items.Add(new MenuItem { Header = "当前没有其他事件可以绑定哦~", IsEnabled = false });
                }
                else
                {
                    foreach (var id in allIds)
                    {
                        var item = new MenuItem { Header = $"目标: {id}" };
                        item.Click += (senderItem, args) => { TxtParentId.Text = id; }; // 点击自动填入
                        MenuEventList.Items.Add(item);
                    }
                }

                MenuEventList.PlacementTarget = BtnBindEvent;
                MenuEventList.IsOpen = true;
            };
        }


        // ==========================================
        // 🎵 核心逻辑：组装下拉音符列表
        // ==========================================
        private void SetupNoteBindingMenu()
        {
            BtnBindNote.Click += (s, e) =>
            {
                // 如果没有谱面（按钮虽然不该被点，但防呆一下）
                if (_chart == null || _chart.note_list == null) return;

                MenuNoteList.Items.Clear();
                
                // 按照 Tick (时间) 顺序把音符排好
                var sortedNotes = _chart.note_list.OrderBy(n => n.tick).ToList();

                foreach (var note in sortedNotes)
                {
                    // 显示的信息：音符ID + 所在 Tick (可以帮助作者辨认)
                    var item = new MenuItem { Header = $"音符 ID: {note.id}  (Tick: {note.tick})" };

                    item.Click += (senderItem, args) =>
                    {
                        // 点击后，自动把音符ID填入文本框 (注意：ParentId 需要字符串格式)
                        TxtParentId.Text = note.id.ToString();
                    };

                    MenuNoteList.Items.Add(item);
                }

                MenuNoteList.PlacementTarget = BtnBindNote;
                MenuNoteList.IsOpen = true;
            };
        }

        // ==========================================
        // 📤 接口 2：主窗口点击保存时，呼叫这里进行数据验证
        // ==========================================
        public bool ValidateAndSave()
        {
            string newId = TxtObjectId.Text.Trim();

            // 防线一：绝对不能为空
            if (string.IsNullOrEmpty(newId))
            {
                TxtIdWarning.Text = "⚠️ ID 绝对不能为空哦！";
                TxtIdWarning.Visibility = Visibility.Visible;
                return false; // 拦截保存！
            }

            // 防线二：不许跟别人重名 (但如果没改名字，可以和原来的自己重名)
            if (newId != _originalId && IsIdConflict(newId))
            {
                TxtIdWarning.Text = $"⚠️ ID '{newId}' 已经被占用啦，请换一个！";
                TxtIdWarning.Visibility = Visibility.Visible;
                return false; // 拦截保存！
            }

            // 警报解除，正式把数据灌回给克隆体
            TxtIdWarning.Visibility = Visibility.Collapsed;
            _editingObject.Id = newId;

            // 如果玩家把框清空了，就等于解除了跟随 (null)
            _editingObject.ParentId = string.IsNullOrWhiteSpace(TxtParentId.Text) ? null : TxtParentId.Text.Trim();

            return true; // 放行！
        }

        // 查字典工具
        private bool IsIdConflict(string id)
        {
            if (_root == null) return false;
            if (_root.sprites?.Any(x => x.Id == id) == true) return true;
            if (_root.texts?.Any(x => x.Id == id) == true) return true;
            if (_root.lines?.Any(x => x.Id == id) == true) return true;
            if (_root.videos?.Any(x => x.Id == id) == true) return true;
            if (_root.controllers?.Any(x => x.Id == id) == true) return true;
            if (_root.note_controllers?.Any(x => x.Id == id) == true) return true;
            return false;
        }
    }
}