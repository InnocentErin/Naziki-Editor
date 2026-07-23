using System;
using System.Collections.Generic;
using System.Reflection;

namespace Naziki_Editor.Core
{
    // 🛡️ 故事板核心数据校验器 (纯净解耦版)
    public static class StoryboardValidator
    {
        // ==========================================
        // 🚨 时空纠察雷达：拦截同一时间点存在的重复属性
        // 💡 返回值：(是否通过校验, 具体的错误情报情报)
        // ==========================================
        public static (bool IsValid, string ErrorMessage) ValidateStateConflicts(object editingObj)
        {
            var statesProp = editingObj.GetType().GetProperty("States");
            var statesList = statesProp?.GetValue(editingObj) as System.Collections.IList;

            Dictionary<string, HashSet<string>> timePropMap = new Dictionary<string, HashSet<string>>();
            List<object> allFrames = new List<object> { editingObj };

            if (statesList != null)
            {
                foreach (var state in statesList) allFrames.Add(state);
            }

            // 防呆白名单：静态 DNA 或底层系统锚点
            HashSet<string> ignoreProps = new HashSet<string> {
                "Time", "Easing", "AddTime", "RelativeTime",
                "Id", "ParentId", "TargetId", "States",
                "Path", "TextContent", "Text", "Pos", "Template", "Layer", "Font", "Align", "Note"
            };

            for (int i = 0; i < allFrames.Count; i++)
            {
                var frame = allFrames[i];
                string frameName = i == 0 ? "初始状态(Root)" : $"补间关键帧[{i}]";

                List<string> timePoints = new List<string>();
                object timeObj = frame.GetType().GetProperty("Time")?.GetValue(frame);

                // 时间点提取逻辑 (完美兼容单点、多点触发与逗号分隔)
                if (timeObj is Newtonsoft.Json.Linq.JArray jArray)
                {
                    foreach (var t in jArray) timePoints.Add(t.ToString().Trim());
                }
                else if (timeObj is System.Collections.IList iList)
                {
                    foreach (var t in iList) timePoints.Add(t.ToString().Trim());
                }
                else if (timeObj != null)
                {
                    string tStr = timeObj.ToString();
                    if (tStr.Contains(","))
                    {
                        var parts = tStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var p in parts) timePoints.Add(p.Trim());
                    }
                    else timePoints.Add(tStr.Trim());
                }
                else timePoints.Add("0");

                // 提取当前帧被激活的动作属性
                List<string> activeProps = new List<string>();
                foreach (var prop in frame.GetType().GetProperties())
                {
                    if (ignoreProps.Contains(prop.Name)) continue;
                    if (prop.GetValue(frame) != null) activeProps.Add(prop.Name);
                }

                // 交叉比对，发现冲突直接返回错误情报！
                foreach (var t in timePoints)
                {
                    if (!timePropMap.ContainsKey(t)) timePropMap[t] = new HashSet<string>();

                    foreach (var p in activeProps)
                    {
                        if (timePropMap[t].Contains(p))
                        {
                            // 👑 彻底解耦：不再弹窗，而是将冰冷的错误报告直接上报给调用者！
                            string errorMsg = $"💥 发现时空悖论！非法数据被拦截！\n\n" +
                                              $"【异常位置】: {frameName}\n" +
                                              $"【重叠时间】: {t}\n" +
                                              $"【冲突属性】: {p}\n\n" +
                                              $"呆胶布？引擎不允许在相同的时间点对 [{p}] 下达两次修改指令哦！\n请将重复的属性合并，或错开到不同的时间点吧！";

                            return (false, errorMsg);
                        }
                        timePropMap[t].Add(p);
                    }
                }
            }

            // 完美通关，放行！
            return (true, string.Empty);
        }
    }
}