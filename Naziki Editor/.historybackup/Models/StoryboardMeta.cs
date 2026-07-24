using Newtonsoft.Json;
using System.Collections.Generic;

namespace Naziki_Editor.Models
{
    // 🌟 故事板的专属“小账本”
    public class StoryboardMeta
    {
        // 🌟 轨道元数据账本 (Key 为 控制器/事件对象的 Id)
        [JsonProperty("track_metas")]
        public Dictionary<string, EditorTrackMeta> TrackMetas { get; set; } = new Dictionary<string, EditorTrackMeta>();

        // 🌟 模板元数据账本 (Key 为 模板的名字 TemplateName)
        [JsonProperty("template_metas")]
        public Dictionary<string, EditorTemplateMeta> TemplateMetas { get; set; } = new Dictionary<string, EditorTemplateMeta>();
    }
}