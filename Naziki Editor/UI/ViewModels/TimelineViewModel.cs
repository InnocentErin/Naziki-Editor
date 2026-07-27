using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.State;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Naziki_Editor.UI.ViewModels
{
    public class TimelineViewModel : INotifyPropertyChanged
    {
        private readonly IMessageBroker _messageBroker;
        private ProjectDataContext? _context;

        public ObservableCollection<MainTimelineGroupViewModel> TrackGroups { get; } = new();

        public ObservableCollection<MainTimelineGroupViewModel> UpperTrackGroups { get; } = new();
        public ObservableCollection<MainTimelineGroupViewModel> LowerTrackGroups { get; } = new();

        private double _pixelsPerSecond = 100.0;
        public double PixelsPerSecond
        {
            get => _pixelsPerSecond;
            set { _pixelsPerSecond = value; OnPropertyChanged(); }
        }

        private double _totalDurationSeconds = 60.0;
        public double TotalDurationSeconds
        {
            get => _totalDurationSeconds;
            set { _totalDurationSeconds = value; OnPropertyChanged(); }
        }

        private double _currentPlayheadSeconds;
        public double CurrentPlayheadSeconds
        {
            get => _currentPlayheadSeconds;
            set { _currentPlayheadSeconds = value; OnPropertyChanged(); }
        }

        public TimelineViewModel(IMessageBroker messageBroker)
        {
            _messageBroker = messageBroker;
            _messageBroker.Subscribe("DataModified", () => RefreshFromContext());
        }

        public void LoadContext(ProjectDataContext context)
        {
            _context = context;
            RefreshFromContext();
        }

        public void RefreshFromContext()
        {
            if (_context == null) return;
            var calculatedGroups = new UI.Services.TimelineDataEngine().BuildMainTimeline(_context);
            TrackGroups.Clear();
            UpperTrackGroups.Clear();
            LowerTrackGroups.Clear();
            foreach (var g in calculatedGroups)
            {
                TrackGroups.Add(g);
                if (g.GroupIndex >= 0)
                    UpperTrackGroups.Add(g);
                else
                    LowerTrackGroups.Add(g);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
