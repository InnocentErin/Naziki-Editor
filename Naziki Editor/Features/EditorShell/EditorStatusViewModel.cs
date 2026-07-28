using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Naziki_Editor.Features.EditorShell;

public sealed class EditorStatusViewModel : INotifyPropertyChanged
{
    private double _currentTime;
    private double _duration;
    private string _selectedObject = string.Empty;
    private int _objectCount;
    private bool _isModified;

    public double CurrentTime { get => _currentTime; set { _currentTime = value; OnPropertyChanged(); OnPropertyChanged(nameof(PlaybackText)); } }
    public double Duration { get => _duration; set { _duration = value; OnPropertyChanged(); OnPropertyChanged(nameof(PlaybackText)); } }
    public string SelectedObject { get => _selectedObject; set { _selectedObject = value; OnPropertyChanged(); } }
    public int ObjectCount { get => _objectCount; set { _objectCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(ObjectCountText)); } }
    public bool IsModified { get => _isModified; set { _isModified = value; OnPropertyChanged(); OnPropertyChanged(nameof(ModifiedText)); } }
    public string PlaybackText => $"{Format(CurrentTime)} / {Format(Duration)}";
    public string ObjectCountText => $"对象: {ObjectCount}";
    public string ModifiedText => IsModified ? "● 未保存" : string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private static string Format(double seconds) =>
        $"{(int)Math.Max(0, seconds) / 60:00}:{(int)Math.Max(0, seconds) % 60:00}";
}
