// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Engine;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace ThemeLens.Platform.Windows;

/// <summary>
/// A window that is never shown and exists to be told things: global shortcuts, a changed display layout,
/// and the machine waking up. It is an ordinary top-level window, because Windows does not broadcast display
/// and power messages to message-only windows.
/// </summary>
internal sealed class SystemEventsWindow : NativeWindow
{
    private const nuint SettleTimer = 1;

    /// <summary>A display change arrives as a burst of messages; act once, when it has gone quiet for this long.</summary>
    private const uint SettleMilliseconds = 250;

    public SystemEventsWindow()
    {
        Create(
            "ThemeLens.Events",
            0,
            string.Empty,
            WINDOW_STYLE.WS_POPUP,
            WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE,
            new PixelRect(0, 0, 0, 0));
    }

    public event Action<HotkeyAction>? HotkeyPressed;

    /// <summary>
    /// The display changed under the lens: a monitor connected or removed, a new resolution or scale, or the
    /// machine came back from sleep (FR-19).
    /// </summary>
    public event Action? DisplayChanged;

    protected override unsafe bool OnMessage(uint message, WPARAM wParam, LPARAM lParam, out LRESULT result)
    {
        result = default;
        switch (message)
        {
            case PInvoke.WM_HOTKEY:
                if (Hotkeys.ActionOf(wParam.Value) is HotkeyAction action)
                {
                    HotkeyPressed?.Invoke(action);
                }

                return true;

            case PInvoke.WM_DISPLAYCHANGE:
            case PInvoke.WM_DPICHANGED:
                PInvoke.SetTimer(Hwnd, SettleTimer, SettleMilliseconds, null);
                return false;

            case PInvoke.WM_POWERBROADCAST
                when wParam.Value is PInvoke.PBT_APMRESUMEAUTOMATIC or PInvoke.PBT_APMRESUMESUSPEND:
                PInvoke.SetTimer(Hwnd, SettleTimer, SettleMilliseconds, null);
                return false;

            case PInvoke.WM_TIMER when wParam.Value == SettleTimer:
                PInvoke.KillTimer(Hwnd, SettleTimer);
                DisplayChanged?.Invoke();
                return true;

            default:
                return false;
        }
    }
}
