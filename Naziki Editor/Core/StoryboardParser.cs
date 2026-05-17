using Newtonsoft.Json;
using System.IO;
using System;
using Naziki_Editor.Models;

namespace Naziki_Editor.Core
{
    public static class StoryboardParser
    {
        // 🔮 解析法术：把读取文本、转成对象、检查是否为空的体力活全包了！
        public static StoryboardRoot Load(string filePath)
        {
            string jsonText = File.ReadAllText(filePath);
            StoryboardRoot root = JsonConvert.DeserializeObject<StoryboardRoot>(jsonText);

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

            // 遇到坏数据，直接抛出 Exception，这样 UI 的 try-catch 就能瞬间接住并弹窗！
            if (!hasContent)
                throw new Exception("纳尼？！这个 JSON 里面什么有效对象都没有，请重新选择~");

            return root;
        }
    }
}