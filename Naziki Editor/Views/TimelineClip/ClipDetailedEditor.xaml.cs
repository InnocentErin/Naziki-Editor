using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Naziki_Editor.Models;
using Naziki_Editor.State;

namespace Naziki_Editor.Views.TimelineClip
{
    public partial class ClipDetailedEditor : UserControl
    {
        private TimelineClipModel _clipModel;
        private ProjectDataContext _context;
        private double _pixelsPerSecond;

        public ClipDetailedEditor()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 🚀 【数据接线关口】：主轴双击方块后，此方法会被轰轰烈烈地激活！
        /// </summary>
        public void LoadClipData(TimelineClipModel clipModel, ProjectDataContext context, double pixelsPerSecond)
        {
            _clipModel = clipModel;
            _context = context;
            _pixelsPerSecond = pixelsPerSecond;

            // 1. 🧹 清空上一次残留的旧属性时光屋
            PropHeadersStackPanel.Children.Clear();
            PropTracksStackPanel.Children.Clear();

            if (_clipModel.AssociatedObject == null) return;

            // 2. ⏱️ 锁死微观比例尺宽度：由于端点钉死，总长度刚好等于方块的物理 Duration 像素
            double duration = _clipModel.EndTime - _clipModel.StartTime;
            if (duration <= 0) duration = 2.0; // 常驻永生元素兜底
            double visualWidth = duration * _pixelsPerSecond;

            MicroRulerCanvas.Width = visualWidth;

            // 3. 🔬 【智能门派拆分】：利用 Cytoid 强类型反射，分发不同的微观属性轨道！
            string typeName = _clipModel.AssociatedObject.GetType().Name;
            List<string> supportedProperties = new List<string>();

            if (typeName == "C2Sprite" || typeName == "C2Text" || typeName == "C2Line" || typeName == "C2Video")
            {
                // 场景图层对象特有的几何运动属性
                supportedProperties.AddRange(new[] { "X", "Y", "Z", "Opacity", "ScaleX", "ScaleY", "RotZ", "Order" });
            }
            else if (typeName == "C2SceneController")
            {
                // 场景控制器特有的全局黑科技属性
                supportedProperties.AddRange(new[] { "Fov", "BackgroundDim", "UiOpacity", "StoryboardOpacity", "ScanlineOpacity", "Brightness", "GlitchIntensity" });
            }
            else if (typeName == "C2NoteController")
            {
                // 音符控制器的打击偏移属性
                supportedProperties.AddRange(new[] { "X", "Y", "XMultiplier", "YMultiplier", "XOffset", "YOffset", "OpacityMultiplier" });
            }

            // 4. 🧵 机械化流水线：批量手绘每一个属性的“表头 + 关键帧长轨”
            foreach (string prop in supportedProperties)
            {
                // A. 左侧：纯净的属性名文字边框
                Border headerBorder = new Border
                {
                    Height = 40,
                    BorderBrush = (Brush)Application.Current.FindResource("BorderColor"),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(10, 0, 0, 0)
                };
                TextBlock headerText = new TextBlock
                {
                    Text = prop,
                    Foreground = (Brush)Application.Current.FindResource("MainTextColor"),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeights.SemiBold
                };
                headerBorder.Child = headerText;
                PropHeadersStackPanel.Children.Add(headerBorder);

                // B. 右侧：降临单属性关键帧格线行！
                ClipPropertyTrackRow trackRow = new ClipPropertyTrackRow();
                // 把模型、万能上下文基站、缩放比例顺着管道一股脑全部塞进去！
                trackRow.Init(prop, _clipModel, _context, _pixelsPerSecond);
                PropTracksStackPanel.Children.Add(trackRow);
            }

            // 5. 📏 绘制微观局部的时间尺小刻度（可自选实现）
            RenderMicroRulerTicks(duration);
        }

        private void RenderMicroRulerTicks(double duration)
        {
            MicroRulerCanvas.Children.Clear();
            // 在 MicroRulerCanvas 上根据 duration 简单画几根白线，代表 0s ~ 结束s 即可
            // 默认微观模式左端是 0s 的相对起点
        }
    }
}