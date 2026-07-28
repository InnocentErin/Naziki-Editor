using System.Threading.Tasks;
using Naziki_Editor.Models;
using Naziki_Editor.State;

namespace Naziki_Editor.Core.Abstractions
{
    /// <summary>
    /// 项目服务抽象，负责项目文件加载、保存及导出流程。
    /// </summary>
    public interface IProjectService
    {
        // 异步接口
        Task<ProjectDataContext?> LoadProjectAsync(string filePath);
        Task SaveProjectAsync(ProjectDataContext context, string filePath);
        Task ExportCytoidStoryboardAsync(
            StoryboardRoot storyboard,
            string outputPath,
            ProjectDataContext? context = null);
        Task SaveStoryboardMetaAsync(ProjectDataContext context, string storyboardPath);
        Task SaveProjectNepFileAsync(ProjectDataContext context, string? filePath = null);

        // 同步辅助方法（保留给现有 UI 调用链使用）
        void SaveProjectNepFile(ProjectDataContext context, string? filePath = null);
        string SaveAssetCapsule(ProjectDataContext context, IStoryboardEntity entity, string materialType);
        ProjectDataContext? LoadProjectData(string filePath);
        StoryboardRoot LoadStoryboard(string filePath);
        StoryboardMeta LoadStoryboardMeta(string storyboardPath);
        (StoryboardRoot Storyboard, StoryboardMeta Meta) ImportStoryboard(string storyboardPath, NazikiProjectModel? projectData);

        /// <summary>
        /// 加载项目关联的故事板：校验文件、反序列化、标准化 ID、同步控制板映射并读取元数据。
        /// 若文件不存在或路径为空，则返回空故事板与空元数据。
        /// </summary>
        (StoryboardRoot? Storyboard, StoryboardMeta Meta) LoadProjectStoryboard(string storyboardPath, NazikiProjectModel projectData);

        C2Chart? SilentImportChart(string chartPath);
    }
}
