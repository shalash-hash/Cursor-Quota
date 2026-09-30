using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Quota.Helpers;

internal static class NativeWindowHelper
{
    private const int SwRestore = 9;
    private const int SwShow = 5;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    /// <summary>
    /// Win32 restore/foreground for tray activation when WPF Activate() is not enough.
    /// </summary>
    public static void RestoreForeground(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;

        ShowWindow(handle, IsIconic(handle) ? SwRestore : SwShow);
        SetForegroundWindow(handle);
    }
}
