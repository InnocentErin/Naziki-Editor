using System.Collections.Generic;
using Naziki_Editor.Models;

namespace Naziki_Editor.Core
{
    public static class ChartLogic
    {
        // 🔮 算法 1：寻找“连坐保释”的纯数学计算
        // 注意看：我们把 chart, engine, filters 全都当成参数传进来，这样它就不需要去认识 UI 长什么样了！
        public static bool IsChainVisible(C2Chart chart, ChartTimeEngine engine, int rootIndex, double searchMin, double searchMax, int searchType, bool[] typeFilters)
        {
            C2Note rootNote = chart.note_list[rootIndex];

            // 1. 类型过滤器一票否决
            if (!IsNoteTypeVisible(rootNote.type, typeFilters)) return false;

            // 2. 检查族长自己
            double rootVal = searchType == 0 ? engine.TickToSeconds(rootNote.tick) : rootNote.x;
            if (rootVal >= searchMin && rootVal <= searchMax) return true;

            // 3. 检查孩子们 (Drag 和 CDrag)
            if (rootNote.type == 3 || rootNote.type == 6)
            {
                int next = rootNote.next_id;
                HashSet<int> visited = new HashSet<int>();

                while (next >= 0 && next < chart.note_list.Count && !visited.Contains(next))
                {
                    visited.Add(next);
                    C2Note child = chart.note_list[next];

                    double childVal = searchType == 0 ? engine.TickToSeconds(child.tick) : child.x;
                    if (childVal >= searchMin && childVal <= searchMax) return true;

                    next = child.next_id;
                }
            }
            return false;
        }

        // 🔮 算法 2：类型过滤器匹配
        public static bool IsNoteTypeVisible(int type, bool[] filters)
        {
            // filters 的顺序: 0:Click, 1:Hold, 2:LHold, 3:Drag, 4:Flick, 5:CDrag
            if (type == 0) return filters[0];
            if (type == 1) return filters[1];
            if (type == 2) return filters[2];
            if (type == 3 || type == 4) return filters[3];
            if (type == 5) return filters[4];
            if (type == 6 || type == 7) return filters[5];
            return true;
        }

        // 🔮 算法 3：认亲大会（找出所有的子节点）
        public static bool[] FindChildren(C2Chart chart)
        {
            bool[] isChild = new bool[chart.note_list.Count];
            for (int i = 0; i < chart.note_list.Count; i++)
            {
                C2Note currentNote = chart.note_list[i];
                if (currentNote.type == 3 || currentNote.type == 4 || currentNote.type == 6 || currentNote.type == 7)
                {
                    int next = currentNote.next_id;
                    if (next >= 0 && next < chart.note_list.Count) isChild[next] = true;
                }
            }
            return isChild;
        }

        // 🔮 算法 4：纯粹的 BPM 换算引擎 (从 MainWindow 成功流放至此！)
        public static string GetBpmText(System.Collections.Generic.List<TempoEvent> tempoList)
        {
            if (tempoList == null || tempoList.Count == 0) return "未知";

            if (tempoList.Count == 1)
            {
                double bpm = 60000000.0 / tempoList[0].value;
                return System.Math.Round(bpm, 2).ToString();
            }
            else
            {
                // value 越大速度越慢，最小 BPM 对应最大的 value
                double minBpm = 60000000.0 / tempoList.Max(t => t.value);
                double maxBpm = 60000000.0 / tempoList.Min(t => t.value);
                return $"{System.Math.Round(minBpm, 2)} ~ {System.Math.Round(maxBpm, 2)}";
            }
        }
        // 🌟 完美安家的音符核心模型逻辑
        public static string GetNoteTypeString(int type)
        {
            switch (type)
            {
                case 0: return "Click";
                case 1: return "Hold";
                case 2: return "LongHold"; // 或者是你定义的 L-Hold
                case 3: return "Drag";
                case 4: return "CDrag";
                case 5: return "Flick";
                default: return "Unknown";
            }
        }
    }
}