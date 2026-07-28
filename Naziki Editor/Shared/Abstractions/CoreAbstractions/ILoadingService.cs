using System;
using System.Threading;
using System.Windows;

namespace Naziki_Editor.Core.Abstractions;

public interface ILoadingService
{
    void Register(FrameworkElement owner, FrameworkElement overlay);
    LoadingScope Begin(FrameworkElement owner, string message);
}

public sealed class LoadingScope : IDisposable
{
    private readonly Action _close;
    private int _disposed;

    internal LoadingScope(Action close) => _close = close;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _close();
    }
}
