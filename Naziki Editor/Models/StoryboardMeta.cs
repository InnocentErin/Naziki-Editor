using Newtonsoft.Json;
using System.Collections.Generic;

namespace Naziki_Editor.Models
{

    // 🌟 故事板的专属“小账本”
    public class StoryboardMeta
    {
        [JsonProperty("template_overrides")]
        public Dictionary<string, TemplateType> TemplateOverrides { get; set; } = new Dictionary<string, TemplateType>();
    }
}