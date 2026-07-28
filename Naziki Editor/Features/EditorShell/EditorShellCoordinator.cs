using Naziki_Editor.Core.Abstractions;
using Naziki_Editor.Features.Preview;
using Naziki_Editor.Features.Project.Resources;

namespace Naziki_Editor.Features.EditorShell;

public interface IEditorShellCoordinator : IDisposable
{
    void Attach(IEditorShellView view);
}

/// <summary>
/// Owns shell-level message subscriptions and project-session orchestration.
/// Feature-specific commands remain in their respective modules.
/// </summary>
public sealed class EditorShellCoordinator : IEditorShellCoordinator
{
    private readonly IMessageBroker _messages;
    private readonly IStoryboardPreviewPublisher _preview;
    private readonly List<IDisposable> _subscriptions = [];
    private IEditorShellView? _view;

    public EditorShellCoordinator(
        IMessageBroker messages,
        IStoryboardPreviewPublisher preview)
    {
        _messages = messages;
        _preview = preview;
    }

    public void Attach(IEditorShellView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        DisposeSubscriptions();
        _view = view;
        _subscriptions.Add(_messages.Subscribe("ProjectLoaded", OnProjectLoaded));
        _subscriptions.Add(_messages.Subscribe<ProjectResourceChanged>(
            "ProjectResourcesChanged",
            change => _preview.PublishReset($"Project.Resource.{change.Kind}")));
    }

    private void OnProjectLoaded()
    {
        var view = _view;
        if (view is null) return;

        _preview.EndSession();
        _preview.StartSession();
        _preview.PublishReset("Project.Loaded");
        view.ApplyLoadedProject();
    }

    public void Dispose()
    {
        DisposeSubscriptions();
        _view = null;
    }

    private void DisposeSubscriptions()
    {
        foreach (var subscription in _subscriptions)
            subscription.Dispose();
        _subscriptions.Clear();
    }
}
