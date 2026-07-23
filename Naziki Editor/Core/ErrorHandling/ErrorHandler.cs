using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using Naziki_Editor.Core.Abstractions;

namespace Naziki_Editor.Core.ErrorHandling
{
    /// <summary>
    /// 统一错误处理器实现，负责错误的标准化记录、日志输出和消息广播
    /// </summary>
    public class ErrorHandler : IErrorHandler
    {
        private readonly IMessageBroker _messageBroker;
        private readonly ConcurrentQueue<ErrorInfo> _errorLog = new ConcurrentQueue<ErrorInfo>();
        private const int MaxErrorLogSize = 1000;

        /// <summary>错误发生时的回调事件（供外部监控，如崩溃日志写入）</summary>
        public event Action<ErrorInfo>? OnErrorOccurred;

        /// <summary>获取已记录的错误列表快照</summary>
        public IReadOnlyList<ErrorInfo> GetErrorLog() => _errorLog.ToArray();

        public ErrorHandler(IMessageBroker messageBroker)
        {
            _messageBroker = messageBroker;
        }

        public void HandleError(ErrorInfo errorInfo)
        {
            if (errorInfo == null) return;

            // 1. 记录到内存日志队列
            _errorLog.Enqueue(errorInfo);
            while (_errorLog.Count > MaxErrorLogSize)
                _errorLog.TryDequeue(out _);

            // 2. 输出到调试控制台
            Debug.WriteLine(errorInfo.ToString());

            // 3. 触发外部回调事件
            OnErrorOccurred?.Invoke(errorInfo);

            // 4. 根据严重级别广播消息
            var topic = errorInfo.Severity switch
            {
                ErrorSeverity.Critical or ErrorSeverity.Fatal => "Error.Critical",
                ErrorSeverity.Error => "Error.Occurred",
                ErrorSeverity.Warning => "Error.Warning",
                _ => "Error.Info"
            };
            _messageBroker.Publish(topic, errorInfo);
        }

        public void HandleException(
            Exception ex,
            ErrorSeverity severity,
            string errorType,
            string description,
            string location,
            string? contextData = null)
        {
            var errorInfo = new ErrorInfo(
                severity,
                errorType,
                description,
                location,
                ex,
                contextData);
            HandleError(errorInfo);
        }

        public bool TryExecute(
            Action action,
            string errorType,
            string location,
            string? contextData = null)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception ex)
            {
                HandleException(ex, ErrorSeverity.Error, errorType,
                    $"操作执行失败: {ex.Message}", location, contextData);
                return false;
            }
        }

        public T? TryExecute<T>(
            Func<T> func,
            string errorType,
            string location,
            string? contextData = null)
        {
            try
            {
                return func();
            }
            catch (Exception ex)
            {
                HandleException(ex, ErrorSeverity.Error, errorType,
                    $"操作执行失败: {ex.Message}", location, contextData);
                return default;
            }
        }
    }
}