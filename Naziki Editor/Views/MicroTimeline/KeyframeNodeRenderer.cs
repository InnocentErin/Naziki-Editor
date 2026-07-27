using Naziki_Editor.Core.Timeline.Shared;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Naziki_Editor.Views.MicroTimeline
{
    /// <summary>
    /// Renders keyframe node thumbs (diamonds/circles) on property tracks,
    /// handles drag events, and draws opacity curves.
    /// </summary>
    public class KeyframeNodeRenderer
    {
        private readonly Canvas _trackCanvas;
        private readonly List<Thumb> _nodeThumbs = new();

        public event Action<DecodedKeyframeBox, double, double> OnKeyframeDragged;
        public event Action<DecodedKeyframeBox> OnKeyframeSelected;

        public KeyframeNodeRenderer(Canvas trackCanvas)
        {
            _trackCanvas = trackCanvas;
        }

        public void ClearNodes()
        {
            foreach (var thumb in _nodeThumbs)
            {
                _trackCanvas.Children.Remove(thumb);
            }
            _nodeThumbs.Clear();

            // Remove any existing curve paths
            var curvesToRemove = new List<UIElement>();
            foreach (UIElement child in _trackCanvas.Children)
            {
                if (child is Path && ((Path)child).Tag is string tag && tag == "OpacityCurve")
                    curvesToRemove.Add(child);
            }
            foreach (var curve in curvesToRemove)
                _trackCanvas.Children.Remove(curve);
        }

        public void RenderKeyframeNodes(List<DecodedKeyframeBox> keyframes, double pixelsPerSecond, double microStartTime, double trackHeight)
        {
            ClearNodes();

            if (keyframes == null) return;
            double midY = trackHeight / 2;

            foreach (var kf in keyframes)
            {
                double xPos = (kf.VisualRelTime) * pixelsPerSecond;
                var thumb = CreateKeyframeThumb(kf, xPos, midY);
                _trackCanvas.Children.Add(thumb);
                _nodeThumbs.Add(thumb);
            }
        }

        private Thumb CreateKeyframeThumb(DecodedKeyframeBox kf, double xPos, double midY)
        {
            // Diamond shape for keyframes
            var diamond = new Polygon
            {
                Points = new PointCollection
                {
                    new Point(0, -5), new Point(5, 0), new Point(0, 5), new Point(-5, 0)
                },
                Fill = (Brush)Application.Current.FindResource("HighlightBorderColor"),
                Stroke = (Brush)Application.Current.FindResource("MainTextColor"),
                StrokeThickness = 0.5
            };

            var thumb = new Thumb
            {
                Width = 12,
                Height = 12,
                Cursor = Cursors.SizeAll,
                Tag = kf,
                Template = new ControlTemplate(typeof(Thumb))
                {
                    VisualTree = new FrameworkElementFactory(typeof(Canvas))
                }
            };

            // Add diamond to thumb template
            var canvasFactory = (FrameworkElementFactory)thumb.Template.VisualTree;
            var diamondFactory = new FrameworkElementFactory(typeof(Polygon));
            diamondFactory.SetValue(Polygon.PointsProperty, diamond.Points);
            diamondFactory.SetValue(Polygon.FillProperty, diamond.Fill);
            diamondFactory.SetValue(Polygon.StrokeProperty, diamond.Stroke);
            diamondFactory.SetValue(Polygon.StrokeThicknessProperty, diamond.StrokeThickness);
            canvasFactory.AppendChild(diamondFactory);

            Canvas.SetLeft(thumb, xPos - 6);
            Canvas.SetTop(thumb, midY - 6);

            thumb.DragDelta += (s, e) =>
            {
                double newX = Canvas.GetLeft(thumb) + e.HorizontalChange;
                Canvas.SetLeft(thumb, newX);
                OnKeyframeDragged?.Invoke(kf, newX + 6, 0);
            };

            thumb.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 1)
                    OnKeyframeSelected?.Invoke(kf);
            };

            return thumb;
        }

        public void RedrawOpacityCurve(List<DecodedKeyframeBox> keyframes, double pixelsPerSecond, double microStartTime, double trackHeight, string propertyName)
        {
            if (propertyName != "Opacity" || keyframes == null || keyframes.Count < 2) return;

            double midY = trackHeight / 2;
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                double startX = (keyframes[0].VisualRelTime) * pixelsPerSecond;
                double startY = midY - (GetOpacityValue(keyframes[0]) * (midY - 5));
                ctx.BeginFigure(new Point(startX, startY), false, false);

                for (int i = 1; i < keyframes.Count; i++)
                {
                    double x = (keyframes[i].VisualRelTime) * pixelsPerSecond;
                    double y = midY - (GetOpacityValue(keyframes[i]) * (midY - 5));
                    ctx.LineTo(new Point(x, y), true, false);
                }
            }
            geometry.Freeze();

            var curvePath = new Path
            {
                Data = geometry,
                Stroke = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)),
                StrokeThickness = 1.5,
                Tag = "OpacityCurve"
            };
            _trackCanvas.Children.Add(curvePath);
        }

        private static double GetOpacityValue(DecodedKeyframeBox kf)
        {
            if (kf.Value is double d) return Math.Clamp(d, 0, 1);
            if (kf.Value is float f) return Math.Clamp(f, 0, 1);
            if (kf.Value is int i) return Math.Clamp(i / 255.0, 0, 1);
            return 0.5;
        }
    }
}