using Naziki_Editor.Core;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Services;
using Naziki_Editor.Core.Messaging;
using Naziki_Editor.Core.Storyboard;
using Naziki_Editor.Core.Shortcuts;
using Naziki_Editor.Models;
using Naziki_Editor.UI.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.IO;
using System.Collections.Specialized;

namespace Naziki_Editor.Views
{
    public partial class AssetListControl : UserControl, IShortcutAware
    {
        public ShortcutContext ShortcutContext => ShortcutContext.AssetList;
        public bool OnShortcutFocusGained() => true;
        public void OnShortcutFocusLost() { }

        // 🌟 正式接入粮仓！再也不用强行认主窗口当爹啦！
        public State.ProjectDataContext Context { get; private set; }
        public void LoadContext(State.ProjectDataContext context) => Context = context;

        private readonly IEntityFactory _entityFactory = new EntityFactory();
        private readonly IMessageBroker _messageBroker;
        private readonly IDialogService _dialogService;

        public AssetListControl()
        {
            InitializeComponent();
        }

        public AssetListControl(IMessageBroker messageBroker, IDialogService dialogService) : this()
        {
            _messageBroker = messageBroker;
            _dialogService = dialogService;
        }

        private void EditBox_KeyDown(object sender, KeyEventArgs e)
        {
            var textBox = sender as TextBox;
            var item = textBox.DataContext as AssetItemViewModel;

            if (e.Key == Key.Enter) CommitRename(item);
            else if (e.Key == Key.Escape) item.IsEditing = false;
        }

        private void CommitRename(AssetItemViewModel item)
        {
            // ✨ 改成从安全的 Context 粮仓里拿数据！
            if (item == null || Context == null || Context.ProjectData == null) return;

            string projectDir = System.IO.Path.GetDirectoryName(Context.ProjectFilePath);
            string matFolder = Context.ProjectData.MaterialFolderPath;

            if (item.AssetType == "Image" || item.AssetType == "Video")
                new AssetMetaManager().SetExternalAssetDisplayName(projectDir, matFolder, item.FileName, item.DisplayName);
            else if (item.AssetType == "Text" || item.AssetType == "Line")
                new AssetMetaManager().RenameNemAsset(item.FilePath, item.DisplayName);

            item.IsEditing = false;
        }

        private void EditBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && tb.DataContext is AssetItemViewModel item && item.IsEditing) CommitRename(item);
        }

        private void Command_CanExecuteWithSelection(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = GetSelectedAsset() != null;
        private void CommandCopy_Executed(object sender, ExecutedRoutedEventArgs e) => ExecuteCopy(GetSelectedAsset());
        private void CommandPaste_Executed(object sender, ExecutedRoutedEventArgs e) => ExecutePaste();
        private void CommandDelete_Executed(object sender, ExecutedRoutedEventArgs e) => ExecuteDelete(GetSelectedAsset());

        /// <summary>
        /// 公开的复制入口（供快捷键系统调用）。
        /// </summary>
        public void ExecuteCopy() => ExecuteCopy(GetSelectedAsset());

        /// <summary>
        /// 公开的粘贴入口（供快捷键系统调用）。
        /// </summary>
        public void ExecutePaste() => ExecutePasteInternal();

        /// <summary>
        /// 公开的删除入口（供快捷键系统调用）。
        /// </summary>
        public void ExecuteDelete() => ExecuteDelete(GetSelectedAsset());

        private AssetItemViewModel GetSelectedAsset()
        {
            if (MediaListBox.IsKeyboardFocusWithin) return MediaListBox.SelectedItem as AssetItemViewModel;
            if (TextListBox.IsKeyboardFocusWithin) return TextListBox.SelectedItem as AssetItemViewModel;
            if (LineListBox.IsKeyboardFocusWithin) return LineListBox.SelectedItem as AssetItemViewModel;
            if (TemplateListBox.IsKeyboardFocusWithin) return TemplateListBox.SelectedItem as AssetItemViewModel;
            return (MediaListBox.SelectedItem ?? TextListBox.SelectedItem ?? LineListBox.SelectedItem ?? TemplateListBox.SelectedItem) as AssetItemViewModel;
        }

        private void ExecuteCopy(AssetItemViewModel item)
        {
            if (item != null && File.Exists(item.FilePath)) Clipboard.SetFileDropList(new StringCollection { item.FilePath });
        }

        private void ExecutePasteInternal()
        {
            if (Context == null || string.IsNullOrEmpty(Context.ProjectFilePath)) return;

            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList();
                string projectDir = Path.GetDirectoryName(Context.ProjectFilePath);
                string targetDir = Path.Combine(projectDir, Context.ProjectData.MaterialFolderPath);

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
                if (hasChanged) _messageBroker.Publish("RequestRefreshAssets");
            }
        }

        private void ExecuteDelete(AssetItemViewModel item)
        {
            if (item != null)
            {
                var result = _dialogService.ShowYesNo($"确定要将素材【{item.DisplayName}】彻底删除吗？\n这是物理级销毁，不可撤销哦！", "小艾的危险警告");
                if (result)
                {
                    try { if (File.Exists(item.FilePath)) File.Delete(item.FilePath); _messageBroker.Publish("RequestRefreshAssets"); }
                    catch (System.Exception ex) { _dialogService.ShowErrorDialog($"呜哇！删除被阻挡了 QAQ：\n{ex.Message}", "删除失败", ex.ToString()); }
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
        private void MenuRename_Click(object sender, RoutedEventArgs e) { if (sender is MenuItem menuItem && menuItem.DataContext is AssetItemViewModel item) item.IsEditing = true; }
        private void MenuRefresh_Click(object sender, RoutedEventArgs e) => _messageBroker.Publish("RequestRefreshAssets");

        private void ListAssets_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBox listBox && listBox.SelectedItem is AssetItemViewModel selectedAsset)
            {
                IStoryboardEntity newEvent = null; // ✨ 完美升级至核心万能接口 IStoryboardEntity

                if (!selectedAsset.FileName.EndsWith(".nem"))
                {
                    // ✨ 核心修正：利用全新的分离型出厂配置 BaseState 灌入素材路径，移除过时的 CytoidColor 
                    if (selectedAsset.AssetType == "Image")
                    {
                        newEvent = _entityFactory.CreateSpriteFromAsset(selectedAsset.FileName);
                    }
                    else if (selectedAsset.AssetType == "Video")
                    {
                        newEvent = _entityFactory.CreateVideoFromAsset(selectedAsset.FileName);
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
                    catch (System.Exception ex) { _dialogService.ShowErrorDialog($"解析胶囊失败啦！\n原因：{ex.Message}", "小艾的报错提醒", ex.ToString()); }
                }

                if (newEvent != null)
                {
                    // 📢 对着大喇叭喊：有个素材被双击解析好啦！主窗口快接单！
                    _messageBroker.Publish("CreateEventFromAsset", newEvent);
                    e.Handled = true;
                }
            }
        }
    }
}