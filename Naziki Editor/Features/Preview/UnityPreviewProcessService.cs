using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace Naziki_Editor.Features.Preview;

public sealed record UnityPreviewLaunchOptions(
    IntPtr ParentWindow,
    string SessionId,
    string PipeName,
    string AuthenticationNonce,
    int PixelWidth,
    int PixelHeight,
    int JobWorkerCount);

public interface IUnityPreviewProcessService : IDisposable
{
    bool IsRunning { get; }
    string RuntimePath { get; }
    event EventHandler<int?>? Exited;
    Task StartAsync(UnityPreviewLaunchOptions options, CancellationToken cancellationToken = default);
    Task ReparentAsync(IntPtr parentWindow, CancellationToken cancellationToken = default);
    Task StopAsync(TimeSpan gracefulTimeout, CancellationToken cancellationToken = default);
}

public sealed class UnityPreviewProcessService : IUnityPreviewProcessService
{
    private readonly object _sync = new();
    private Process? _process;
    private IntPtr _graphicsWindow;
    private bool _stopping;

    public bool IsRunning
    {
        get
        {
            lock (_sync)
                return _process is { HasExited: false };
        }
    }

    public string RuntimePath
    {
        get
        {
            var overridePath = Environment.GetEnvironmentVariable("NAZIKI_ORIGINAL_PLAYER_PATH");
            if (!string.IsNullOrWhiteSpace(overridePath))
                return Path.GetFullPath(overridePath);
            return Path.Combine(AppContext.BaseDirectory, "Runtime", "OriginalPlayer", "NazikiOriginalPlayer.exe");
        }
    }

    public event EventHandler<int?>? Exited;

    public async Task StartAsync(UnityPreviewLaunchOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Process process;
        lock (_sync)
        {
            if (_process is { HasExited: false })
                return;
            if (!File.Exists(RuntimePath))
                throw new FileNotFoundException(
                    "未找到 Unity Original Player。请使用 Unity 6000.0.75f1 构建 Windows Preview。",
                    RuntimePath);

            var startInfo = new ProcessStartInfo
            {
                FileName = RuntimePath,
                WorkingDirectory = Path.GetDirectoryName(RuntimePath)!,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Add(startInfo, "-parentHWND");
            Add(startInfo, options.ParentWindow.ToInt64().ToString(CultureInfo.InvariantCulture));
            Add(startInfo, "delayed");
            Add(startInfo, "-force-d3d11");
            Add(startInfo, "-screen-width");
            Add(startInfo, Math.Max(1, options.PixelWidth).ToString(CultureInfo.InvariantCulture));
            Add(startInfo, "-screen-height");
            Add(startInfo, Math.Max(1, options.PixelHeight).ToString(CultureInfo.InvariantCulture));
            Add(startInfo, "-job-worker-count");
            Add(startInfo, Math.Clamp(options.JobWorkerCount, 1, 16).ToString(CultureInfo.InvariantCulture));
            Add(startInfo, "--naziki-preview-session");
            Add(startInfo, options.SessionId);
            Add(startInfo, "--naziki-preview-pipe");
            Add(startInfo, options.PipeName);
            Add(startInfo, "--naziki-preview-nonce");
            Add(startInfo, options.AuthenticationNonce);

            process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.Exited += OnProcessExited;
            if (!process.Start())
                throw new InvalidOperationException("Unity Original Player 进程启动失败。");
            _process = process;
        }
        var graphicsWindow = await WaitForGraphicsWindowAsync(
            process,
            options.ParentWindow,
            cancellationToken).ConfigureAwait(false);
        lock (_sync)
        {
            if (ReferenceEquals(_process, process))
                _graphicsWindow = graphicsWindow;
        }
    }

    public async Task ReparentAsync(IntPtr parentWindow, CancellationToken cancellationToken = default)
    {
        Process? process;
        IntPtr graphicsWindow;
        lock (_sync)
        {
            process = _process;
            graphicsWindow = _graphicsWindow;
        }
        if (process is not { HasExited: false })
            return;
        if (graphicsWindow != IntPtr.Zero && IsWindow(graphicsWindow))
        {
            SetParent(graphicsWindow, parentWindow);
            return;
        }

        graphicsWindow = await WaitForGraphicsWindowAsync(
            process,
            parentWindow,
            cancellationToken).ConfigureAwait(false);
        lock (_sync)
        {
            if (ReferenceEquals(_process, process))
                _graphicsWindow = graphicsWindow;
        }
    }

    public async Task StopAsync(TimeSpan gracefulTimeout, CancellationToken cancellationToken = default)
    {
        Process? process;
        lock (_sync)
        {
            process = _process;
            _stopping = process is not null;
        }
        if (process is null)
            return;

        if (!process.HasExited)
        {
            try
            {
                await process.WaitForExitAsync(cancellationToken)
                    .WaitAsync(gracefulTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // Only the exact child process created for this preview session is terminated.
                try { process.Kill(true); }
                catch (InvalidOperationException) { }
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        lock (_sync)
        {
            if (ReferenceEquals(_process, process))
            {
                _process = null;
                _graphicsWindow = IntPtr.Zero;
            }
            _stopping = false;
        }
        process.Dispose();
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        var process = (Process)sender!;
        int? exitCode = null;
        try { exitCode = process.ExitCode; }
        catch (InvalidOperationException) { }
        lock (_sync)
        {
            if (_stopping)
                return;
        }
        Exited?.Invoke(this, exitCode);
    }

    private static async Task<IntPtr> WaitForGraphicsWindowAsync(
        Process process,
        IntPtr parentWindow,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline && !process.HasExited)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var embeddedWindow = FindProcessChildWindow(parentWindow, (uint)process.Id);
            if (embeddedWindow != IntPtr.Zero)
                return embeddedWindow;

            process.Refresh();
            var window = process.MainWindowHandle;
            if (window != IntPtr.Zero)
            {
                SetParent(window, parentWindow);
                return window;
            }
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }
        if (process.HasExited)
            throw new InvalidOperationException($"Unity Preview exited during graphics initialization ({process.ExitCode}).");
        throw new TimeoutException("Unity Preview graphics window did not become ready within 15 seconds.");
    }

    private static IntPtr FindProcessChildWindow(IntPtr parentWindow, uint processId)
    {
        var result = IntPtr.Zero;
        EnumChildWindows(
            parentWindow,
            (window, parameter) =>
            {
                _ = GetWindowThreadProcessId(window, out var ownerProcessId);
                if (ownerProcessId != processId)
                    return true;
                result = window;
                return false;
            },
            IntPtr.Zero);
        return result;
    }

    private static void Add(ProcessStartInfo info, string argument) => info.ArgumentList.Add(argument);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(
        IntPtr parentWindow,
        EnumWindowsCallback callback,
        IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);

    public void Dispose()
    {
        Process? process;
        lock (_sync)
        {
            process = _process;
            _process = null;
            _graphicsWindow = IntPtr.Zero;
        }
        if (process is null)
            return;
        process.Exited -= OnProcessExited;
        if (!process.HasExited)
        {
            try { process.Kill(true); }
            catch { }
        }
        process.Dispose();
    }
}
