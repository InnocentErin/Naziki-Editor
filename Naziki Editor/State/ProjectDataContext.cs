using Naziki_Editor.Models; // 引入实体模型

namespace Naziki_Editor.State
{
    /// <summary>
    /// 全局工程上下文数据包 (Context Injection)
    /// 包含了当前正在编辑的这个项目所有的核心数据实体。
    /// 无论 UI 嵌套多深，只要把这个包裹传下去，所有人都能拿到所需数据！
    /// </summary>
    public class ProjectDataContext
    {
        // ==========================================
        // 🌟 核心数据资产
        // ==========================================

        /// <summary>
        /// 当前的故事板宇宙 (包含所有的 Sprite, Text, Controller 等)
        /// </summary>
        public StoryboardRoot Storyboard { get; set; }

        /// <summary>
        /// 当前加载的谱面数据 (包含所有的音符 Notes)
        /// </summary>
        public C2Chart Chart { get; set; }

        // ==========================================
        // 📁 工程环境信息
        // ==========================================

        /// <summary>
        /// 当前项目的根目录路径
        /// </summary>
        public string ProjectDirectory { get; set; }

        /// <summary>
        /// 当前音频文件的绝对路径
        /// </summary>
        public string AudioFilePath { get; set; }

        // ==========================================
        // 🔍 状态快捷判定
        // ==========================================

        /// <summary>
        /// 检查是否已经初始化了故事板
        /// </summary>
        public bool HasStoryboard => Storyboard != null;

        /// <summary>
        /// 检查是否已经加载了谱面
        /// </summary>
        public bool HasChart => Chart != null;
    }
}