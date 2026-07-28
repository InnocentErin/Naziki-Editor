using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Features.EditorShell.Commands;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Naziki_Editor.Features.EditorShell;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly ICommandDispatcher _dispatcher;
    private readonly IRecentProjectService _recentProjects;
    private double _zoomLevel = 100;

    public MainWindowViewModel(ICommandDispatcher dispatcher, IRecentProjectService recentProjects)
    {
        _dispatcher = dispatcher;
        _recentProjects = recentProjects;
        NewProjectCommand = Dispatch("NewProject");
        OpenProjectCommand = Dispatch("OpenProject");
        SaveProjectCommand = Dispatch("SaveProject");
        UndoCommand = Dispatch("Undo");
        RedoCommand = Dispatch("Redo");
        ImportLevelCommand = Dispatch("ImportLevel");
        ImportChartCommand = Dispatch("ImportChart");
        ImportStoryboardCommand = Dispatch("ImportStoryboard");
        ImportAudioCommand = Dispatch("ImportAudio");
        ImportBackgroundCommand = Dispatch("ImportBackground");
        PlayPauseCommand = Dispatch("TimelinePlayPause");
        StopCommand = Dispatch("TimelineGoToStart");
        ZoomInCommand = Dispatch("TimelineZoomIn");
        ZoomOutCommand = Dispatch("TimelineZoomOut");
        ZoomResetCommand = Dispatch("TimelineZoomReset");
        ExitCommand = Dispatch("Exit");
        AboutCommand = Dispatch("About");
    }

    public EditorStatusViewModel Status { get; } = new();
    public ObservableCollection<string> RecentProjects { get; } = [];
    public double ZoomLevel
    {
        get => _zoomLevel;
        set { if (Math.Abs(_zoomLevel - value) < 0.001) return; _zoomLevel = value; OnPropertyChanged(); }
    }

    public ICommand NewProjectCommand { get; }
    public ICommand OpenProjectCommand { get; }
    public ICommand SaveProjectCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand ImportLevelCommand { get; }
    public ICommand ImportChartCommand { get; }
    public ICommand ImportStoryboardCommand { get; }
    public ICommand ImportAudioCommand { get; }
    public ICommand ImportBackgroundCommand { get; }
    public ICommand PlayPauseCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand ZoomInCommand { get; }
    public ICommand ZoomOutCommand { get; }
    public ICommand ZoomResetCommand { get; }
    public ICommand ExitCommand { get; }
    public ICommand AboutCommand { get; }

    public void ReloadRecentProjects()
    {
        RecentProjects.Clear();
        foreach (var item in _recentProjects.GetExisting()) RecentProjects.Add(item);
    }

    public void AddRecentProject(string path)
    {
        _recentProjects.Add(path);
        ReloadRecentProjects();
    }

    private ICommand Dispatch(string name) => new RelayCommand(() => _dispatcher.Execute(name));
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
