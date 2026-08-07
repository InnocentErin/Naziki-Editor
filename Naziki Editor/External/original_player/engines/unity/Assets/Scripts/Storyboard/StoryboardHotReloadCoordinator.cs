#if CYTOID_EDITOR_HOST
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Cytoid.Storyboard
{
    /// <summary>
    /// Validates and initializes a complete candidate tree before atomically replacing
    /// the live storyboard. The change list is retained only for protocol diagnostics.
    /// </summary>
    public static class StoryboardHotReloadCoordinator
    {
        public static async UniTask Apply(
            Game game,
            string candidateJson,
            JArray changes,
            string assetRoot = null)
        {
            await game.PreviewReplaceStoryboard(candidateJson, assetRoot);
        }
    }
}
#endif
