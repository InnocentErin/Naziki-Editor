using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Models;
using Naziki_Editor.State;

namespace Naziki_Editor.Core.Editor
{
    /// <summary>
    /// 编辑器协调器实现，统一处理属性编辑器提交后的数据变更与状态同步。
    /// </summary>
    public class EditorCoordinator : IEditorCoordinator
    {
        public void CommitEntityEdit(IStoryboardEntity? originalObj, IStoryboardEntity modifiedObj, ProjectDataContext context, IStoryboardRepository repository)
        {
            if (modifiedObj == null) return;
            if (context?.Storyboard == null) return;

            if (originalObj == null)
            {
                repository.Add(context.Storyboard, modifiedObj);
            }
            else
            {
                repository.Replace(context.Storyboard, originalObj, modifiedObj);
            }

            context.MarkAsModified();
        }

        public void CommitTemplateEdit(ProjectDataContext context)
        {
            context?.MarkAsModified();
        }
    }
}
