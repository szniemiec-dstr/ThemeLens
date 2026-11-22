// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;

namespace ThemeLens.Platform.Windows;

/// <summary>
/// Diagnostics for developers. Only short texts about what the app itself did go here: never a frame, a
/// thumbnail, a colour found on screen or anything else derived from screen content (NFR-02).
/// </summary>
internal static class PlatformLog
{
    public static void Warn(string message) => Trace.TraceWarning("ThemeLens: " + message);

    public static void Warn(string what, Exception exception) =>
        Trace.TraceWarning($"ThemeLens: {what}: {exception.GetType().Name} 0x{exception.HResult:X8} {exception.Message}");
}
