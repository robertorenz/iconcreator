using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace IconCreator.Views;

/// <summary>Applies the Windows dark title bar to a WPF window.</summary>
public static class NativeChrome
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void ApplyDarkTitleBar(Window window)
    {
        void Apply()
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            int on = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
        }

        if (window.IsLoaded) Apply();
        else window.SourceInitialized += (_, _) => Apply();
    }
}
