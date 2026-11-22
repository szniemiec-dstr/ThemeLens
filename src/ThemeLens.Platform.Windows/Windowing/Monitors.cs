// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ThemeLens.Engine;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;

namespace ThemeLens.Platform.Windows;

/// <summary>The monitors of the desktop, in physical pixels (section 7.3: per-monitor DPI awareness v2).</summary>
public static unsafe class Monitors
{
    private const double FallbackRefreshRate = 60.0;

    /// <summary>Every monitor, ordered by the number Windows shows for it in Display settings.</summary>
    public static IReadOnlyList<ScreenInfo> Enumerate()
    {
        var found = new List<(HMONITOR Monitor, PixelRect Bounds, string Device, bool Primary)>();
        using (new PhysicalPixels())
        {
            var handles = new List<nint>();
            GCHandle pin = GCHandle.Alloc(handles);
            try
            {
                PInvoke.EnumDisplayMonitors(default, (RECT*)null, &Collect, GCHandle.ToIntPtr(pin));
            }
            finally
            {
                pin.Free();
            }

            foreach (nint handle in handles)
            {
                var monitor = new HMONITOR(handle);
                if (TryDescribe(monitor, out PixelRect bounds, out string device, out bool primary))
                {
                    found.Add((monitor, bounds, device, primary));
                }
            }
        }

        var screens = new List<ScreenInfo>(found.Count);
        var used = new HashSet<int>();
        int next = found.Count + 1;
        foreach ((_, PixelRect bounds, string device, bool primary) in found)
        {
            // The device name is "\\.\DISPLAY2"; its number is the one Windows shows.
            int number = DisplayNumber(device);
            if (number <= 0 || !used.Add(number))
            {
                number = next++;
                used.Add(number);
            }

            screens.Add(new ScreenInfo(number, bounds, primary));
        }

        screens.Sort(static (a, b) => a.Number.CompareTo(b.Number));
        return screens;
    }

    /// <summary>Time between two refreshes of the monitor the rectangle is mostly on.</summary>
    public static TimeSpan RefreshIntervalFor(PixelRect area)
    {
        double rate = FallbackRefreshRate;
        using (new PhysicalPixels())
        {
            if (TryDescribe(MonitorFor(area), out _, out string device, out _))
            {
                var mode = new DEVMODEW { dmSize = (ushort)sizeof(DEVMODEW) };
                if (PInvoke.EnumDisplaySettings(device, ENUM_DISPLAY_SETTINGS_MODE.ENUM_CURRENT_SETTINGS, ref mode)
                    && mode.dmDisplayFrequency > 1)
                {
                    // 0 and 1 both mean "the hardware's default rate", which tells us nothing.
                    rate = mode.dmDisplayFrequency;
                }
            }
        }

        return TimeSpan.FromSeconds(1.0 / Math.Clamp(rate, 24.0, 500.0));
    }

    /// <summary>The monitor a rectangle is mostly on, with its full bounds.</summary>
    internal static bool TryFind(PixelRect area, out HMONITOR monitor, out PixelRect bounds)
    {
        using (new PhysicalPixels())
        {
            monitor = MonitorFor(area);
            return TryDescribe(monitor, out bounds, out _, out _);
        }
    }

    /// <summary>The scale of the monitor a rectangle is mostly on: 1 at 100%, 1.5 at 150%.</summary>
    internal static float ScaleFor(PixelRect area)
    {
        using (new PhysicalPixels())
        {
            HMONITOR monitor = MonitorFor(area);
            return !monitor.IsNull && PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out uint dpi, out _).Succeeded && dpi > 0
                ? dpi / 96f
                : 1f;
        }
    }

    private static HMONITOR MonitorFor(PixelRect area)
    {
        var rect = new RECT(area.X, area.Y, area.Right, area.Bottom);
        return PInvoke.MonitorFromRect(in rect, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
    }

    private static bool TryDescribe(HMONITOR monitor, out PixelRect bounds, out string device, out bool primary)
    {
        bounds = default;
        device = string.Empty;
        primary = false;
        if (monitor.IsNull)
        {
            return false;
        }

        var info = default(MONITORINFOEXW);
        info.monitorInfo.cbSize = (uint)sizeof(MONITORINFOEXW);
        if (!PInvoke.GetMonitorInfo(monitor, ref info.monitorInfo))
        {
            return false;
        }

        RECT r = info.monitorInfo.rcMonitor;
        bounds = new PixelRect(r.left, r.top, r.right - r.left, r.bottom - r.top);
        device = info.szDevice.ToString();
        primary = (info.monitorInfo.dwFlags & PInvoke.MONITORINFOF_PRIMARY) != 0;
        return !bounds.IsEmpty;
    }

    private static int DisplayNumber(string device)
    {
        int end = device.Length;
        int start = end;
        while (start > 0 && char.IsAsciiDigit(device[start - 1]))
        {
            start--;
        }

        return start < end && int.TryParse(device.AsSpan(start, end - start), NumberStyles.None, CultureInfo.InvariantCulture, out int number)
            ? number
            : 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static BOOL Collect(HMONITOR monitor, HDC deviceContext, RECT* bounds, LPARAM state)
    {
        if (GCHandle.FromIntPtr(state.Value).Target is List<nint> handles)
        {
            handles.Add((nint)monitor.Value);
        }

        return true;
    }

    /// <summary>
    /// Monitor rectangles depend on the DPI awareness of the calling thread. This switches the thread to
    /// per-monitor v2 for the duration, so callers get physical pixels whatever the process manifest says.
    /// </summary>
    private readonly struct PhysicalPixels : IDisposable
    {
        private readonly DPI_AWARENESS_CONTEXT _previous;

        public PhysicalPixels()
        {
            _previous = PInvoke.SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        }

        public void Dispose()
        {
            if (!_previous.IsNull)
            {
                PInvoke.SetThreadDpiAwarenessContext(_previous);
            }
        }
    }
}
