using Microsoft.Win32;
using Naziki_Editor.Core;
using Naziki_Editor.Models;
using Naziki_Editor.State;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Naziki_Editor.Views
{
    public partial class EventListControl : UserControl
    {
        public event Action<string, StoryboardRoot> OnStoryboardLoaded;
        public event Action<AssetBundle> OnAssetScanned;
        public event Action<object> OnEventNodeSelected;

        public event Action OnAddTextRequested;
        public event Action OnAddLineRequested;
        public event Action OnAddSceneRequested;

        private void BtnAddText_Click(object sender, RoutedEventArgs e) => OnAddTextRequested?.Invoke();
        private void BtnAddLine_Click(object sender, RoutedEventArgs e) => OnAddLineRequested?.Invoke();
        private void BtnAddScene_Click(object sender, RoutedEventArgs e) => OnAddSceneRequested?.Invoke();

        public ProjectDataContext Context { get; private set; }

        public void LoadContext(ProjectDataContext context) => Context = context;

        public EventListControl()
        {
            InitializeComponent();
            UpdateEmptyHintVisibility();
        }

        // ==========================================
        // 🔮 一键智能读档基站
        // ==========================================
        public void ExecuteOpenProject()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog { Filter = "Cytoid 故事板 (*.json)|*.json|所有文件 (*.*)|*.*", Title = "请选择你的故事板文件" };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    // ✨ 核心重写：完美消灭过时的 StoryboardParser.Load！
                    string jsonText = File.ReadAllText(openFileDialog.FileName);
                    StoryboardRoot root = JsonConvert.DeserializeObject<StoryboardRoot>(jsonText, StoryboardSerializer.GetSettings());
                    // 让自增发证官入场，全盘清洗无 ID 的伪空列表！
                    StoryboardParser.StandardizeStoryboardIds(root);
                    if (root == null) throw new Exception("故事板解析出来空空如也，是不是文件损坏了？");

                    Context.Storyboard = root;
                    LoadStoryboardUI();
                    OnStoryboardLoaded?.Invoke(openFileDialog.FileName, root);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "加载失败 QAQ");
                    ClearAllDrawers();
                    UpdateEmptyHintVisibility();
                }
            }
        }

        public void AddNoteGroupToTree(TreeViewItem groupItem)
        {
            NoteCtrlListBox.Items.Add(groupItem);
            UpdateEmptyHintVisibility();
        }

        public void LoadStoryboardUI()
        {
            if (Context == null || !Context.HasStoryboard) return;
            var root = Context.Storyboard;

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
                // ✨ 核心修正：属性读取路径更正
                if (obj.BaseState?.NoteTarget is JObject) { item.Foreground = Brushes.DarkCyan; item.FontWeight = FontWeights.Bold; }
                NoteCtrlListBox.Items.Add(item);
            }

            if (root.templates?.Count > 0) foreach (var kvp in root.templates)
                EventTemplateListBox.Items.Add(new ListBoxItem { Content = string.IsNullOrEmpty(kvp.Key) ? "未命名模板" : kvp.Key, Tag = kvp.Value });

            UpdateEmptyHintVisibility();
        }

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

        private void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is ListBoxItem selectedItem)
            {
                if (selectedItem.Tag != null) OnEventNodeSelected?.Invoke(selectedItem.Tag);
            }
            else if (e.AddedItems.Count == 0)
            {
                OnEventNodeSelected?.Invoke(null);
            }
        }

        private void EventTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateEmptyHintVisibility();

        private void EventTree_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender == EventTemplateListBox)
            {
                if (EventTemplateListBox.SelectedItem is ListBoxItem templateItem && templateItem.Tag is C2Template template)
                {
                    string templateKey = templateItem.Content?.ToString();
                    if (templateKey == "未命名模板") templateKey = "";

                    if (Window.GetWindow(this) is MainWindow main && templateKey != null)
                    {
                        ((dynamic)main).OpenTemplatePropertyEditor(templateKey, template);
                    }
                    e.Handled = true;
                    return;
                }
            }

            // ✨ 核心重写：双击全面拥抱万能接口 IStoryboardEntity！
            if (sender is ListBox listBox && listBox.SelectedItem is ListBoxItem item && item.Tag is IStoryboardEntity selectedObj)
            {
                if (Window.GetWindow(this) is MainWindow main)
                {
                    ((dynamic)main).OpenPropertyEditor(selectedObj);
                    e.Handled = true;
                }
            }
        }

        private void BtnDeleteEvent_Click(object sender, RoutedEventArgs e)
        {
            if (Context == null || !Context.HasStoryboard) return;

            ListBox activeList = GetCurrentActiveListBox();
            if (activeList == null || activeList.SelectedItems.Count == 0)
            {
                MessageBox.Show("呆胶布？你还没有在列表中选择要删除的事件哦！", "小艾的提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show($"确认要将这 {activeList.SelectedItems.Count} 个事件从故事板中彻底抹除吗？\n此操作目前不可撤销哦！", "危险警告", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            var root = Context.Storyboard;
            bool hasDeleted = false;
            var selectedItems = activeList.SelectedItems.Cast<ListBoxItem>().ToList();

            foreach (var item in selectedItems)
            {
                var tag = item.Tag;

                // ✨ 核心重写：删除算法全线拥抱 C2 新军团
                if (tag is IStoryboardEntity objToDelete)
                {
                    if (objToDelete is C2Sprite s) { root.sprites?.Remove(s); hasDeleted = true; }
                    else if (objToDelete is C2Text t) { root.texts?.Remove(t); hasDeleted = true; }
                    else if (objToDelete is C2Line l) { root.lines?.Remove(l); hasDeleted = true; }
                    else if (objToDelete is C2Video v) { root.videos?.Remove(v); hasDeleted = true; }
                    else if (objToDelete is C2SceneController c) { root.controllers?.Remove(c); hasDeleted = true; }
                    else if (objToDelete is C2NoteController nc) { root.note_controllers?.Remove(nc); hasDeleted = true; }
                }
                else if (tag is C2Template st)
                {
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
                LoadStoryboardUI();
                OnEventNodeSelected?.Invoke(null);
            }
        }
    }
}