using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.ErrorHandling;

namespace NazikiEditor.Tests;

/// <summary>
/// 测试用空错误处理器，所有操作静默吞掉异常
/// </summary>
public class NullErrorHandler : IErrorHandler
{
    public void HandleError(ErrorInfo errorInfo) { }

    public void HandleException(
        Exception ex,
        ErrorSeverity severity,
        string errorType,
        string description,
        string location,
        string? contextData = null) { }

    public bool TryExecute(
        Action action,
        string errorType,
        string location,
        string? contextData = null)
    {
        try { action(); return true; }
        catch { return false; }
    }

    public T? TryExecute<T>(
        Func<T> func,
        string errorType,
        string location,
        string? contextData = null)
    {
        try { return func(); }
        catch { return default; }
    }
}