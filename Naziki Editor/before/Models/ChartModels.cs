using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Naziki_Editor.Core;

namespace Naziki_Editor.Models
{
    // ==========================================
    // 🌟 C2 谱面数据模具 (100% 严格对齐官方 Cytoid/Cytus II 格式)
    // ==========================================
    public class C2Chart
    {
        public int format_version { get; set; }
        public int time_base { get; set; }

        // 🎵 新增：音频偏移与结算控制 (来自官方 JSON 示例)
        public double? music_offset { get; set; }
        public bool? skip_music_on_completion { get; set; }

        // 核心四大将
        public List<C2Page> page_list { get; set; } = new List<C2Page>();
        public List<TempoEvent> tempo_list { get; set; } = new List<TempoEvent>(); // 保持你原有的引用
        public List<C2Note> note_list { get; set; } = new List<C2Note>();

        // 🎬 新增：将原先敷衍的 object 彻底升级为强类型的事件列表！
        public List<C2EventOrder> event_order_list { get; set; } = new List<C2EventOrder>();
    }

    public class C2Page
    {
        public int start_tick { get; set; }
        public int end_tick { get; set; }
        public int scan_line_direction { get; set; }
    }

    // (如果你在其他文件里没定义 TempoEvent，这里提供一个官方标准的备份)
    public class TempoEvent
    {
        public int tick { get; set; }
        public long value { get; set; } // 微秒/拍
    }

    public class C2Note
    {
        // === 原有基础属性 ===
        public int page_index { get; set; }
        public int type { get; set; }
        public int id { get; set; }
        public int tick { get; set; }
        public double x { get; set; }

        // === ⬇️ 根据官方文档补全的核心基因 ⬇️ ===

        // 📏 长按音符持续长度 (极为关键)
        public int hold_tick { get; set; }

        // 🔗 锁链/滑条指向的下一个 ID (-1 表示链条结束)
        public int next_id { get; set; }

        // 👯 是否包含同屏同步判定 (由于并非每个音符都有，使用可空类型防止报错)
        public bool? has_sibling { get; set; }

        // 🔄 跨页判定：如果是 true，判定会提前到上一页
        public bool? is_forward { get; set; }

        // ↕️ 官方的坠落音符方向 (Cytoid虽不支持，但必须保留席位)
        public int? NoteDirection { get; set; }
    }

    // ==========================================
    // 🎬 新增：官方事件系统实体类
    // ==========================================
    public class C2EventOrder
    {
        public int tick { get; set; }
        public List<C2Event> event_list { get; set; } = new List<C2Event>();
    }

    public class C2Event
    {
        public int type { get; set; }
        public string args { get; set; }
    }
}