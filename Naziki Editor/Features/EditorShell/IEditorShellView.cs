using Naziki_Editor.State;

namespace Naziki_Editor.Features.EditorShell;

/// <summary>
/// Narrow compatibility boundary for shell operations that still require WPF controls.
/// The coordinator deliberately does not depend on MainWindow or child control types.
/// </summary>
public interface IEditorShellView
{
    ProjectDataContext Context { get; }
    void ApplyLoadedProject();
}
