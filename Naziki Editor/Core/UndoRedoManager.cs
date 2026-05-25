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

            // 将对象化为纯粹的 JSON 字符串，彻底斩断引用纠葛！
            string jsonSnapshot = JsonConvert.SerializeObject(currentState);

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

            // 把当前状态压入重做栈
            _redoStack.Add(JsonConvert.SerializeObject(currentState));

            // 提取上一个状态的 JSON 并移除
            string previousStateJson = _undoStack[_undoStack.Count - 1];
            _undoStack.RemoveAt(_undoStack.Count - 1);

            success = true;
            // 魔法：自动把 JSON 变回设计师想要的类型（比如 StoryboardRoot）
            return JsonConvert.DeserializeObject<T>(previousStateJson);
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

            // 把当前状态压入撤销栈
            _undoStack.Add(JsonConvert.SerializeObject(currentState));

            // 提取下一个状态的 JSON 并移除
            string nextStateJson = _redoStack[_redoStack.Count - 1];
            _redoStack.RemoveAt(_redoStack.Count - 1);

            success = true;
            return JsonConvert.DeserializeObject<T>(nextStateJson);
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