using System;
using System.Collections.Generic;
using System.Windows;
using Naziki_Editor.Core.Abstractions;

namespace Naziki_Editor.Views.Loading;

public sealed class LoadingService : ILoadingService
{
    private readonly Dictionary<FrameworkElement, (LoadingOverlay Overlay, int Count)> _registrations = new();

    public void Register(FrameworkElement owner, FrameworkElement overlayElement)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (overlayElement is not LoadingOverlay overlay)
            throw new ArgumentException("The overlay must be a LoadingOverlay.", nameof(overlayElement));
        _registrations[owner] = (overlay, 0);
        owner.Unloaded += (_, _) => _registrations.Remove(owner);
    }

    public LoadingScope Begin(FrameworkElement owner, string message)
    {
        if (!_registrations.TryGetValue(owner, out var registration))
            throw new InvalidOperationException("The owner has not registered a LoadingOverlay.");

        registration.Overlay.Message = message;
        registration.Overlay.IsLoading = true;
        registration.Overlay.IsIndeterminate = true;
        registration.Overlay.IsProgressVisible = Visibility.Collapsed;
        _registrations[owner] = (registration.Overlay, registration.Count + 1);
        return new LoadingScope(() => End(owner));
    }

    public void Update(FrameworkElement owner, string message, double? progress = null)
    {
        if (!_registrations.TryGetValue(owner, out var registration))
            throw new InvalidOperationException("The owner has not registered a LoadingOverlay.");
        registration.Overlay.Message = message;
        if (progress is { } value)
        {
            registration.Overlay.Progress = Math.Clamp(value, 0, 100);
            registration.Overlay.IsIndeterminate = false;
            registration.Overlay.IsProgressVisible = Visibility.Visible;
        }
    }

    public void SetCancellation(FrameworkElement owner, Action? cancel)
    {
        if (!_registrations.TryGetValue(owner, out var registration))
            throw new InvalidOperationException("The owner has not registered a LoadingOverlay.");
        registration.Overlay.SetCancelAction(cancel);
    }

    private void End(FrameworkElement owner)
    {
        if (!_registrations.TryGetValue(owner, out var registration)) return;
        var count = Math.Max(0, registration.Count - 1);
        registration.Overlay.IsLoading = count > 0;
        if (count == 0)
        {
            registration.Overlay.SetCancelAction(null);
        }
        _registrations[owner] = (registration.Overlay, count);
    }
}
