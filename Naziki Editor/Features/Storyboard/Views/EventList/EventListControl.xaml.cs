using Microsoft.Win32;
using Naziki_Editor.Core;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Services;
using Naziki_Editor.Core.Messaging;
using Naziki_Editor.Core.Project;
using Naziki_Editor.Core.Storyboard;
using Naziki_Editor.Core.Shortcuts;
using Naziki_Editor.Models;
using Naziki_Editor.State;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Naziki_Editor.Views
{
    public partial class EventListControl : UserControl, IShortcutAware
    {
        public ShortcutContext ShortcutContext => ShortcutContext.EventList;
        public bool OnShortcutFocusGained() => true;
        public void OnShortcutFocusLost() { }

        public event Action<string, StoryboardRoot> OnStoryboardLoaded;
        public event Action<AssetBundle> OnAssetScanned;
        public event Action<object> OnEventNodeSelected;

        public event Action OnAddSpriteRequested;
        public event Action OnAddTextRequested;
        public event Action OnAddLineRequested;
        public event Action OnAddVideoRequested;
        public event Action OnAddSceneRequested;
        public event Action OnAddNoteCtrlRequested;
        public event Action OnAddTemplateRequested; 

        private void BtnAddSprite_Click(object sender, RoutedEventArgs e) => OnAddSpriteRequested?.Invoke();
        private void BtnAddText_Click(object sender, RoutedEventArgs e) => OnAddTextRequested?.Invoke();
        private void BtnAddLine_Click(object sender, RoutedEventArgs e) => OnAddLineRequested?.Invoke();
        private void BtnAddVideo_Click(object sender, RoutedEventArgs e) => OnAddVideoRequested?.Invoke();
        private void BtnAddScene_Click(object sender, RoutedEventArgs e) => OnAddSceneRequested?.Invoke();
        private void BtnAddNoteCtrl_Click(object sender, RoutedEventArgs e) => OnAddNoteCtrlRequested?.Invoke();
        private void BtnAddTemplate_Click(object sender, RoutedEventArgs e) => OnAddTemplateRequested?.Invoke();



        public event Action<string, C2Template> OnTemplateDoubleClicked;

        public ProjectDataContext Context { get; private set; }
        private IProjectService _projectService;
        private IStoryboardRepository _storyboardRepository;
        private IMessageBroker _messageBroker;
        private IDialogService _dialogService;
        private INotificationService _notificationService;
        private IStoryboardEntity? _clipboardEntity;

        public void LoadContext(ProjectDataContext context) => Context = context;

        public EventListControl()
        {
            InitializeComponent();
            UpdateEmptyHintVisibility();
        }

        // 修改点 1：将原本带参数的构造函数逻辑提取成公开的 InitDependencies 方法
        public void InitDependencies(IMessageBroker messageBroker, IDialogService dialogService, IProjectService projectService, IStoryboardRepository storyboardRepository, INotificationService notificationService)
        {
            _messageBroker = messageBroker;
            _dialogService = dialogService;
            _projectService = projectService;
            _storyboardRepository = storyboardRepository;
            _notificationService = notificationService;
        }

        // 修改点 2：原有的带参构造改为调用该方法（保持向后兼容）
        public EventListControl(IMessageBroker messageBroker, IDialogService dialogService, IProjectService projectService, IStoryboardRepository storyboardRepository, INotificationService notificationService) : this()
        {
            InitDependencies(messageBroker, dialogService, projectService, storyboardRepository, notificationService);
        }

        // ==========================================
        // 🔮 一键智能读档基站
        // ==========================================
        public void ExecuteImportStoryboard()
        {
            // 无谱面时允许导入，但给出提示
            if (Context == null || !Context.HasChart)
            {
                // 不阻止，仅日志记录（提示条已在 XAML 中显示）
                System.Diagnostics.Debug.WriteLine("[EventListControl] 无谱面状态下导入故事板，Note Controller 相关功能将受限。");
            }

            OpenFileDialog openFileDialog = new OpenFileDialog { Filter = "Cytoid 故事板 (*.json)|*.json|所有文件 (*.*)|*.*", Title = "请选择你的故事板文件" };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    var result = _projectService.ImportStoryboard(openFileDialog.FileName, Context.ProjectData);

                    // 🌟 物理接线：提前将路径汇报给大管家，确保 TryLoad 探头能拿到正确的坐标！
                    Context.StoryboardPath = openFileDialog.FileName;
                    Context.Storyboard = result.Storyboard;
                    Context.StoryboardMeta = result.Meta;

                    LoadStoryboardUI();
                    OnStoryboardLoaded?.Invoke(openFileDialog.FileName, result.Storyboard);
                }
                catch (Exception ex)
                {
                    _dialogService.ShowErrorDialog(ex.Message, "加载失败", ex.ToString());
                    ClearAllDrawers();
                    UpdateEmptyHintVisibility();
                }
            }
        }

        public void AddNoteGroupToTree(TreeViewItem groupItem)
        {
            // 兼容 ItemsSource 模式：刷新整个列表
            LoadStoryboardUI();
            UpdateEmptyHintVisibility();
        }

        public void LoadStoryboardUI()
        {
            if (Context == null || !Context.HasStoryboard) return;
            var root = Context.Storyboard;
            AppServices.GetService<IStoryboardDocumentValidator>().Validate(root, Context);

            ClearAllDrawers();

            // ✨ 降临过滤器：只要 TargetId 属性里存在非空字串，意味着它是提线木偶，前台列表冷酷蒸发！
            var spriteItems = new List<EventListItemViewModel>();
            if (root.sprites?.Count > 0) foreach (var obj in root.sprites)
            {
                if (!string.IsNullOrEmpty(obj.TargetId)) continue;
                spriteItems.Add(new EventListItemViewModel
                {
                    Id = obj.Id ?? "?",
                    DisplayContent = EventNameResolver.GetDisplayName(obj),
                    DisplayTime = FormatTime(GetStartTime(obj)),
                    Tag = obj,
                    SortTime = GetStartTime(obj)
                });
            }
            spriteItems = spriteItems.OrderBy(s => s.SortTime).ToList();
            SpriteListBox.ItemsSource = spriteItems;
            SpriteListBox.Visibility = spriteItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            var textItems = new List<EventListItemViewModel>();
            if (root.texts?.Count > 0) foreach (var obj in root.texts)
            {
                if (!string.IsNullOrEmpty(obj.TargetId)) continue;
                textItems.Add(new EventListItemViewModel
                {
                    Id = obj.Id ?? "?",
                    DisplayContent = EventNameResolver.GetDisplayName(obj),
                    DisplayTime = FormatTime(GetStartTime(obj)),
                    Tag = obj,
                    SortTime = GetStartTime(obj)
                });
            }
            textItems = textItems.OrderBy(s => s.SortTime).ToList();
            TextListBox.ItemsSource = textItems;
            TextListBox.Visibility = textItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            var videoItems = new List<EventListItemViewModel>();
            if (root.videos?.Count > 0) foreach (var obj in root.videos)
            {
                if (!string.IsNullOrEmpty(obj.TargetId)) continue;
                videoItems.Add(new EventListItemViewModel
                {
                    Id = obj.Id ?? "?",
                    DisplayContent = EventNameResolver.GetDisplayName(obj),
                    DisplayTime = FormatTime(GetStartTime(obj)),
                    Tag = obj,
                    SortTime = GetStartTime(obj)
                });
            }
            videoItems = videoItems.OrderBy(s => s.SortTime).ToList();
            VideoListBox.ItemsSource = videoItems;
            VideoListBox.Visibility = videoItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            var lineItems = new List<EventListItemViewModel>();
            if (root.lines?.Count > 0) foreach (var obj in root.lines)
            {
                if (!string.IsNullOrEmpty(obj.TargetId)) continue;
                lineItems.Add(new EventListItemViewModel
                {
                    Id = obj.Id ?? "?",
                    DisplayContent = EventNameResolver.GetDisplayName(obj),
                    DisplayTime = FormatTime(GetStartTime(obj)),
                    Tag = obj,
                    SortTime = GetStartTime(obj)
                });
            }
            lineItems = lineItems.OrderBy(s => s.SortTime).ToList();
            LinesListBox.ItemsSource = lineItems;
            LinesListBox.Visibility = lineItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            var sceneItems = new List<EventListItemViewModel>();
            if (root.controllers?.Count > 0) foreach (var obj in root.controllers)
            {
                if (!string.IsNullOrEmpty(obj.TargetId)) continue;
                sceneItems.Add(new EventListItemViewModel
                {
                    Id = obj.Id ?? "?",
                    DisplayContent = EventNameResolver.GetDisplayName(obj),
                    DisplayTime = FormatTime(GetStartTime(obj)),
                    Tag = obj,
                    SortTime = GetStartTime(obj)
                });
            }
            sceneItems = sceneItems.OrderBy(s => s.SortTime).ToList();
            SceneListBox.ItemsSource = sceneItems;
            SceneListBox.Visibility = sceneItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            var noteItems = new List<EventListItemViewModel>();
            if (root.note_controllers?.Count > 0) foreach (var obj in root.note_controllers)
            {
                if (!string.IsNullOrEmpty(obj.TargetId)) continue;
                noteItems.Add(new EventListItemViewModel
                {
                    Id = obj.Id ?? "?",
                    DisplayContent = EventNameResolver.GetDisplayName(obj),
                    DisplayTime = FormatTime(GetStartTime(obj)),
                    Tag = obj,
                    SortTime = GetStartTime(obj)
                });
            }
            noteItems = noteItems.OrderBy(s => s.SortTime).ToList();
            NoteCtrlListBox.ItemsSource = noteItems;
            NoteCtrlListBox.Visibility = noteItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            var templateItems = new List<EventListItemViewModel>();
            if (root.templates?.Count > 0) foreach (var kvp in root.templates)
                templateItems.Add(new EventListItemViewModel
                {
                    Id = string.IsNullOrEmpty(kvp.Key) ? "未命名模板" : kvp.Key,
                    DisplayContent = string.IsNullOrEmpty(kvp.Key) ? "未命名模板" : kvp.Key,
                    DisplayTime = "",
                    Tag = kvp.Value,
                    SortTime = 0
                });
            templateItems = templateItems.OrderBy(s => s.SortTime).ToList();
            EventTemplateListBox.ItemsSource = templateItems;
            EventTemplateListBox.Visibility = templateItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            UpdateEmptyHintVisibility();
        }

        public void UpdateEmptyHintVisibility()
        {
            if (EventTabControl == null || DynamicEmptyHint == null) return;
            var currentListBox = GetCurrentActiveListBox();
            if (currentListBox != null)
            {
                if (currentListBox.ItemsSource is IList sourceList && sourceList.Count > 0)
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
            SpriteListBox.ItemsSource = null;
            TextListBox.ItemsSource = null;
            VideoListBox.ItemsSource = null;
            LinesListBox.ItemsSource = null;
            SceneListBox.ItemsSource = null;
            NoteCtrlListBox.ItemsSource = null;
            EventTemplateListBox.ItemsSource = null;
        }

        private void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is EventListItemViewModel vm)
            {
                if (vm.Tag != null) OnEventNodeSelected?.Invoke(vm.Tag);
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
                if (EventTemplateListBox.SelectedItem is EventListItemViewModel templateVm && templateVm.Tag is C2Template template)
                {
                    string templateKey = templateVm.Id;
                    if (templateKey == "未命名模板") templateKey = "";

                    if (templateKey != null)
                    {
                        _messageBroker.Publish("RequestOpenPropertyEditor", (object)template);
                    }
                    e.Handled = true;
                    return;
                }
            }

            // ✨ 核心重写：双击全面拥抱万能接口 IStoryboardEntity！
            if (sender is ListBox listBox && listBox.SelectedItem is EventListItemViewModel vm && vm.Tag is IStoryboardEntity selectedObj)
            {
                _messageBroker.Publish("RequestOpenPropertyEditor", (object)selectedObj);
                    e.Handled = true;
            }
        }

        private void BtnDeleteEvent_Click(object sender, RoutedEventArgs e)
        {
            ExecuteDeleteSelected();
        }

        /// <summary>
        /// 公开的删除入口（供快捷键系统调用）。
        /// </summary>
        public void ExecuteDeleteSelected()
        {
            if (Context == null || !Context.HasStoryboard) return;

            ListBox activeList = GetCurrentActiveListBox();
            if (activeList == null || activeList.SelectedItems.Count == 0)
            {
                _notificationService.ShowWarning("你还没有在列表中选择要删除的事件哦！");
                return;
            }

            var result = _dialogService.ShowYesNo($"确认要将这 {activeList.SelectedItems.Count} 个事件从故事板中彻底抹除吗？\n此操作不可撤销哦！", "危险警告");
            if (!result) return;

            var root = Context.Storyboard;
            bool hasDeleted = false;
            var selectedItems = activeList.SelectedItems.Cast<EventListItemViewModel>().ToList();

            foreach (var vm in selectedItems)
            {
                var tag = vm.Tag;

                // ✨ 核心重写：删除算法全线拥抱 C2 新军团与仓储接口
                if (tag is IStoryboardEntity objToDelete)
                {
                    _storyboardRepository.Remove(root, objToDelete);
                    hasDeleted = true;
                }
                else if (tag is C2Template)
                {
                    string templateKey = vm.Id;
                    if (templateKey == "未命名模板") templateKey = "";

                    if (templateKey != null && _storyboardRepository.ContainsTemplate(root, templateKey))
                    {
                        _storyboardRepository.RemoveTemplate(root, templateKey);
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



        // ==========================================
        // 🛡️ 谱面状态锁：向外部暴漏的结界开关
        // ==========================================
        public void UpdateChartLockState(bool hasChart)
        {
            if (ChartMissingOverlay != null)
            {
                // 有谱面就隐藏结界，没谱面就显示结界！
                ChartMissingOverlay.Visibility = hasChart ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        // 🔘 结界上的"导入谱面"按钮被点击时，跨频道呼叫主窗口的大魔法！
        private void BtnOverlayImportChart_Click(object sender, RoutedEventArgs e)
        {
            // 📢 对着大喇叭喊话：有人按下了导入谱面按钮！主窗口你听到了吗，快去干活！
            _messageBroker.Publish("RequestImportChart");
        }

        // ==========================================
        // 🔍 搜索过滤
        // ==========================================
        private void TxtSearchFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            var filter = (sender as TextBox)?.Text?.Trim()?.ToLower() ?? "";
            var listBox = GetCurrentActiveListBox();
            if (listBox == null) return;

            if (listBox.ItemsSource is IList sourceList)
            {
                var view = CollectionViewSource.GetDefaultView(sourceList);
                if (view != null)
                {
                    view.Filter = item =>
                    {
                        if (string.IsNullOrEmpty(filter)) return true;
                        if (item is EventListItemViewModel vm)
                            return (vm.Id?.ToLower().Contains(filter) == true) ||
                                   (vm.DisplayContent?.ToLower().Contains(filter) == true) ||
                                   (vm.DisplayTime?.ToLower().Contains(filter) == true);
                        return true;
                    };
                }
            }
        }

        // ==========================================
        // ⏱️ 时间格式化工具
        // ==========================================
        private static string FormatTime(double seconds)
        {
            if (seconds >= 60)
                return $"{(int)(seconds / 60)}:{(seconds % 60):00.0}";
            return $"{seconds:F1}s";
        }

        private static double GetStartTime(IStoryboardEntity entity)
        {
            var baseState = entity.GetBaseState();
            if (baseState != null)
            {
                var timeProp = baseState.GetType().GetProperty("Time");
                if (timeProp != null)
                {
                    var timeVal = timeProp.GetValue(baseState);
                    if (timeVal != null)
                    {
                        if (timeVal is double d) return d;
                        if (timeVal is float f) return f;
                        if (timeVal is long l) return l;
                        if (timeVal is int i) return i;
                        if (double.TryParse(timeVal.ToString(), out double result)) return result;
                    }
                }
            }
            return 0;
        }

        // ==========================================
        // 📋 复制粘贴功能
        // ==========================================
        /// <summary>
        /// 公开的复制入口（供快捷键系统调用）。
        /// </summary>
        public void ExecuteCopySelected()
        {
            var listBox = GetCurrentActiveListBox();
            if (listBox?.SelectedItem is EventListItemViewModel vm && vm.Tag is IStoryboardEntity entity)
            {
                _clipboardEntity = entity;
                _notificationService?.Show($"已复制事件: {vm.Id}", NotificationType.Info);
            }
        }

        /// <summary>
        /// 公开的粘贴入口（供快捷键系统调用）。
        /// </summary>
        public void ExecutePaste()
        {
            if (_clipboardEntity == null || Context?.Storyboard == null) return;

            // 深拷贝并生成新ID
            var cloned = CloneEntity(_clipboardEntity);
            if (cloned == null) return;

            _storyboardRepository.Add(Context.Storyboard, cloned);
            LoadStoryboardUI();
            _notificationService?.Show($"已粘贴事件: {cloned.Id}", NotificationType.Info);
        }

        private IStoryboardEntity? CloneEntity(IStoryboardEntity source)
        {
            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                Formatting = Formatting.Indented
            };
            var json = JsonConvert.SerializeObject(source, settings);
            var cloned = JsonConvert.DeserializeObject(json, source.GetType(), settings) as IStoryboardEntity;
            if (cloned != null)
            {
                var idProp = cloned.GetType().GetProperty("Id");
                if (idProp != null)
                {
                    var oldId = idProp.GetValue(cloned)?.ToString() ?? "";
                    var newId = oldId + "_copy_" + DateTime.Now.Ticks;
                    idProp.SetValue(cloned, newId);
                }
            }
            return cloned;
        }

        // ==========================================
        // ✏️ 重命名功能
        // ==========================================
        /// <summary>
        /// 公开的重命名入口（供快捷键系统调用）。
        /// </summary>
        public void ExecuteRenameSelected()
        {
            var listBox = GetCurrentActiveListBox();
            if (listBox?.SelectedItem is EventListItemViewModel vm && vm.Tag is IStoryboardEntity entity)
            {
                var result = _dialogService?.ShowInput("请输入新的事件 ID:", "重命名事件", vm.Id ?? "");
                if (!string.IsNullOrEmpty(result) && result != vm.Id)
                {
                    var idProp = entity.GetType().GetProperty("Id");
                    idProp?.SetValue(entity, result);
                    LoadStoryboardUI();
                    Context?.MarkAsModified();
                }
            }
        }


    }

    /// <summary>
    /// 事件列表行视图模型，用于三列显示绑定。
    /// </summary>
    public class EventListItemViewModel
    {
        public string Id { get; set; }
        public string DisplayContent { get; set; }
        public string DisplayTime { get; set; }
        public object Tag { get; set; }
        public double SortTime { get; set; }
        public Brush WarningBrush =>
            Tag is IStoryboardEntity entity && entity.AllDiagnostics().Count > 0
                ? new SolidColorBrush(Color.FromArgb(72, 255, 69, 58))
                : Brushes.Transparent;
        public string WarningToolTip =>
            Tag is IStoryboardEntity entity
                ? string.Join(Environment.NewLine, entity.AllDiagnostics().Select(item => $"{item.Path}: {item.Message}"))
                : "";
    }
}
