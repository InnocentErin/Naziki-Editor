using System;
using System.Collections.Generic;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.ErrorHandling;
using Newtonsoft.Json;

namespace Naziki_Editor.Core.History
{
    /// <summary>
    /// 历史记录服务实现，基于 JSON 快照提供撤销/重做能力。
    /// </summary>
    public class HistoryService : IHistoryService
    {
        // 内部使用 List 模拟栈，方便剔除最旧的记录以控制内存
        private readonly List<string> _undoStack = new List<string>();
        private readonly List<string> _redoStack = new List<string>();
        private readonly IErrorHandler _errorHandler;

        public HistoryService(IErrorHandler errorHandler)
        {
            _errorHandler = errorHandler;
        }

        /// <inheritdoc />
        public int MaxCapacity { get; set; } = 50;

        /// <inheritdoc />
        public bool CanUndo => _undoStack.Count > 0;

        /// <inheritdoc />
        public bool CanRedo => _redoStack.Count > 0;

        /// <inheritdoc />
        public void RecordSnapshot(object currentState)
        {
            if (currentState == null) return;

            _errorHandler.TryExecute(() =>
            {
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
            }, "Serialization", "HistoryService.RecordSnapshot");
        }

        /// <inheritdoc />
        public T Undo<T>(T currentState, out bool success) where T : class
        {
            success = false;
            if (!CanUndo) return null;

            try
            {
                _redoStack.Add(JsonConvert.SerializeObject(currentState, StoryboardSerializer.GetSettings()));

                string previousStateJson = _undoStack[_undoStack.Count - 1];
                _undoStack.RemoveAt(_undoStack.Count - 1);

                success = true;
                return JsonConvert.DeserializeObject<T>(previousStateJson, StoryboardSerializer.GetSettings());
            }
            catch (Exception ex)
            {
                _errorHandler.HandleException(ex, ErrorSeverity.Error, "Serialization",
                    "撤销操作时 JSON 反序列化失败", "HistoryService.Undo");
                return null;
            }
        }

        /// <inheritdoc />
        public T Redo<T>(T currentState, out bool success) where T : class
        {
            success = false;
            if (!CanRedo) return null;

            try
            {
                _undoStack.Add(JsonConvert.SerializeObject(currentState, StoryboardSerializer.GetSettings()));

                string nextStateJson = _redoStack[_redoStack.Count - 1];
                _redoStack.RemoveAt(_redoStack.Count - 1);

                success = true;
                return JsonConvert.DeserializeObject<T>(nextStateJson, StoryboardSerializer.GetSettings());
            }
            catch (Exception ex)
            {
                _errorHandler.HandleException(ex, ErrorSeverity.Error, "Serialization",
                    "重做操作时 JSON 反序列化失败", "HistoryService.Redo");
                return null;
            }
        }

        /// <inheritdoc />
        public void Reset()
        {
            _undoStack.Clear();
            _redoStack.Clear();
        }
    }
}
