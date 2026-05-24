using Naziki_Editor.Core;
using Naziki_Editor.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.IO;
using System.Collections.Specialized;

namespace Naziki_Editor.Views
{
    public partial class AssetListControl : UserControl
    {
        public MainWindow ParentMainWindow => Window.GetWindow(this) as MainWindow;

        public AssetListControl()
        {
            InitializeComponent();
        }

        private void EditBox_KeyDown(object sender, KeyEventArgs e)
        {
            var textBox = sender as TextBox;
            var item = textBox.DataContext as AssetItemModel;

            if (e.Key == Key.Enter) CommitRename(item);
            else if (e.Key == Key.Escape) item.IsEditing = false;
        }

        private void CommitRename(AssetItemModel item)
        {
            if (item == null || ParentMainWindow == null) return;

            string projectDir = System.IO.Path.GetDirectoryName(ParentMainWindow.CurrentProjectFilePath);
            string matFolder = ParentMainWindow.CurrentProjectData.MaterialFolderPath;

            if (item.AssetType == "Image" || item.AssetType == "Video")
                AssetMetaManager.SetExternalAssetDisplayName(projectDir, matFolder, item.FileName, item.DisplayName);
            else if (item.AssetType == "Text" || item.AssetType == "Line")
                AssetMetaManager.RenameNemAsset(item.FilePath, item.DisplayName);

            item.IsEditing = false;
        }

        private void EditBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && tb.DataContext is AssetItemModel item && item.IsEditing) CommitRename(item);
        }

        private void Command_CanExecuteWithSelection(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = GetSelectedAsset() != null;
        private void CommandCopy_Executed(object sender, ExecutedRoutedEventArgs e) => ExecuteCopy(GetSelectedAsset());
        private void CommandPaste_Executed(object sender, ExecutedRoutedEventArgs e) => ExecutePaste();
        private void CommandDelete_Executed(object sender, ExecutedRoutedEventArgs e) => ExecuteDelete(GetSelectedAsset());

        private AssetItemModel GetSelectedAsset()
        {
            if (MediaListBox.IsKeyboardFocusWithin) return MediaListBox.SelectedItem as AssetItemModel;
            if (TextListBox.IsKeyboardFocusWithin) return TextListBox.SelectedItem as AssetItemModel;
            if (LineListBox.IsKeyboardFocusWithin) return LineListBox.SelectedItem as AssetItemModel;
            if (TemplateListBox.IsKeyboardFocusWithin) return TemplateListBox.SelectedItem as AssetItemModel;
            return (MediaListBox.SelectedItem ?? TextListBox.SelectedItem ?? LineListBox.SelectedItem ?? TemplateListBox.SelectedItem) as AssetItemModel;
        }

        private void ExecuteCopy(AssetItemModel item)
        {
            if (item != null && File.Exists(item.FilePath)) Clipboard.SetFileDropList(new StringCollection { item.FilePath });
        }

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
                        try { File.Copy(sourceFile, destFile); hasChanged = true; } catch { }
                    }
                }
                if (hasChanged) ParentMainWindow.RefreshAllAssets();
            }
        }

        private void ExecuteDelete(AssetItemModel item)
        {
            if (item != null)
            {
                var result = MessageBox.Show($"确定要将素材【{item.DisplayName}】彻底删除吗？\n这是物理级销毁，不可撤销哦！", "小艾的危险警告", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    try { if (File.Exists(item.FilePath)) File.Delete(item.FilePath); ParentMainWindow.RefreshAllAssets(); }
                    catch (System.Exception ex) { MessageBox.Show($"呜哇！删除被阻挡了 QAQ：\n{ex.Message}", "删除失败"); }
                }
            }
        }

        public void RefreshAssetListUI(AssetBundle bundle)
        {
            if (bundle == null) { MediaListBox.ItemsSource = null; TextListBox.ItemsSource = null; LineListBox.ItemsSource = null; TemplateListBox.ItemsSource = null; return; }
            MediaListBox.ItemsSource = bundle.MediaAssets;
            TextListBox.ItemsSource = bundle.TextAssets;
            LineListBox.ItemsSource = bundle.LineAssets;
            TemplateListBox.ItemsSource = bundle.TemplateAssets;
        }

        private void EditBox_Loaded(object sender, RoutedEventArgs e) { var tb = sender as TextBox; tb.Focus(); tb.SelectAll(); }
        private void MenuRename_Click(object sender, RoutedEventArgs e) { if (sender is MenuItem menuItem && menuItem.DataContext is AssetItemModel item) item.IsEditing = true; }
        private void MenuRefresh_Click(object sender, RoutedEventArgs e) => ParentMainWindow?.RefreshAllAssets();

        private void ListAssets_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBox listBox && listBox.SelectedItem is AssetItemModel selectedAsset)
            {
                IStoryboardEntity newEvent = null; // ✨ 完美升级至核心万能接口 IStoryboardEntity
                string tempId = selectedAsset.AssetType.ToLower() + "_" + System.DateTime.Now.Ticks;

                if (!selectedAsset.FileName.EndsWith(".nem"))
                {
                    // ✨ 核心修正：利用全新的分离型出厂配置 BaseState 灌入素材路径，移除过时的 CytoidColor 
                    if (selectedAsset.AssetType == "Image")
                    {
                        var sprite = new C2Sprite { Id = tempId };
                        sprite.BaseState.Path = selectedAsset.FileName;
                        sprite.BaseState.X = new UnitFloat { Value = 0, Unit = ReferenceUnit.World };
                        sprite.BaseState.Y = new UnitFloat { Value = 0, Unit = ReferenceUnit.World };
                        newEvent = sprite;
                    }
                    else if (selectedAsset.AssetType == "Video")
                    {
                        var video = new C2Video { Id = tempId };
                        video.BaseState.Path = selectedAsset.FileName;
                        newEvent = video;
                    }
                }
                else if (selectedAsset.AssetType == "Text" || selectedAsset.AssetType == "Line" || selectedAsset.AssetType == "Template")
                {
                    try
                    {
                        if (selectedAsset.Tag is StoryboardRoot miniRoot)
                        {
                            if (miniRoot.sprites?.Count > 0) newEvent = miniRoot.sprites[0];
                            else if (miniRoot.texts?.Count > 0) newEvent = miniRoot.texts[0];
                            else if (miniRoot.lines?.Count > 0) newEvent = miniRoot.lines[0];
                            else if (miniRoot.videos?.Count > 0) newEvent = miniRoot.videos[0];
                            else if (miniRoot.controllers?.Count > 0) newEvent = miniRoot.controllers[0];
                            else if (miniRoot.note_controllers?.Count > 0) newEvent = miniRoot.note_controllers[0];
                        }

                        if (newEvent != null) newEvent.Id = newEvent.Id + "_nem_" + System.DateTime.Now.Ticks;
                    }
                    catch (System.Exception ex) { MessageBox.Show($"解析胶囊失败啦！\\n原因：{ex.Message}", "小艾的报错提醒"); }
                }

                if (newEvent != null && Window.GetWindow(this) is MainWindow main)
                {
                    main.CreateNewEventFromAsset(newEvent);
                    e.Handled = true;
                }
            }
        }
    }
}