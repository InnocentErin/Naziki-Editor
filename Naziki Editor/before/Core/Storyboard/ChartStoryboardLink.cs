using Naziki_Editor.Models;
using System;
using System.Linq;

namespace Naziki_Editor.Core
{
    // ==========================================
    // 🏛️ 专职的联姻服务机构 (纯数据引擎版，绝不碰 UI)
    // ==========================================
    public static class ChartStoryboardLink
    {
        // 🔮 1. 提纯后的纯逻辑联姻引擎：只算数，不画图！
        public static int ExecuteAutoLink(C2Chart chart, StoryboardRoot storyboardRoot)
        {
            // 防呆结界：如果没有数据，直接返回 0
            if (chart == null || storyboardRoot == null || storyboardRoot.note_controllers == null)
                return 0;

            int linkedCount = 0;

            // 🌟 遍历故事板里的所有音符控制器
            foreach (var ctrl in storyboardRoot.note_controllers)
            {
                // 抓取当前控制器原本想看的时间或目标
                if (ctrl.BaseState?.NoteTarget == null) continue;

                var target = ctrl.BaseState.NoteTarget;

                // 如果目标是普通的数字 ID
                if (target is long || target is int || long.TryParse(target.ToString(), out _))
                {
                    int targetId = Convert.ToInt32(target);
                    var matchedNote = chart.note_list.FirstOrDefault(n => n.id == targetId);

                    // 如果在谱面里找到了，就让成功计数器 +1
                    if (matchedNote != null)
                    {
                        linkedCount++;
                    }
                }
            }

            // 纯粹地返回配对成功的数量，绝不弹窗，绝不造 ListBoxItem！
            return linkedCount;
        }

        // 🔮 2. 纯逻辑安全校验引擎
        public static bool CheckChartStoryboardLinkValidity(C2Chart chart, StoryboardRoot storyboard, out string errorMessage)
        {
            errorMessage = string.Empty;
            int maxChartId = chart.note_list.Count > 0 ? chart.note_list.Max(n => n.id) : -1;
            long maxStoryboardId = -1;

            foreach (var ctrl in storyboard.note_controllers)
            {
                if (ctrl.BaseState?.NoteTarget != null)
                {
                    var target = ctrl.BaseState.NoteTarget;
                    if (target is long l) maxStoryboardId = System.Math.Max(maxStoryboardId, l);
                    else if (target is int i) maxStoryboardId = System.Math.Max(maxStoryboardId, i);
                    else if (long.TryParse(target.ToString(), out long parsed)) maxStoryboardId = System.Math.Max(maxStoryboardId, parsed);
                }
            }

            if (maxStoryboardId > maxChartId)
            {
                errorMessage = "发现异常！故事板中引用的最大音符 ID 超出了当前谱面的最大音符 ID。(°ロ°)\n该故事板可能与该谱面不匹配，配对操作已中止。";
                return false;
            }

            return true;
        }
    }
}