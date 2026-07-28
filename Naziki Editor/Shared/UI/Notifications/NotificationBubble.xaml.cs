using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Notifications;

namespace Naziki_Editor.Views.Notifications
{
    /// <summary>
    /// 通知气泡 UserControl，负责单条通知消息的渲染和交互。
    /// 支持鼠标悬停暂停计时、点击关闭、自动消失等交互。
    /// </summary>
    public partial class NotificationBubble : UserControl
    {
        private readonly NotificationMessage _notification;
        private readonly Action _onDismiss;
        private CancellationTokenSource? _autoDismissCts;
        private bool _isMouseOver;
        private bool _isDisposed;

        #region 依赖属性

        public static readonly DependencyProperty MessageProperty =
            DependencyProperty.Register(nameof(Message), typeof(string), typeof(NotificationBubble),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty TypeProperty =
            DependencyProperty.Register(nameof(Type), typeof(NotificationType), typeof(NotificationBubble),
                new PropertyMetadata(NotificationType.Info));

        public string Message
        {
            get => (string)GetValue(MessageProperty);
            set => SetValue(MessageProperty, value);
        }

        public NotificationType Type
        {
            get => (NotificationType)GetValue(TypeProperty);
            set => SetValue(TypeProperty, value);
        }

        #endregion

        /// <summary>
        /// 通知被关闭时触发（无论自动还是手动）。
        /// </summary>
        public event EventHandler? Dismissed;

        public NotificationBubble(NotificationMessage notification, Action onDismiss)
        {
            if (notification == null) throw new ArgumentNullException(nameof(notification));
            if (onDismiss == null) throw new ArgumentNullException(nameof(onDismiss));

            _notification = notification;
            _onDismiss = onDismiss;

            InitializeComponent();

            // 绑定数据
            Message = notification.Message;
            Type = notification.Type;

            // 启动自动消失计时器
            if (notification.DurationMs > 0)
            {
                StartAutoDismiss(notification.DurationMs);
            }
        }

        /// <summary>
        /// 启动自动消失倒计时。
        /// </summary>
        private async void StartAutoDismiss(int durationMs)
        {
            _autoDismissCts = new CancellationTokenSource();
            var token = _autoDismissCts.Token;

            try
            {
                await Task.Delay(durationMs, token);

                if (!token.IsCancellationRequested && !_isDisposed)
                {
                    await DismissWithAnimation();
                }
            }
            catch (TaskCanceledException)
            {
                // 计时器被取消（鼠标悬停暂停），无需处理
            }
        }

        /// <summary>
        /// 带动画的关闭流程。
        /// </summary>
        public async Task DismissWithAnimation()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _autoDismissCts?.Cancel();
            _autoDismissCts?.Dispose();

            // 使用统一动画模块执行淡出 + 滑出
            await Core.Animation.AnimationUtils.SlideOutToBottomAsync(this, 40, 300);

            _onDismiss?.Invoke();
            Dismissed?.Invoke(this, EventArgs.Empty);
        }

        #region 事件处理

        /// <summary>
        /// 鼠标进入：暂停自动消失计时。
        /// </summary>
        private void Bubble_MouseEnter(object sender, MouseEventArgs e)
        {
            if (_isDisposed) return;
            _isMouseOver = true;

            _autoDismissCts?.Cancel();
        }

        /// <summary>
        /// 鼠标离开：恢复自动消失计时。
        /// </summary>
        private async void Bubble_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_isDisposed) return;
            _isMouseOver = false;

            // 重新计算剩余时间（至少 1 秒）
            int remainingMs = Math.Max(1000, _notification.DurationMs / 2);
            _autoDismissCts?.Dispose();
            _autoDismissCts = new CancellationTokenSource();
            var token = _autoDismissCts.Token;

            try
            {
                await Task.Delay(remainingMs, token);
                if (!token.IsCancellationRequested && !_isDisposed)
                {
                    await DismissWithAnimation();
                }
            }
            catch (TaskCanceledException) { }
        }

        /// <summary>
        /// 点击气泡主体：关闭通知。
        /// </summary>
        private async void Bubble_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isDisposed) return;
            await DismissWithAnimation();
        }

        /// <summary>
        /// 点击关闭按钮：关闭通知。
        /// </summary>
        private async void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isDisposed) return;
            e.Handled = true; // 防止冒泡触发 Bubble_MouseLeftButtonDown
            await DismissWithAnimation();
        }

        #endregion
    }
}