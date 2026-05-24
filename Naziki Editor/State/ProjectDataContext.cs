using System;
using Naziki_Editor.Models;
using Naziki_Editor.Core;

namespace Naziki_Editor.State
{
    public class ProjectDataContext
    {
        public event Action OnDataModified;

        public string ProjectFilePath { get; set; }
        public NazikiProjectModel ProjectData { get; set; }

        public string StoryboardPath { get; set; }
        // 🌟 现在的 StoryboardRoot 肚子里装的全是 C2 包装盒啦！
        public StoryboardRoot Storyboard { get; set; } = new StoryboardRoot();

        public C2Chart Chart { get; set; }
        public ChartTimeEngine TimeEngine { get; set; }

        public bool HasStoryboard => Storyboard != null;
        public bool HasChart => Chart != null;

        public void MarkAsModified()
        {
            OnDataModified?.Invoke();
            if (ProjectData != null)
            {
                ProjectData.LastModifiedTime = DateTime.Now;
            }
        }
    }
}