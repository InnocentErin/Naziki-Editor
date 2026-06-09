using System;
using System.Collections.Generic;
using System.Linq;
using Naziki_Editor.Models;

namespace Naziki_Editor.Core
{
    // 🌟 定义一下 JSON 里的 Tempo 对象长什么样

    // 🌟 我们的核心时空引擎！
    public class ChartTimeEngine
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
        public double TickToSeconds(int targetTick)
        {
            // 如果谱面坏了，连速度都没有，那就直接返回 0 秒
            if (_tempoList == null || _tempoList.Count == 0) return 0;

            double totalSeconds = 0;  // 记录总共花了多少秒
            int currentTick = 0;      // 记录我们当前走到了第几个里程碑

            // 开始像切蛋糕一样，一段一段地算时间
            for (int i = 0; i < _tempoList.Count; i++)
            {
                TempoEvent currentEvent = _tempoList[i];

                // 如果我们查询的位置，甚至还没到这个变速点，那就直接结束计算
                if (targetTick <= currentEvent.tick)
                    break;

                // 确定我们这一次要计算的终点
                int nextTick = targetTick; // 先假设目标就在当前这段速度里

                // 如果后面还有变速点，并且我们的目标超越了那个变速点
                if (i + 1 < _tempoList.Count && targetTick > _tempoList[i + 1].tick)
                {
                    // 那我们这一小段，只能算到下一个变速点为止！
                    nextTick = _tempoList[i + 1].tick;
                }

                // 计算这一小段路程，总共跨越了多少个 Tick
                int deltaTick = nextTick - Math.Max(currentTick, currentEvent.tick);

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
        public double ParseCytoidTimeExpression(object timeObj, List<Models.C2Note> allNotes)
        {
            if (timeObj == null) return 0;
            string str = timeObj.ToString().Trim();

            // 1. 如果是纯绝对秒数，直接秒解
            if (double.TryParse(str, out double directVal)) return directVal;

            // 2. 遇到锚点魔法，呼叫内部 TickToSeconds 引擎算账！
            if (allNotes != null)
            {
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

                    var targetNote = allNotes.FirstOrDefault(n => n.id == noteId);
                    if (targetNote != null)
                    {
                        double baseSeconds = this.TickToSeconds(targetNote.tick); // 直接调用自己的引擎
                        return baseSeconds + offset;
                    }
                }
                catch { /* 解析失败则兜底返回0 */ }
            }
            return 0;
        }



    }
    
}