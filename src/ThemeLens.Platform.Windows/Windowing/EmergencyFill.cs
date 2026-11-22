// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Core.Colour;
using ThemeLens.Engine;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace ThemeLens.Platform.Windows;

/// <summary>
/// The last line of "background first" (FR-11): a plain popup in one solid colour that can cover the lens
/// without the frame loop, the GPU or the lens window's own thread. The watchdog shows it when the frame loop
/// has stalled, and the surface falls back to it when Direct3D cannot draw.
/// </summary>
/// <remarks>
/// The window lives on a small thread of its own and is painted with GDI, so nothing it needs can be held up by
/// whatever went wrong. <see cref="Show"/> and <see cref="Hide"/> take no lock and return at once.
/// </remarks>
internal sealed class EmergencyFill : IDisposable
{
    private readonly WindowThread _thread;
    private FillWindow? _window;

    public EmergencyFill(bool devMode)
    {
        _thread = new WindowThread("ThemeLens emergency fill");
        try
        {
            _thread.Invoke(() => _window = new FillWindow(devMode));
        }
        catch
        {
            _thread.Dispose();
            throw;
        }
    }

    public void Show(PixelRect bounds, Rgb8 colour) => _thread.Post(() => _window?.Show(bounds, colour));

    public void Hide() => _thread.Post(() => _window?.Hide());

    public void Dispose()
    {
        try
        {
            _thread.Invoke(() =>
            {
                _window?.Destroy();
                _window = null;
            });
        }
        catch (ObjectDisposedException)
        {
        }

        _thread.Dispose();
    }

    private sealed unsafe class FillWindow : NativeWindow
    {
        private readonly bool _devMode;
        private Rgb8 _colour;

        public FillWindow(bool devMode)
        {
            _devMode = devMode;

            // Click-through like the lens, and with an ordinary redirection surface so plain GDI can fill it.
            WINDOW_EX_STYLE style = WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_TRANSPARENT
                | WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW;
            if (!devMode)
            {
                style |= WINDOW_EX_STYLE.WS_EX_TOPMOST;
            }

            Create("ThemeLens.Fill", 0, string.Empty, WINDOW_STYLE.WS_POPUP, style, new PixelRect(0, 0, 1, 1));
            PInvoke.SetLayeredWindowAttributes(Hwnd, default, 255, LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_ALPHA);
            CaptureExclusion.Apply(Handle);
        }

        public void Show(PixelRect bounds, Rgb8 colour)
        {
            if (bounds.IsEmpty)
            {
                return;
            }

            _colour = colour;
            PInvoke.SetWindowPos(
                Hwnd,
                _devMode ? HWND.HWND_TOP : HWND.HWND_TOPMOST,
                bounds.X,
                bounds.Y,
                bounds.Width,
                bounds.Height,
                SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);
            PInvoke.InvalidateRect(Hwnd, (RECT*)null, true);
        }

        public void Hide() => PInvoke.ShowWindow(Hwnd, SHOW_WINDOW_CMD.SW_HIDE);

        protected override bool OnMessage(uint message, WPARAM wParam, LPARAM lParam, out LRESULT result)
        {
            result = default;
            switch (message)
            {
                case PInvoke.WM_NCHITTEST:
                    result = new LRESULT(PInvoke.HTTRANSPARENT);
                    return true;

                case PInvoke.WM_MOUSEACTIVATE:
                    result = new LRESULT((nint)PInvoke.MA_NOACTIVATE);
                    return true;

                case PInvoke.WM_ERASEBKGND:
                    FillWith(new HDC((nint)wParam.Value));
                    result = new LRESULT(1);
                    return true;

                case PInvoke.WM_PAINT:
                    HDC dc = PInvoke.BeginPaint(Hwnd, out PAINTSTRUCT paint);
                    if (!dc.IsNull)
                    {
                        FillWith(dc);
                        PInvoke.EndPaint(Hwnd, in paint);
                    }

                    return true;

                default:
                    return false;
            }
        }

        private void FillWith(HDC dc)
        {
            if (dc.IsNull || !PInvoke.GetClientRect(Hwnd, out RECT client))
            {
                return;
            }

            HBRUSH brush = PInvoke.CreateSolidBrush(ToColorRef(_colour));
            _ = PInvoke.FillRect(dc, in client, brush);
            PInvoke.DeleteObject(brush);
        }
    }
}
