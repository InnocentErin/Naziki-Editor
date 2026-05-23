using System;
using Naziki_Editor.Models;
using Naziki_Editor.Core;

namespace Naziki_Editor.State
{
    /// <summary>
    /// 全局工程上下文数据包 (Context Injection)
    /// </summary>
    public class ProjectDataContext
    {


        // ==========================================
        // 🌟 新增：全局数据修改“广播站” (Event)
        // ==========================================
        public event Action OnDataModified;

        // ==========================================
        // 🌟 物理工程账本环境
        // ==========================================
        public string ProjectFilePath { get; set; }
        public NazikiProjectModel ProjectData { get; set; }

        // ==========================================
        // 🌟 核心数据资产
        // ==========================================
        public string StoryboardPath { get; set; }
        public StoryboardRoot Storyboard { get; set; } = new StoryboardRoot();

        public C2Chart Chart { get; set; }

        /// <summary>
        /// 谱面时间转换引擎，有了它，所有窗口都能随时算时间！
        /// </summary>
        public ChartTimeEngine TimeEngine { get; set; }

        // ==========================================
        // 🔍 状态快捷判定
        // ==========================================
        public bool HasStoryboard => Storyboard != null;
        public bool HasChart => Chart != null;







        // ==========================================
        // 🌟 新增：触发广播的方法 (MarkAsModified)
        // ==========================================
        public void MarkAsModified()
        {
            // 只要任何地方调用了这个方法，就会向全宇宙广播：“数据被修改啦！”
            // 问号 ? 是安全检查，意思是“如果有人订阅了这个广播，才去呼叫他们”
            OnDataModified?.Invoke();
        }
    }
}