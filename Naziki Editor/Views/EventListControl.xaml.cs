using Microsoft.Win32;
using Naziki_Editor.Core;
using Naziki_Editor.Models;
using Naziki_Editor.Views;
using Newtonsoft.Json.Linq;
using System;
using System.Linq; // 🌟 补上 Linq 扩展支持
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

        // 🌟 新增：三大造物主按钮事件
        public event Action OnAddTextRequested;
        public event Action OnAddLineRequested;
        public event Action OnAddSceneRequested;

        // 🌟 在类的下面加上这三个按钮的响应：
        private void BtnAddText_Click(object sender, RoutedEventArgs e) => OnAddTextRequested?.Invoke();
        private void BtnAddLine_Click(object sender, RoutedEventArgs e) => OnAddLineRequested?.Invoke();
        private void BtnAddScene_Click(object sender, RoutedEventArgs e) => OnAddSceneRequested?.Invoke();


        public EventListControl()
        {
            InitializeComponent();
            UpdateEmptyHintVisibility();
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
                    LoadStoryboardUI(root); // UI 专心负责画树叶
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
        public void LoadStoryboardUI(StoryboardRoot root)
        {
            ClearAllDrawers();

            if (root.sprites?.Count > 0)
            {
                foreach (var sprite in root.sprites)
                {
                    ListBoxItem item = new ListBoxItem { Tag = sprite };
                    item.SetBinding(ListBoxItem.ContentProperty, new System.Windows.Data.Binding("DisplayName") { Source = sprite });
                    SpriteListBox.Items.Add(item);
                }
            }

            if (root.texts?.Count > 0)
            {
                foreach (var txt in root.texts)
                {
                    ListBoxItem item = new ListBoxItem { Tag = txt };
                    item.SetBinding(ListBoxItem.ContentProperty, new System.Windows.Data.Binding("DisplayName") { Source = txt });
                    TextListBox.Items.Add(item);
                }
            }

            if (root.videos?.Count > 0)
            {
                foreach (var video in root.videos)
                {
                    ListBoxItem item = new ListBoxItem { Tag = video };
                    item.SetBinding(ListBoxItem.ContentProperty, new System.Windows.Data.Binding("DisplayName") { Source = video });
                    VideoListBox.Items.Add(item);
                }
            }

            if (root.lines?.Count > 0)
            {
                foreach (var line in root.lines)
                {
                    ListBoxItem item = new ListBoxItem { Tag = line };
                    item.SetBinding(ListBoxItem.ContentProperty, new System.Windows.Data.Binding("DisplayName") { Source = line });
                    LinesListBox.Items.Add(item);
                }
            }

            if (root.controllers?.Count > 0)
            {
                int index = 1;
                foreach (var ctrl in root.controllers)
                {
                    ListBoxItem item = new ListBoxItem { Content = $"场景控制器 {index++}", Tag = ctrl };
                    SceneListBox.Items.Add(item);
                }
            }

            if (root.note_controllers?.Count > 0)
            {
                foreach (var ctrl in root.note_controllers)
                {
                    ListBoxItem item = new ListBoxItem { Tag = ctrl };
                    item.SetBinding(ListBoxItem.ContentProperty, new System.Windows.Data.Binding("DisplayName") { Source = ctrl });
                    if (ctrl.NoteTarget is JObject)
                    {
                        item.Foreground = Brushes.DarkCyan;
                        item.FontWeight = FontWeights.Bold;
                    }
                    NoteCtrlListBox.Items.Add(item);
                }
            }

            if (root.templates?.Count > 0)
            {
                foreach (var kvp in root.templates)
                {
                    ListBoxItem item = new ListBoxItem { Content = string.IsNullOrEmpty(kvp.Key) ? "未命名模板" : kvp.Key, Tag = kvp.Value };
                    EventTemplateListBox.Items.Add(item);
                }
            }

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
        }


        private void EventTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateEmptyHintVisibility();
    }
}