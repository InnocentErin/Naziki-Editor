using Naziki_Editor.Core;
using Naziki_Editor.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Naziki_Editor.Views
{
    public partial class AssetListControl : UserControl
    {
        // 🌟 指挥官：我们需要获取当前主窗口引用的路径数据来读写账本
        public MainWindow ParentMainWindow => Window.GetWindow(this) as MainWindow;

        public AssetListControl()
        {
            InitializeComponent();
        }

        // ==========================================
        // ⌨️ 输入框魔法 1：按下按键的处理
        // ==========================================
        private void EditBox_KeyDown(object sender, KeyEventArgs e)
        {
            var textBox = sender as TextBox;
            var item = textBox.DataContext as AssetItemModel;

            if (e.Key == Key.Enter) // 确认修改
            {
                CommitRename(item);
            }
            else if (e.Key == Key.Escape) // 放弃修改
            {
                item.IsEditing = false;
                // 这里其实可以加上重新 Load 逻辑来恢复旧名字
            }
        }

        // ==========================================
        // 💾 核心法术：将新名字刻进账本或 .nem 内部
        // ==========================================
        private void CommitRename(AssetItemModel item)
        {
            if (item == null || ParentMainWindow == null) return;

            string projectDir = System.IO.Path.GetDirectoryName(ParentMainWindow.CurrentProjectFilePath);
            string matFolder = ParentMainWindow.CurrentProjectData.MaterialFolderPath;

            if (item.AssetType == "Image" || item.AssetType == "Video")
            {
                // 1. 调用咱们上一轮写的【账本管家】去记账！
                AssetMetaManager.SetExternalAssetDisplayName(projectDir, matFolder, item.FileName, item.DisplayName);
            }
            else if (item.AssetType == "Text" || item.AssetType == "Line")
            {
                // 2. 调用咱们上一轮写的【账本管家】去改写物理 .nem 文件内容！
                AssetMetaManager.RenameNemAsset(item.FilePath, item.DisplayName);
            }

            item.IsEditing = false; // 退出编辑模式
        }

        // 失去焦点时自动确认
        private void EditBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && tb.DataContext is AssetItemModel item && item.IsEditing)
            {
                CommitRename(item);
            }
        }

        // ==========================================
        // 🔄 接收扫描大礼包，瞬间画出所有磁贴！
        // ==========================================
        public void RefreshAssetListUI(AssetBundle bundle)
        {
            // 如果没数据，直接清空数据源
            if (bundle == null)
            {
                MediaListBox.ItemsSource = null;
                TextListBox.ItemsSource = null;
                LineListBox.ItemsSource = null;
                TemplateListBox.ItemsSource = null;
                return;
            }

            // 🌟 魔法降临：只要把数据源喂给它们，磁贴瞬间自己长出来！
            MediaListBox.ItemsSource = bundle.MediaAssets;
            TextListBox.ItemsSource = bundle.TextAssets;
            LineListBox.ItemsSource = bundle.LineAssets;
            TemplateListBox.ItemsSource = bundle.TemplateAssets;
        }


        // 输入框出现时自动全选并夺取焦点，这才是顶级体验！
        private void EditBox_Loaded(object sender, RoutedEventArgs e)
        {
            var tb = sender as TextBox;
            tb.Focus();
            tb.SelectAll();
        }

        // ==========================================
        // ✏️ 右键菜单：点选“重命名素材”触发内联变形
        // ==========================================
        private void MenuRename_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is AssetItemModel item)
            {
                // 瞬间激活变身状态！前台 DataTrigger 就会把打字框顶出来
                item.IsEditing = true;
            }
        }

        // ==========================================
        // 🔄 右键菜单：在列表空白处右键点击“刷新素材库”
        // ==========================================
        private void MenuRefresh_Click(object sender, RoutedEventArgs e)
        {
            // 越级呼叫大本营的雷达，全盘重新扫描磁盘！
            ParentMainWindow?.RefreshAllAssets();
        }

        private void ListAssets_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is ListBox listBox && listBox.SelectedItem is Models.AssetItemModel selectedAsset)
            {
                Models.StoryboardObject newEvent = null;
                // 生成一个暂时的身份证，等进了编辑器玩家还会改
                string tempId = selectedAsset.AssetType.ToLower() + "_" + DateTime.Now.Ticks;

                if (selectedAsset.AssetType == "Image")
                {
                    newEvent = new Models.Sprite
                    {
                        Id = tempId,
                        States = new System.Collections.Generic.List<Models.SpriteState>
                { new Models.SpriteState { Time = 0f, Path = selectedAsset.FileName, Color = new Models.CytoidColor() } }
                    };
                }
                else if (selectedAsset.AssetType == "Video")
                {
                    newEvent = new Models.Video
                    {
                        Id = tempId,
                        States = new System.Collections.Generic.List<Models.VideoState>
                { new Models.VideoState { Time = 0f, Path = selectedAsset.FileName, Color = new Models.CytoidColor() } }
                    };
                }
                // TODO: 如果是 Template (.nem)，稍后我们在这里加入解析逻辑！

                if (newEvent != null)
                {
                    // ✨ 穿甲弹雷达：绝对能找到主窗口！
                    if (Window.GetWindow(this) is MainWindow main)
                    {
                        main.CreateNewEventFromAsset(newEvent);
                        e.Handled = true;
                    }
                }
            }
        }
    }
}