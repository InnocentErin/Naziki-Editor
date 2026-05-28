using System;
using Naziki_Editor.Core;

namespace Naziki_Editor.Core.Timeline
{
    public class TimelineCoordEngine
    {
        private double _pixelsPerSecond;

        public TimelineCoordEngine(double pixelsPerSecond)
        {
            _pixelsPerSecond = pixelsPerSecond;
        }

        public void UpdatePixelsPerSecond(double newPps)
        {
            _pixelsPerSecond = newPps;
        }

        /// <summary>
        /// ⏱️ 时间 ──> 物理像素 X 坐标
        /// </summary>
        public double TimeToX(double seconds) => seconds * _pixelsPerSecond;

        /// <summary>
        /// 📏 物理像素 X 坐标 ──> 时间
        /// </summary>
        public double XToTime(double x) => x / _pixelsPerSecond;

        /// <summary>
        /// 🧲 核心重构：计算常驻永生元素在方块内部的虚拟截止线相对位置
        /// </summary>
        public double CalculateVirtualEndPosition(double startTime, double lastStateTime)
        {
            if (lastStateTime <= startTime) return 180.0; // 兜底基础相对像素
            return (lastStateTime - startTime) * _pixelsPerSecond;
        }
    }
}