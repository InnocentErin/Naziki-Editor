using System;
using System.Collections.Generic;
using System.Reflection;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Models;

namespace Naziki_Editor.Core.Storyboard
{
    /// <summary>
    /// 故事板实体工厂，集中管理各类场景实体与控制器的创建逻辑。
    /// </summary>
    public class EntityFactory : IEntityFactory
    {
        public C2Sprite CreateSpriteFromAsset(string fileName)
        {
            var sprite = new C2Sprite
            {
                Id = GenerateTempId("image")
            };
            sprite.BaseState.Path = fileName;
            sprite.BaseState.X = new UnitFloat { Value = 0, Unit = ReferenceUnit.World };
            sprite.BaseState.Y = new UnitFloat { Value = 0, Unit = ReferenceUnit.World };
            return sprite;
        }

        public C2Video CreateVideoFromAsset(string fileName)
        {
            var video = new C2Video
            {
                Id = GenerateTempId("video")
            };
            video.BaseState.Path = fileName;
            return video;
        }

        public C2Text CreateText()
        {
            var text = new C2Text
            {
                Id = GenerateTempId("text")
            };
            text.BaseState.TextContent = "默认文本";
            text.BaseState.Size = 30f;
            text.BaseState.Color = "#FFFFFF";
            text.BaseState.X = new UnitFloat { Value = 0, Unit = ReferenceUnit.World };
            text.BaseState.Y = new UnitFloat { Value = 0, Unit = ReferenceUnit.World };
            return text;
        }

        public C2Line CreateLine()
        {
            var line = new C2Line
            {
                Id = GenerateTempId("line")
            };
            line.BaseState.Width = new UnitFloat
            {
                Value = 2.0f,
                Unit = ReferenceUnit.World
            };
            line.BaseState.Color = "#FFFFFF";
            line.BaseState.Pos = new List<LinePosition>
            {
                new LinePosition { X = new UnitFloat { Value = -100, Unit = ReferenceUnit.World }, Y = new UnitFloat { Value = 0, Unit = ReferenceUnit.World } },
                new LinePosition { X = new UnitFloat { Value = 100, Unit = ReferenceUnit.World }, Y = new UnitFloat { Value = 0, Unit = ReferenceUnit.World } }
            };
            return line;
        }

        public C2SceneController CreateSceneController()
        {
            var controller = new C2SceneController
            {
                Id = GenerateTempId("scene")
            };
            controller.BaseState.StoryboardOpacity = 1.0f;
            controller.BaseState.UiOpacity = 1.0f;
            controller.BaseState.BackgroundDim = 0.85f;
            controller.BaseState.ScanlineOpacity = 1.0f;
            return controller;
        }

        public C2NoteController CreateNoteController(C2Note note)
        {
            if (note == null) throw new ArgumentNullException(nameof(note));

            var noteCtrl = new C2NoteController
            {
                Id = $"note_ctrl_{note.id}_{DateTime.Now.Ticks}"
            };

            var baseState = noteCtrl.GetType().GetProperty("BaseState")?.GetValue(noteCtrl);
            if (baseState != null)
            {
                var noteProp = baseState.GetType().GetProperty("Note");
                if (noteProp != null)
                {
                    if (noteProp.PropertyType == typeof(string))
                        noteProp.SetValue(baseState, note.id.ToString());
                    else if (noteProp.PropertyType == typeof(int))
                        noteProp.SetValue(baseState, note.id);
                    else
                        noteProp.SetValue(baseState, note.id);
                }
            }

            return noteCtrl;
        }

        public C2Template CreateTemplate(string baseName)
        {
            return new C2Template();
        }

        public string GenerateUniqueTemplateKey(StoryboardRoot root, string baseName)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (string.IsNullOrEmpty(baseName)) baseName = "generic";

            if (root.templates == null)
                root.templates = new Dictionary<string, C2Template>();

            string newKey = $"{baseName}_{Guid.NewGuid().ToString().Substring(0, 4)}";
            while (root.templates.ContainsKey(newKey))
            {
                newKey = $"{baseName}_{Guid.NewGuid().ToString().Substring(0, 5)}";
            }
            return newKey;
        }

        /// <summary>
        /// 创建空白故事板根对象，包含一个默认场景控制器。
        /// 默认场景控制器启用透视投影（Perspective=true, Fov=53.2）。
        /// </summary>
        public StoryboardRoot CreateEmptyStoryboard()
        {
            var root = new StoryboardRoot();
            
            var defaultController = CreateSceneController();
            defaultController.Id = "scene_default";
            defaultController.BaseState.Perspective = true;
            defaultController.BaseState.Fov = 53.2f;
            
            root.controllers.Add(defaultController);
            return root;
        }

        private static string GenerateTempId(string prefix)
        {
            return $"{prefix}_{DateTime.Now.Ticks}";
        }
    }
}
