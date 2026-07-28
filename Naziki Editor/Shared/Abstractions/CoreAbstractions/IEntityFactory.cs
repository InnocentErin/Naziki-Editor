using Naziki_Editor.Models;

namespace Naziki_Editor.Core.Abstractions
{
    /// <summary>
    /// 故事板实体工厂抽象，负责创建各类初始化的场景实体与控制器。
    /// </summary>
    public interface IEntityFactory
    {
        C2Sprite CreateSpriteFromAsset(string fileName);
        C2Video CreateVideoFromAsset(string fileName);
        C2Text CreateText();
        C2Line CreateLine();
        C2SceneController CreateSceneController();
        C2NoteController CreateNoteController(C2Note note);
        C2Template CreateTemplate(string baseName);
        string GenerateUniqueTemplateKey(StoryboardRoot root, string baseName);

        /// <summary>
        /// 创建空白故事板根对象，包含默认场景控制器。
        /// 用于从零开始创建新故事板。
        /// </summary>
        StoryboardRoot CreateEmptyStoryboard();
    }
}
