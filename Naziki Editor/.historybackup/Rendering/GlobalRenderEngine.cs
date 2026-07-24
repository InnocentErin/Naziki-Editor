using System;
using System.Windows.Media;

namespace Naziki_Editor.Core
{
    // ==========================================
    // 👁️ 全局硬件级渲染引擎 (V-Sync)
    // ==========================================
    public class GlobalRenderEngine
    {
        private static GlobalRenderEngine _instance;
        public static GlobalRenderEngine Instance => _instance ??= new GlobalRenderEngine();

        // 📡 全局广播：任何需要每帧刷新的 UI（时间轴、预览画布），都来订阅我！
        public event Action OnRenderTick;

        // ⚡ 性能挡位控制
        public bool IsHighRefreshRate { get; set; } = false;
        private int _frameSkipCounter = 0;

        private GlobalRenderEngine()
        {
            // 🌟 核心魔法：接入 WPF 的底层硬件渲染环，与显示器刷新率同频！
            CompositionTarget.Rendering += (s, e) =>
            {
                // 如果是“低”性能模式，我们拦截一半的帧（降频到约 30fps，极度省电）
                if (!IsHighRefreshRate)
                {
                    _frameSkipCounter++;
                    if (_frameSkipCounter % 2 != 0) return;
                }

                // 广播：所有挂载的 UI 组件，立刻更新画面！
                OnRenderTick?.Invoke();
            };
        }
    }
}