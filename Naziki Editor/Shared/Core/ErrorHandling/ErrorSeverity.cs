namespace Naziki_Editor.Core.ErrorHandling
{
    /// <summary>
    /// 错误严重级别枚举
    /// </summary>
    public enum ErrorSeverity
    {
        /// <summary>调试信息，仅开发环境关注</summary>
        Debug,

        /// <summary>一般信息，不影响正常运行</summary>
        Info,

        /// <summary>警告，可能存在潜在问题但当前可继续运行</summary>
        Warning,

        /// <summary>错误，当前操作失败但应用程序可继续运行</summary>
        Error,

        /// <summary>严重错误，可能导致数据丢失或应用程序不稳定</summary>
        Critical,

        /// <summary>致命错误，应用程序无法继续运行</summary>
        Fatal
    }
}