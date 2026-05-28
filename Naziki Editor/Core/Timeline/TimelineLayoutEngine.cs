using System;
using System.Collections.Generic;
using System.Linq;
using Naziki_Editor.Models;

namespace Naziki_Editor.Core.Timeline
{
    public class TimelineLayoutEngine
    {
        /// <summary>
        /// 🧙‍♂️ 核心法术：时空轨道一键智能大整理（右键菜单与导入直通车共有）
        /// </summary>
        /// <param name="allClips">当前宇宙中所有的方块集合</param>
        /// <param name="centerTrackBaseline">期望的中心常驻轨道起始索引（默认设为 10，方便普通方块上下扩散）</param>
        public static void OptimizeTrackLayout(List<TimelineClipModel> allClips, int centerTrackBaseline = 10)
        {
            if (allClips == null || allClips.Count == 0) return;

            // 1. 🔍 【基因筛选】：将大军划分为“常驻永生军团”和“普通时效军团”
            var permanentClips = new List<TimelineClipModel>();
            var normalClips = new List<TimelineClipModel>();

            foreach (var clip in allClips)
            {
                // 如果 EndTime 极大，或者根本没有截止时间，判定为常驻常驻元素
                if (clip.EndTime >= 999999 || clip.EndTime <= clip.StartTime)
                {
                    permanentClips.Add(clip);
                }
                else
                {
                    normalClips.Add(clip);
                }
            }

            // ==========================================================
            // 👥 第一阶段：常驻永生军团的“中心合宿布局”
            // ==========================================================
            // 因为常驻元素会霸占后半段所有时空，如果它们在时间上有交叠，也必须分不同的轨道，否则就会重叠！
            // 我们按照 StartTime 从小到大排序，用无碰撞贪心算法把它们排在中心轨区 (centerTrackBaseline 往上递增)
            var permanentSorted = permanentClips.OrderBy(c => c.StartTime).ToList();
            var permanentTrackEnds = new List<double>(); // 记录每条常驻轨道的“当前最后占用时间”

            foreach (var clip in permanentSorted)
            {
                int assignedTrack = -1;

                // 尝试在已开辟的常驻轨道里找一个空档（前一个常驻元素在它出生前就消失了，虽然极少见）
                for (int i = 0; i < permanentTrackEnds.Count; i++)
                {
                    if (clip.StartTime >= permanentTrackEnds[i])
                    {
                        assignedTrack = centerTrackBaseline + i;
                        permanentTrackEnds[i] = double.MaxValue; // 常驻一旦出生，该轨后半段彻底锁死
                        break;
                    }
                }

                // 如果找不到空档，开辟一条全新的中心常驻轨！
                if (assignedTrack == -1)
                {
                    assignedTrack = centerTrackBaseline + permanentTrackEnds.Count;
                    permanentTrackEnds.Add(double.MaxValue);
                }

                clip.TrackIndex = assignedTrack;
            }

            // 算一下常驻军团最终霸占了哪些核心中央轨道
            int minPermTrack = centerTrackBaseline;
            int maxPermTrack = centerTrackBaseline + Math.Max(0, permanentTrackEnds.Count - 1);

            // ==========================================================
            // 🌠 第二阶段：普通时效军团的“双侧护法扩散布局”
            // ==========================================================
            // 普通方块绝对不能侵占中央常驻轨，它们需要以常驻轨为轴心，向上（天）或者向下（地）扩散排布！
            var normalSorted = normalClips.OrderBy(c => c.StartTime).ToList();

            // 维护普通轨道的占用情况：Key是相对于常驻轨区的偏移量，Value是该轨道的最后结束时间
            // 偏移量 1 代表中央上方第一轨，-1 代表中央下方第一轨，2 代表上方第二轨... 依此类推
            var normalTrackEnds = new Dictionary<int, double>();

            foreach (var clip in normalSorted)
            {
                int bestOffset = 0;
                double minGap = double.MaxValue;

                // 贪心搜索：寻找能够塞下当前方块且离中心区最近的普通轨道
                // 我们在 -30 到 +30 的虚拟轨道里进行全量嗅探
                for (int offset = 1; offset <= 30; offset++)
                {
                    // 1. 优先探测上方轨道
                    if (!normalTrackEnds.ContainsKey(offset) || clip.StartTime >= normalTrackEnds[offset])
                    {
                        bestOffset = offset;
                        break;
                    }
                    // 2. 次要探测下方轨道（对称扩散）
                    if (!normalTrackEnds.ContainsKey(-offset) || clip.StartTime >= normalTrackEnds[-offset])
                    {
                        bestOffset = -offset;
                        break;
                    }
                }

                // 如果 30 轨都爆满了（纳尼？！这谱面是有多硬核），那就强行往上叠新轨
                if (bestOffset == 0)
                {
                    int maxCurrentOffset = normalTrackEnds.Keys.Count > 0 ? normalTrackEnds.Keys.Max() : 0;
                    bestOffset = maxCurrentOffset + 1;
                }

                // 登记占用时间
                normalTrackEnds[bestOffset] = clip.EndTime;

                // 物理映射反算：将相对中心区的偏移量，换算成最终绝对的图层 Z-Index 轨道索引！
                if (bestOffset > 0)
                {
                    clip.TrackIndex = maxPermTrack + bestOffset; // 在常驻军团的头顶往上叠
                }
                else
                {
                    clip.TrackIndex = Math.Max(0, minPermTrack + bestOffset); // 在常驻军团的脚底下往下延
                }
            }
        }
    }
}