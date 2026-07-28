using System;

using Newtonsoft.Json;

namespace Naziki_Editor.Models
{
    // ==========================================
    // 🌟 素材胶囊：被序列化成 .nem 物理文件的模具
    // ==========================================
    public class NemDocument
    {
        [JsonProperty("format_version")] public int FormatVersion { get; set; } = 2;
        // 素材的种类："Text", "Line", "Template" 等
        [JsonProperty("material_type")] public string MaterialType { get; set; }

        // 素材的展示名称
        [JsonProperty("material_name")] public string MaterialName { get; set; }

        [JsonProperty("payload")] public StoryboardRoot Payload { get; set; } = new();

        // 制造日期
        [JsonProperty("creation_time")] public DateTime CreationTime { get; set; } = DateTime.Now;
    }
}
