using Naziki_Editor.Models;

namespace Naziki_Editor.Core
{
    // ==========================================
    // 🌟 全局事件名称解析中枢 (已完美适配 C2 实体体系)
    // ==========================================
    public static class EventNameResolver
    {
        public static string GetDisplayName(object obj)
        {
            switch (obj)
            {
                case C2Sprite sprite:
                    string sPath = sprite.BaseState?.Path ?? "";
                    return string.IsNullOrEmpty(sPath) ? sprite.Id : $"{sprite.Id} [{sPath}]";

                case C2Video video:
                    string vPath = video.BaseState?.Path ?? "";
                    return string.IsNullOrEmpty(vPath) ? video.Id : $"{video.Id} [{vPath}]";

                case C2Text text:
                    string rawText = text.BaseState?.TextContent ?? "";
                    if (!string.IsNullOrEmpty(rawText))
                        return rawText.Length > 10 ? $"{text.Id} (\"{rawText.Substring(0, 10)}...\")" : $"{text.Id} (\"{rawText}\")";
                    return text.Id;

                case C2Line line:
                    return line.Id;

                case C2SceneController ctrl:
                    return ctrl.Id;

                case C2NoteController noteCtrl:
                    // 直接读取 BaseState 里的 NoteTarget
                    return $"{noteCtrl.Id} | Target: {noteCtrl.BaseState?.NoteTarget}";

                case C2Template template:
                    return template.Id;

                default:
                    return "未知异界物质";
            }
        }
    }
}