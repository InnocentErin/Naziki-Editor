using System;

namespace Naziki_Editor.Core.ErrorHandling
{
    /// <summary>
    /// 标准化错误信息模型，包含错误类型、描述、位置、时间戳等关键信息
    /// </summary>
    public class ErrorInfo
    {
        /// <summary>错误唯一标识</summary>
        public string ErrorId { get; }

        /// <summary>错误严重级别</summary>
        public ErrorSeverity Severity { get; }

        /// <summary>错误类型分类（如 "FileIO", "DataValidation", "Network" 等）</summary>
        public string ErrorType { get; }

        /// <summary>错误描述</summary>
        public string Description { get; }

        /// <summary>发生位置（类名.方法名）</summary>
        public string Location { get; }

        /// <summary>发生时间戳</summary>
        public DateTime Timestamp { get; }

        /// <summary>原始异常（如有）</summary>
        public Exception? OriginalException { get; }

        /// <summary>附带上下文数据（如文件路径、参数值等）</summary>
        public string? ContextData { get; }

        public ErrorInfo(
            ErrorSeverity severity,
            string errorType,
            string description,
            string location,
            Exception? originalException = null,
            string? contextData = null)
        {
            ErrorId = Guid.NewGuid().ToString("N")[..8];
            Severity = severity;
            ErrorType = errorType;
            Description = description;
            Location = location;
            Timestamp = DateTime.Now;
            OriginalException = originalException;
            ContextData = contextData;
        }

        /// <summary>
        /// 格式化输出完整的错误信息
        /// </summary>
        public override string ToString()
        {
            var msg = $"[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Severity}] [{ErrorType}] [{ErrorId}] {Location}\n" +
                      $"  Description: {Description}";
            if (!string.IsNullOrEmpty(ContextData))
                msg += $"\n  Context: {ContextData}";
            if (OriginalException != null)
                msg += $"\n  Exception: {OriginalException.GetType().Name}: {OriginalException.Message}";
            return msg;
        }
    }
}