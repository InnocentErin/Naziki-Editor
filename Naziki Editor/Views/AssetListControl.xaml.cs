using Naziki_Editor.Core;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Services;
using Naziki_Editor.Core.Messaging;
using Naziki_Editor.Core.Storyboard;
using Naziki_Editor.Core.Shortcuts;
using Naziki_Editor.Models;
using Naziki_Editor.UI.ViewModels;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.IO;
using System.Collections.Specialized;
using Microsoft.Win32;

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

        // 🖱️ 拖拽相关：记录鼠标按下位置，用于判断是否启动拖拽
        private System.Windows.Point _dragStartPoint;
        private bool _isDragging = false;

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
                IStoryboardEntity newEvent = CreateEntityFromAsset(selectedAsset);
                if (newEvent != null)
                {
                    // 📢 对着大喇叭喊：有个素材被双击解析好啦！主窗口快接单！
                    _messageBroker.Publish("CreateEventFromAsset", newEvent);
                    e.Handled = true;
                }
            }
        }

        // ==========================================
        // 🖱️ 拖拽功能：将素材拖到时间轴创建对象
        // ==========================================

        /// <summary>
        /// 鼠标按下时记录起始位置，准备可能的拖拽操作。
        /// </summary>
        private void TileBorder_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            _isDragging = false;
        }

        /// <summary>
        /// 鼠标移动时检测是否超过拖拽阈值，若是则启动拖拽。
        /// 拖拽数据包含素材的显示名称，供时间轴等目标区域识别。
        /// </summary>
        private void TileBorder_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (Mouse.LeftButton != MouseButtonState.Pressed) return;

            System.Windows.Point currentPos = e.GetPosition(null);
            System.Windows.Vector diff = currentPos - _dragStartPoint;

            // 只有移动超过系统拖拽阈值才启动拖拽，避免误触
            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                if (_isDragging) return;
                _isDragging = true;

                // 获取当前拖拽源对应的素材
                if (sender is Border border && border.DataContext is AssetItemViewModel asset)
                {
                    // 先选中该素材，确保拖拽的就是它
                    SelectAssetInListBox(asset);

                    // 创建对应的实体对象作为拖拽数据
                    IStoryboardEntity entity = CreateEntityFromAsset(asset);
                    if (entity != null)
                    {
                        // 包装拖拽数据：同时携带实体和素材信息
                        var dragData = new AssetDragData
                        {
                            Entity = entity,
                            Asset = asset
                        };

                        // 启动拖拽操作，Copy 模式表示从素材库复制到时间轴
                        DragDrop.DoDragDrop(border, dragData, DragDropEffects.Copy);
                        _isDragging = false;
                    }
                }
            }
        }

        /// <summary>
        /// 根据素材类型选中对应的 ListBox 中的项。
        /// </summary>
        private void SelectAssetInListBox(AssetItemViewModel asset)
        {
            if (asset == null) return;

            ListBox targetListBox = null;
            switch (asset.AssetType)
            {
                case "Image":
                case "Video":
                    targetListBox = MediaListBox;
                    break;
                case "Text":
                    targetListBox = TextListBox;
                    break;
                case "Line":
                    targetListBox = LineListBox;
                    break;
                case "Template":
                case "Scene":
                    targetListBox = TemplateListBox;
                    break;
            }

            if (targetListBox != null)
            {
                targetListBox.SelectedItem = asset;
                targetListBox.Focus();
            }
        }

        // ==========================================
        // 📥 导入素材功能
        // ==========================================

        /// <summary>
        /// 点击"导入素材"按钮：打开文件选择对话框，将选中的文件复制到项目素材目录。
        /// </summary>
        private void BtnImportAsset_Click(object sender, RoutedEventArgs e)
        {
            if (Context == null || string.IsNullOrEmpty(Context.ProjectFilePath))
            {
                _dialogService?.ShowErrorDialog("请先打开或创建一个项目再导入素材哦！", "小艾的温馨提示", null);
                return;
            }

            var openFileDialog = new OpenFileDialog
            {
                Title = "选择要导入的素材文件",
                Multiselect = true,
                Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|视频文件|*.mp4;*.webm;*.avi;*.mov|所有支持的素材|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.mp4;*.webm;*.avi;*.mov|所有文件|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string projectDir = Path.GetDirectoryName(Context.ProjectFilePath);
                string targetDir = Path.Combine(projectDir, Context.ProjectData.MaterialFolderPath);

                if (!Directory.Exists(targetDir))
                    Directory.CreateDirectory(targetDir);

                int importCount = 0;
                foreach (string sourceFile in openFileDialog.FileNames)
                {
                    if (!File.Exists(sourceFile)) continue;

                    string fileName = Path.GetFileName(sourceFile);
                    string destFile = Path.Combine(targetDir, fileName);

                    // 文件名冲突处理：自动添加序号
                    int counter = 1;
                    while (File.Exists(destFile))
                    {
                        string nameOnly = Path.GetFileNameWithoutExtension(fileName);
                        string ext = Path.GetExtension(fileName);
                        destFile = Path.Combine(targetDir, $"{nameOnly}_导入{counter}{ext}");
                        counter++;
                    }

                    try
                    {
                        File.Copy(sourceFile, destFile);
                        importCount++;
                    }
                    catch (Exception ex)
                    {
                        _dialogService?.ShowErrorDialog($"导入文件失败：{fileName}\n原因：{ex.Message}", "导入失败", ex.ToString());
                    }
                }

                if (importCount > 0)
                {
                    _messageBroker?.Publish("RequestRefreshAssets");
                    _dialogService?.ShowMessage($"成功导入 {importCount} 个素材文件！", "导入完成", Core.Abstractions.DialogMessageType.Info);
                }
            }
        }

        // ==========================================
        // 🏭 素材 → 实体创建辅助方法
        // ==========================================

        /// <summary>
        /// 根据素材创建一个可用于时间轴的 IStoryboardEntity。
        /// 与双击逻辑共用，拖拽时也调用此方法。
        /// </summary>
        private IStoryboardEntity CreateEntityFromAsset(AssetItemViewModel asset)
        {
            if (asset == null) return null;

            IStoryboardEntity newEvent = null;

            if (!asset.FileName.EndsWith(".nem"))
            {
                // 外部媒体文件：图片→Sprite，视频→Video
                if (asset.AssetType == "Image")
                {
                    newEvent = _entityFactory.CreateSpriteFromAsset(asset.FileName);
                }
                else if (asset.AssetType == "Video")
                {
                    newEvent = _entityFactory.CreateVideoFromAsset(asset.FileName);
                }
            }
            else if (asset.AssetType == "Text" || asset.AssetType == "Line" || asset.AssetType == "Template")
            {
                // .nem 胶囊文件：从预解析的 StoryboardRoot 中提取第一个实体
                try
                {
                    if (asset.Tag is StoryboardRoot miniRoot)
                    {
                        if (miniRoot.sprites?.Count > 0) newEvent = miniRoot.sprites[0];
                        else if (miniRoot.texts?.Count > 0) newEvent = miniRoot.texts[0];
                        else if (miniRoot.lines?.Count > 0) newEvent = miniRoot.lines[0];
                        else if (miniRoot.videos?.Count > 0) newEvent = miniRoot.videos[0];
                        else if (miniRoot.controllers?.Count > 0) newEvent = miniRoot.controllers[0];
                        else if (miniRoot.note_controllers?.Count > 0) newEvent = miniRoot.note_controllers[0];

                        if (newEvent != null)
                            newEvent.Id = newEvent.Id + "_nem_" + DateTime.Now.Ticks;
                    }
                }
                catch (Exception ex)
                {
                    _dialogService?.ShowErrorDialog($"解析胶囊失败啦！\n原因：{ex.Message}", "小艾的报错提醒", ex.ToString());
                }
            }

            return newEvent;
        }
    }

    // ==========================================
    // 📦 拖拽数据包装类：同时携带实体对象和素材信息
    // ==========================================
    /// <summary>
    /// 素材拖拽时传递的数据包，包含已创建的实体和原始素材信息。
    /// 时间轴等目标区域通过此对象获取要添加的实体。
    /// </summary>
    public class AssetDragData
    {
        /// <summary>已创建的故事板实体（Sprite/Text/Line 等）</summary>
        public IStoryboardEntity Entity { get; set; }

        /// <summary>原始素材 ViewModel 信息</summary>
        public AssetItemViewModel Asset { get; set; }
    }

    // ==========================================
    // 🖼️ 文件路径 → 缩略图转换器
    // ==========================================
    /// <summary>
    /// 将素材文件路径转换为 WPF BitmapImage，用于列表缩略图显示。
    /// 使用 BitmapCacheOption.OnLoad 避免文件锁定，缓存缩略图。
    /// </summary>
    public class FilePathToImageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string filePath && !string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;  // 加载后立即释放文件锁
                    bitmap.DecodePixelWidth = 128;                   // 缩略图尺寸限制，节省内存
                    bitmap.EndInit();
                    bitmap.Freeze();                                  // 冻结以支持跨线程访问
                    return bitmap;
                }
                catch
                {
                    // 加载失败（例如损坏的图片），返回 null，由 DataTrigger 显示 emoji 兜底
                    return null;
                }
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException("FilePathToImageConverter 不支持反向转换。");
        }
    }
}