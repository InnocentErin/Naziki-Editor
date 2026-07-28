namespace Naziki_Editor.Core.Abstractions
{
    /// <summary>
    /// 历史记录服务抽象，提供撤销/重做快照管理。
    /// </summary>
    public interface IHistoryService
    {
        /// <summary>
        /// 最大快照容量。
        /// </summary>
        int MaxCapacity { get; set; }

        /// <summary>
        /// 是否可以撤销。
        /// </summary>
        bool CanUndo { get; }

        /// <summary>
        /// 是否可以重做。
        /// </summary>
        bool CanRedo { get; }

        /// <summary>
        /// 记录当前状态快照。
        /// </summary>
        void RecordSnapshot(object currentState);

        /// <summary>
        /// 撤销一步并返回上一个状态。
        /// </summary>
        T Undo<T>(T currentState, out bool success) where T : class;

        /// <summary>
        /// 重做一个并返回下一个状态。
        /// </summary>
        T Redo<T>(T currentState, out bool success) where T : class;

        /// <summary>
        /// 清空所有历史记录。
        /// </summary>
        void Reset();
    }
}
