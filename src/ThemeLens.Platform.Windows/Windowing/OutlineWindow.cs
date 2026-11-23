// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Core.Colour;
using ThemeLens.Engine;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace ThemeLens.Platform.Windows;

/// <summary>
/// The outline of the lens (figures 3 and 4): thin while the lens is locked, thick in edit mode. It is a
/// window of its own, cut down to a hollow frame with a window region, so it can change at once without the
/// lens picture being drawn again. Like the lens it is click-through, never takes focus and is excluded from
/// capture.
/// </summary>
internal sealed unsafe class OutlineWindow : NativeWindow
{
    private const int LockedDip = 1;
    private const int EditDip = 3;

    /// <summary>Side of a resize grip in edit mode. The lens takes a drag as a resize within the same distance of its edge.</summary>
    private const int GripDip = 12;

    private readonly bool _devMode;
    private readonly Rgb8 _colour;
    private PixelRect _lens;
    private bool _wanted;
    private bool _edit;
    private bool _shown;
    private (int Width, int Height, int Line, int Grip) _shape;

    public OutlineWindow(bool devMode, Rgb8 colour)
    {
        _devMode = devMode;
        _colour = colour;
        WINDOW_EX_STYLE style = WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_TRANSPARENT
            | WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW;
        if (!devMode)
        {
            style |= WINDOW_EX_STYLE.WS_EX_TOPMOST;
        }

        Create("ThemeLens.Outline", 0, string.Empty, WINDOW_STYLE.WS_POPUP, style, new PixelRect(0, 0, 1, 1));
        PInvoke.SetLayeredWindowAttributes(Hwnd, default, 255, LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_ALPHA);
        CaptureExclusion.Apply(Handle);
    }

    public void Follow(PixelRect lens)
    {
        _lens = lens;
        Layout();
    }

    public void SetVisible(bool visible)
    {
        if (_wanted != visible)
        {
            _wanted = visible;
            Layout();
        }
    }

    public void SetEditMode(bool edit)
    {
        if (_edit != edit)
        {
            _edit = edit;
            Layout();
        }
    }

    /// <summary>Puts the outline back above the lens after the lens has been raised.</summary>
    public void Raise()
    {
        if (_shown)
        {
            PInvoke.SetWindowPos(
                Hwnd,
                _devMode ? HWND.HWND_TOP : HWND.HWND_TOPMOST,
                0,
                0,
                0,
                0,
                SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
        }
    }

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

    private void Layout()
    {
        if (_lens.IsEmpty || !_wanted)
        {
            if (_shown)
            {
                _shown = false;
                PInvoke.ShowWindow(Hwnd, SHOW_WINDOW_CMD.SW_HIDE);
            }

            return;
        }

        float scale = Monitors.ScaleFor(_lens);
        int line = Math.Max(1, (int)MathF.Round((_edit ? EditDip : LockedDip) * scale));
        line = Math.Min(line, Math.Min(_lens.Width, _lens.Height) / 2);
        int grip = _edit ? Math.Max(line, (int)MathF.Round(GripDip * scale)) : 0;
        grip = Math.Min(grip, Math.Min(_lens.Width, _lens.Height) / 4);
        var shape = (_lens.Width, _lens.Height, line, grip);
        if (shape != _shape)
        {
            // Everything but a frame along the edge is cut out of the window; the region belongs to Windows once set.
            HRGN frame = PInvoke.CreateRectRgn(0, 0, _lens.Width, _lens.Height);
            HRGN hole = PInvoke.CreateRectRgn(line, line, _lens.Width - line, _lens.Height - line);
            PInvoke.CombineRgn(frame, frame, hole, RGN_COMBINE_MODE.RGN_DIFF);
            PInvoke.DeleteObject(hole);
            if (grip > 0)
            {
                // Edit mode (figure 4): a grip on every corner and in the middle of every edge shows where the
                // lens can be dragged to resize it. The outline stays click-through; the lens underneath takes the drag.
                int right = _lens.Width - grip;
                int bottom = _lens.Height - grip;
                int centre = right / 2;
                int middle = bottom / 2;
                ReadOnlySpan<(int X, int Y)> grips =
                [
                    (0, 0), (centre, 0), (right, 0),
                    (0, middle), (right, middle),
                    (0, bottom), (centre, bottom), (right, bottom),
                ];
                foreach ((int x, int y) in grips)
                {
                    HRGN square = PInvoke.CreateRectRgn(x, y, x + grip, y + grip);
                    PInvoke.CombineRgn(frame, frame, square, RGN_COMBINE_MODE.RGN_OR);
                    PInvoke.DeleteObject(square);
                }
            }

            if (PInvoke.SetWindowRgn(Hwnd, frame, true) == 0)
            {
                PInvoke.DeleteObject(frame);
            }

            _shape = shape;
        }

        SET_WINDOW_POS_FLAGS flags = SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW;
        if (_shown)
        {
            flags |= SET_WINDOW_POS_FLAGS.SWP_NOZORDER;
        }

        _shown = true;
        PInvoke.SetWindowPos(
            Hwnd, _devMode ? HWND.HWND_TOP : HWND.HWND_TOPMOST, _lens.X, _lens.Y, _lens.Width, _lens.Height, flags);
        PInvoke.InvalidateRect(Hwnd, (RECT*)null, true);
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
