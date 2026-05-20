using Naziki_Editor.Models;
using Newtonsoft.Json.Linq;

namespace Naziki_Editor.Core
{
    // ==========================================
    // 🌟 全局事件名称解析中枢 (供 Timeline、List、Panel 共享调用)
    // ==========================================
    public static class EventNameResolver
    {
        public static string GetDisplayName(object obj)
        {
            // 感谢 Parser 里的强行补齐逻辑，现在所有对象必定有 Id！
            switch (obj)
            {
                case Sprite sprite:
                    string sPath = sprite.States?.Count > 0 ? sprite.States[0].Path : "";
                    return string.IsNullOrEmpty(sPath) ? sprite.Id : $"{sprite.Id} [{sPath}]";

                case Video video:
                    string vPath = video.States?.Count > 0 ? video.States[0].Path : "";
                    return string.IsNullOrEmpty(vPath) ? video.Id : $"{video.Id} [{vPath}]";

                case Text text:
                    string rawText = text.States?.Count > 0 ? text.States[0].Text : "";
                    if (!string.IsNullOrEmpty(rawText))
                        return rawText.Length > 10 ? $"{text.Id} (\"{rawText.Substring(0, 10)}...\")" : $"{text.Id} (\"{rawText}\")";
                    return text.Id;

                case Line line:
                    return line.Id;

                case Controller ctrl:
                    return ctrl.Id;

                case NoteController noteCtrl:
                    // 音符控制器比较特殊，把它的绑定目标也打印出来更直观
                    if (noteCtrl.NoteTarget is JObject jobj)
                    {
                        var selector = jobj.ToObject<NoteCtrlEventSelect>();
                        return $"{noteCtrl.Id} | {selector.DisplayName}";
                    }
                    return $"{noteCtrl.Id} | Target: {noteCtrl.NoteTarget}";

                default:
                    return "未知异界物质";
            }
        }
    }
}