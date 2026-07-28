using System;

namespace Naziki_Editor.Core.Abstractions
{
    /// <summary>
    /// 工作区冲突解决策略。
    /// </summary>
    public enum ConflictResolution
    {
        /// <summary>保留源代码版本。</summary>
        ApplySource,
        /// <summary>保留属性视图版本并刷新源代码视图。</summary>
        RefreshView,
        /// <summary>取消当前操作。</summary>
        Cancel
    }

    /// <summary>
    /// 工作区服务抽象，负责判断并解决属性面板与源代码编辑器之间的数据冲突。
    /// 不包含任何 UI 依赖。
    /// </summary>
    public interface IWorkspaceService
    {
        /// <summary>
        /// 判断当前是否存在需要解决的数据冲突。
        /// </summary>
        /// <param name="hasUnappliedSourceChanges">源代码编辑器是否存在未应用的修改。</param>
        /// <param name="isVisualDirty">属性面板是否导致视觉画面变脏。</param>
        bool HasConflict(bool hasUnappliedSourceChanges, bool isVisualDirty);

        /// <summary>
        /// 根据用户选择的策略执行冲突解决。
        /// </summary>
        /// <param name="resolution">用户选择的解决策略。</param>
        /// <param name="applySource">应用源代码版本的操作，返回是否成功。</param>
        /// <param name="refreshView">刷新源代码视图以匹配属性版本的操作。</param>
        /// <returns>冲突是否已成功解决（Cancel 返回 false）。</returns>
        bool ResolveConflict(ConflictResolution resolution, Func<bool> applySource, Action refreshView);
    }
}
