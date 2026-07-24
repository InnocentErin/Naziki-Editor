using System;

namespace Naziki_Editor.Models
{
    // ==========================================
    // 🌟 素材胶囊：被序列化成 .nem 物理文件的模具
    // ==========================================
    public class NemDocument
    {
        // 素材的种类："Text", "Line", "Template" 等
        public string MaterialType { get; set; }

        // 素材的展示名称
        public string MaterialName { get; set; }

        // 序列化后的真正对象数据（直接存 JSON 字符串，反序列化时极其安全）
        public string PayloadJson { get; set; }

        // 制造日期
        public DateTime CreationTime { get; set; } = DateTime.Now;
    }
}