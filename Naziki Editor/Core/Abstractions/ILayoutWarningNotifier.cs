namespace Naziki_Editor.Core.Abstractions
{
    /// <summary>
    /// 时间轴布局警告通知接口，用于替代 Core 层直接调用 MessageBox。
    /// </summary>
    public interface ILayoutWarningNotifier
    {
        void WarnTooManyOverlappingObjects(int overlappingCount);
    }
}
