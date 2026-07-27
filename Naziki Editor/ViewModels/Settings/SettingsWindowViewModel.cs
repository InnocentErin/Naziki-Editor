using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.ErrorHandling;
using Naziki_Editor.Core.Settings;
using Naziki_Editor.Core.Shortcuts;

namespace Naziki_Editor.ViewModels.Settings
{
    /// <summary>
    /// 设置窗口的主 ViewModel，管理左侧分类导航和右侧设置内容。
    /// 遵循 MVVM 模式，通过数据绑定驱动 UI 更新。
    /// </summary>
    public class SettingsWindowViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly ISettingsStore _settingsStore;
        private readonly IErrorHandler _errorHandler;
        private readonly INotificationService _notificationService;

        private SettingsCategoryViewModel? _selectedCategory;
        private ObservableCollection<SettingItem>? _currentItems;
        private string _searchText = string.Empty;
        private bool _isDirty;
        private bool _isDisposed;
        private Dictionary<string, object?> _openingSnapshot = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>所有设置分类的列表（左侧导航）</summary>
        public ObservableCollection<SettingsCategoryViewModel> Categories { get; } = new();

        /// <summary>当前选中分类的设置项列表（右侧内容）</summary>
        public ObservableCollection<SettingItem>? CurrentItems
        {
            get => _currentItems;
            set { _currentItems = value; OnPropertyChanged(); }
        }

        /// <summary>当前选中的分类</summary>
        public SettingsCategoryViewModel? SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (_selectedCategory != value)
                {
                    // 取消旧分类的选中状态
                    if (_selectedCategory != null)
                        _selectedCategory.IsSelected = false;

                    _selectedCategory = value;

                    // 设置新分类的选中状态
                    if (_selectedCategory != null)
                    {
                        _selectedCategory.IsSelected = true;
                        LoadCategoryItems(_selectedCategory.Key);
                    }
                    else
                    {
                        CurrentItems = null;
                    }

                    OnPropertyChanged();
                }
            }
        }

        /// <summary>搜索文本（用于过滤设置项）</summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText != value)
                {
                    _searchText = value;
                    OnPropertyChanged();
                    ApplySearchFilter();
                }
            }
        }

        /// <summary>是否有未保存的更改</summary>
        public bool IsDirty
        {
            get => _isDirty;
            set { _isDirty = value; OnPropertyChanged(); }
        }

        /// <summary>窗口标题</summary>
        public string WindowTitle => "Naziki Editor - 设置";

        // ==========================================
        // 🎮 命令绑定
        // ==========================================

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand ResetCategoryCommand { get; }
        public ICommand ResetAllCommand { get; }

        public SettingsWindowViewModel(
            ISettingsStore settingsStore,
            IErrorHandler errorHandler,
            INotificationService notificationService)
        {
            _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
            _errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));

            // 初始化命令
            SaveCommand = new RelayCommand(ExecuteSave, () => IsDirty);
            CancelCommand = new RelayCommand(ExecuteCancel);
            ResetCategoryCommand = new RelayCommand(ExecuteResetCategory, () => SelectedCategory != null);
            ResetAllCommand = new RelayCommand(ExecuteResetAll);

            // 订阅设置变更
            _settingsStore.SettingChanged += OnSettingChanged;

            // 加载分类
            LoadCategories();
            CaptureSnapshot();
        }

        // ==========================================
        // 📥 数据加载
        // ==========================================

        /// <summary>
        /// 从 ISettingsStore 加载所有分类到 Categories 集合。
        /// </summary>
        private void LoadCategories()
        {
            _errorHandler.TryExecute(() =>
            {
                var categories = _settingsStore.GetCategories();
                Categories.Clear();

                foreach (var cat in categories)
                {
                    Categories.Add(new SettingsCategoryViewModel(cat));
                }

                // 默认选中第一个分类
                if (Categories.Count > 0)
                    SelectedCategory = Categories[0];
            }, "SettingsUI", "SettingsWindowViewModel.LoadCategories");
        }

        /// <summary>
        /// 根据分类键名加载对应的设置项。
        /// </summary>
        private void LoadCategoryItems(string categoryKey)
        {
            _errorHandler.TryExecute(() =>
            {
                var items = _settingsStore.GetCategoryItems(categoryKey);
                CurrentItems = new ObservableCollection<SettingItem>(items);
                ApplySearchFilter();
            }, "SettingsUI", "SettingsWindowViewModel.LoadCategoryItems", $"CategoryKey: {categoryKey}");
        }

        /// <summary>
        /// 根据搜索文本过滤当前显示的设置项。
        /// </summary>
        private void ApplySearchFilter()
        {
            if (CurrentItems == null) return;

            var allItems = _settingsStore.GetCategoryItems(SelectedCategory?.Key ?? string.Empty);

            foreach (var item in allItems)
            {
                if (string.IsNullOrWhiteSpace(_searchText))
                {
                    item.IsVisible = true;
                }
                else
                {
                    item.IsVisible =
                        item.DisplayName.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
                        item.Description.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
                        item.Key.Contains(_searchText, StringComparison.OrdinalIgnoreCase);
                }
            }

            // 刷新集合
            CurrentItems = new ObservableCollection<SettingItem>(allItems.Where(i => i.IsVisible));
        }

        // ==========================================
        // 💾 命令执行
        // ==========================================

        private void ExecuteSave()
        {
            _errorHandler.TryExecute(() =>
            {
                _settingsStore.Save();
                CaptureSnapshot();
                IsDirty = false;
                _notificationService.ShowSuccess("设置已保存");
            }, "SettingsUI", "SettingsWindowViewModel.ExecuteSave");
        }

        private void ExecuteCancel()
        {
            _errorHandler.TryExecute(() =>
            {
                RestoreSnapshot();
                LoadCategories();
                IsDirty = false;
                _notificationService.Show("已取消更改，设置已还原", NotificationType.Info);
            }, "SettingsUI", "SettingsWindowViewModel.ExecuteCancel");
        }

        private void ExecuteResetCategory()
        {
            if (SelectedCategory == null) return;

            _errorHandler.TryExecute(() =>
            {
                _settingsStore.ResetCategory(SelectedCategory.Key);
                LoadCategoryItems(SelectedCategory.Key);
                _notificationService.ShowSuccess($"已重置「{SelectedCategory.DisplayName}」分类的所有设置");
            }, "SettingsUI", "SettingsWindowViewModel.ExecuteResetCategory",
                $"CategoryKey: {SelectedCategory.Key}");
        }

        private void ExecuteResetAll()
        {
            _errorHandler.TryExecute(() =>
            {
                foreach (var cat in Categories)
                {
                    _settingsStore.ResetCategory(cat.Key);
                }
                // 重新加载当前选中分类
                if (SelectedCategory != null)
                    LoadCategoryItems(SelectedCategory.Key);
                _notificationService.ShowSuccess("所有设置已重置为默认值");
            }, "SettingsUI", "SettingsWindowViewModel.ExecuteResetAll");
        }

        // ==========================================
        // ⌨️ 快捷键冲突检测
        // ==========================================

        /// <summary>
        /// 检测快捷键冲突：遍历所有 KeyBinding 类型的设置项，查找与指定手势文本重复的项。
        /// </summary>
        /// <param name="currentKey">当前正在编辑的设置项键名（排除自身）</param>
        /// <param name="gestureText">待检测的手势文本（如 "Ctrl+S"）</param>
        /// <returns>冲突项的描述列表（显示名称 + 手势文本）</returns>
        public List<string> DetectKeyBindingConflicts(string currentKey, string gestureText)
        {
            var conflicts = new List<string>();

            if (string.IsNullOrEmpty(gestureText))
                return conflicts;

            _errorHandler.TryExecute(() =>
            {
                var bindingsById = DefaultShortcuts.GetAll()
                    .ToDictionary(b => b.Id, StringComparer.OrdinalIgnoreCase);
                var currentId = currentKey.StartsWith("Shortcuts.", StringComparison.OrdinalIgnoreCase)
                    ? currentKey["Shortcuts.".Length..]
                    : currentKey;
                bindingsById.TryGetValue(currentId, out var currentBinding);

                // 遍历所有分类中的所有设置项
                var categories = _settingsStore.GetCategories();
                foreach (var cat in categories)
                {
                    var items = _settingsStore.GetCategoryItems(cat.Key);
                    foreach (var item in items)
                    {
                        // 只检查 KeyBinding 类型，且排除自身
                        if (item.ValueType != SettingValueType.KeyBinding)
                            continue;
                        if (item.Key == currentKey)
                            continue;

                        // 比较手势文本（忽略大小写和空格）
                        var itemGesture = item.CurrentValue?.ToString() ?? string.Empty;
                        if (string.Equals(itemGesture.Trim(), gestureText.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            var otherId = item.Key.StartsWith("Shortcuts.", StringComparison.OrdinalIgnoreCase)
                                ? item.Key["Shortcuts.".Length..]
                                : item.Key;
                            bindingsById.TryGetValue(otherId, out var otherBinding);
                            if (currentBinding != null && otherBinding != null &&
                                currentBinding.Context != ShortcutContext.Global &&
                                otherBinding.Context != ShortcutContext.Global &&
                                (currentBinding.Context & otherBinding.Context) == 0)
                                continue;
                            conflicts.Add($"{item.DisplayName}（{itemGesture}）");
                        }
                    }
                }
            }, "SettingsUI", "SettingsWindowViewModel.DetectKeyBindingConflicts",
                $"CurrentKey: {currentKey}, GestureText: {gestureText}");

            return conflicts;
        }

        // ==========================================
        // 📡 事件响应
        // ==========================================

        private void OnSettingChanged(object? sender, SettingsChangedEventArgs e)
        {
            IsDirty = true;
        }

        private void CaptureSnapshot()
        {
            _openingSnapshot = _settingsStore.GetCategories()
                .SelectMany(c => _settingsStore.GetCategoryItems(c.Key))
                .ToDictionary(i => i.Key, i => i.CurrentValue, StringComparer.OrdinalIgnoreCase);
        }

        private void RestoreSnapshot()
        {
            foreach (var pair in _openingSnapshot)
                _settingsStore.Set(pair.Key, pair.Value);
        }

        // ==========================================
        // 🔔 INotifyPropertyChanged
        // ==========================================

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _settingsStore.SettingChanged -= OnSettingChanged;
        }
    }

    /// <summary>
    /// 简单的 RelayCommand 实现，用于 ViewModel 中的命令绑定。
    /// </summary>
    internal class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

        public void Execute(object? parameter) => _execute();

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}
