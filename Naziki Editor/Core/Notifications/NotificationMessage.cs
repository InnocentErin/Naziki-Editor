using System;

namespace Naziki_Editor.Core.Notifications
{
    /// <summary>
    /// 通知消息数据模型，封装一条通知的完整信息。
    /// </summary>
    public class NotificationMessage
    {
        /// <summary>通知唯一标识符</summary>
        public string Id { get; } = Guid.NewGuid().ToString("N")[..8];

        /// <summary>通知文本内容</summary>
        public string Message { get; init; } = string.Empty;

        /// <summary>通知类型</summary>
        public Abstractions.NotificationType Type { get; init; } = Abstractions.NotificationType.Info;

        /// <summary>自动消失时长（毫秒），0 表示不自动消失</summary>
        public int DurationMs { get; init; } = 3000;

        /// <summary>创建时间</summary>
        public DateTime CreatedAt { get; init; } = DateTime.Now;

        /// <summary>通知是否已被关闭</summary>
        public bool IsDismissed { get; set; }

        /// <summary>
        /// 获取通知类型对应的图标字符。
        /// </summary>
        public string Icon => Type switch
        {
            Abstractions.NotificationType.Success => "✓",
            Abstractions.NotificationType.Warning => "⚠",
            Abstractions.NotificationType.Error => "✕",
            Abstractions.NotificationType.Info => "ℹ",
            _ => "ℹ"
        };

        /// <summary>
        /// 获取通知类型对应的颜色标识键（用于绑定到 ResourceDictionary）。
        /// </summary>
        public string ColorKey => Type switch
        {
            Abstractions.NotificationType.Success => "NotificationSuccessColor",
            Abstractions.NotificationType.Warning => "NotificationWarningColor",
            Abstractions.NotificationType.Error => "NotificationErrorColor",
            Abstractions.NotificationType.Info => "NotificationInfoColor",
            _ => "NotificationInfoColor"
        };
    }
}