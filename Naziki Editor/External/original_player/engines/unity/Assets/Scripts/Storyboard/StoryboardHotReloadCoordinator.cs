#if CYTOID_EDITOR_HOST
using System;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Cytoid.Storyboard
{
    /// <summary>
    /// Applies independent visual entity changes without reloading the Game scene.
    /// Templates, triggers and dependency changes deliberately use the atomic full-tree
    /// replacement path because they cannot be swapped safely one renderer at a time.
    /// </summary>
    public static class StoryboardHotReloadCoordinator
    {
        public static async UniTask Apply(Game game, string candidateJson, JArray changes)
        {
            if (changes == null || changes.Count == 0 || changes.Count > 32 ||
                RequiresAtomicRebuild(changes))
            {
                await game.PreviewReplaceStoryboard(candidateJson);
                return;
            }

            var live = game.Storyboard;
            var candidate = new Storyboard(game, candidateJson);
            candidate.Parse();
            try
            {
                foreach (var token in changes)
                {
                    var change = token as JObject;
                    var id = change?["EntityId"]?.Value<string>() ?? change?["entityId"]?.Value<string>();
                    if (string.IsNullOrWhiteSpace(id))
                        throw new InvalidOperationException("Hot reload change is missing entityId.");
                    live.Renderer.DestroyObjectsById(id);
                    RemoveEntity(live, id);

                    var operation = change?["Operation"]?.Value<string>() ??
                                    change?["operation"]?.Value<string>() ?? "Update";
                    if (string.Equals(operation, "Delete", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!CopyEntity(candidate, live, id))
                        throw new InvalidOperationException($"Candidate entity '{id}' was not found.");
                    await live.Renderer.SpawnObjectByIdAsync(id);
                }
                UnitFloat.Storyboard = live;
            }
            catch
            {
                UnitFloat.Storyboard = live;
                await game.PreviewReplaceStoryboard(live.RootObject.ToString());
                throw;
            }
        }

        static bool RequiresAtomicRebuild(JArray changes)
        {
            foreach (var token in changes)
            {
                var change = token as JObject;
                var type = change?["EntityType"]?.Value<string>() ??
                           change?["entityType"]?.Value<string>();
                if (string.Equals(type, "Trigger", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(type, "Template", StringComparison.OrdinalIgnoreCase))
                    return true;
                var dependencies = change?["DependencyIds"] as JArray ??
                                   change?["dependencyIds"] as JArray;
                if (dependencies?.Count > 0) return true;
            }
            return false;
        }

        static void RemoveEntity(Storyboard storyboard, string id)
        {
            storyboard.Texts.Remove(id);
            storyboard.Sprites.Remove(id);
            storyboard.Lines.Remove(id);
            storyboard.Videos.Remove(id);
            storyboard.Controllers.Remove(id);
            storyboard.NoteControllers.Remove(id);
        }

        static bool CopyEntity(Storyboard source, Storyboard destination, string id)
        {
            if (source.Texts.TryGetValue(id, out var text)) destination.Texts[id] = text;
            else if (source.Sprites.TryGetValue(id, out var sprite)) destination.Sprites[id] = sprite;
            else if (source.Lines.TryGetValue(id, out var line)) destination.Lines[id] = line;
            else if (source.Videos.TryGetValue(id, out var video)) destination.Videos[id] = video;
            else if (source.Controllers.TryGetValue(id, out var controller)) destination.Controllers[id] = controller;
            else if (source.NoteControllers.TryGetValue(id, out var noteController)) destination.NoteControllers[id] = noteController;
            else return false;
            return true;
        }
    }
}
#endif
