using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Notifications;

namespace Naziki_Editor.Views.Notifications
{
    /// <summary>
    /// 通知叠加容器，管理所有通知气泡的渲染、堆叠排列和生命周期。
    /// 始终位于父窗口的右下角。
    /// </summary>
    public partial class NotificationOverlay : UserControl, IDisposable
    {
        private readonly INotificationService _notificationService;
        private readonly List<NotificationBubble> _activeBubbles = new();
        private readonly object _bubbleLock = new();
        private bool _isDisposed;

        public NotificationOverlay(INotificationService notificationService)
        {
            if (notificationService == null)
                throw new ArgumentNullException(nameof(notificationService));

            _notificationService = notificationService;

            InitializeComponent();

            // 订阅通知服务的请求事件
            if (notificationService is NotificationService ns)
            {
                ns.OnNotificationRequested += OnNotificationRequested;
            }
        }

        /// <summary>
        /// 当通知服务请求显示新通知时，创建对应的气泡控件。
        /// </summary>
        private void OnNotificationRequested(NotificationMessage notification, Action dismissCallback)
        {
            // 确保在 UI 线程上执行
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => OnNotificationRequested(notification, dismissCallback));
                return;
            }

            // 创建气泡控件
            var bubble = new NotificationBubble(notification, dismissCallback);
            bubble.Dismissed += (s, e) => RemoveBubble(bubble);

            // 插入到 StackPanel 并播放入场动画
            lock (_bubbleLock)
            {
                _activeBubbles.Add(bubble);
            }

            NotificationStack.Children.Insert(0, bubble);

            // 播放从底部滑入的入场动画
            _ = Core.Animation.AnimationUtils.SlideInFromBottomAsync(bubble, 50, 350);
        }

        /// <summary>
        /// 移除已关闭的气泡控件。
        /// </summary>
        private void RemoveBubble(NotificationBubble bubble)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => RemoveBubble(bubble));
                return;
            }

            lock (_bubbleLock)
            {
                _activeBubbles.Remove(bubble);
            }

            NotificationStack.Children.Remove(bubble);
        }

        /// <summary>
        /// 手动关闭所有通知。
        /// </summary>
        public async void DismissAllBubbles()
        {
            List<NotificationBubble> bubbles;
            lock (_bubbleLock)
            {
                bubbles = _activeBubbles.ToList();
            }

            foreach (var bubble in bubbles)
            {
                await bubble.DismissWithAnimation();
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            if (_notificationService is NotificationService ns)
            {
                ns.OnNotificationRequested -= OnNotificationRequested;
            }

            _activeBubbles.Clear();
            NotificationStack.Children.Clear();
        }
    }
}