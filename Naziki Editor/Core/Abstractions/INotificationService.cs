using System;
using System.Threading.Tasks;

namespace Naziki_Editor.Core.Abstractions
{
    /// <summary>
    /// 通知类型枚举，用于区分通知的颜色标识和行为。
    /// </summary>
    public enum NotificationType
    {
        /// <summary>成功信息（绿色）</summary>
        Success,
        /// <summary>警告信息（橙色）</summary>
        Warning,
        /// <summary>错误信息（红色）</summary>
        Error,
        /// <summary>普通信息（蓝色）</summary>
        Info
    }

    /// <summary>
    /// 通知服务接口，提供统一的跨层通知调用能力。
    /// Core/ViewModel 层通过此接口发送通知，不依赖具体的 UI 实现。
    /// </summary>
    public interface INotificationService
    {
        /// <summary>
        /// 显示一条通知消息。
        /// </summary>
        /// <param name="message">通知文本内容</param>
        /// <param name="type">通知类型，默认 Info</param>
        /// <param name="durationMs">自动消失时长（毫秒），默认 3000ms。设为 0 表示不自动消失</param>
        void Show(string message, NotificationType type = NotificationType.Info, int durationMs = 3000);

        /// <summary>
        /// 显示一条成功通知（便捷方法）。
        /// </summary>
        void ShowSuccess(string message, int durationMs = 3000);

        /// <summary>
        /// 显示一条警告通知（便捷方法）。
        /// </summary>
        void ShowWarning(string message, int durationMs = 4000);

        /// <summary>
        /// 显示一条错误通知（便捷方法）。
        /// </summary>
        void ShowError(string message, int durationMs = 5000);

        /// <summary>
        /// 异步显示通知，返回 Task 在通知关闭时完成。
        /// </summary>
        Task ShowAsync(string message, NotificationType type = NotificationType.Info, int durationMs = 3000);

        /// <summary>
        /// 关闭所有当前显示的通知。
        /// </summary>
        void DismissAll();
    }
}