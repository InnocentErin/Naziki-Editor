using Naziki_Editor.Core.Compilation;
using Naziki_Editor.Models;
using Naziki_Editor.State;

namespace Naziki_Editor.Core.Abstractions
{
    /// <summary>
    /// 故事板编译服务抽象，负责模板展平、控制器归并/分裂等编译流程。
    /// </summary>
    public interface ICompilationService
    {
        void CompileStoryboard(ProjectDataContext context);
        void OptimizeScatteredControllers(ProjectDataContext context, OptimizeTarget target);

        /// <summary>
        /// 为导出编译故事板：深拷贝并展平，返回编译后的影子故事板，不修改上下文中的原始故事板。
        /// </summary>
        StoryboardRoot CompileForExport(ProjectDataContext context);

        /// <summary>
        /// 根据当前故事板模板更新元数据中的模板类型信息，并清理已删除模板的残留记录。
        /// </summary>
        void SyncTemplateMetadata(ProjectDataContext context);
    }
}
