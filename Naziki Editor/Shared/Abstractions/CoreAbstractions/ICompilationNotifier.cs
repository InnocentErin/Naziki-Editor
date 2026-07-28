namespace Naziki_Editor.Core.Abstractions
{
    /// <summary>
    /// 编译过程通知接口，用于替代 Core 层直接调用 MessageBox。
    /// </summary>
    public interface ICompilationNotifier
    {
        void NotifyInfo(string title, string message);
        void NotifyWarning(string title, string message);
    }
}
