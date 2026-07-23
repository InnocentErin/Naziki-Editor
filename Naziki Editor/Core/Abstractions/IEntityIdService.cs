using Naziki_Editor.Models;

namespace Naziki_Editor.Core.Abstractions
{
    /// <summary>
    /// 实体 ID 服务抽象，负责生成唯一 ID 与检测 ID 冲突。
    /// </summary>
    public interface IEntityIdService
    {
        /// <summary>
        /// 根据实体类型与内容生成全局唯一 ID。
        /// </summary>
        string GenerateUniqueId(IStoryboardEntity entity, StoryboardRoot root);

        /// <summary>
        /// 检查指定 ID 是否已存在于故事板中。
        /// </summary>
        bool IsIdExists(string id, StoryboardRoot root);

        /// <summary>
        /// 检查新 ID 是否与现有 ID 冲突（排除自身原始 ID）。
        /// </summary>
        bool IsIdConflict(string id, string originalId, StoryboardRoot root);
    }
}
