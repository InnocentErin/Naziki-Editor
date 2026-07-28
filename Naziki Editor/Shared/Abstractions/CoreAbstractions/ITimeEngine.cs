using System.Collections.Generic;
using Naziki_Editor.Models;

namespace Naziki_Editor.Core.Abstractions
{
    /// <summary>
    /// 时间引擎抽象，负责谱面 tick 与绝对秒数之间的换算，以及解析音符锚点时间表达式。
    /// </summary>
    public interface ITimeEngine
    {
        /// <summary>
        /// 将目标 tick 转换为绝对秒数。
        /// </summary>
        double TickToSeconds(int targetTick);

        /// <summary>
        /// 解析时间对象（纯数字或音符锚点表达式）为绝对秒数。
        /// </summary>
        double ParseCytoidTimeExpression(object timeObj, List<C2Note> allNotes);
    }
}
