using System;
using System.Collections.Generic;
using System.Text;
using Naziki_Editor.Core;

namespace Naziki_Editor.Models
{
    internal class ChartModels
    {

    }
    // ==========================================
    // 🌟 C2 谱面数据模具 (对应 JSON 结构)
    // ==========================================
    public class C2Chart
    {
        public int format_version { get; set; }
        public int time_base { get; set; }
        // 核心四大将
        public List<C2Page> page_list { get; set; }
        public List<TempoEvent> tempo_list { get; set; } // 这个在我们刚才的引擎里定义过啦！
        public List<C2Note> note_list { get; set; }
        // 事件列表 (暂时留空，以后可能用到)
        public object event_order_list { get; set; }
    }

    public class C2Page
    {
        public int start_tick { get; set; }
        public int end_tick { get; set; }
        public int scan_line_direction { get; set; }
    }

    public class C2Note
    {
        public int page_index { get; set; }
        public int type { get; set; }
        public int id { get; set; }
        public int tick { get; set; }
        public double x { get; set; }
        public bool has_sibling { get; set; }
        public int hold_tick { get; set; }
        // 🌟 驱魔法术 1：如果 JSON 没写，强行默认是 -1，绝对不能是 0！
        public int next_id { get; set; } = -1;
        public int is_forward { get; set; }
    }
}
