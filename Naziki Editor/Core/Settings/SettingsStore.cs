using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.ErrorHandling;
using Naziki_Editor.Core.Shortcuts;
using Newtonsoft.Json;

namespace Naziki_Editor.Core.Settings
{
    /// <summary>
    /// 设置存储的默认实现，使用 JSON 文件持久化到用户 AppData 目录。
    /// 启动时从文件加载，每次设置变更时自动保存。
    /// </summary>
    public class SettingsStore : ISettingsStore
    {
        private readonly IErrorHandler _errorHandler;
        private readonly IMessageBroker _messageBroker;
        private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SettingsCategory> _categories = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SettingItem> _allItems = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _filePath;
        private readonly object _lock = new();
        private bool _isLoaded;

        /// <summary>
        /// 默认设置文件路径：%AppData%/NazikiEditor/settings.json
        /// </summary>
        public static string DefaultFilePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NazikiEditor",
                "settings.json");

        public event EventHandler<SettingsChangedEventArgs>? SettingChanged;

        public SettingsStore(IErrorHandler errorHandler, IMessageBroker messageBroker)
            : this(errorHandler, messageBroker, DefaultFilePath)
        {
        }

        public SettingsStore(IErrorHandler errorHandler, IMessageBroker messageBroker, string filePath)
        {
            _errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
            _messageBroker = messageBroker ?? throw new ArgumentNullException(nameof(messageBroker));
            _filePath = filePath;
        }

        // ==========================================
        // 🔌 ISettingsStore 接口实现
        // ==========================================

        public T Get<T>(string key, T defaultValue = default)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("设置键名不能为空", nameof(key));

            lock (_lock)
            {
                if (_values.TryGetValue(key, out var val) && val is T typedVal)
                    return typedVal;

                return defaultValue;
            }
        }

        public void Set<T>(string key, T value)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("设置键名不能为空", nameof(key));

            object? oldValue;
            string categoryKey = string.Empty;

            lock (_lock)
            {
                _values.TryGetValue(key, out oldValue);

                // 若值未变化则跳过
                if (Equals(oldValue, value))
                    return;

                _values[key] = value;

                // 同步更新 SettingItem 的 CurrentValue
                if (_allItems.TryGetValue(key, out var item))
                {
                    item.CurrentValue = value;
                    categoryKey = item.CategoryKey;
                }
            }

            // 触发事件和消息广播
            var args = new SettingsChangedEventArgs(key, oldValue, value, categoryKey);
            SettingChanged?.Invoke(this, args);
            _messageBroker.Publish("Settings.Changed", args);
            _messageBroker.Publish($"Settings.Changed.{key}", args);

            // 自动持久化保存
            Save();
        }

        public bool ContainsKey(string key)
        {
            lock (_lock)
                return _values.ContainsKey(key);
        }

        public IReadOnlyList<SettingsCategory> GetCategories()
        {
            lock (_lock)
                return _categories.Values.OrderBy(c => c.Order).ToList().AsReadOnly();
        }

        public IReadOnlyList<SettingItem> GetCategoryItems(string categoryKey)
        {
            lock (_lock)
            {
                if (_categories.TryGetValue(categoryKey, out var cat))
                    return cat.Items.OrderBy(i => i.Order).ToList().AsReadOnly();
                return Array.Empty<SettingItem>();
            }
        }

        public void Load()
        {
            _errorHandler.TryExecute(() =>
            {
                lock (_lock)
                {
                    if (_isLoaded) return;

                    // 确保目录存在
                    var dir = Path.GetDirectoryName(_filePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    if (File.Exists(_filePath))
                    {
                        var json = File.ReadAllText(_filePath);
                        var loaded = JsonConvert.DeserializeObject<Dictionary<string, object?>>(json);
                        if (loaded != null)
                        {
                            foreach (var kvp in loaded)
                                _values[kvp.Key] = kvp.Value;
                        }
                    }

                    MigrateSetting("Editor.AutoExpandTimeline", "Timeline.AutoExpandTracks");
                    MigrateSetting("Appearance.TimelineColorMode", "Timeline.ColorMode");
                    MigrateSetting("Editor.DefaultEasing", "Timeline.DefaultEasing");

                    // 用已保存的值覆盖所有 SettingItem 的 CurrentValue
                    foreach (var item in _allItems.Values)
                    {
                        if (_values.TryGetValue(item.Key, out var savedVal))
                            item.CurrentValue = savedVal ?? item.DefaultValue;
                        else
                            item.CurrentValue = item.DefaultValue;
                    }

                    _isLoaded = true;
                }
            }, "SettingsIO", "SettingsStore.Load", $"FilePath: {_filePath}");
        }

        private void MigrateSetting(string legacyKey, string currentKey)
        {
            if (!_values.ContainsKey(currentKey) && _values.TryGetValue(legacyKey, out var value))
                _values[currentKey] = value;
        }

        public void Save()
        {
            _errorHandler.TryExecute(() =>
            {
                lock (_lock)
                {
                    var dir = Path.GetDirectoryName(_filePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    var json = JsonConvert.SerializeObject(_values, Formatting.Indented);
                    File.WriteAllText(_filePath, json);
                }
            }, "SettingsIO", "SettingsStore.Save", $"FilePath: {_filePath}");
        }

        public void Reset(string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("设置键名不能为空", nameof(key));

            lock (_lock)
            {
                if (_allItems.TryGetValue(key, out var item))
                {
                    Set(key, item.DefaultValue);
                }
            }
        }

        public void ResetCategory(string categoryKey)
        {
            if (string.IsNullOrEmpty(categoryKey))
                throw new ArgumentException("分类键名不能为空", nameof(categoryKey));

            List<SettingItem> items;
            lock (_lock)
            {
                if (!_categories.TryGetValue(categoryKey, out var cat))
                    return;
                items = cat.Items.ToList();
            }

            foreach (var item in items)
                Set(item.Key, item.DefaultValue);
        }

        public void RegisterCategory(SettingsCategory category)
        {
            if (category == null)
                throw new ArgumentNullException(nameof(category));
            if (string.IsNullOrEmpty(category.Key))
                throw new ArgumentException("分类键名不能为空", nameof(category));

            lock (_lock)
            {
                _categories[category.Key] = category;

                foreach (var item in category.Items)
                {
                    item.CategoryKey = category.Key;

                    // 订阅设置项的内部值变更，自动同步到存储
                    item.OnValueChanged += (changedItem) =>
                    {
                        if (changedItem.CurrentValue != null)
                            Set(changedItem.Key, changedItem.CurrentValue);
                    };

                    _allItems[item.Key] = item;
                }
            }
        }

        // ==========================================
        // 🏭 便捷工厂方法：创建预设分类
        // ==========================================

        /// <summary>
        /// 创建并注册默认的预设设置分类（通用、外观、编辑器、快捷键等）。
        /// 可在应用启动时调用以快速搭建设置框架。
        /// </summary>
        public void RegisterDefaultCategories()
        {
            RegisterCategory(CreateGeneralCategory());
            RegisterCategory(CreateAppearanceCategory());
            RegisterCategory(CreateEditorCategory());
            RegisterCategory(CreateTimelineCategory());
            RegisterCategory(CreatePerformanceCategory());
            RegisterCategory(CreateShortcutsCategory());
        }

        private static SettingsCategory CreateGeneralCategory()
        {
            return new SettingsCategory
            {
                Key = "General",
                DisplayName = "基本设置",
                Icon = "⚙️",
                Description = "语言、启动行为等基础设置",
                Order = 0,
                Items = new List<SettingItem>
                {
                    new()
                    {
                        Key = "General.Language",
                        DisplayName = "界面语言",
                        Description = "应用程序的显示语言（需重启生效）",
                        ValueType = SettingValueType.Combo,
                        DefaultValue = "zh-CN",
                        ComboOptions = new List<string> { "zh-CN", "en-US", "ja-JP", "ko-KR" },
                        Order = 0
                    },
                    new()
                    {
                        Key = "General.AutoSave",
                        DisplayName = "自动保存",
                        Description = "启用后每隔指定时间自动保存工程",
                        ValueType = SettingValueType.Bool,
                        DefaultValue = true,
                        Order = 1
                    },
                    new()
                    {
                        Key = "General.AutoSaveInterval",
                        DisplayName = "自动保存间隔（分钟）",
                        Description = "两次自动保存之间的时间间隔",
                        ValueType = SettingValueType.Integer,
                        DefaultValue = 5,
                        MinValue = 1,
                        MaxValue = 60,
                        Order = 2
                    },
                    new()
                    {
                        Key = "General.CheckUpdatesOnStartup",
                        DisplayName = "启动时检查更新",
                        Description = "应用启动时自动检查是否有新版本",
                        ValueType = SettingValueType.Bool,
                        DefaultValue = true,
                        Order = 3
                    },
                    new()
                    {
                        Key = "General.RecentFilesCount",
                        DisplayName = "最近文件数量",
                        Description = "在文件菜单中显示的最近打开文件数",
                        ValueType = SettingValueType.Integer,
                        DefaultValue = 10,
                        MinValue = 1,
                        MaxValue = 30,
                        Order = 4
                    }
                }
            };
        }

        private static SettingsCategory CreateAppearanceCategory()
        {
            return new SettingsCategory
            {
                Key = "Appearance",
                DisplayName = "外观设置",
                Icon = "🎨",
                Description = "主题、字体、颜色等外观相关设置",
                Order = 1,
                Items = new List<SettingItem>
                {
                    new()
                    {
                        Key = "Appearance.Theme",
                        DisplayName = "主题模式",
                        Description = "选择深色、浅色或跟随系统主题（实时生效）",
                        ValueType = SettingValueType.Combo,
                        DefaultValue = "跟随系统",
                        ComboOptions = new List<string> { "跟随系统", "深色", "浅色" },
                        Order = 0
                    },
                    new()
                    {
                        Key = "Appearance.FontSize",
                        DisplayName = "界面字体大小",
                        Description = "调整全局界面字体大小",
                        ValueType = SettingValueType.Integer,
                        DefaultValue = 12,
                        MinValue = 8,
                        MaxValue = 24,
                        Order = 1
                    },
                    new()
                    {
                        Key = "Appearance.AccentColor",
                        DisplayName = "强调色",
                        Description = "高亮、选中等强调元素的颜色",
                        ValueType = SettingValueType.Color,
                        DefaultValue = "#007ACC",
                        Order = 2
                    },
                    new()
                    {
                        Key = "Appearance.ShowStatusBar",
                        DisplayName = "显示状态栏",
                        Description = "在窗口底部显示状态信息栏",
                        ValueType = SettingValueType.Bool,
                        DefaultValue = true,
                        Order = 3
                    },
                }
            };
        }

        private static SettingsCategory CreateEditorCategory()
        {
            return new SettingsCategory
            {
                Key = "Editor",
                DisplayName = "编辑器设置",
                Icon = "✏️",
                Description = "画布、时间轴、属性面板等编辑器行为设置",
                Order = 2,
                Items = new List<SettingItem>
                {
                    new()
                    {
                        Key = "Editor.GridSize",
                        DisplayName = "网格大小",
                        Description = "画布上网格的间距（像素）",
                        ValueType = SettingValueType.Integer,
                        DefaultValue = 20,
                        MinValue = 5,
                        MaxValue = 100,
                        Order = 0
                    },
                    new()
                    {
                        Key = "Editor.SnapToGrid",
                        DisplayName = "对齐到网格",
                        Description = "拖拽对象时自动吸附到最近的网格线",
                        ValueType = SettingValueType.Bool,
                        DefaultValue = true,
                        Order = 1
                    },
                    new()
                    {
                        Key = "Editor.UndoDepth",
                        DisplayName = "撤销深度",
                        Description = "最多可撤销的操作步数",
                        ValueType = SettingValueType.Integer,
                        DefaultValue = 50,
                        MinValue = 10,
                        MaxValue = 500,
                        Order = 2
                    },
                    new()
                    {
                        Key = "Editor.PreviewQuality",
                        DisplayName = "预览质量",
                        Description = "故事板预览的渲染质量（低质量更流畅）",
                        ValueType = SettingValueType.Combo,
                        DefaultValue = "Medium",
                        ComboOptions = new List<string> { "Low", "Medium", "High" },
                        Order = 4
                    }
                }
            };
        }

        private static SettingsCategory CreateTimelineCategory()
        {
            var items = new List<SettingItem>();
            void Add(string key, string name, string description, SettingValueType type, object value,
                double? min = null, double? max = null, IEnumerable<string>? options = null)
            {
                items.Add(new SettingItem
                {
                    Key = key, DisplayName = name, Description = description, ValueType = type,
                    DefaultValue = value, MinValue = min, MaxValue = max,
                    ComboOptions = options?.ToList() ?? new List<string>(), Order = items.Count
                });
            }

            Add("Timeline.AutoExpandTracks", "自动展开轨道", "自动展开包含事件的时间轴轨道", SettingValueType.Bool, true);
            Add("Timeline.AutoScrollDuringPlayback", "播放时自动跟随", "播放时自动滚动时间轴视口", SettingValueType.Bool, true);
            Add("Timeline.PlayheadFollowMode", "播放头跟随模式", "关闭、分页跟随或居中跟随", SettingValueType.Combo, "Page", options: ["Off", "Page", "Centered"]);
            Add("Timeline.InitialPixelsPerSecond", "初始缩放（像素/秒）", "打开时间轴时每秒对应的像素数", SettingValueType.Float, 100d, 20, 500);
            Add("Timeline.MinimumPixelsPerSecond", "最小缩放", "时间轴允许的最小像素/秒", SettingValueType.Float, 10d, 1, 100);
            Add("Timeline.MaximumPixelsPerSecond", "最大缩放", "时间轴允许的最大像素/秒", SettingValueType.Float, 1000d, 200, 5000);
            Add("Timeline.ZoomStepPercent", "缩放步长（%）", "每次放大或缩小的百分比", SettingValueType.Float, 20d, 5, 100);
            Add("Timeline.MouseWheelZoomModifier", "滚轮缩放修饰键", "按住指定修饰键时滚轮缩放时间轴", SettingValueType.Combo, "Ctrl", options: ["Ctrl", "Alt", "None", "Disabled"]);
            Add("Timeline.TrackHeight", "主轨道高度", "主时间轴轨道高度（像素）", SettingValueType.Float, 40d, 24, 96);
            Add("Timeline.MicroTrackHeight", "属性轨道高度", "微观时间轴属性轨道高度（像素）", SettingValueType.Float, 40d, 28, 120);
            Add("Timeline.ZeroDurationMarkerWidth", "零时长标记宽度", "零时长事件的最小可点击宽度", SettingValueType.Float, 8d, 3, 24);
            Add("Timeline.TimeDisplayFormat", "时间显示格式", "时间刻度和播放头的显示格式", SettingValueType.Combo, "Seconds", options: ["Seconds", "MinutesSeconds"]);
            Add("Timeline.ColorMode", "颜色模式", "事件方块的着色方式", SettingValueType.Combo, "Category", options: ["Category", "Monochrome", "HighContrast"]);
            Add("Timeline.SnapEnabled", "启用吸附", "时间轴编辑的总吸附开关", SettingValueType.Bool, true);
            Add("Timeline.SnapIntervalSeconds", "网格间隔（秒）", "时间网格和方向移动的基础间隔", SettingValueType.Float, .1d, .001, 10);
            Add("Timeline.SnapToPlayhead", "吸附播放头", "拖动时吸附到播放头", SettingValueType.Bool, true);
            Add("Timeline.SnapToEventEdges", "吸附事件边界", "拖动时吸附到其他事件的边界", SettingValueType.Bool, true);
            Add("Timeline.SnapToKeyframes", "吸附关键帧", "拖动时吸附到关键帧", SettingValueType.Bool, true);
            Add("Timeline.SnapToNotes", "吸附音符", "拖动时吸附到音符时间", SettingValueType.Bool, true);
            Add("Timeline.SnapTolerancePixels", "吸附容差（像素）", "距离目标多少像素时触发吸附", SettingValueType.Float, 8d, 2, 30);
            Add("Timeline.NudgeStepSeconds", "微移步长（秒）", "方向键移动的时间步长", SettingValueType.Float, .01d, .001, 10);
            Add("Timeline.LargeNudgeStepSeconds", "大步移动（秒）", "Shift+方向键移动的时间步长", SettingValueType.Float, .1d, .001, 30);
            Add("Timeline.DefaultEasing", "默认缓动函数", "新增关键帧时采用的缓动", SettingValueType.Combo, "EaseInOutQuad",
                options: ["Linear", "EaseInQuad", "EaseOutQuad", "EaseInOutQuad", "EaseInCubic", "EaseOutCubic", "EaseInOutCubic"]);
            Add("Timeline.TemplateResizePolicy", "模板缩放策略", "事件缩放涉及共享模板时的处理方式", SettingValueType.Combo, "AskThenDetach",
                options: ["AskThenDetach", "Block", "EditSharedTemplate"]);
            Add("Timeline.ShowTemplateExpandedFrames", "显示模板展开帧", "在微观时间轴显示模板产生的关键帧", SettingValueType.Bool, true);
            Add("Timeline.ShowTemplateSourceLabels", "显示模板来源", "显示展开帧的模板来源标签", SettingValueType.Bool, true);
            Add("Timeline.ConfirmTemplateDetach", "解绑模板前确认", "将模板实例转换为独立关键帧前显示确认", SettingValueType.Bool, true);
            Add("Timeline.CurveDisplayMode", "曲线显示模式", "数值轨道的曲线绘制方式", SettingValueType.Combo, "Auto", options: ["Auto", "LinearSegments", "EasingCurves"]);
            Add("Timeline.ShowInvalidTimeLane", "显示时间异常区域", "集中显示无法解析时间的事件", SettingValueType.Bool, true);

            return new SettingsCategory
            {
                Key = "Timeline", DisplayName = "时间轴设置", Icon = "🎞️",
                Description = "时间轴视图、吸附、关键帧与模板行为", Order = 3, Items = items
            };
        }

        private static SettingsCategory CreatePerformanceCategory()
        {
            return new SettingsCategory
            {
                Key = "Performance",
                DisplayName = "性能设置",
                Icon = "🚀",
                Description = "渲染、缓存、硬件加速等性能相关设置",
                Order = 4,
                Items = new List<SettingItem>
                {
                    new()
                    {
                        Key = "Performance.HardwareAcceleration",
                        DisplayName = "硬件加速",
                        Description = "启用 GPU 硬件加速渲染（需重启生效）",
                        ValueType = SettingValueType.Bool,
                        DefaultValue = true,
                        Order = 0
                    },
                    new()
                    {
                        Key = "Performance.MaxCacheSize",
                        DisplayName = "最大缓存大小（MB）",
                        Description = "素材缓存的最大内存占用",
                        ValueType = SettingValueType.Integer,
                        DefaultValue = 512,
                        MinValue = 64,
                        MaxValue = 4096,
                        Order = 1
                    },
                    new()
                    {
                        Key = "Performance.RenderThreads",
                        DisplayName = "渲染线程数",
                        Description = "用于故事板渲染的并行线程数",
                        ValueType = SettingValueType.Integer,
                        DefaultValue = 4,
                        MinValue = 1,
                        MaxValue = 16,
                        Order = 2
                    },
                    new()
                    {
                        Key = "Performance.FrameSkipThreshold",
                        DisplayName = "跳帧阈值（ms）",
                        Description = "当帧渲染时间超过此阈值时自动跳帧",
                        ValueType = SettingValueType.Float,
                        DefaultValue = 16.67,
                        MinValue = 8.0,
                        MaxValue = 50.0,
                        Order = 3
                    }
                }
            };
        }

        /// <summary>
        /// 创建快捷键设置分类，从 DefaultShortcuts.GetAll() 自动生成所有快捷键设置项。
        /// 每个快捷键绑定映射为一个 SettingItem，ValueType = KeyBinding。
        /// </summary>
        private static SettingsCategory CreateShortcutsCategory()
        {
            var defaultBindings = DefaultShortcuts.GetAll().ToList();
            var items = new List<SettingItem>();

            for (int i = 0; i < defaultBindings.Count; i++)
            {
                var binding = defaultBindings[i];
                var gestureText = binding.ToGestureText();

                // 构建上下文描述
                var contextDesc = binding.Context == ShortcutContext.Global
                    ? "全局"
                    : binding.Context.ToString();

                items.Add(new SettingItem
                {
                    Key = $"Shortcuts.{binding.Id}",
                    DisplayName = binding.Description,
                    Description = $"命令：{binding.CommandName} | 上下文：{contextDesc}",
                    ValueType = SettingValueType.KeyBinding,
                    DefaultValue = gestureText,
                    DefaultKeyGesture = gestureText,
                    Order = i
                });
            }

            return new SettingsCategory
            {
                Key = "Shortcuts",
                DisplayName = "快捷键设置",
                Icon = "⌨️",
                Description = "自定义所有快捷键绑定，支持录制、冲突检测和重置",
                Order = 5,
                Items = items
            };
        }
    }
}
