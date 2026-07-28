using System;
using Naziki_Editor.Core.Abstractions;

namespace Naziki_Editor.Core.Workspace
{
    /// <summary>
    /// 工作区服务实现，负责属性面板与源代码编辑器之间数据冲突的判断与解决。
    /// </summary>
    public class WorkspaceService : IWorkspaceService
    {
        public bool HasConflict(bool hasUnappliedSourceChanges, bool isVisualDirty)
        {
            return hasUnappliedSourceChanges && isVisualDirty;
        }

        public bool ResolveConflict(ConflictResolution resolution, Func<bool> applySource, Action refreshView)
        {
            if (applySource == null) throw new ArgumentNullException(nameof(applySource));
            if (refreshView == null) throw new ArgumentNullException(nameof(refreshView));

            switch (resolution)
            {
                case ConflictResolution.ApplySource:
                    return applySource();

                case ConflictResolution.RefreshView:
                    refreshView();
                    return true;

                case ConflictResolution.Cancel:
                default:
                    return false;
            }
        }
    }
}
