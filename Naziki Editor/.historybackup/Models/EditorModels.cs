using System;

namespace Naziki_Editor.Models
{
    // ==========================================
    // 🌟 编辑器全局专属模型 (Editor 状态管理)
    // ==========================================

    /// <summary>
    /// 🎛️ 专属轨道视觉与交互元数据（绝对不污染 Cytoid 底层 JSON）
    /// </summary>
    public class EditorTrackMeta
    {
        public bool IsMuted { get; set; } = false;           // 👁️ 旁路/隐藏开关 (导出时是否忽略)
        public string TrackColor { get; set; } = null;       // 🎨 轨道专属颜色 (用于音符控制器连线)
        public string EditorAlias { get; set; } = null;      // 📝 轨道的自定义别名
        public bool IsLocked { get; set; } = false;          // 🔒 轨道锁定防误触
    }

    /// <summary>
    /// 🪄 专属模板视觉与交互元数据（绝对不污染 Cytoid 底层 JSON）
    /// </summary>
    public class EditorTemplateMeta
    {
        public TemplateType Type { get; set; } = TemplateType.Generic; // 门派归属
        public string EditorAlias { get; set; } = null;                // 📝 模板备注 (如 "高频闪烁")
        public string FolderPath { get; set; } = "/";                  // 📁 预留：虚拟文件夹路径
    }
}