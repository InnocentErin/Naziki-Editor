namespace Naziki_Editor.Models
{
    // 🌟 Cytoid 官方标准缓动大辞典
    public class EasingFunction
    {
        public enum Ease
        {
            None = 0, EaseInQuad = 1, EaseOutQuad = 2, EaseInOutQuad = 3,
            EaseInCubic = 4, EaseOutCubic = 5, EaseInOutCubic = 6,
            EaseInQuart = 7, EaseOutQuart = 8, EaseInOutQuart = 9,
            EaseInQuint = 10, EaseOutQuint = 11, EaseInOutQuint = 12,
            EaseInSine = 13, EaseOutSine = 14, EaseInOutSine = 15,
            EaseInExpo = 16, EaseOutExpo = 17, EaseInOutExpo = 18,
            EaseInCirc = 19, EaseOutCirc = 20, EaseInOutCirc = 21,
            Linear = 22, Spring = 23,
            EaseInBounce = 24, EaseOutBounce = 25, EaseInOutBounce = 26,
            EaseInBack = 27, EaseOutBack = 28, EaseInOutBack = 29,
            EaseInElastic = 30, EaseOutElastic = 31, EaseInOutElastic = 32,
            Blink = 33
        }
    }
}