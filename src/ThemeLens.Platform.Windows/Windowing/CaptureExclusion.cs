// SPDX-License-Identifier: GPL-3.0-or-later
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace ThemeLens.Platform.Windows;

/// <summary>
/// Keeps a window out of screen capture (section 7.3). The lens, its handle and the control window all need
/// this: the capture then sees the desktop as if they were not there, so the lens never captures its own
/// picture and the control window is never recoloured. Needs Windows 10 version 2004 or later.
/// </summary>
public static class CaptureExclusion
{
    /// <summary>Excludes a window of this process from capture. Returns false when Windows refuses.</summary>
    public static bool Apply(nint windowHandle) =>
        windowHandle != 0
        && PInvoke.SetWindowDisplayAffinity(new HWND(windowHandle), WINDOW_DISPLAY_AFFINITY.WDA_EXCLUDEFROMCAPTURE);

    /// <summary>True when the window is currently excluded from capture.</summary>
    public static bool IsApplied(nint windowHandle) =>
        windowHandle != 0
        && PInvoke.GetWindowDisplayAffinity(new HWND(windowHandle), out uint affinity)
        && affinity == (uint)WINDOW_DISPLAY_AFFINITY.WDA_EXCLUDEFROMCAPTURE;
}
