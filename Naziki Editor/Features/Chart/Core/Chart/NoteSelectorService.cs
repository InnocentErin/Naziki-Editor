using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Models;

namespace Naziki_Editor.Core.Chart
{
    /// <summary>
    /// 音符选择器服务实现：封装 NoteTarget 解析、音符过滤与时间范围计算。
    /// </summary>
    public class NoteSelectorService : INoteSelectorService
    {
        public NoteSelectorModel ParseSelector(string targetStr)
        {
            if (string.IsNullOrWhiteSpace(targetStr)) return null;

            string trimmed = targetStr.Trim();
            if (trimmed.StartsWith("{"))
            {
                try { return JsonConvert.DeserializeObject<NoteSelectorModel>(trimmed); } catch { }
                return null;
            }

            if (int.TryParse(trimmed, out int singleId))
            {
                return new NoteSelectorModel { Start = singleId, End = singleId };
            }

            return null;
        }

        public List<C2Note> SelectNotes(C2Chart chart, NoteSelectorModel selector)
        {
            var result = new List<C2Note>();
            if (chart?.note_list == null || selector == null) return result;

            foreach (var note in chart.note_list)
            {
                int noteDirection = 1;
                if (note.page_index >= 0 && chart.page_list != null && note.page_index < chart.page_list.Count)
                    noteDirection = chart.page_list[note.page_index].scan_line_direction;

                bool isMatch = true;
                if (selector.Type != null && !selector.Type.Contains(note.type)) isMatch = false;
                if (selector.Start.HasValue && note.id < selector.Start.Value) isMatch = false;
                if (selector.End.HasValue && note.id > selector.End.Value) isMatch = false;
                if (selector.Direction.HasValue && noteDirection != selector.Direction.Value) isMatch = false;
                if (selector.MinX.HasValue && note.x < selector.MinX.Value) isMatch = false;
                if (selector.MaxX.HasValue && note.x > selector.MaxX.Value) isMatch = false;

                if (isMatch) result.Add(note);
            }

            return result;
        }

        public (double minSec, double maxSec) GetMatchedTimeRange(C2Chart chart, NoteSelectorModel selector, ITimeEngine timeEngine)
        {
            var matched = SelectNotes(chart, selector);
            if (matched.Count == 0) return (0, 0);

            double minSec = matched.Min(n => timeEngine.TickToSeconds(n.tick));
            double maxSec = matched.Max(n => timeEngine.TickToSeconds(n.tick + (n.hold_tick > 0 ? n.hold_tick : 0)));
            return (minSec, maxSec);
        }
    }
}
