using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WpfStoryboard = System.Windows.Media.Animation.Storyboard;
using WpfDoubleAnimation = System.Windows.Media.Animation.DoubleAnimation;

namespace Naziki_Editor.Core.Animation
{
    /// <summary>
    /// WPF 动画工具模块，提供统一的淡入淡出、平移等基础动画函数。
    /// 所有 UI 动画效果应统一调用此模块，确保动画风格一致且不阻塞主线程。
    /// 基于 WPF 原生 Storyboard / DoubleAnimation / ThicknessAnimation，
    /// 无需额外 NuGet 依赖。
    /// </summary>
    public static class AnimationUtils
    {
        #region 淡入淡出 (FadeIn / FadeOut)

        /// <summary>
        /// 淡入动画：将元素从当前透明度平滑过渡到 1.0。
        /// </summary>
        /// <param name="element">目标 UI 元素</param>
        /// <param name="durationMs">动画持续时间（毫秒），默认 300ms</param>
        /// <param name="delayMs">延迟开始时间（毫秒），默认 0</param>
        /// <returns>可等待的 Task，在动画完成时触发</returns>
        public static Task FadeInAsync(FrameworkElement element, double durationMs = 300, double delayMs = 0)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));

            var tcs = new TaskCompletionSource<bool>();
            var storyboard = new WpfStoryboard();

            var animation = new WpfDoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            WpfStoryboard.SetTarget(animation, element);
            WpfStoryboard.SetTargetProperty(animation, new PropertyPath(UIElement.OpacityProperty));
            storyboard.Children.Add(animation);

            storyboard.Completed += (s, e) =>
            {
                element.Opacity = 1.0;
                tcs.TrySetResult(true);
            };

            element.Opacity = 0.0;
            element.Visibility = Visibility.Visible;
            storyboard.Begin();

            return tcs.Task;
        }

        /// <summary>
        /// 淡出动画：将元素从当前透明度平滑过渡到 0.0，完成后自动隐藏。
        /// </summary>
        /// <param name="element">目标 UI 元素</param>
        /// <param name="durationMs">动画持续时间（毫秒），默认 300ms</param>
        /// <param name="delayMs">延迟开始时间（毫秒），默认 0</param>
        /// <returns>可等待的 Task，在动画完成时触发</returns>
        public static Task FadeOutAsync(FrameworkElement element, double durationMs = 300, double delayMs = 0)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));

            var tcs = new TaskCompletionSource<bool>();
            var storyboard = new WpfStoryboard();

            var animation = new WpfDoubleAnimation
            {
                From = element.Opacity,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            WpfStoryboard.SetTarget(animation, element);
            WpfStoryboard.SetTargetProperty(animation, new PropertyPath(UIElement.OpacityProperty));
            storyboard.Children.Add(animation);

            storyboard.Completed += (s, e) =>
            {
                element.Opacity = 0.0;
                element.Visibility = Visibility.Collapsed;
                tcs.TrySetResult(true);
            };

            storyboard.Begin();

            return tcs.Task;
        }

        #endregion

        #region 平移动画 (Slide)

        /// <summary>
        /// 水平平移动画：沿 X 轴移动指定距离。
        /// </summary>
        /// <param name="element">目标 UI 元素</param>
        /// <param name="fromX">起始 X 偏移量</param>
        /// <param name="toX">目标 X 偏移量</param>
        /// <param name="durationMs">动画持续时间（毫秒），默认 350ms</param>
        /// <param name="delayMs">延迟开始时间（毫秒），默认 0</param>
        /// <returns>可等待的 Task</returns>
        public static Task SlideXAsync(FrameworkElement element, double fromX, double toX,
            double durationMs = 350, double delayMs = 0)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));

            EnsureRenderTransform(element, out TranslateTransform transform);

            var tcs = new TaskCompletionSource<bool>();
            var storyboard = new WpfStoryboard();

            var animation = new WpfDoubleAnimation
            {
                From = fromX,
                To = toX,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            WpfStoryboard.SetTarget(animation, element);
            WpfStoryboard.SetTargetProperty(animation,
                new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.X)"));
            storyboard.Children.Add(animation);

            storyboard.Completed += (s, e) =>
            {
                transform.X = toX;
                tcs.TrySetResult(true);
            };

            transform.X = fromX;
            storyboard.Begin();

            return tcs.Task;
        }

        /// <summary>
        /// 垂直平移动画：沿 Y 轴移动指定距离。
        /// </summary>
        public static Task SlideYAsync(FrameworkElement element, double fromY, double toY,
            double durationMs = 350, double delayMs = 0)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));

            EnsureRenderTransform(element, out TranslateTransform transform);

            var tcs = new TaskCompletionSource<bool>();
            var storyboard = new WpfStoryboard();

            var animation = new WpfDoubleAnimation
            {
                From = fromY,
                To = toY,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            WpfStoryboard.SetTarget(animation, element);
            WpfStoryboard.SetTargetProperty(animation,
                new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));
            storyboard.Children.Add(animation);

            storyboard.Completed += (s, e) =>
            {
                transform.Y = toY;
                tcs.TrySetResult(true);
            };

            transform.Y = fromY;
            storyboard.Begin();

            return tcs.Task;
        }

        /// <summary>
        /// 从底部滑入动画（组合：Y 平移 + 淡入）。
        /// </summary>
        public static async Task SlideInFromBottomAsync(FrameworkElement element,
            double slideDistance = 60, double durationMs = 400)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));

            element.Visibility = Visibility.Visible;

            var slideTask = SlideYAsync(element, slideDistance, 0, durationMs);
            var fadeTask = FadeInAsync(element, durationMs);

            await Task.WhenAll(slideTask, fadeTask);
        }

        /// <summary>
        /// 向底部滑出动画（组合：Y 平移 + 淡出）。
        /// </summary>
        public static async Task SlideOutToBottomAsync(FrameworkElement element,
            double slideDistance = 60, double durationMs = 350)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));

            var slideTask = SlideYAsync(element, 0, slideDistance, durationMs);
            var fadeTask = FadeOutAsync(element, durationMs);

            await Task.WhenAll(slideTask, fadeTask);
        }

        #endregion

        #region 缩放动画 (Scale)

        /// <summary>
        /// 缩放动画。
        /// </summary>
        public static Task ScaleAsync(FrameworkElement element, double fromScale, double toScale,
            double durationMs = 300, double delayMs = 0)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));

            EnsureScaleTransform(element, out ScaleTransform transform);

            var tcs = new TaskCompletionSource<bool>();
            var storyboard = new WpfStoryboard();

            var animX = new WpfDoubleAnimation
            {
                From = fromScale,
                To = toScale,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            WpfStoryboard.SetTarget(animX, element);
            WpfStoryboard.SetTargetProperty(animX,
                new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));
            storyboard.Children.Add(animX);

            var animY = new WpfDoubleAnimation
            {
                From = fromScale,
                To = toScale,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            WpfStoryboard.SetTarget(animY, element);
            WpfStoryboard.SetTargetProperty(animY,
                new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));
            storyboard.Children.Add(animY);

            storyboard.Completed += (s, e) =>
            {
                transform.ScaleX = toScale;
                transform.ScaleY = toScale;
                tcs.TrySetResult(true);
            };

            storyboard.Begin();

            return tcs.Task;
        }

        /// <summary>
        /// 弹入效果（从 0.8 缩放到 1.0 并淡入）。
        /// </summary>
        public static async Task PopInAsync(FrameworkElement element, double durationMs = 350)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));

            element.Visibility = Visibility.Visible;

            var scaleTask = ScaleAsync(element, 0.8, 1.0, durationMs);
            var fadeTask = FadeInAsync(element, durationMs);

            await Task.WhenAll(scaleTask, fadeTask);
        }

        #endregion

        #region 帧动画 (Frame-based Animation)

        /// <summary>
        /// 使用 CompositionTarget.Rendering 实现逐帧动画回调。
        /// 适用于需要精确控制每一帧的场景（如自定义缓动曲线）。
        /// </summary>
        /// <param name="element">关联的 UI 元素（用于检查是否已卸载）</param>
        /// <param name="durationMs">总时长（毫秒）</param>
        /// <param name="onFrame">每帧回调，参数 progress 为 0.0~1.0 的归一化进度</param>
        /// <param name="easingMode">缓动类型</param>
        /// <returns>可等待的 Task</returns>
        public static Task AnimatePerFrameAsync(FrameworkElement element, double durationMs,
            Action<double> onFrame, EasingMode easingMode = EasingMode.EaseOut)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            if (onFrame == null) throw new ArgumentNullException(nameof(onFrame));

            var tcs = new TaskCompletionSource<bool>();
            var startTime = DateTime.Now;
            var duration = TimeSpan.FromMilliseconds(durationMs);
            var ease = new CubicEase { EasingMode = easingMode };

            EventHandler? handler = null;
            handler = (s, e) =>
            {
                // 安全检查：元素可能已被卸载
                if (element.Dispatcher.HasShutdownStarted || !element.IsLoaded)
                {
                    CompositionTarget.Rendering -= handler;
                    tcs.TrySetResult(false);
                    return;
                }

                var elapsed = DateTime.Now - startTime;
                if (elapsed >= duration)
                {
                    onFrame(1.0);
                    CompositionTarget.Rendering -= handler;
                    tcs.TrySetResult(true);
                }
                else
                {
                    double rawProgress = elapsed.TotalMilliseconds / durationMs;
                    double easedProgress = ease.Ease(rawProgress);
                    onFrame(easedProgress);
                }
            };

            CompositionTarget.Rendering += handler;
            return tcs.Task;
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 确保元素具有 TranslateTransform，若不存在则创建。
        /// </summary>
        private static void EnsureRenderTransform(FrameworkElement element, out TranslateTransform transform)
        {
            if (element.RenderTransform is TranslateTransform tt)
            {
                transform = tt;
            }
            else if (element.RenderTransform is TransformGroup tg &&
                     tg.Children.Count > 0 &&
                     tg.Children[0] is TranslateTransform ttg)
            {
                transform = ttg;
            }
            else
            {
                transform = new TranslateTransform();
                if (element.RenderTransform is TransformGroup existingGroup)
                {
                    existingGroup.Children.Insert(0, transform);
                }
                else if (element.RenderTransform is Transform existingTransform &&
                         existingTransform != Transform.Identity)
                {
                    var group = new TransformGroup();
                    group.Children.Add(transform);
                    group.Children.Add(existingTransform);
                    element.RenderTransform = group;
                }
                else
                {
                    element.RenderTransform = transform;
                }
            }
        }

        /// <summary>
        /// 确保元素具有 ScaleTransform，若不存在则创建。
        /// </summary>
        private static void EnsureScaleTransform(FrameworkElement element, out ScaleTransform transform)
        {
            if (element.RenderTransform is ScaleTransform st)
            {
                transform = st;
            }
            else if (element.RenderTransform is TransformGroup tg)
            {
                var existing = tg.Children.OfType<ScaleTransform>().FirstOrDefault();
                if (existing != null)
                {
                    transform = existing;
                    return;
                }
                transform = new ScaleTransform();
                tg.Children.Add(transform);
            }
            else if (element.RenderTransform is Transform existingTransform &&
                     existingTransform != Transform.Identity)
            {
                var group = new TransformGroup();
                group.Children.Add(existingTransform);
                transform = new ScaleTransform();
                group.Children.Add(transform);
                element.RenderTransform = group;
            }
            else
            {
                transform = new ScaleTransform();
                element.RenderTransform = transform;
            }
        }

        /// <summary>
        /// 停止元素上所有正在运行的动画。
        /// </summary>
        public static void StopAllAnimations(FrameworkElement element)
        {
            if (element == null) return;
            element.BeginAnimation(UIElement.OpacityProperty, null);
            if (element.RenderTransform is TranslateTransform tt)
            {
                tt.BeginAnimation(TranslateTransform.XProperty, null);
                tt.BeginAnimation(TranslateTransform.YProperty, null);
            }
            if (element.RenderTransform is ScaleTransform st)
            {
                st.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            }
        }

        #endregion
    }
}