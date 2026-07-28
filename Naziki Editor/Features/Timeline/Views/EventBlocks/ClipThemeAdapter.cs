using Naziki_Editor.Models;
using System.Windows.Controls;

namespace Naziki_Editor.Views.EventBlocks
{
    /// <summary>
    /// Adapts the clip control's visual theme based on the associated entity type.
    /// </summary>
    public static class ClipThemeAdapter
    {
        /// <summary>
        /// Determines the base resource key for the given entity type.
        /// </summary>
        public static string GetResourceKey(IStoryboardEntity entity)
        {
            if (entity == null) return "TextClip";

            return entity.GetType().Name switch
            {
                "C2Sprite" => "SpriteClip",
                "C2Text" => "TextClip",
                "C2Video" => "VideoClip",
                "C2Line" => "LineClip",
                "C2SceneController" => "ControllerClip",
                "C2NoteController" => "NoteControllerClip",
                _ => "TextClip"
            };
        }

        /// <summary>
        /// Applies dynamic theme resources to the clip's background border.
        /// </summary>
        public static void ApplyTheme(Border clipBackground, IStoryboardEntity entity)
        {
            if (clipBackground == null) return;
            string key = GetResourceKey(entity);
            clipBackground.SetResourceReference(Border.BackgroundProperty, $"{key}BgBrush");
            clipBackground.SetResourceReference(Border.BorderBrushProperty, $"{key}BorderBrush");
        }
    }
}
