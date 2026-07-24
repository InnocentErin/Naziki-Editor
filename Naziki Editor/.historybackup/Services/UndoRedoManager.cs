using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Naziki_Editor.Core
{
    public class UndoRedoManager
    {
        // 🌍 唯一的主宇宙时光机（供主窗口使用）
        public static UndoRedoManager Global { get; } = new UndoRedoManager();

        // 内部使用 List 模拟栈，方便剔除最旧的记录以控制内存
        private List<string> _undoStack = new List<string>();
        private List<string> _redoStack = new List<string>();

        // 记忆容量限制（弹窗里随便调，最多记 50 步）
        public int MaxCapacity { get; set; } = 50;

        /// <summary>
        /// 📸 拍下当前快照并压入时光机
        /// </summary>
        public void RecordSnapshot(object currentState)
        {
            if (currentState == null) return;

            // ✨ 核心修正：使用大本营定制的 GetSettings()，强行粉碎 [JsonIgnore] 带来的时空遗忘信息，完整保留核心资产！
            string jsonSnapshot = JsonConvert.SerializeObject(currentState, StoryboardSerializer.GetSettings());

            // 如果和上一步一模一样，就不记录（防止无意义的重复存档）
            if (_undoStack.Count > 0 && _undoStack[_undoStack.Count - 1] == jsonSnapshot)
                return;

            _undoStack.Add(jsonSnapshot);
            _redoStack.Clear(); // 产生了新的世界线，未来的重做记录必须抹除

            // 超过记忆上限，遗忘最古老的记忆
            if (_undoStack.Count > MaxCapacity)
            {
                _undoStack.RemoveAt(0);
            }
        }

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        /// <summary>
        /// ⏪ 撤销 (Ctrl+Z)
        /// </summary>
        public T Undo<T>(T currentState, out bool success) where T : class
        {
            if (!CanUndo)
            {
                success = false;
                return null;
            }

            // ✨ 核心修正：重做压栈也要携带高级时空透视镜
            _redoStack.Add(JsonConvert.SerializeObject(currentState, StoryboardSerializer.GetSettings()));

            // 提取上一个状态的 JSON 并移除
            string previousStateJson = _undoStack[_undoStack.Count - 1];
            _undoStack.RemoveAt(_undoStack.Count - 1);

            success = true;

            // ✨ 核心修正：使用高级反序列化配置，完美复活 BaseState 和每一帧动画！
            return JsonConvert.DeserializeObject<T>(previousStateJson, StoryboardSerializer.GetSettings());
        }

        /// <summary>
        /// ⏩ 重做 (Ctrl+Y)
        /// </summary>
        public T Redo<T>(T currentState, out bool success) where T : class
        {
            if (!CanRedo)
            {
                success = false;
                return null;
            }

            // ✨ 核心修正：撤销压栈同步携带高级配置
            _undoStack.Add(JsonConvert.SerializeObject(currentState, StoryboardSerializer.GetSettings()));

            // 提取下一个状态的 JSON 并移除
            string nextStateJson = _redoStack[_redoStack.Count - 1];
            _redoStack.RemoveAt(_redoStack.Count - 1);

            success = true;

            // ✨ 核心修正：完美复活未来世界线的全量数据
            return JsonConvert.DeserializeObject<T>(nextStateJson, StoryboardSerializer.GetSettings());
        }

        /// <summary>
        /// 🧹 清空时光机 (比如新建工程、关闭弹窗时调用)
        /// </summary>
        public void Reset()
        {
            _undoStack.Clear();
            _redoStack.Clear();
        }
    }
}