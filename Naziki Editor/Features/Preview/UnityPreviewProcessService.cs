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
    bool IsGraphicsReady { get; }
    int? ProcessId { get; }
    long Generation { get; }
    string RuntimePath { get; }
    event EventHandler<UnityPreviewProcessExited>? Exited;
    Task StartAsync(UnityPreviewLaunchOptions options, CancellationToken cancellationToken = default);
    Task ReparentAsync(IntPtr parentWindow, CancellationToken cancellationToken = default);
    Task StopAsync(TimeSpan gracefulTimeout, CancellationToken cancellationToken = default);
}

public sealed record UnityPreviewProcessExited(
    long Generation,
    int ProcessId,
    int? ExitCode,
    bool Expected);

public sealed class UnityPreviewProcessService : IUnityPreviewProcessService
{
    private readonly object _sync = new();
    private Process? _process;
    private IntPtr _graphicsWindow;
    private bool _stopping;
    private long _generation;

    public bool IsRunning
    {
        get
        {
            lock (_sync)
                return _process is { HasExited: false };
        }
    }

    public bool IsGraphicsReady { get { lock (_sync) return _graphicsWindow != IntPtr.Zero; } }
    public int? ProcessId { get { lock (_sync) return _process is { HasExited: false } p ? p.Id : null; } }
    public long Generation { get { lock (_sync) return _generation; } }

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

    public event EventHandler<UnityPreviewProcessExited>? Exited;

    public async Task StartAsync(UnityPreviewLaunchOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var applicationWindow = GetAncestor(options.ParentWindow, GetAncestorFlags.Root);
        if (applicationWindow == IntPtr.Zero)
            throw new InvalidOperationException("Cannot resolve the editor top-level window for Unity Preview.");
        Process process;
        lock (_sync)
        {
            if (_process is { HasExited: false })
                return;
            if (!File.Exists(RuntimePath))
                throw new FileNotFoundException(
                    "未找到 Unity Original Player。请使用 Unity 6000.0.80f1 构建 Windows Editor Preview。",
                    RuntimePath);

            var startInfo = new ProcessStartInfo
            {
                FileName = RuntimePath,
                WorkingDirectory = Path.GetDirectoryName(RuntimePath)!,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Add(startInfo, "-parentHWND");
            // Unity's delayed embedding contract requires the parent
            // application's top-level HWND here. The render window is moved
            // into the preview surface only after graphics initialization.
            Add(startInfo, applicationWindow.ToInt64().ToString(CultureInfo.InvariantCulture));
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
            _generation++;
            process.Exited += OnProcessExited;
            if (!process.Start())
                throw new InvalidOperationException("Unity Original Player 进程启动失败。");
            _process = process;
        }
        try
        {
            var graphicsWindow = await WaitForGraphicsWindowAsync(
                process,
                applicationWindow,
                options.ParentWindow,
                cancellationToken).ConfigureAwait(false);
            lock (_sync)
            {
                if (ReferenceEquals(_process, process))
                {
                    _graphicsWindow = graphicsWindow;
                }
            }
        }
        catch
        {
            process.Exited -= OnProcessExited;
            if (!process.HasExited)
            {
                try { process.Kill(true); }
                catch (InvalidOperationException) { }
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            lock (_sync)
            {
                if (ReferenceEquals(_process, process))
                {
                    _process = null;
                    _graphicsWindow = IntPtr.Zero;
                }
            }
            process.Dispose();
            throw;
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
            ReparentWindow(graphicsWindow, parentWindow);
            return;
        }

        graphicsWindow = await WaitForGraphicsWindowAsync(
            process,
            GetAncestor(parentWindow, GetAncestorFlags.Root),
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
                IntPtr graphicsWindow;
                lock (_sync)
                    graphicsWindow = ReferenceEquals(_process, process) ? _graphicsWindow : IntPtr.Zero;
                if (graphicsWindow != IntPtr.Zero && IsWindow(graphicsWindow))
                    PostMessage(graphicsWindow, WindowMessage.Close, IntPtr.Zero, IntPtr.Zero);
                try
                {
                    await process.WaitForExitAsync(CancellationToken.None)
                        .WaitAsync(TimeSpan.FromMilliseconds(750))
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
        long generation;
        bool expected;
        int? exitCode = null;
        try { exitCode = process.ExitCode; }
        catch (InvalidOperationException) { }
        lock (_sync)
        {
            if (!ReferenceEquals(_process, process))
                return;
            generation = _generation;
            expected = _stopping;
        }
        Exited?.Invoke(this,
            new UnityPreviewProcessExited(generation, process.Id, exitCode, expected));
    }

    private static async Task<IntPtr> WaitForGraphicsWindowAsync(
        Process process,
        IntPtr applicationWindow,
        IntPtr targetWindow,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        var candidate = IntPtr.Zero;
        while (DateTime.UtcNow < deadline && !process.HasExited)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidate == IntPtr.Zero || !IsWindow(candidate))
                candidate = FindProcessChildWindow(applicationWindow, (uint)process.Id);

            if (candidate == IntPtr.Zero)
            {
                process.Refresh();
                candidate = process.MainWindowHandle;
            }

            if (candidate != IntPtr.Zero && HasGraphicsReadyMarker(candidate))
            {
                ReparentWindow(candidate, targetWindow);
                return candidate;
            }
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }
        if (process.HasExited)
            throw new InvalidOperationException($"Unity Preview exited during graphics initialization ({process.ExitCode}).");
        throw new TimeoutException("Unity Preview graphics window did not become ready within 15 seconds.");
    }

    private static bool HasGraphicsReadyMarker(IntPtr window) =>
        (GetWindowLongPtr(window, WindowLongIndex.UserData).ToInt64() & 1L) != 0;

    private static void ReparentWindow(IntPtr child, IntPtr parent)
    {
        Marshal.SetLastPInvokeError(0);
        var previousParent = SetParent(child, parent);
        var error = Marshal.GetLastPInvokeError();
        if (previousParent == IntPtr.Zero && error != 0)
            throw new InvalidOperationException(
                $"Failed to reparent the Unity Preview window (Win32={error}).");
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

    private enum GetAncestorFlags : uint
    {
        Root = 2
    }

    private enum WindowLongIndex
    {
        UserData = -21
    }

    private enum WindowMessage : uint
    {
        Close = 0x0010
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, GetAncestorFlags flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, WindowLongIndex index);

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

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(
        IntPtr window,
        WindowMessage message,
        IntPtr wParam,
        IntPtr lParam);

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
