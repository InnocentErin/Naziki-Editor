using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Naziki_Editor.Core.Abstractions;

namespace Naziki_Editor.Core.Notifications
{
    /// <summary>
    /// 通知服务核心实现，管理通知队列、生命周期和调度。
    /// 通过 <see cref="OnNotificationRequested"/> 事件将 UI 渲染委托给 View 层，
    /// 保持 Core 层对 UI 的零依赖。
    /// </summary>
    public class NotificationService : INotificationService
    {
        private readonly ConcurrentQueue<NotificationMessage> _pendingQueue = new();
        private readonly List<NotificationMessage> _activeNotifications = new();
        private readonly object _lock = new();
        private int _maxVisible = 5; // 同时最多显示 5 条

        /// <summary>
        /// 当需要显示新通知时触发。View 层订阅此事件以创建 WPF 控件。
        /// 参数：通知消息、关闭回调 Action。
        /// </summary>
        public event Action<NotificationMessage, Action>? OnNotificationRequested;

        /// <summary>
        /// 当通知被关闭时触发（无论是自动还是手动）。
        /// </summary>
        public event Action<NotificationMessage>? OnNotificationDismissed;

        /// <summary>
        /// 同时最多显示的通知数量，默认 5。
        /// </summary>
        public int MaxVisibleNotifications
        {
            get => _maxVisible;
            set => _maxVisible = Math.Max(1, value);
        }

        #region INotificationService 实现

        public void Show(string message, NotificationType type = NotificationType.Info, int durationMs = 3000)
        {
            var notification = new NotificationMessage
            {
                Message = message ?? string.Empty,
                Type = type,
                DurationMs = durationMs
            };

            EnqueueOrShow(notification);
        }

        public void ShowSuccess(string message, int durationMs = 3000)
            => Show(message, NotificationType.Success, durationMs);

        public void ShowWarning(string message, int durationMs = 4000)
            => Show(message, NotificationType.Warning, durationMs);

        public void ShowError(string message, int durationMs = 5000)
            => Show(message, NotificationType.Error, durationMs);

        public Task ShowAsync(string message, NotificationType type = NotificationType.Info, int durationMs = 3000)
        {
            var tcs = new TaskCompletionSource<bool>();
            var notification = new NotificationMessage
            {
                Message = message ?? string.Empty,
                Type = type,
                DurationMs = durationMs
            };

            // 包装关闭回调，在通知关闭时完成 Task
            Action dismissCallback = () =>
            {
                notification.IsDismissed = true;
                tcs.TrySetResult(true);
                OnNotificationDismissed?.Invoke(notification);
                CheckPendingQueue();
            };

            EnqueueOrShow(notification, dismissCallback);
            return tcs.Task;
        }

        public void DismissAll()
        {
            lock (_lock)
            {
                var toDismiss = _activeNotifications.ToList();
                foreach (var n in toDismiss)
                {
                    n.IsDismissed = true;
                }
                _activeNotifications.Clear();
            }

            while (_pendingQueue.TryDequeue(out _)) { }
        }

        #endregion

        #region 内部调度逻辑

        private void EnqueueOrShow(NotificationMessage notification, Action? explicitCallback = null)
        {
            lock (_lock)
            {
                if (_activeNotifications.Count >= _maxVisible)
                {
                    // 超过最大可见数，加入等待队列
                    _pendingQueue.Enqueue(notification);
                    return;
                }

                _activeNotifications.Add(notification);
            }

            // 触发 UI 层创建控件
            Action dismissAction = explicitCallback ?? (() =>
            {
                notification.IsDismissed = true;
                OnNotificationDismissed?.Invoke(notification);
                RemoveActive(notification);
                CheckPendingQueue();
            });

            OnNotificationRequested?.Invoke(notification, dismissAction);
        }

        private void RemoveActive(NotificationMessage notification)
        {
            lock (_lock)
            {
                _activeNotifications.Remove(notification);
            }
        }

        private void CheckPendingQueue()
        {
            lock (_lock)
            {
                if (_pendingQueue.IsEmpty || _activeNotifications.Count >= _maxVisible)
                    return;

                if (_pendingQueue.TryDequeue(out var next))
                {
                    _activeNotifications.Add(next);

                    Action dismissAction = () =>
                    {
                        next.IsDismissed = true;
                        OnNotificationDismissed?.Invoke(next);
                        RemoveActive(next);
                        CheckPendingQueue();
                    };

                    // 需要在 UI 线程上触发，委托给调用方处理
                    OnNotificationRequested?.Invoke(next, dismissAction);
                }
            }
        }

        #endregion
    }
}