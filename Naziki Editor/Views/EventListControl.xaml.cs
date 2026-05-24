using Microsoft.Win32;
using Naziki_Editor.Core;
using Naziki_Editor.Models;
using Naziki_Editor.Views;
using Newtonsoft.Json.Linq;
using System;
using System.Linq; // 🌟 补上 Linq 扩展支持
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Naziki_Editor.State;

namespace Naziki_Editor.Views
{
    public partial class EventListControl : UserControl
    {
        public event Action<string, StoryboardRoot> OnStoryboardLoaded;
        public event Action<AssetBundle> OnAssetScanned;
        public event Action<object> OnEventNodeSelected;

        // 🌟 新增：三大造物主按钮事件
        public event Action OnAddTextRequested;
        public event Action OnAddLineRequested;
        public event Action OnAddSceneRequested;

        // 🌟 在类的下面加上这三个按钮的响应：
        private void BtnAddText_Click(object sender, RoutedEventArgs e) => OnAddTextRequested?.Invoke();
        private void BtnAddLine_Click(object sender, RoutedEventArgs e) => OnAddLineRequested?.Invoke();
        private void BtnAddScene_Click(object sender, RoutedEventArgs e) => OnAddSceneRequested?.Invoke();

        // 🌟 新增：项目数据上下文 (如果需要的话，后续可以扩展成更复杂的状态管理系统！)
        public ProjectDataContext Context { get; private set; }

        // 🌟 新增：加载项目数据上下文的公开方法，供主窗口调用
        public void LoadContext(ProjectDataContext context)
        {
            Context = context;
        }


        public EventListControl()
        {
            InitializeComponent();
            UpdateEmptyHintVisibility();
            // 在 MainWindow.xaml.cs 的构造函数或加载逻辑里：
            
        }


        // ==========================================
        // 🌟 核心：打开故事板 (脏活全甩给解析和扫描引擎！)
        // ==========================================
        public void ExecuteOpenProject()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog { Filter = "Cytoid 故事板 (*.json)|*.json|所有文件 (*.*)|*.*", Title = "请选择你的故事板文件" };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    // 🔮 呼叫解析引擎！
                    StoryboardRoot root = StoryboardParser.Load(openFileDialog.FileName);
                    LoadStoryboardUI(); // UI 专心负责画树叶
                    OnStoryboardLoaded?.Invoke(openFileDialog.FileName, root);// 抛出事件给主窗口，告诉它故事板加载好了！


                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "加载失败 QAQ");
                    ClearAllDrawers(); // 失败了就把抽屉清空
                    UpdateEmptyHintVisibility();
                }
            }
        }

        // 接收音符组的公开法术
        public void AddNoteGroupToTree(TreeViewItem groupItem)
        {
            NoteCtrlListBox.Items.Add(groupItem);
            UpdateEmptyHintVisibility();
        }




        // ==========================================
        // 🎨 UI 专职：扁平化列表渲染 (解除文件夹封装)
        // ==========================================
        // 🎨 UI 专职：扁平化列表渲染 (一键大一统！)
        public void LoadStoryboardUI()
        {
            // 防呆：如果没有通电或者没有故事板，直接退出
            if (Context == null || !Context.HasStoryboard) return;

            var root = Context.Storyboard; // 提取图纸

            ClearAllDrawers();

            if (root.sprites?.Count > 0) foreach (var obj in root.sprites)
                SpriteListBox.Items.Add(new ListBoxItem { Content = EventNameResolver.GetDisplayName(obj), Tag = obj });

            if (root.texts?.Count > 0) foreach (var obj in root.texts)
                TextListBox.Items.Add(new ListBoxItem { Content = EventNameResolver.GetDisplayName(obj), Tag = obj });

            if (root.videos?.Count > 0) foreach (var obj in root.videos)
                VideoListBox.Items.Add(new ListBoxItem { Content = EventNameResolver.GetDisplayName(obj), Tag = obj });

            if (root.lines?.Count > 0) foreach (var obj in root.lines)
                LinesListBox.Items.Add(new ListBoxItem { Content = EventNameResolver.GetDisplayName(obj), Tag = obj });

            if (root.controllers?.Count > 0) foreach (var obj in root.controllers)
                SceneListBox.Items.Add(new ListBoxItem { Content = EventNameResolver.GetDisplayName(obj), Tag = obj });

            if (root.note_controllers?.Count > 0) foreach (var obj in root.note_controllers)
            {
                ListBoxItem item = new ListBoxItem { Content = EventNameResolver.GetDisplayName(obj), Tag = obj };
                if (obj.NoteTarget is JObject) { item.Foreground = Brushes.DarkCyan; item.FontWeight = FontWeights.Bold; }
                NoteCtrlListBox.Items.Add(item);
            }

            if (root.templates?.Count > 0) foreach (var kvp in root.templates)
                EventTemplateListBox.Items.Add(new ListBoxItem { Content = string.IsNullOrEmpty(kvp.Key) ? "未命名模板" : kvp.Key, Tag = kvp.Value });

            UpdateEmptyHintVisibility();
        }


        // ==========================================
        // 🌟 动态 UI 维护：空空如也提示
        // ==========================================
        public void UpdateEmptyHintVisibility()
        {
            if (EventTabControl == null || DynamicEmptyHint == null) return;
            var currentListBox = GetCurrentActiveListBox();
            if (currentListBox != null)
            {
                if (currentListBox.Items.Count > 0)
                {
                    currentListBox.Visibility = Visibility.Visible;
                    DynamicEmptyHint.Visibility = Visibility.Collapsed;
                }
                else
                {
                    currentListBox.Visibility = Visibility.Collapsed;
                    DynamicEmptyHint.Visibility = Visibility.Visible;
                }
            }
        }

        //树状列表，切换时，更新空空如也提示的显示状态
        private ListBox GetCurrentActiveListBox()
        {
            if (EventTabControl.SelectedItem is TabItem selectedTab)
            {
                switch (selectedTab.Header.ToString())
                {
                    case "图片": return SpriteListBox;
                    case "文字": return TextListBox;
                    case "线条": return LinesListBox;
                    case "视频": return VideoListBox;
                    case "场景": return SceneListBox;
                    case "音符": return NoteCtrlListBox;
                    case "模板": return EventTemplateListBox;
                }
            }
            return null;
        }

        // 🧹 专门用来清空左侧大本营的法术
        private void ClearAllDrawers()
        {
            SpriteListBox.Items.Clear();
            TextListBox.Items.Clear();
            VideoListBox.Items.Clear();
            LinesListBox.Items.Clear();
            SceneListBox.Items.Clear();
            NoteCtrlListBox.Items.Clear();
            EventTemplateListBox.Items.Clear();
        }

        // ==========================================
        // 📡 跨服发射器：捕获树叶点击，并把隐藏的数据对象 (Tag) 发射出去！
        // ==========================================
        private void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 如果用户框选了多个，或者点了一个，我们把最新加入选区的那一个提取出来发送给雷达和属性面板
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is ListBoxItem selectedItem)
            {
                if (selectedItem.Tag != null)
                {
                    OnEventNodeSelected?.Invoke(selectedItem.Tag);
                }
            }
            else if (e.AddedItems.Count == 0)
            {
                // 🌟 新增兜底：如果用户点击空白处导致没有项被选中，直接发送 null 触发全家福！
                OnEventNodeSelected?.Invoke(null);
            }
        }


        private void EventTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateEmptyHintVisibility();




        private void EventTree_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // ==========================================
            // ✨ 核心修复：降维打击！直接从 UI 的 Content 里拿名字！
            // ==========================================
            if (sender == EventTemplateListBox)
            {
                if (EventTemplateListBox.SelectedItem is ListBoxItem templateItem && templateItem.Tag is StoryboardTemplate template)
                {
                    // 👑 直接读取 ListBoxItem 上的文字作为 Key
                    string templateKey = templateItem.Content?.ToString();
                    if (templateKey == "未命名模板") templateKey = "";

                    if (Window.GetWindow(this) is MainWindow main && templateKey != null)
                    {
                        main.OpenTemplatePropertyEditor(templateKey, template);
                    }
                    e.Handled = true;
                    return;
                }
            }

            // ✨ 魔法：不管是哪个分类的 ListBox 被双击了，sender 就是它！先剥开 ListBoxItem 的外壳，再去拿 Tag 肚子里的数据！
            if (sender is ListBox listBox && listBox.SelectedItem is ListBoxItem item && item.Tag is Models.StoryboardObject selectedObj)
            {
                // 呼叫主窗口的传送门,直接按着当前控件的族谱往上找它真正所在的窗口！
                if (Window.GetWindow(this) is MainWindow main)
                {
                    main.OpenPropertyEditor(selectedObj);
                    e.Handled = true; // 拦截鼠标事件，防止冒泡
                }
            }
        }
        // ==========================================
        // 🗑️ 核心功能：删除选中的事件 (支持多选批量删除！)
        // ==========================================
        private void BtnDeleteEvent_Click(object sender, RoutedEventArgs e)
        {
            // 防呆拦截：没有通电或者没有数据
            if (Context == null || !Context.HasStoryboard) return;

            ListBox activeList = GetCurrentActiveListBox();
            if (activeList == null || activeList.SelectedItems.Count == 0)
            {
                MessageBox.Show("呆胶布？你还没有在列表中选择要删除的事件哦！", "小艾的提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 危险操作！必须经过指挥官同意！
            var result = MessageBox.Show($"确认要将这 {activeList.SelectedItems.Count} 个事件从故事板中彻底抹除吗？\n此操作目前不可撤销哦！",
                                         "危险警告", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            var root = Context.Storyboard;
            bool hasDeleted = false;

            // 提取所有选中的项目 (使用 Linq 转为 List 防止遍历时修改集合报错)
            var selectedItems = activeList.SelectedItems.Cast<ListBoxItem>().ToList();

            foreach (var item in selectedItems)
            {
                // 获取原始标签对象，不要强行转化为 StoryboardObject
                var tag = item.Tag;

                if (item.Tag is StoryboardObject objToDelete)
                {
                    // 根据类型，精准切断它们在底层 DNA 库中的连接！
                    if (objToDelete is Sprite s) { root.sprites?.Remove(s); hasDeleted = true; }
                    else if (objToDelete is Text t) { root.texts?.Remove(t); hasDeleted = true; }
                    else if (objToDelete is Line l) { root.lines?.Remove(l); hasDeleted = true; }
                    else if (objToDelete is Video v) { root.videos?.Remove(v); hasDeleted = true; }
                    else if (objToDelete is Controller c) { root.controllers?.Remove(c); hasDeleted = true; }
                    else if (objToDelete is NoteController nc) { root.note_controllers?.Remove(nc); hasDeleted = true; }
                }
                else if (tag is StoryboardTemplate st)
                {
                    // 👑 独立处理模板对象：同样降维打击，直接拿 Content 当 Key 删！
                    string templateKey = item.Content?.ToString();
                    if (templateKey == "未命名模板") templateKey = "";

                    if (templateKey != null && root.templates.ContainsKey(templateKey))
                    {
                        root.templates.Remove(templateKey);
                        hasDeleted = true;
                    }
                }

            }

            if (hasDeleted)
            {
                // 1. 刷新左侧树叶列表，更新空空如也提示
                LoadStoryboardUI();

                // 2. 发射空信号，让右侧的属性面板和预览画布取消选中高亮状态！
                OnEventNodeSelected?.Invoke(null);
            }
        }

    }

}