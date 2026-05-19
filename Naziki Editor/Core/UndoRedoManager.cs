using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Naziki_Editor.Core
{
    // 🌟 泛型时光管理器：可以倒流任何纯数据对象的状态！
    public class UndoRedoManager<T>
    {
        private readonly Stack<string> _undoStack = new Stack<string>();
        private readonly Stack<string> _redoStack = new Stack<string>();
        private readonly int _maxHistory;

        public UndoRedoManager(int maxHistory = 30)
        {
            _maxHistory = maxHistory; // 默认最多记录 30 步时光痕迹
        }

        // ==========================================
        // 📸 拍照存证：在进行任何修改前，把当前的完美状态拍个快照
        // ==========================================
        public void RecordSnapshot(T state)
        {
            if (state == null) return;

            // 将当前状态深度序列化为一条独立的物理快照文本
            string jsonSnapshot = JsonConvert.SerializeObject(state);

            // 防呆优化：如果当前快照和上一动一模一样，就不重复记录，省内存！
            if (_undoStack.Count > 0 && _undoStack.Peek() == jsonSnapshot) return;

            _undoStack.Push(jsonSnapshot);
            _redoStack.Clear(); // 一旦用户在撤回后干了新坏事，重做堆栈立刻清空！

            // 满了就扔掉最古老的记忆
            if (_undoStack.Count > _maxHistory)
            {
                // 简单维护，防止内存无限爆满
                var list = new List<string>(_undoStack);
                list.RemoveAt(list.Count - 1);
                _undoStack.Clear();
                for (int i = list.Count - 1; i >= 0; i--) _undoStack.Push(list[i]);
            }
        }

        // ==========================================
        // ⏪ 时光倒流 (Undo)
        // ==========================================
        public T Undo(T currentState, out bool success)
        {
            success = false;
            // 至少要留一个底稿状态，或者当前堆栈还有之前踩过的脚印
            if (_undoStack.Count <= 1) return currentState;

            // 1. 把当前的现状扔进重做池里备用
            string currentJson = JsonConvert.SerializeObject(currentState);
            _redoStack.Push(currentJson);

            // 2. 扔掉当前的副本，拔出上一步留下的远古快照
            _undoStack.Pop();
            string previousJson = _undoStack.Peek();

            success = true;
            return JsonConvert.DeserializeObject<T>(previousJson);
        }

        // ==========================================
        // ⏩ 时光快进 (Redo)
        // ==========================================
        public T Redo(T currentState, out bool success)
        {
            success = false;
            if (_redoStack.Count == 0) return currentState;

            // 1. 把现状压入撤回池
            string currentJson = JsonConvert.SerializeObject(currentState);
            _undoStack.Push(currentJson);

            // 2. 从重做池里把未来的记忆捞出来
            string nextJson = _redoStack.Pop();

            success = true;
            return JsonConvert.DeserializeObject<T>(nextJson);
        }

        // 🧹 洗脑法术：切换谱面或清空项目时调用
        public void Reset()
        {
            _undoStack.Clear();
            _redoStack.Clear();
        }
    }
}