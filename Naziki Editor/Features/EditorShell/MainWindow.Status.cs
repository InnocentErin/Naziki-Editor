using Naziki_Editor.Models;

namespace Naziki_Editor.Views;

public partial class MainWindow
{
    private void UpdateStatusBar()
    {
        UpdateStatusPlaybackTime();
        UpdateStatusSelectedObject();
        UpdateStatusObjectCount();
        UpdateStatusModified();
    }

    private void UpdateStatusPlaybackTime()
    {
        try
        {
            _viewModel.Status.CurrentTime = _audioEngine?.GetCurrentSmoothTime() ?? 0;
            _viewModel.Status.Duration = _audioEngine?.Duration ?? 0;
            StatusPlaybackTime.Text = _viewModel.Status.PlaybackText;
        }
        catch
        {
            _viewModel.Status.CurrentTime = 0;
            _viewModel.Status.Duration = 0;
            StatusPlaybackTime.Text = _viewModel.Status.PlaybackText;
        }
    }

    private void UpdateStatusSelectedObject()
    {
        var selectedObject = PropertyPanel?.GetSelectedObject();
        _viewModel.Status.SelectedObject = selectedObject switch
        {
            IStoryboardEntity entity => $"选中: {entity.Id}",
            not null => $"选中: {selectedObject.GetType().Name}",
            _ => string.Empty
        };
        StatusSelectedObject.Text = _viewModel.Status.SelectedObject;
    }

    private void UpdateStatusObjectCount()
    {
        var root = Context?.Storyboard;
        _viewModel.Status.ObjectCount = root is null ? 0 :
            (root.sprites?.Count ?? 0) +
            (root.texts?.Count ?? 0) +
            (root.lines?.Count ?? 0) +
            (root.videos?.Count ?? 0) +
            (root.controllers?.Count ?? 0) +
            (root.note_controllers?.Count ?? 0) +
            (root.templates?.Count ?? 0);
        StatusObjectCount.Text = _viewModel.Status.ObjectCountText;
    }

    private void UpdateStatusModified()
    {
        _viewModel.Status.IsModified = _isVisualDirty;
        StatusModified.Text = _viewModel.Status.ModifiedText;
    }

    private void LoadRecentProjects()
    {
        _viewModel.ReloadRecentProjects();
        RecentProjectsList.ItemsSource = _viewModel.RecentProjects;
    }

    private void SaveRecentProject(string path)
    {
        _viewModel.AddRecentProject(path);
        RecentProjectsList.ItemsSource = _viewModel.RecentProjects;
    }
}
