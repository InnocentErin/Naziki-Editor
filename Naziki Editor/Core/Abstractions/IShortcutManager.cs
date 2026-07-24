using Naziki_Editor.Core.Shortcuts;
using System.Windows.Input;

namespace Naziki_Editor.Core.Abstractions
{
    /// <summary>
    /// 统一快捷键管理器接口。
    /// 负责快捷键的注册、注销、路由分发和冲突检测。
    /// 所有快捷键逻辑集中在此接口管理，不依赖任何 UI 控件。
    /// </summary>
    public interface IShortcutManager
    {
        /// <summary>
        /// 注册一个快捷键绑定。
        /// </summary>
        /// <param name="binding">快捷键绑定定义。</param>
        /// <returns>绑定 ID。如果存在冲突，返回 null。</returns>
        /// <exception cref="ArgumentNullException">binding 为 null。</exception>
        /// <exception cref="ArgumentException">binding.Id 为空或已存在。</exception>
        string Register(ShortcutBinding binding);

        /// <summary>
        /// 批量注册多个快捷键绑定。
        /// 遇到冲突的绑定会被跳过，其余正常注册。
        /// </summary>
        /// <returns>成功注册的绑定数量。</returns>
        int RegisterBatch(IEnumerable<ShortcutBinding> bindings);

        /// <summary>
        /// 注销指定 ID 的快捷键绑定。
        /// </summary>
        /// <param name="bindingId">要注销的绑定 ID。</param>
        /// <returns>true 表示成功注销，false 表示该 ID 不存在。</returns>
        bool Unregister(string bindingId);

        /// <summary>
        /// 处理按键事件，查找匹配的快捷键绑定并路由到对应命令。
        /// 查找顺序：先在当前激活上下文中按优先级查找，再查找全局绑定。
        /// </summary>
        /// <param name="key">按下的键。</param>
        /// <param name="modifiers">当前修饰键状态。</param>
        /// <param name="activeContext">当前激活的上下文。</param>
        /// <returns>true 表示快捷键已被处理并执行了命令。</returns>
        bool HandleKeyDown(Key key, ModifierKeys modifiers, ShortcutContext activeContext);

        /// <summary>
        /// 检测指定上下文中是否存在冲突的快捷键绑定。
        /// 冲突定义：同一按键组合在相同或重叠的上下文中被多个绑定使用。
        /// </summary>
        /// <param name="context">要检测的上下文。</param>
        /// <returns>存在冲突的绑定列表。</returns>
        IReadOnlyList<ShortcutBinding> DetectConflicts(ShortcutContext context);

        /// <summary>
        /// 获取所有已注册的绑定。
        /// </summary>
        IReadOnlyList<ShortcutBinding> GetAllBindings();

        /// <summary>
        /// 获取指定上下文中所有匹配的绑定。
        /// </summary>
        IReadOnlyList<ShortcutBinding> GetBindings(ShortcutContext context);

        /// <summary>
        /// 根据 ID 查找绑定。
        /// </summary>
        /// <param name="bindingId">绑定 ID。</param>
        /// <returns>找到的绑定，如果不存在则返回 null。</returns>
        ShortcutBinding? FindBinding(string bindingId);

        /// <summary>
        /// 启用或禁用指定快捷键。
        /// </summary>
        /// <param name="bindingId">绑定 ID。</param>
        /// <param name="enabled">是否启用。</param>
        /// <returns>true 表示操作成功。</returns>
        bool SetBindingEnabled(string bindingId, bool enabled);

        /// <summary>
        /// 已注册的快捷键绑定总数。
        /// </summary>
        int BindingCount { get; }

        /// <summary>
        /// 清除所有已注册的快捷键绑定。
        /// 用于快捷键重载（如用户自定义快捷键后重新加载）。
        /// </summary>
        void Clear();
    }
}