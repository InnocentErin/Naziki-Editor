using Naziki_Editor.Core;
using Naziki_Editor.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.IO; // ✨ 新增：文件操作
using System.Collections.Specialized; // ✨ 新增：剪贴板文件集合

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
        // 📢 WPF 路由命令：接收与执行中枢
        // ==========================================

        // 🛡️ 权限安检：只有当列表里真的有东西被选中时，才允许执行复制和删除！
        // 如果返回 false，UI 上的按钮甚至会自动变成灰色不可点击状态哦！
        private void Command_CanExecuteWithSelection(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = GetSelectedAsset() != null;
        }

        // 🚀 执行复制
        private void CommandCopy_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            ExecuteCopy(GetSelectedAsset());
        }

        // 🚀 执行粘贴
        private void CommandPaste_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            ExecutePaste();
        }

        // 🚀 执行删除
        private void CommandDelete_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            ExecuteDelete(GetSelectedAsset());
        }


        // ==========================================
        // 🛠️ 底层执行法术 (和我们之前讨论的核心逻辑一样)
        // ==========================================

        // 抓取当前选中的素材
        private AssetItemModel GetSelectedAsset()
        {
            if (MediaListBox.IsKeyboardFocusWithin) return MediaListBox.SelectedItem as AssetItemModel;
            if (TextListBox.IsKeyboardFocusWithin) return TextListBox.SelectedItem as AssetItemModel;
            if (LineListBox.IsKeyboardFocusWithin) return LineListBox.SelectedItem as AssetItemModel;
            if (TemplateListBox.IsKeyboardFocusWithin) return TemplateListBox.SelectedItem as AssetItemModel;

            return (MediaListBox.SelectedItem ?? TextListBox.SelectedItem ?? LineListBox.SelectedItem ?? TemplateListBox.SelectedItem) as AssetItemModel;
        }

        // 写入剪贴板
        private void ExecuteCopy(AssetItemModel item)
        {
            if (item != null && File.Exists(item.FilePath))
            {
                var files = new StringCollection { item.FilePath };
                Clipboard.SetFileDropList(files);
            }
        }

        // 从剪贴板粘贴
        private void ExecutePaste()
        {
            if (ParentMainWindow == null || string.IsNullOrEmpty(ParentMainWindow.CurrentProjectFilePath)) return;

            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList();
                string projectDir = Path.GetDirectoryName(ParentMainWindow.CurrentProjectFilePath);
                string targetDir = Path.Combine(projectDir, ParentMainWindow.CurrentProjectData.MaterialFolderPath);

                if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

                bool hasChanged = false;
                foreach (string sourceFile in files)
                {
                    if (File.Exists(sourceFile))
                    {
                        string fileName = Path.GetFileName(sourceFile);
                        string destFile = Path.Combine(targetDir, fileName);

                        int counter = 1;
                        while (File.Exists(destFile))
                        {
                            string nameOnly = Path.GetFileNameWithoutExtension(fileName);
                            string ext = Path.GetExtension(fileName);
                            destFile = Path.Combine(targetDir, $"{nameOnly}_副本{counter}{ext}");
                            counter++;
                        }

                        try
                        {
                            File.Copy(sourceFile, destFile);
                            hasChanged = true;
                        }
                        catch { }
                    }
                }

                if (hasChanged) ParentMainWindow.RefreshAllAssets();
            }
        }

        // 物理删除
        private void ExecuteDelete(AssetItemModel item)
        {
            if (item != null)
            {
                var result = MessageBox.Show($"确定要将素材【{item.DisplayName}】彻底删除吗？\n这是物理级销毁，不可撤销哦！", "小艾的危险警告", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        if (File.Exists(item.FilePath)) File.Delete(item.FilePath);
                        ParentMainWindow.RefreshAllAssets();
                    }
                    catch (System.Exception ex)
                    {
                        MessageBox.Show($"呜哇！删除被阻挡了 QAQ：\n{ex.Message}", "删除失败");
                    }
                }
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
            // ✨ 确保双击的是真实的素材，而不是列表的空白处
            if (sender is ListBox listBox && listBox.SelectedItem is Models.AssetItemModel selectedAsset)
            {
                Models.StoryboardObject newEvent = null;
                // 生成一个暂时的身份证，等进了编辑器玩家还会改
                string tempId = selectedAsset.AssetType.ToLower() + "_" + System.DateTime.Now.Ticks;

                // 根据素材类型创建对应的事件对象，并把路径属性刻在初始状态里，方便编辑器一打开就能看到效果
                if (!selectedAsset.FileName.EndsWith(".nem"))
                {
                    if (selectedAsset.AssetType == "Image")
                    {
                        newEvent = new Models.Sprite
                        {
                            Id = tempId,
                            // ✨ 路径属性刻在初始状态 (States[0]) 里
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
                }




                else if (selectedAsset.AssetType == "Text" || selectedAsset.AssetType == "Line" || selectedAsset.AssetType == "Template")
                {
                    try
                    {
                        if (selectedAsset.Tag is Models.StoryboardRoot miniRoot)
                        {
                            // 智能捕获生命体
                            if (miniRoot.sprites?.Count > 0) newEvent = miniRoot.sprites[0];
                            else if (miniRoot.texts?.Count > 0) newEvent = miniRoot.texts[0];
                            else if (miniRoot.lines?.Count > 0) newEvent = miniRoot.lines[0];
                            else if (miniRoot.videos?.Count > 0) newEvent = miniRoot.videos[0];
                            else if (miniRoot.controllers?.Count > 0) newEvent = miniRoot.controllers[0];
                            else if (miniRoot.note_controllers?.Count > 0) newEvent = miniRoot.note_controllers[0];
                        }

                        // 4. 重置身份：为了防止和别人撞名字，给它发一张新的临时身份证
                        if (newEvent != null)
                        {
                            newEvent.Id = newEvent.Id + "_nem_" + System.DateTime.Now.Ticks;
                        }
                    }
                    catch (System.Exception ex)
                    {
                        System.Windows.MessageBox.Show($"解析胶囊失败啦！是不是文件坏了呢？\n原因：{ex.Message}", "小艾的报错提醒");
                    }
                }



                if (newEvent != null)
                {
                    // 🚀 穿甲弹雷达：使用本文件最上方定义好的 ParentMainWindow，绝对不会迷路！
                    if (Window.GetWindow(this) is MainWindow main)
                    {
                        // 呼叫全新的流程：带上字典检查重名，编辑完再决定要不要保存！
                        main.CreateNewEventFromAsset(newEvent);
                        e.Handled = true;// 拦截鼠标事件，防止穿透
                    }
                }
            }
        }




    }
}