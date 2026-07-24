using Naziki_Editor.Core.Abstractions;
using System.Windows.Input;

namespace Naziki_Editor.Core.Shortcuts
{
    /// <summary>
    /// 统一快捷键管理器实现。
    /// 使用字典索引实现 O(1) 按键查找，支持上下文感知路由和优先级排序。
    /// 
    /// 内部使用两套索引：
    /// - _bindings: ID → ShortcutBinding，用于按 ID 查找和管理
    /// - _keyLookup: (Key, Modifiers) → List&lt;ShortcutBinding&gt;，用于按键快速匹配
    /// </summary>
    public class ShortcutManager : IShortcutManager
    {
        private readonly ICommandDispatcher _commandDispatcher;
        private readonly Dictionary<string, ShortcutBinding> _bindings = new();
        private readonly Dictionary<(Key Key, ModifierKeys Modifiers), List<ShortcutBinding>> _keyLookup = new();
        private readonly object _lock = new();

        /// <summary>
        /// 创建 ShortcutManager 实例。
        /// </summary>
        /// <param name="commandDispatcher">命令调度器，用于执行快捷键对应的命令。</param>
        public ShortcutManager(ICommandDispatcher commandDispatcher)
        {
            _commandDispatcher = commandDispatcher ?? throw new ArgumentNullException(nameof(commandDispatcher));
        }

        /// <inheritdoc />
        public int BindingCount
        {
            get { lock (_lock) { return _bindings.Count; } }
        }

        /// <inheritdoc />
        public string Register(ShortcutBinding binding)
        {
            if (binding == null)
                throw new ArgumentNullException(nameof(binding));
            if (string.IsNullOrEmpty(binding.Id))
                throw new ArgumentException("快捷键绑定 ID 不能为空。", nameof(binding));

            lock (_lock)
            {
                if (_bindings.ContainsKey(binding.Id))
                    throw new ArgumentException($"快捷键绑定 ID '{binding.Id}' 已存在。", nameof(binding));

                // 冲突检测：检查同一按键组合 + 重叠上下文
                var lookupKey = binding.ToLookupKey();
                if (_keyLookup.TryGetValue(lookupKey, out var existingBindings))
                {
                    foreach (var existing in existingBindings)
                    {
                        if (binding.ConflictsWith(existing))
                        {
                            throw new InvalidOperationException(
                                $"快捷键冲突：'{binding.Id}' ({binding.ToGestureText()}) " +
                                $"与 '{existing.Id}' ({existing.ToGestureText()}) 在上下文中存在冲突。");
                        }
                    }
                }

                // 添加到 ID 索引
                _bindings[binding.Id] = binding;

                // 添加到按键索引
                if (!_keyLookup.ContainsKey(lookupKey))
                    _keyLookup[lookupKey] = new List<ShortcutBinding>();
                _keyLookup[lookupKey].Add(binding);
            }

            return binding.Id;
        }

        /// <inheritdoc />
        public int RegisterBatch(IEnumerable<ShortcutBinding> bindings)
        {
            if (bindings == null)
                throw new ArgumentNullException(nameof(bindings));

            int successCount = 0;
            foreach (var binding in bindings)
            {
                try
                {
                    Register(binding);
                    successCount++;
                }
                catch (Exception)
                {
                    // 冲突的绑定跳过，继续注册其余绑定
                }
            }
            return successCount;
        }

        /// <inheritdoc />
        public bool Unregister(string bindingId)
        {
            if (string.IsNullOrEmpty(bindingId))
                return false;

            lock (_lock)
            {
                if (!_bindings.TryGetValue(bindingId, out var binding))
                    return false;

                // 从按键索引中移除
                var lookupKey = binding.ToLookupKey();
                if (_keyLookup.TryGetValue(lookupKey, out var list))
                {
                    list.Remove(binding);
                    if (list.Count == 0)
                        _keyLookup.Remove(lookupKey);
                }

                // 从 ID 索引中移除
                _bindings.Remove(bindingId);
                return true;
            }
        }

        /// <inheritdoc />
        public bool HandleKeyDown(Key key, ModifierKeys modifiers, ShortcutContext activeContext)
        {
            // 过滤掉纯修饰键（Ctrl、Shift、Alt、Win 单独按下时不触发）
            if (IsModifierKey(key))
                return false;

            // 规范化修饰键：去除 Windows 键的干扰（除非显式绑定）
            var normalizedModifiers = modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift);

            List<ShortcutBinding>? candidates;

            lock (_lock)
            {
                if (!_keyLookup.TryGetValue((key, normalizedModifiers), out candidates))
                    return false;

                // 复制列表以避免在枚举期间修改
                candidates = new List<ShortcutBinding>(candidates);
            }

            // 筛选：匹配上下文、已启用
            var matchingBindings = candidates
                .Where(b => b.IsEnabled && b.MatchesContext(activeContext))
                .OrderByDescending(b => b.Priority)
                .ToList();

            // 按优先级依次尝试执行
            foreach (var binding in matchingBindings)
            {
                try
                {
                    if (_commandDispatcher.CanExecute(binding.CommandName))
                    {
                        _commandDispatcher.Execute(binding.CommandName);
                        return true;
                    }
                }
                catch (Exception)
                {
                    // 命令执行失败，继续尝试下一个匹配的绑定
                }
            }

            return false;
        }

        /// <inheritdoc />
        public IReadOnlyList<ShortcutBinding> DetectConflicts(ShortcutContext context)
        {
            lock (_lock)
            {
                var conflicts = new List<ShortcutBinding>();

                foreach (var kvp in _keyLookup)
                {
                    var bindings = kvp.Value;
                    if (bindings.Count <= 1)
                        continue;

                    for (int i = 0; i < bindings.Count; i++)
                    {
                        for (int j = i + 1; j < bindings.Count; j++)
                        {
                            if (bindings[i].ConflictsWith(bindings[j]))
                            {
                                // 检查是否与指定上下文相关
                                bool relevant = bindings[i].MatchesContext(context) ||
                                                bindings[j].MatchesContext(context);

                                if (relevant)
                                {
                                    if (!conflicts.Contains(bindings[i]))
                                        conflicts.Add(bindings[i]);
                                    if (!conflicts.Contains(bindings[j]))
                                        conflicts.Add(bindings[j]);
                                }
                            }
                        }
                    }
                }

                return conflicts.AsReadOnly();
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<ShortcutBinding> GetAllBindings()
        {
            lock (_lock)
            {
                return _bindings.Values.ToList().AsReadOnly();
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<ShortcutBinding> GetBindings(ShortcutContext context)
        {
            lock (_lock)
            {
                return _bindings.Values
                    .Where(b => b.MatchesContext(context))
                    .ToList()
                    .AsReadOnly();
            }
        }

        /// <inheritdoc />
        public ShortcutBinding? FindBinding(string bindingId)
        {
            if (string.IsNullOrEmpty(bindingId))
                return null;

            lock (_lock)
            {
                _bindings.TryGetValue(bindingId, out var binding);
                return binding;
            }
        }

        /// <inheritdoc />
        public bool SetBindingEnabled(string bindingId, bool enabled)
        {
            lock (_lock)
            {
                if (!_bindings.TryGetValue(bindingId, out var binding))
                    return false;

                binding.IsEnabled = enabled;
                return true;
            }
        }

        /// <inheritdoc />
        public void Clear()
        {
            lock (_lock)
            {
                _bindings.Clear();
                _keyLookup.Clear();
            }
        }

        /// <summary>
        /// 判断给定的键是否为纯修饰键（Ctrl、Shift、Alt、Win）。
        /// 纯修饰键单独按下时不应触发快捷键。
        /// </summary>
        private static bool IsModifierKey(Key key)
        {
            return key == Key.LeftCtrl || key == Key.RightCtrl ||
                   key == Key.LeftAlt || key == Key.RightAlt ||
                   key == Key.LeftShift || key == Key.RightShift ||
                   key == Key.LWin || key == Key.RWin ||
                   key == Key.System; // Alt 键在某些情况下映射为 System
        }
    }
}