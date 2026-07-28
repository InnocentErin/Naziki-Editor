using System;
using Naziki_Editor.Core.ErrorHandling;

namespace Naziki_Editor.Core.Abstractions
{
    /// <summary>
    /// 统一错误处理接口，提供"错误捕获-标准化-抛出"的完整处理流程
    /// </summary>
    public interface IErrorHandler
    {
        /// <summary>
        /// 处理并记录错误信息
        /// </summary>
        /// <param name="errorInfo">标准化的错误信息</param>
        void HandleError(ErrorInfo errorInfo);

        /// <summary>
        /// 快捷方法：捕获异常并创建标准化错误信息后处理
        /// </summary>
        /// <param name="ex">原始异常</param>
        /// <param name="severity">严重级别</param>
        /// <param name="errorType">错误类型</param>
        /// <param name="description">错误描述</param>
        /// <param name="location">位置（类名.方法名）</param>
        /// <param name="contextData">上下文数据</param>
        void HandleException(
            Exception ex,
            ErrorSeverity severity,
            string errorType,
            string description,
            string location,
            string? contextData = null);

        /// <summary>
        /// 安全执行操作，自动捕获异常并通过错误处理器处理
        /// </summary>
        /// <param name="action">要执行的操作</param>
        /// <param name="errorType">错误类型</param>
        /// <param name="location">位置</param>
        /// <param name="contextData">上下文数据</param>
        /// <returns>是否执行成功（无异常抛出）</returns>
        bool TryExecute(
            Action action,
            string errorType,
            string location,
            string? contextData = null);

        /// <summary>
        /// 安全执行带返回值的操作，自动捕获异常
        /// </summary>
        /// <returns>操作结果，失败时返回 default</returns>
        T? TryExecute<T>(
            Func<T> func,
            string errorType,
            string location,
            string? contextData = null);
    }
}