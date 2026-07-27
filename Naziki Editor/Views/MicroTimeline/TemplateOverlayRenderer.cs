using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Messaging;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Naziki_Editor.Views.MicroTimeline
{
    /// <summary>
    /// Manages template groups in the micro-editor: header rendering, star markers,
    /// track rows, and batch unbind operations.
    /// </summary>
    public class TemplateOverlayRenderer
    {
        private readonly Canvas _templateCanvas;
        private readonly IMessageBroker _messageBroker;
        private readonly IDialogService _dialogService;

        private readonly List<TemplateGroupInfo> _templateGroups = new();

        public TemplateOverlayRenderer(Canvas templateCanvas, IMessageBroker messageBroker, IDialogService dialogService)
        {
            _templateCanvas = templateCanvas;
            _messageBroker = messageBroker;
            _dialogService = dialogService;
        }

        public void Clear() => _templateGroups.Clear();

        public void AddTemplateGroup(TemplateGroupInfo groupInfo)
        {
            _templateGroups.Add(groupInfo);
        }

        public void RenderTemplates(Canvas canvas, double pixelsPerSecond, double microStartTime, double microEndTime)
        {
            if (_templateCanvas == null) return;

            foreach (var group in _templateGroups)
            {
                // Render group header
                var headerBorder = new Border
                {
                    Height = 24,
                    Background = new SolidColorBrush(Color.FromArgb(30, 255, 215, 0)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 215, 0)),
                    BorderThickness = new Thickness(0, 0, 0, 1)
                };
                headerBorder.Child = new TextBlock
                {
                    Text = $"⭐ 模板: {group.GroupName}",
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 215, 0)),
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(5, 0, 0, 0)
                };
                canvas.Children.Add(headerBorder);

                // Render template tracks
                foreach (var template in group.Templates)
                {
                    var trackBorder = new Border
                    {
                        Height = 36,
                        BorderBrush = new SolidColorBrush(Color.FromArgb(30, 255, 215, 0)),
                        BorderThickness = new Thickness(0, 0, 0, 1)
                    };
                    var trackContent = new StackPanel { Orientation = Orientation.Horizontal };
                    var starIcon = new TextBlock
                    {
                        Text = "⭐",
                        FontSize = 14,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(5, 0, 5, 0)
                    };
                    trackContent.Children.Add(starIcon);

                    var label = new TextBlock
                    {
                        Text = template.TemplateName,
                        FontSize = 10,
                        Foreground = (Brush)Application.Current.FindResource("MainTextColor"),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    trackContent.Children.Add(label);
                    trackBorder.Child = trackContent;
                    canvas.Children.Add(trackBorder);
                }
            }
        }

        public void BatchUnbindTemplates()
        {
            var allTemplates = _templateGroups.SelectMany(g => g.Templates).ToList();
            if (allTemplates.Count == 0)
            {
                _dialogService.ShowMessage("当前没有可解绑的模板。", "解绑失败");
                return;
            }

            bool confirmed = _dialogService.ShowYesNo(
                $"确认解绑全部 {allTemplates.Count} 个模板？",
                "批量解绑模板");

            if (confirmed)
            {
                _messageBroker.Publish("TemplateBatchUnbind", allTemplates);
                _dialogService.ShowMessage("所有模板已解绑。", "解绑完成");
            }
        }
    }

    public class TemplateGroupInfo
    {
        public string GroupName { get; set; }
        public List<TemplateInfo> Templates { get; set; } = new();
    }

    public class TemplateInfo
    {
        public string TemplateName { get; set; }
        public string TemplateId { get; set; }
        public object AssociatedEntity { get; set; }
    }
}
