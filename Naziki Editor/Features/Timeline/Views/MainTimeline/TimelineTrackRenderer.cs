using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Core.Messaging;
using Naziki_Editor.Core.Timeline.EventBlocks.Abstractions;
using Naziki_Editor.Core.Timeline.Shared;
using Naziki_Editor.Models;
using Naziki_Editor.State;
using Naziki_Editor.UI.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Naziki_Editor.Views.MainTimeline
{
    /// <summary>
    /// Renders the timeline track groups, tracks, and clip blocks.
    /// </summary>
    public class TimelineTrackRenderer
    {
        private readonly StackPanel _trackHeadersContainer;
        private readonly StackPanel _trackGroupsContainer;
        private readonly StackPanel _bottomTrackHeadersContainer;
        private readonly StackPanel _bottomTrackGroupsContainer;
        private readonly IEventBlockService _clipService;
        private readonly UI.Rendering.NoteVisualEngine _noteVisualEngine;
        private readonly IMessageBroker _messageBroker;
        private readonly IDialogService _dialogService;

        private ProjectDataContext _context;
        private double _pixelsPerSecond = 100.0;
        private double _totalDurationSeconds = 60.0;

        public List<TrackRegistryItem> UpperTrackRegistry { get; } = new();
        public List<TrackRegistryItem> LowerTrackRegistry { get; } = new();

        public event Action<EventBlockViewModel> OnRequestDetailedEditMode;
        public event Action<EventBlockViewModel> OnClipSelected;
        public event Action<EventBlockViewModel> OnRequestPropertyEditor;
        public event Action<EventBlockControl, System.Windows.Input.MouseEventArgs, EventBlockControl.MacroDragStage> OnMacroGridDrag;

        public TimelineTrackRenderer(
            StackPanel trackHeadersContainer,
            StackPanel trackGroupsContainer,
            StackPanel bottomTrackHeadersContainer,
            StackPanel bottomTrackGroupsContainer,
            IEventBlockService clipService,
            UI.Rendering.NoteVisualEngine noteVisualEngine,
            IMessageBroker messageBroker,
            IDialogService dialogService)
        {
            _trackHeadersContainer = trackHeadersContainer;
            _trackGroupsContainer = trackGroupsContainer;
            _bottomTrackHeadersContainer = bottomTrackHeadersContainer;
            _bottomTrackGroupsContainer = bottomTrackGroupsContainer;
            _clipService = clipService;
            _noteVisualEngine = noteVisualEngine;
            _messageBroker = messageBroker;
            _dialogService = dialogService;
        }

        public void Update(ProjectDataContext context, double pixelsPerSecond, double totalDurationSeconds)
        {
            _context = context;
            _pixelsPerSecond = pixelsPerSecond;
            _totalDurationSeconds = totalDurationSeconds;
        }

        public void RefreshUI(ObservableCollection<MainTimelineGroupViewModel> trackGroups)
        {
            if (_trackHeadersContainer == null || _trackGroupsContainer == null) return;

            UpperTrackRegistry.Clear();
            LowerTrackRegistry.Clear();

            _trackHeadersContainer.Children.Clear();
            _trackGroupsContainer.Children.Clear();
            _bottomTrackHeadersContainer?.Children.Clear();
            _bottomTrackGroupsContainer?.Children.Clear();

            var sortedGroups = trackGroups.OrderByDescending(g => g.GroupIndex).ToList();

            foreach (var group in sortedGroups)
            {
                StackPanel targetHeader = group.GroupIndex >= 0 ? _trackHeadersContainer : _bottomTrackHeadersContainer;
                StackPanel targetTrack = group.GroupIndex >= 0 ? _trackGroupsContainer : _bottomTrackGroupsContainer;

                targetHeader ??= _trackHeadersContainer;
                targetTrack ??= _trackGroupsContainer;

                RenderGroupHeader(targetHeader, targetTrack, group);

                if (!group.IsExpanded) continue;

                var sortedTracks = group.SortTracksAscending
                    ? group.Tracks.OrderBy(t => t.TrackIndex).ToList()
                    : group.Tracks.OrderByDescending(t => t.TrackIndex).ToList();

                foreach (var track in sortedTracks)
                {
                    var registryItem = RenderTrack(targetHeader, targetTrack, group, track);
                    if (group.GroupIndex >= 0) UpperTrackRegistry.Add(registryItem);
                    else LowerTrackRegistry.Add(registryItem);
                }
            }
        }

        private void RenderGroupHeader(StackPanel targetHeader, StackPanel targetTrack, MainTimelineGroupViewModel group)
        {
            var headerLeft = new Border
            {
                Height = 26,
                Background = (Brush)Application.Current.FindResource("MenuBgColor"),
                BorderBrush = (Brush)Application.Current.FindResource("BorderColor"),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            headerLeft.Child = new TextBlock
            {
                Text = group.GroupName,
                Foreground = (Brush)Application.Current.FindResource("HighlightBorderColor"),
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 0, 0)
            };
            targetHeader.Children.Add(headerLeft);

            var headerRight = new Border
            {
                Height = 26,
                Background = (Brush)Application.Current.FindResource("MenuBgColor"),
                BorderBrush = (Brush)Application.Current.FindResource("BorderColor"),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            targetTrack.Children.Add(headerRight);
        }

        private TrackRegistryItem RenderTrack(StackPanel targetHeader, StackPanel targetTrack, MainTimelineGroupViewModel group, MainTimelineTrackViewModel track)
        {
            var trackHeight = AppServices.GetService<Core.Timeline.Settings.ITimelineSettings>().Current.TrackHeight;
            var headerText = new TextBlock
            {
                Text = track.TrackName,
                Foreground = (Brush)Application.Current.FindResource("MainTextColor"),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(15, 0, 0, 0)
            };
            var trackLeft = new Border
            {
                Height = trackHeight,
                BorderBrush = (Brush)Application.Current.FindResource("BorderColor"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = headerText
            };
            targetHeader.Children.Add(trackLeft);

            var trackCanvas = new Canvas
            {
                Height = trackHeight,
                Background = Brushes.Transparent,
                ClipToBounds = true,
                Width = _totalDurationSeconds * _pixelsPerSecond + 200
            };
            var trackRight = new Border
            {
                Height = trackHeight,
                BorderBrush = (Brush)Application.Current.FindResource("BorderColor"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = trackCanvas
            };

            var registryItem = new TrackRegistryItem
            {
                TrackBorder = trackRight,
                HeaderTextBlock = headerText,
                Group = group,
                Track = track
            };

            targetTrack.Children.Add(trackRight);

            foreach (var clip in track.Clips)
            {
                var clipCtrl = new EventBlockControl(_messageBroker, _dialogService, _noteVisualEngine, _clipService);
                clipCtrl.Tag = clip;
                clipCtrl.Init(clip, _context, _pixelsPerSecond, clip.TrackIndex, 999, _noteVisualEngine);

                clipCtrl.OnRequestDetailedEditMode += (m) => OnRequestDetailedEditMode?.Invoke(m);
                clipCtrl.OnClipSelected += (m) => OnClipSelected?.Invoke(m);
                clipCtrl.OnRequestPropertyEditor += (m) => OnRequestPropertyEditor?.Invoke(m);
                clipCtrl.OnMacroGridDrag += (c, e, s) => OnMacroGridDrag?.Invoke(c, e, s);

                bool isGlobalController = _clipService.IsGlobalController(clip.AssociatedObject);

                if (isGlobalController)
                {
                    Canvas.SetLeft(clipCtrl, 0);
                    Canvas.SetTop(clipCtrl, 6);
                    clipCtrl.Width = _totalDurationSeconds * _pixelsPerSecond + 200;
                }
                else
                {
                    Canvas.SetLeft(clipCtrl, clip.StartTime * _pixelsPerSecond);
                    Canvas.SetTop(clipCtrl, 6);

                    double clipDuration = clip.EndTime - clip.StartTime;
                    if (clipDuration <= 0)
                    {
                        clipCtrl.Width = AppServices.GetService<Core.Timeline.Settings.ITimelineSettings>().Current.ZeroDurationMarkerWidth;
                    }
                    else
                    {
                        if (clipDuration > 300) clipDuration = 300;
                        clipCtrl.Width = Math.Max(10, clipDuration * _pixelsPerSecond);
                    }
                }

                trackCanvas.Children.Add(clipCtrl);
            }

            return registryItem;
        }

        public void FastUpdateZoom()
        {
            double newWidth = _totalDurationSeconds * _pixelsPerSecond + 200;
            UpdateTracksZoom(_trackGroupsContainer, newWidth);
            UpdateTracksZoom(_bottomTrackGroupsContainer, newWidth);
        }

        private void UpdateTracksZoom(StackPanel container, double newWidth)
        {
            if (container == null) return;
            foreach (UIElement child in container.Children)
            {
                if (child is Border border && border.Child is Canvas trackCanvas)
                {
                    trackCanvas.Width = newWidth;
                    foreach (UIElement clipObj in trackCanvas.Children)
                    {
                        if (clipObj is EventBlockControl clipCtrl && clipCtrl.Tag is EventBlockViewModel clip)
                        {
                            bool isGlobalController = _clipService.IsGlobalController(clip.AssociatedObject);

                            if (isGlobalController)
                            {
                                Canvas.SetLeft(clipCtrl, 0);
                                clipCtrl.Width = newWidth;
                            }
                            else
                            {
                                Canvas.SetLeft(clipCtrl, clip.StartTime * _pixelsPerSecond);
                                double clipDuration = clip.EndTime - clip.StartTime;
                                if (clipDuration <= 0)
                                {
                                    clipCtrl.Width = AppServices.GetService<Core.Timeline.Settings.ITimelineSettings>().Current.ZeroDurationMarkerWidth;
                                }
                                else
                                {
                                    if (clipDuration > 300) clipDuration = 300;
                                    clipCtrl.Width = Math.Max(10, clipDuration * _pixelsPerSecond);
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    public class TrackRegistryItem
    {
        public Border TrackBorder { get; set; }
        public TextBlock HeaderTextBlock { get; set; }
        public MainTimelineGroupViewModel Group { get; set; }
        public MainTimelineTrackViewModel Track { get; set; }
    }
}



