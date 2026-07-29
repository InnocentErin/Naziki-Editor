using System;
using Naziki_Editor.Models;
using Naziki_Editor.Core;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Messaging;

namespace Naziki_Editor.State
{
    public class ProjectDataContext
    {
        private readonly IMessageBroker _messageBroker;

        public string ProjectFilePath { get; set; }
        public NazikiProjectModel ProjectData { get; set; }

        public string StoryboardPath { get; set; }
        public EditorStoryboardDocument EditorStoryboard { get; set; } = new();
        public string StoryboardSourcePath { get; set; }
        public string LegacyStoryboardProjectionHash { get; set; }
        // 🌟 现在的 StoryboardRoot 肚子里装的全是 C2 包装盒啦！
        [Obsolete("Use EditorStoryboard for new code. This is a compatibility projection.")]
        public StoryboardRoot Storyboard { get; set; } = new StoryboardRoot();
        // 📒【新点亮的科技树】：元数据小账本专属内存房间！
        public StoryboardMeta StoryboardMeta { get; set; } = new StoryboardMeta();

        public C2Chart Chart { get; set; }
        public ChartTimeEngine TimeEngine { get; set; }

        public bool HasStoryboard => !EditorStoryboard.IsEmpty || Storyboard != null;
        public bool HasChart => Chart != null;

        public ProjectDataContext(IMessageBroker messageBroker)
        {
            _messageBroker = messageBroker;
        }

        public void MarkAsModified()
        {
            _messageBroker.Publish("DataModified");
            if (ProjectData != null)
            {
                ProjectData.LastModifiedTime = DateTime.Now;
            }
        }
    }
}
