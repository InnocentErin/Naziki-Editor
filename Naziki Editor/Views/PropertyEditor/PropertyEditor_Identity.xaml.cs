using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Naziki_Editor.Models;
using Naziki_Editor.State;
using Naziki_Editor.Core; // 🌟 引入核心库，使用 ChartLogic 门派转换器

namespace Naziki_Editor.Views.PropertyEditor
{
    public partial class PropertyEditor_Identity : UserControl
    {
        private IStoryboardEntity _editingObject;
        private string _originalId;
        private ProjectDataContext _context;
        private bool _isTemplateMode = false;
        public event Action<TemplateType> OnTemplateTypeChanged;

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

            // 1. 🟢【谱面数据接通】：如果绑定了谱面，接通音符雷达网
            if (_context != null && _context.HasChart && _context.Chart.note_list != null && _context.Chart.note_list.Count > 0)
            {
                BtnBindNote.IsEnabled = true;
                BtnBindNote.ToolTip = "点击一键召唤谱面音符列表魔方！";
                BuildNoteGroupContextMenu();
            }

            // 2. ⚡【故事板数据接通】：只要进入编辑模式，立刻连通事件大集结抽屉！
            if (_context != null && _context.HasStoryboard)
            {
                BuildEventGroupContextMenu();
            }
        }

        // ==========================================
        // 🔗【小艾特制】：解除闪闭干扰的全局事件集结菜单
        // ==========================================
        private void BuildEventGroupContextMenu()
        {
            if (_context?.Storyboard == null) return;

            // 🔬【金蝉脱壳】：强行剥离槽位引用，彻底解决左键点击一闪而过的 WPF 老 Bug！
            if (BtnBindEvent.ContextMenu != null)
            {
                BtnBindEvent.ContextMenu = null;
            }

            // 🎯 左键点击：一键唤醒事件大抽屉
            BtnBindEvent.Click += (s, e) =>
            {
                // 每次点击时动态重新分拣，保证用户刚刚新建的对象能实时刷新进去！
                RefreshEventGroupMenu();

                MenuEventList.PlacementTarget = BtnBindEvent;
                MenuEventList.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                MenuEventList.IsOpen = true;
                e.Handled = true; // 阻止事件冒泡
            };
        }

        // 🗄️ 动态事件数据分拣传输基站
        private void RefreshEventGroupMenu()
        {
            MenuEventList.Items.Clear();
            var root = _context.Storyboard;
            string currentId = _editingObject?.Id ?? "";

            // 📦 整理全宇宙六大门派的数据源映射表
            var categories = new List<(string Name, IEnumerable<IStoryboardEntity> Items)>
            {
                ("🖼️ 精灵图层 (Sprites)", root.sprites?.Cast<IStoryboardEntity>()),
                ("📝 文本对象 (Texts)", root.texts?.Cast<IStoryboardEntity>()),
                ("〰️ 矢量线条 (Lines)", root.lines?.Cast<IStoryboardEntity>()),
                ("🎥 视频图层 (Videos)", root.videos?.Cast<IStoryboardEntity>()),
                ("🎛️ 场景控制器 (Controllers)", root.controllers?.Cast<IStoryboardEntity>()),
                ("🎵 音符控制器 (Note Controllers)", root.note_controllers?.Cast<IStoryboardEntity>())
            };

            bool hasAnyOtherEvent = false;

            foreach (var cat in categories)
            {
                if (cat.Items == null) continue;

                // 🛡️ 铁腕重定向防呆：过滤掉自己！绝对不允许认自己当爸爸，防止底层陷入无穷死循环！
                var validItems = cat.Items.Where(x => x.Id != currentId).ToList();
                if (validItems.Count == 0) continue;

                hasAnyOtherEvent = true;
                var catMenu = new MenuItem { Header = cat.Name };

                foreach (var eventObj in validItems)
                {
                    string displayName = string.IsNullOrEmpty(eventObj.Id) ? "（未命名匿名对象）" : eventObj.Id;
                    var item = new MenuItem { Header = displayName };

                    // ⚡ 点击事件项：将选中的对象 ID 写入跟随目标框
                    item.Click += (s, e) =>
                    {
                        TxtParentId.Text = eventObj.Id;
                        _context?.MarkAsModified(); // 惊醒时光机记账
                    };
                    catMenu.Items.Add(item);
                }
                MenuEventList.Items.Add(catMenu);
            }

            // 🌌 孤岛守护：万一这是一个完全纯净的、没有写任何其他对象的新故事板
            if (!hasAnyOtherEvent)
            {
                MenuEventList.Items.Add(new MenuItem { Header = "🚫 当前没有其他可以绑定的故事板对象哦", IsEnabled = false });
            }
        }

        // ==========================================
        // 🎵【旧线保留】：多级页面抽屉式音符魔方菜单
        // ==========================================
        private void BuildNoteGroupContextMenu()
        {
            if (_context?.Chart?.note_list == null || _context.TimeEngine == null) return;

            if (BtnBindNote.ContextMenu != null) BtnBindNote.ContextMenu = null;
            MenuNoteList.Items.Clear();

            BtnBindNote.Click += (s, e) =>
            {
                MenuNoteList.PlacementTarget = BtnBindNote;
                MenuNoteList.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                MenuNoteList.IsOpen = true;
                e.Handled = true;
            };

            var pages = _context.Chart.page_list;
            var notes = _context.Chart.note_list.OrderBy(n => n.tick).ToList();
            Dictionary<int, MenuItem> pageMenus = new Dictionary<int, MenuItem>();

            for (int i = 0; i < (pages?.Count ?? 0); i++)
            {
                var pageItem = new MenuItem { Header = $"📄 第 {i} 页 (Page {i}) 的音符" };
                pageMenus[i] = pageItem;
                MenuNoteList.Items.Add(pageItem);
            }

            if (pageMenus.Count == 0)
            {
                var generalMenu = new MenuItem { Header = "🎵 谱面全量音符列表" };
                pageMenus[0] = generalMenu;
                MenuNoteList.Items.Add(generalMenu);
            }

            foreach (var note in notes)
            {
                double seconds = _context.TimeEngine.TickToSeconds(note.tick);
                string typeStr = ChartLogic.GetNoteTypeString(note.type);

                var noteItem = new MenuItem
                {
                    Header = $"🆔 {note.id} ［{seconds:0.000}s］ ({typeStr})",
                    FontWeight = FontWeights.Normal
                };

                noteItem.Click += (s, e) =>
                {
                    TxtParentId.Text = $"note_controller_{note.id}";
                    _context?.MarkAsModified();
                };

                if (pageMenus.ContainsKey(note.page_index))
                    pageMenus[note.page_index].Items.Add(noteItem);
                else
                    pageMenus.Values.First().Items.Add(noteItem);
            }
        }

        // 🌟 增加模板专属加载入口
        public void LoadTemplateData(string templateName, TemplateType currentType)
        {
            _isTemplateMode = true;
            RowTemplateType.Visibility = Visibility.Visible;
            TxtParentId.Visibility = Visibility.Collapsed;

            TxtObjectId.Text = templateName;

            foreach (ComboBoxItem item in CmbTemplateType.Items)
            {
                if (item.Tag.ToString() == currentType.ToString())
                {
                    CmbTemplateType.SelectedItem = item;
                    break;
                }
            }
        }

        private void CmbTemplateType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isTemplateMode || CmbTemplateType.SelectedItem == null) return;

            if (Enum.TryParse((CmbTemplateType.SelectedItem as ComboBoxItem).Tag.ToString(), out TemplateType newType))
            {
                OnTemplateTypeChanged?.Invoke(newType);
            }
        }

        public bool ValidateAndSave()
        {
            string newId = TxtObjectId.Text.Trim();
            if (string.IsNullOrEmpty(newId))
            {
                TxtIdWarning.Text = "⚠️ 设计师，ID绝对不能为空哦！";
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