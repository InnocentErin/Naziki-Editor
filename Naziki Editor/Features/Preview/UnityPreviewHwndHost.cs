using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Naziki_Editor.Features.Preview;

public sealed class UnityPreviewHwndHost : HwndHost
{
    private IntPtr _handle;

    public IntPtr HostHandle => _handle;
    public event EventHandler<IntPtr>? HostHandleCreated;
    public event EventHandler? HostHandleDestroyed;

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _handle = CreateWindowEx(
            0,
            "static",
            string.Empty,
            WindowStyles.WS_CHILD | WindowStyles.WS_VISIBLE | WindowStyles.WS_CLIPCHILDREN |
            WindowStyles.WS_CLIPSIBLINGS | WindowStyles.SS_BLACKRECT,
            0,
            0,
            Math.Max(1, (int)ActualWidth),
            Math.Max(1, (int)ActualHeight),
            hwndParent.Handle,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);
        if (_handle == IntPtr.Zero)
            throw new InvalidOperationException($"无法创建 Unity Preview HWND，Win32={Marshal.GetLastWin32Error()}。");
        HostHandleCreated?.Invoke(this, _handle);
        return new HandleRef(this, _handle);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        HostHandleDestroyed?.Invoke(this, EventArgs.Empty);
        if (hwnd.Handle != IntPtr.Zero)
            DestroyWindow(hwnd.Handle);
        _handle = IntPtr.Zero;
    }

    protected override void OnWindowPositionChanged(System.Windows.Rect rcBoundingBox)
    {
        base.OnWindowPositionChanged(rcBoundingBox);
        if (_handle != IntPtr.Zero)
            SetWindowPos(
                _handle,
                IntPtr.Zero,
                0,
                0,
                Math.Max(1, (int)rcBoundingBox.Width),
                Math.Max(1, (int)rcBoundingBox.Height),
                SetWindowPosFlags.SWP_NOACTIVATE | SetWindowPosFlags.SWP_NOZORDER);
    }

    public void RefreshSurface()
    {
        if (_handle == IntPtr.Zero)
            return;
        _ = RedrawWindow(
            _handle,
            IntPtr.Zero,
            IntPtr.Zero,
            RedrawWindowFlags.RDW_INVALIDATE |
            RedrawWindowFlags.RDW_UPDATENOW |
            RedrawWindowFlags.RDW_ALLCHILDREN);
    }

    [Flags]
    private enum WindowStyles : uint
    {
        WS_CHILD = 0x40000000,
        WS_VISIBLE = 0x10000000,
        WS_CLIPSIBLINGS = 0x04000000,
        WS_CLIPCHILDREN = 0x02000000,
        SS_BLACKRECT = 0x00000004
    }

    [Flags]
    private enum SetWindowPosFlags : uint
    {
        SWP_NOZORDER = 0x0004,
        SWP_NOACTIVATE = 0x0010
    }

    [Flags]
    private enum RedrawWindowFlags : uint
    {
        RDW_INVALIDATE = 0x0001,
        RDW_ALLCHILDREN = 0x0080,
        RDW_UPDATENOW = 0x0100
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint exStyle,
        string className,
        string windowName,
        WindowStyles style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hwnd,
        IntPtr insertAfter,
        int x,
        int y,
        int cx,
        int cy,
        SetWindowPosFlags flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(
        IntPtr hwnd,
        IntPtr updateRectangle,
        IntPtr updateRegion,
        RedrawWindowFlags flags);
}
