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
        public event Action<StoryboardRoot> OnStoryboardLoaded;
        public event Action<AssetBundle> OnAssetScanned;
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
                    // 1. 🔮 呼叫解析引擎！
                    StoryboardRoot root = StoryboardParser.Load(openFileDialog.FileName);
                    LoadStoryboardUI(root); // UI 专心负责画树叶
                    OnStoryboardLoaded?.Invoke(root);// 抛出事件给主窗口，告诉它故事板加载好了！

                    // 2. 🔮 呼叫扫描引擎！
                    string folderPath = System.IO.Path.GetDirectoryName(openFileDialog.FileName);
                    AssetBundle bundle = AssetScanner.ScanFolder(folderPath);
                    OnAssetScanned?.Invoke(bundle); // 触发事件，把数据丢出去！ // UI 专心挂载素材

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
            NoteCtrlTreeView.Items.Add(groupItem);
            UpdateEmptyHintVisibility();
        }




        // ==========================================
        // 🎨 UI 专职：把故事板模具画到屏幕上（智能兜底修复版）
        // ==========================================
        private void LoadStoryboardUI(StoryboardRoot root)
        {
            ClearAllDrawers();

            // --- 🍒 1. Sprites (图片对象) ---
            if (root.sprites?.Count > 0)
            {
                TreeViewItem folder = new TreeViewItem() { Header = $"图片 Sprites ({root.sprites.Count})" };
                foreach (var sprite in root.sprites)
                {
                    // 🌟 智能命名：优先采用 "id [path]"，没 id 就用 path，啥都没才叫未命名
                    string displayName = "未命名图片";
                    if (!string.IsNullOrEmpty(sprite.id) && !string.IsNullOrEmpty(sprite.path))
                        displayName = $"{sprite.id} [{sprite.path}]";
                    else if (!string.IsNullOrEmpty(sprite.path))
                        displayName = sprite.path;
                    else if (!string.IsNullOrEmpty(sprite.id))
                        displayName = sprite.id;

                    TreeViewItem item = new TreeViewItem() { Header = displayName };
                    item.Tag = sprite; // 🌟 灵魂绑定：把真实的数据对象塞进 Tag 里！
                    folder.Items.Add(item);
                }
                SpriteTreeView.Items.Add(folder);
            }

            // --- 🍒 2. Texts (文本对象) ---
            if (root.texts?.Count > 0)
            {
                TreeViewItem folder = new TreeViewItem() { Header = $"文字 Texts ({root.texts.Count})" };
                foreach (var txt in root.texts)
                {
                    // 🌟 智能命名：没 id 的时候，直接抓取文本内容的前15个字作为标题！
                    string displayName = "未命名文字";
                    if (!string.IsNullOrEmpty(txt.id) && !string.IsNullOrEmpty(txt.text))
                        displayName = $"{txt.id} (\"{txt.text}\")";
                    else if (!string.IsNullOrEmpty(txt.text))
                        displayName = txt.text.Length > 15 ? txt.text.Substring(0, 15) + "..." : txt.text;
                    else if (!string.IsNullOrEmpty(txt.id))
                        displayName = txt.id;

                    TreeViewItem item = new TreeViewItem() { Header = displayName };
                    item.Tag = txt; // 🌟 灵魂绑定：把真实的数据对象塞进 Tag 里！
                    folder.Items.Add(item);
                }
                TextTreeView.Items.Add(folder);
            }

            // --- 🍒 3. Videos (视频对象) ---
            if (root.videos?.Count > 0)
            {
                TreeViewItem folder = new TreeViewItem() { Header = $"视频 Videos ({root.videos.Count})" };
                foreach (var video in root.videos)
                {
                    // 🌟 智能命名：同图片逻辑
                    string displayName = "未命名视频";
                    if (!string.IsNullOrEmpty(video.id) && !string.IsNullOrEmpty(video.path))
                        displayName = $"{video.id} [{video.path}]";
                    else if (!string.IsNullOrEmpty(video.path))
                        displayName = video.path;
                    else if (!string.IsNullOrEmpty(video.id))
                        displayName = video.id;

                    TreeViewItem item = new TreeViewItem() { Header = displayName };
                    item.Tag = video; // 🌟 灵魂绑定：把真实的数据对象塞进 Tag 里！
                    folder.Items.Add(item);
                }
                VideoTreeView.Items.Add(folder);
            }

            // --- 🍒 4. Lines (线条对象) ---
            if (root.lines?.Count > 0)
            {
                TreeViewItem folder = new TreeViewItem() { Header = $"线条 Lines ({root.lines.Count})" };
                foreach (var line in root.lines)
                {
                    TreeViewItem item = new TreeViewItem() { Header = string.IsNullOrEmpty(line.id) ? "未命名线条" : line.id };
                    item.Tag = line; // 🌟 灵魂绑定！
                    folder.Items.Add(item);
                }
                LinesTreeView.Items.Add(folder);
            }

            // --- 🍒 5. Controllers (场景控制器) ---
            if (root.controllers?.Count > 0)
            {
                TreeViewItem folder = new TreeViewItem() { Header = $"场景控制 Controllers ({root.controllers.Count})" };
                int index = 1;
                foreach (var ctrl in root.controllers)
                {
                    TreeViewItem item = new TreeViewItem() { Header = $"场景控制器 {index++}" };
                    item.Tag = ctrl; // 🌟 灵魂绑定！
                    folder.Items.Add(item);
                }
                SceneTreeView.Items.Add(folder);
            }

            // --- 🍒 6. Note Controllers (音符控制器) ---
            if (root.note_controllers?.Count > 0)
            {
                TreeViewItem folder = new TreeViewItem() { Header = $"音符控制 NoteCtrls ({root.note_controllers.Count})" };
                foreach (var ctrl in root.note_controllers)
                {
                    // 先造一片空白树叶，并把控制器“真身”塞进去
                    TreeViewItem item = new TreeViewItem();
                    item.Tag = ctrl; // 🌟 灵魂绑定：注意，我们绑定的是 ctrl 对象本身！

                    // 智能判断：是对象就解析选择器，是数字就直接显示 ID
                    if (ctrl.note is Newtonsoft.Json.Linq.JObject jobj)
                    {
                        var selector = jobj.ToObject<NoteCtrlEventSelect>();
                        item.Header = selector.DisplayName;
                        item.Foreground = Brushes.DarkCyan;
                        item.FontWeight = FontWeights.Bold;
                    }
                    else
                    {
                        item.Header = $"Note ID: {ctrl.note}";
                    }

                    folder.Items.Add(item);
                }
                NoteCtrlTreeView.Items.Add(folder);
            }

            // --- 🍒 7. Templates (模板) ---
            if (root.templates?.Count > 0)
            {
                TreeViewItem folder = new TreeViewItem() { Header = $"模板 Templates ({root.templates.Count})" };
                foreach (var kvp in root.templates)
                {
                    TreeViewItem item = new TreeViewItem() { Header = string.IsNullOrEmpty(kvp.Key) ? "未命名模板" : kvp.Key };
                    item.Tag = kvp.Value; // 🌟 灵魂绑定：字典比较特殊，Key是名字，Value才是真正的模板对象！
                    folder.Items.Add(item);
                }
                EventTemplateTreeView.Items.Add(folder);
            }

            UpdateEmptyHintVisibility();
        }
        // ==========================================
        // 🌟 动态 UI 维护：空空如也提示
        // ==========================================
        public void UpdateEmptyHintVisibility()
        {
            if (EventTabControl == null || DynamicEmptyHint == null) return;
            var currentTreeView = GetCurrentActiveTreeView();
            if (currentTreeView != null)
            {
                if (currentTreeView.Items.Count > 0)
                {
                    currentTreeView.Visibility = Visibility.Visible;
                    DynamicEmptyHint.Visibility = Visibility.Collapsed;
                }
                else
                {
                    currentTreeView.Visibility = Visibility.Collapsed;
                    DynamicEmptyHint.Visibility = Visibility.Visible;
                }
            }
        }

        //树状列表，切换时，更新空空如也提示的显示状态
        private TreeView GetCurrentActiveTreeView()
        {
            if (EventTabControl.SelectedItem is TabItem selectedTab)
            {
                switch (selectedTab.Header.ToString())
                {
                    case "图片": return SpriteTreeView;
                    case "文字": return TextTreeView;
                    case "线条": return LinesTreeView;
                    case "视频": return VideoTreeView;
                    case "场景": return SceneTreeView;
                    case "音符": return NoteCtrlTreeView;
                    case "模板": return EventTemplateTreeView;
                }
            }
            return null;
        }

        // 🧹 专门用来清空左侧大本营的法术
        private void ClearAllDrawers()
        {
            SpriteTreeView.Items.Clear();
            TextTreeView.Items.Clear();
            VideoTreeView.Items.Clear();
            LinesTreeView.Items.Clear();
            SceneTreeView.Items.Clear();
            NoteCtrlTreeView.Items.Clear();
            EventTemplateTreeView.Items.Clear();
        }

        
        private void EventTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateEmptyHintVisibility();
    }
}