using System;
using System.Collections.Generic;
using Naziki_Editor.Models;
using Naziki_Editor.Core;

namespace Naziki_Editor.Core.Timeline
{
    public static class TimelineAnchorEngine
    {
        /// <summary>
        /// 🎯 核心算法：全量扫盘，寻找离当前绝对秒数最近的音符，并吐出完美的 C2 锚定字符串
        /// </summary>
        public static string CalculateNearestAnchorExpression(double currentSeconds, List<C2Note> noteList, ChartTimeEngine timeEngine, out C2Note nearestNote, out double finalOffset)
        {
            nearestNote = null;
            finalOffset = 0.0;

            if (noteList == null || noteList.Count == 0 || timeEngine == null)
                return null;

            double minDelta = double.MaxValue;

            foreach (var note in noteList)
            {
                double noteSeconds = timeEngine.TickToSeconds(note.tick);
                double delta = Math.Abs(currentSeconds - noteSeconds);
                if (delta < minDelta)
                {
                    minDelta = delta;
                    nearestNote = note;
                }
            }

            if (nearestNote != null)
            {
                double anchorSeconds = timeEngine.TickToSeconds(nearestNote.tick);
                finalOffset = currentSeconds - anchorSeconds;

                // 如果偏移极小，省略后缀
                string offsetStr = Math.Abs(finalOffset) < 0.001 ? "" : $":{finalOffset.ToString("F3")}";
                return $"start:{nearestNote.id}{offsetStr}";
            }

            return null;
        }
    }
}