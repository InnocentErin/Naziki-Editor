using System;
using System.Collections.Generic;
using System.Linq;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Models;

namespace Naziki_Editor.Core
{
    // 🌟 定义一下 JSON 里的 Tempo 对象长什么样

    // 🌟 我们的核心时空引擎！
    public class ChartTimeEngine : ITimeEngine
    {
        private List<TempoEvent> _tempoList;
        private int _timeBase;

        // 构造法术：当我们读取到谱面时，把谱面的 Tempo 列表和 TimeBase 喂给这个引擎
        public ChartTimeEngine(List<TempoEvent> tempoList, int timeBase)
        {
            // 防呆设计：万一谱面里的速度没按时间排好，我们强行给它按 tick 从小到大排个序！
            _tempoList = tempoList.OrderBy(t => t.tick).ToList();
            _timeBase = timeBase;
        }

        // ==========================================
        // 🌟 核心法术：将冷冰冰的 Tick 换算成绝对的秒数！
        // ==========================================
        public double TickToSeconds(double targetTick)
        {
            // 如果谱面坏了，连速度都没有，那就直接返回 0 秒
            if (_tempoList == null || _tempoList.Count == 0) return 0;

            double totalSeconds = 0;  // 记录总共花了多少秒
            double currentTick = 0;   // 记录我们当前走到了第几个里程碑

            // 开始像切蛋糕一样，一段一段地算时间
            for (int i = 0; i < _tempoList.Count; i++)
            {
                TempoEvent currentEvent = _tempoList[i];

                // 如果我们查询的位置，甚至还没到这个变速点，那就直接结束计算
                if (targetTick <= currentEvent.tick)
                    break;

                // 确定我们这一次要计算的终点
                double nextTick = targetTick; // 先假设目标就在当前这段速度里

                // 如果后面还有变速点，并且我们的目标超越了那个变速点
                if (i + 1 < _tempoList.Count && targetTick > _tempoList[i + 1].tick)
                {
                    // 那我们这一小段，只能算到下一个变速点为止！
                    nextTick = _tempoList[i + 1].tick;
                }

                // 计算这一小段路程，总共跨越了多少个 Tick
                double deltaTick = nextTick - Math.Max(currentTick, currentEvent.tick);

                // 🌟 终极换算公式：时间 = (Tick差值 / TimeBase) * Tempo
                // 因为 Tempo 是微秒，为了变成秒，我们把它除以 1000000.0
                double segmentSeconds = ((double)deltaTick / _timeBase) * (currentEvent.value / 1000000.0);

                // 把这一小段花的时间，加到总时间里
                totalSeconds += segmentSeconds;

                // 走到下一个起点，继续下一轮循环
                currentTick = nextTick;

                // 如果已经走到了目标位置，就提早打卡下班！
                if (currentTick >= targetTick) break;
            }

            return totalSeconds;
        }

        // ==========================================\
        // 🌟 终极翻译官：把 "start:1134:2" 或 "12.5" 统一翻译成绝对秒数！
        // ==========================================\
        public double ParseCytoidTimeExpression(object timeObj, List<C2Note> allNotes)
        {
            if (timeObj == null) return 0;

            // 🚨 【Bug 修复核心】：如果时间对象是一个 JArray（JSON 数组），则取其第一个元素进行解析！
            // 这样至少能保证方块正确显示在它的第一个时间点位置。
            if (timeObj is Newtonsoft.Json.Linq.JArray jArray && jArray.Count > 0)
            {
                timeObj = jArray[0];
                System.Diagnostics.Debug.WriteLine($"[时间轴雷达 2] 检测到时间数组，已取第一个元素: '{timeObj}'");
            }
            else if (timeObj is System.Collections.IList list && list.Count > 0)
            {
                timeObj = list[0]; // 兼容普通 List
            }

            string str = timeObj.ToString().Trim();

            // 1. 如果是纯绝对秒数，直接秒解
            if (double.TryParse(str, out double directVal)) return directVal;

            // 2. 如果没有谱面数据...
            if (allNotes == null || allNotes.Count == 0) return double.NaN;

            try
            {
                string[] parts = str.Split(':');
                int noteId = -1;
                double offset = 0;

                if (parts.Length == 1) { if (int.TryParse(parts[0], out noteId)) offset = 0; }
                else if (parts.Length >= 2)
                {
                    int.TryParse(parts[1], out noteId);
                    if (parts.Length == 3) double.TryParse(parts[2], out offset);
                }

                // 🟢 雷达 3：打印当前谱面的总音符数，以及故事板试图寻找的 NoteID
                System.Diagnostics.Debug.WriteLine($"[时间轴雷达 2] 谱面总音符数: {allNotes.Count}");
                System.Diagnostics.Debug.WriteLine($"[时间轴雷达 2] 故事板请求寻找 Note ID: {noteId}");

                var targetNote = allNotes.FirstOrDefault(n => n.id == noteId);
                if (targetNote == null)
                {
                    System.Diagnostics.Debug.WriteLine($"❗ [时间轴雷达 2] 严重失配！谱面中不存在 ID 为 {noteId} 的音符！");
                    return double.NaN;
                }

                var anchor = parts.Length >= 2 ? parts[0].ToLowerInvariant() : "start";
                return anchor switch
                {
                    "end" => TickToSeconds(targetNote.tick + Math.Max(0, targetNote.hold_tick)) + offset,
                    "intro" => TickToSeconds(targetNote.tick) - 1.5 + offset,
                    "at" when parts.Length >= 3 &&
                              double.TryParse(parts[2], out var percentage) =>
                        TickToSeconds(targetNote.tick +
                            Math.Max(0, targetNote.hold_tick) * percentage),
                    _ => TickToSeconds(targetNote.tick) + offset
                };
            }
            catch
            {
                return double.NaN;
            }
        }



    }
    
}
