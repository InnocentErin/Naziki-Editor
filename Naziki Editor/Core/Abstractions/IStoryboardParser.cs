using Naziki_Editor.Models;

namespace Naziki_Editor.Core.Abstractions
{
    public interface IStoryboardParser
    {
        void StandardizeStoryboardIds(StoryboardRoot root, NazikiProjectModel project);
        void SyncControlBoardIdMaps(StoryboardRoot root, NazikiProjectModel project);
    }
}