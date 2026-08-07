using System;
using System.IO;
using Cytoid.Storyboard.Notes;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Cytoid.Storyboard.Sprites
{
    public class NoteControllerRenderer : StoryboardComponentRenderer<NoteController, NoteControllerState>
    {

        public ChartModel.Note Note { get; private set; }

        public override Transform Transform => notePlaceholderTransform;

        public override bool IsOnCanvas => false;

        private Transform notePlaceholderTransform;
        private GameObject noteGameObject;

        public NoteControllerRenderer(StoryboardRenderer mainRenderer, NoteController component) : base(mainRenderer, component)
        {
        }

        public override StoryboardRendererEaser<NoteControllerState> CreateEaser() => new NoteControllerEaser(this);

        public override async UniTask Initialize()
        {
            if (Component.ParentId != null) throw new InvalidOperationException($"Storyboard: NoteController {Component.Id} cannot have a parent");

            if (Component.States == null || Component.States.Count == 0)
                throw CreateBindingException(
                    "PREVIEW_STORYBOARD_NOTE_CONTROLLER_STATES_MISSING",
                    "states",
                    $"Storyboard NoteController '{Component.Id}' has no root state.");

            var note = Component.States[0].Note;
            if (note == null)
                throw CreateBindingException(
                    "PREVIEW_STORYBOARD_NOTE_BINDING_MISSING",
                    "note",
                    $"Storyboard NoteController '{Component.Id}' has no root-level note binding.");

            if (!MainRenderer.Game.Chart.Model.note_map.ContainsKey(note.Value))
                throw CreateBindingException(
                    "PREVIEW_STORYBOARD_NOTE_MISSING",
                    "note",
                    $"Storyboard NoteController '{Component.Id}' references chart note '{note.Value}', which does not exist.",
                    note.Value);

            Note = MainRenderer.Game.Chart.Model.note_map[note.Value];

            // TODO: Optimize this? Don't generate transforms if not in use
            notePlaceholderTransform = new GameObject("NoteControllerPlaceholder_" + Note.id).transform;
            Clear();
        }

        private InvalidDataException CreateBindingException(string code, string property, string message, int? noteId = null)
        {
            var storyboardPath = MainRenderer.Game.StoryboardPath ??
                                 Path.Combine(MainRenderer.Storyboard.AssetRoot,
                                     "storyboard.json");
            var dataPath = $"$.note_controllers[id='{Component.Id}'].{property}";
            var exception = new InvalidDataException($"{code}: {message} Path: {dataPath}; storyboard: {storyboardPath}");
            exception.Data["code"] = code;
            exception.Data["source"] = "storyboard";
            exception.Data["stage"] = "initialize";
            exception.Data["path"] = dataPath;
            exception.Data["entityId"] = Component.Id?.ToString() ?? string.Empty;
            exception.Data["storyboardPath"] = storyboardPath;
            if (noteId.HasValue) exception.Data["noteId"] = noteId.Value;
            return exception;
        }

        public override void Clear()
        {
            notePlaceholderTransform.localPosition = Vector3.zero;
        }

        public override void Dispose()
        {
            if (notePlaceholderTransform != null)
            {
                UnityEngine.Object.Destroy(notePlaceholderTransform.gameObject);
            }
            notePlaceholderTransform = null;
            noteGameObject = null;
        }

        public override void Update(NoteControllerState fromState, NoteControllerState toState)
        {
            base.Update(fromState, toState);
            if (noteGameObject == null && MainRenderer.Game.SpawnedNotes.ContainsKey(Note.id))
            {
                noteGameObject = MainRenderer.Game.SpawnedNotes[Note.id].gameObject;
            }
            if (noteGameObject != null && !MainRenderer.Game.SpawnedNotes.ContainsKey(Note.id))
            {
                noteGameObject = null;
            }
            if (noteGameObject == null)
            {
                notePlaceholderTransform.position = Vector3.zero;
                return;
            }
            notePlaceholderTransform.position = noteGameObject.transform.position;
        }

    }
}
