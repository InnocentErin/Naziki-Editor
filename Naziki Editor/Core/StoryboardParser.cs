using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Naziki_Editor.Models;
using Newtonsoft.Json.Serialization;

namespace Naziki_Editor.Core
{
    // ==========================================
    // 🌟 全局 JSON 输出大管家 (供预览和保存使用)
    // ==========================================
    public static class StoryboardSerializer
    {
        public static JsonSerializerSettings GetSettings()
        {
            return new JsonSerializerSettings
            {
                // 🗡️ 斩杀问题 2：自动隐藏所有 Null 值！垃圾代码瞬间消失！
                NullValueHandling = NullValueHandling.Ignore,

                // 🗡️ 斩杀问题 1：强制将 C# 的 PascalCase 转换为 Cytoid 的 snake_case！
                // 比如 TargetId 自动变成 target_id，States 变成 states！
                ContractResolver = new DefaultContractResolver
                {
                    NamingStrategy = new SnakeCaseNamingStrategy()
                },
                Formatting = Formatting.Indented,
                Converters = new List<JsonConverter>
        {
             new StoryboardObjectConverter(), // 添加自定义转换器
             new UnitFloatConverter(),   // 添加自定义转换器
             new TimeObjectConverter()   // 如果有也需要添加
        }
            };
        }

        public static string ToJson(object obj)
        {
            return JsonConvert.SerializeObject(obj, GetSettings());
        }
    }





    public static class StoryboardParser
    {
        // 🔮 解析法术：把读取文本、转成对象、检查是否为空的体力活全包了！
        public static StoryboardRoot Load(string filePath)
        {
            string jsonText = File.ReadAllText(filePath);
            var settings = StoryboardSerializer.GetSettings();
            StoryboardRoot root = JsonConvert.DeserializeObject<StoryboardRoot>(jsonText, settings);

            // 🛡️ 验明正身：极其严密且防崩溃的安检门！
            bool hasContent = false;
            if (root != null)
            {
                if (root.sprites != null && root.sprites.Count > 0) hasContent = true;
                if (root.texts != null && root.texts.Count > 0) hasContent = true;
                if (root.videos != null && root.videos.Count > 0) hasContent = true;
                if (root.lines != null && root.lines.Count > 0) hasContent = true;
                if (root.controllers != null && root.controllers.Count > 0) hasContent = true;
                if (root.note_controllers != null && root.note_controllers.Count > 0) hasContent = true;
                if (root.templates != null && root.templates.Count > 0) hasContent = true;
            }

            // 🌟 核心：在解析完毕后，立即执行全局 ID 自动纠正！
            if (root != null)
            {
                NormalizeMissingIds(root);
            }

            // 遇到坏数据，直接抛出 Exception，这样 UI 的 try-catch 就能瞬间接住并弹窗！
            if (!hasContent)
                throw new Exception("纳尼？！这个 JSON 里面什么有效对象都没有，请重新选择~");

            return root;
        }

        // ==========================================
        // 🛠️ 全局唯一 ID 自动纠正法术 (全阵营版)！
        // ==========================================
        private static void NormalizeMissingIds(StoryboardRoot root)
        {
            HashSet<string> usedIds = new HashSet<string>();

            // 1. 先把全场已经存在的合法身份证登记在册
            if (root.sprites != null) foreach (var s in root.sprites.Where(x => !string.IsNullOrEmpty(x.Id))) usedIds.Add(s.Id);
            if (root.texts != null) foreach (var t in root.texts.Where(x => !string.IsNullOrEmpty(x.Id))) usedIds.Add(t.Id);
            if (root.lines != null) foreach (var l in root.lines.Where(x => !string.IsNullOrEmpty(x.Id))) usedIds.Add(l.Id);
            if (root.videos != null) foreach (var v in root.videos.Where(x => !string.IsNullOrEmpty(x.Id))) usedIds.Add(v.Id);
            if (root.controllers != null) foreach (var c in root.controllers.Where(x => !string.IsNullOrEmpty(x.Id))) usedIds.Add(c.Id);
            if (root.note_controllers != null) foreach (var n in root.note_controllers.Where(x => !string.IsNullOrEmpty(x.Id))) usedIds.Add(n.Id);
            if (root.templates != null) foreach (var key in root.templates.Keys) usedIds.Add(key);

            // 2. 核心发证机：保证绝对不重名
            string GenerateUniqueId(string baseName)
            {
                if (string.IsNullOrWhiteSpace(baseName)) baseName = "Unnamed";
                string newId = baseName;
                int counter = 1;
                while (usedIds.Contains(newId))
                {
                    newId = $"{baseName}_{counter}";
                    counter++;
                }
                usedIds.Add(newId);
                return newId;
            }

            // 3. 开始挨个车间查户口，没带身份证的当场补办！
            if (root.sprites != null) foreach (var s in root.sprites.Where(x => string.IsNullOrEmpty(x.Id)))
                s.Id = GenerateUniqueId(s.States?.Count > 0 && !string.IsNullOrEmpty(s.States[0].Path) ? Path.GetFileNameWithoutExtension(s.States[0].Path) : "Sprite");

            if (root.videos != null) foreach (var v in root.videos.Where(x => string.IsNullOrEmpty(x.Id)))
                v.Id = GenerateUniqueId(v.States?.Count > 0 && !string.IsNullOrEmpty(v.States[0].Path) ? Path.GetFileNameWithoutExtension(v.States[0].Path) : "Video");

            // 🌟 新增：强制给文字、线条、场景、音符控制器发 ID！
            if (root.texts != null) foreach (var t in root.texts.Where(x => string.IsNullOrEmpty(x.Id))) t.Id = GenerateUniqueId("Text");
            if (root.lines != null) foreach (var l in root.lines.Where(x => string.IsNullOrEmpty(x.Id))) l.Id = GenerateUniqueId("Line");
            if (root.controllers != null) foreach (var c in root.controllers.Where(x => string.IsNullOrEmpty(x.Id))) c.Id = GenerateUniqueId("Controller");
            if (root.note_controllers != null) foreach (var n in root.note_controllers.Where(x => string.IsNullOrEmpty(x.Id))) n.Id = GenerateUniqueId("NoteCtrl");
        }
    }
}