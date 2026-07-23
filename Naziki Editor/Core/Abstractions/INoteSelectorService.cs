using System.Collections.Generic;
using Naziki_Editor.Models;

namespace Naziki_Editor.Core.Abstractions
{
    /// <summary>
    /// 音符选择器服务抽象，负责解析 NoteTarget 表达式并过滤匹配音符。
    /// </summary>
    public interface INoteSelectorService
    {
        /// <summary>
        /// 解析 NoteTarget 字符串：支持 JSON 选择器或单一 ID。
        /// </summary>
        NoteSelectorModel ParseSelector(string targetStr);

        /// <summary>
        /// 根据选择器从谱面中筛选出匹配的音符。
        /// </summary>
        List<C2Note> SelectNotes(C2Chart chart, NoteSelectorModel selector);

        /// <summary>
        /// 计算匹配音符的时间范围（秒）。
        /// </summary>
        (double minSec, double maxSec) GetMatchedTimeRange(C2Chart chart, NoteSelectorModel selector, ITimeEngine timeEngine);
    }
}
