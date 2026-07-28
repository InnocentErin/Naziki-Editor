using Naziki_Editor.Models;
using Naziki_Editor.State;

namespace Naziki_Editor.Core.Abstractions
{
    /// <summary>
    /// 编辑器协调器抽象，负责在属性编辑器提交后统一处理数据落盘与状态标记。
    /// 不包含任何 UI 依赖。
    /// </summary>
    public interface IEditorCoordinator
    {
        /// <summary>
        /// 提交实体编辑结果。若 originalObj 为 null，则视为新建并添加到仓储；否则替换原实体。
        /// </summary>
        void CommitEntityEdit(IStoryboardEntity? originalObj, IStoryboardEntity modifiedObj, ProjectDataContext context, IStoryboardRepository repository);

        /// <summary>
        /// 提交模板编辑结果。
        /// </summary>
        void CommitTemplateEdit(ProjectDataContext context);
    }
}
