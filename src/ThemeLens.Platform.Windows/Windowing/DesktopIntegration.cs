// SPDX-License-Identifier: GPL-3.0-or-later
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.HiDpi;

namespace ThemeLens.Platform.Windows;

/// <summary>Small things the app's own windows need from Windows.</summary>
public static class DesktopIntegration
{
    /// <summary>
    /// Makes the process per-monitor DPI aware (version 2), so that every window works in physical pixels
    /// (section 7.3). The app manifest already says so; this covers starts that bypass the manifest, such as
    /// <c>dotnet ThemeLens.App.dll</c> under a debugger. Call it before the first window exists. Returns false
    /// when the awareness was already fixed, which is fine.
    /// </summary>
    public static bool EnsurePerMonitorDpiAwareness() =>
        PInvoke.SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

    /// <summary>
    /// Asks Windows for a dark title bar on a window of this process. The app is dark from the first pixel
    /// (section 11); a bright title bar above a black window would undo that. Does nothing where Windows does
    /// not know the setting.
    /// </summary>
    public static unsafe bool UseDarkTitleBar(nint windowHandle)
    {
        if (windowHandle == 0 || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            return false;
        }

        BOOL dark = true;
        return PInvoke.DwmSetWindowAttribute(
            new HWND(windowHandle), DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE, &dark, (uint)sizeof(BOOL)).Succeeded;
    }
}
