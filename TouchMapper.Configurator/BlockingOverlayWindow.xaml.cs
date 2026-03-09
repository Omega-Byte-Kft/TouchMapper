using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using GdiRect = System.Drawing.Rectangle;

namespace TouchMapper.Configurator;

/// <summary>
/// Semi-transparent overlay that blocks input on non-target monitors
/// during test or auto-detect operations.
/// </summary>
public partial class BlockingOverlayWindow : Window
{
    public BlockingOverlayWindow(GdiRect bounds)
    {
        InitializeComponent();
        WindowStartupLocation = WindowStartupLocation.Manual;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowPos(hwnd, HWND_TOPMOST,
                bounds.Left, bounds.Top, bounds.Width, bounds.Height,
                SWP_SHOWWINDOW);
        };
    }

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_SHOWWINDOW = 0x0040;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);
}
