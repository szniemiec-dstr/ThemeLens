// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Core.Colour;
using ThemeLens.Engine;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace ThemeLens.Platform.Windows;

/// <summary>The colours of the handle. The defaults are the app's own dark look: black, off-white text, amber accent.</summary>
public sealed record HandleColours(Rgb8 Background, Rgb8 Text, Rgb8 Accent)
{
    public static HandleColours Default { get; } =
        new(Rgb8.Black, Rgb8.FromHex("#C9C4B8"), Rgb8.FromHex("#E0A44A"));
}

/// <summary>
/// The handle (figure 3): a small window on the top edge of the lens, and the only part of the lens that takes
/// clicks while it is locked. Drag it to move the lens; double-click it to fill the screen; its two buttons
/// switch edit mode and fill-screen. It never takes focus and is excluded from capture like the lens itself.
/// </summary>
/// <remarks>
/// Painted with plain GDI. The text is whatever the app passes in ("Lens: Soft dark" in the user's language);
/// nothing here is hard-coded English. The window text is set to the same string, which is the name assistive
/// technology reads for it; the two buttons are not exposed to UI Automation yet.
/// </remarks>
internal sealed unsafe class HandleWindow : NativeWindow
{
    private const string ClassName = "ThemeLens.Handle";
    private const int HeightDip = 32;
    private const int ButtonWidthDip = 44;
    private const int PaddingDip = 10;
    private const int MinimumLabelDip = 72;
    private const int MaximumWidthDip = 420;
    private const int DragThresholdDip = 4;
    private const int FontHeightDip = 15;

    private readonly bool _devMode;
    private readonly HandleColours _colours;
    private string _text = string.Empty;
    private PixelRect _lens;
    private PixelRect _screen;
    private PixelRect _bounds;
    private float _scale = 1f;
    private HFONT _font;
    private float _fontScale;
    private bool _wanted;
    private bool _shown;
    private bool _editOn;
    private bool _fillOn;

    private Part _pressed = Part.None;
    private bool _dragging;
    private System.Drawing.Point _pressPoint;

    public HandleWindow(bool devMode, HandleColours colours)
    {
        _devMode = devMode;
        _colours = colours;
        WINDOW_EX_STYLE style = WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW;
        if (!devMode)
        {
            style |= WINDOW_EX_STYLE.WS_EX_TOPMOST;
        }

        Create(ClassName, WNDCLASS_STYLES.CS_DBLCLKS, string.Empty, WINDOW_STYLE.WS_POPUP, style, new PixelRect(0, 0, 1, 1));
        CaptureExclusion.Apply(Handle);
    }

    private enum Part
    {
        None,
        Label,
        EditButton,
        FillButton,
    }

    /// <summary>The user started to drag the handle.</summary>
    public event Action? DragStarted;

    /// <summary>The handle is being dragged: how far the pointer is from where the drag started, in pixels.</summary>
    public event Action<int, int>? Dragged;

    public event Action? EditClicked;

    /// <summary>The fill-screen button, or a double-click on the label.</summary>
    public event Action? FillClicked;

    public void SetText(string text)
    {
        if (_text == text)
        {
            return;
        }

        _text = text;
        PInvoke.SetWindowText(Hwnd, text);
        Layout();
    }

    /// <summary>Lights the buttons that are switched on.</summary>
    public void SetState(bool editOn, bool fillOn)
    {
        if (_editOn != editOn || _fillOn != fillOn)
        {
            _editOn = editOn;
            _fillOn = fillOn;
            Repaint();
        }
    }

    /// <summary>Puts the handle on the top edge of the lens: above it where there is room, otherwise just inside it.</summary>
    public void Follow(PixelRect lens, PixelRect screen)
    {
        _lens = lens;
        _screen = screen;
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

    /// <summary>Puts the handle back above the lens after the lens has been raised.</summary>
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

    public void Release()
    {
        if (!_font.IsNull)
        {
            PInvoke.DeleteObject(_font);
            _font = default;
        }
    }

    protected override bool OnMessage(uint message, WPARAM wParam, LPARAM lParam, out LRESULT result)
    {
        result = default;
        switch (message)
        {
            case PInvoke.WM_MOUSEACTIVATE:
                result = new LRESULT((nint)PInvoke.MA_NOACTIVATE);
                return true;

            case PInvoke.WM_ERASEBKGND:
                result = new LRESULT(1);
                return true;

            case PInvoke.WM_PAINT:
                Paint();
                return true;

            case PInvoke.WM_LBUTTONDOWN:
                Press(LowWord(lParam.Value));
                return true;

            case PInvoke.WM_LBUTTONDBLCLK:
                if (PartAt(LowWord(lParam.Value)) == Part.Label)
                {
                    FillClicked?.Invoke();
                }
                else
                {
                    Press(LowWord(lParam.Value));
                }

                return true;

            case PInvoke.WM_MOUSEMOVE when _pressed == Part.Label:
                Drag();
                return true;

            case PInvoke.WM_LBUTTONUP when _pressed != Part.None:
                ReleasePress(LowWord(lParam.Value), HighWord(lParam.Value));
                return true;

            case PInvoke.WM_CAPTURECHANGED:
                _pressed = Part.None;
                _dragging = false;
                return false;

            case PInvoke.WM_DPICHANGED:
                Layout();
                return true;

            default:
                return false;
        }
    }

    private int Scaled(int dip) => Math.Max(1, (int)MathF.Round(dip * _scale));

    private Part PartAt(int x)
    {
        int button = Scaled(ButtonWidthDip);
        if (x >= _bounds.Width - button)
        {
            return Part.FillButton;
        }

        return x >= _bounds.Width - (2 * button) ? Part.EditButton : Part.Label;
    }

    private void Press(int x)
    {
        _pressPoint = MessagePoint();
        _pressed = PartAt(x);
        _dragging = false;
        PInvoke.SetCapture(Hwnd);
    }

    private void Drag()
    {
        System.Drawing.Point pointer = MessagePoint();
        int dx = pointer.X - _pressPoint.X;
        int dy = pointer.Y - _pressPoint.Y;
        if (!_dragging)
        {
            int threshold = Scaled(DragThresholdDip);
            if (Math.Abs(dx) < threshold && Math.Abs(dy) < threshold)
            {
                return;
            }

            _dragging = true;
            DragStarted?.Invoke();
        }

        Dragged?.Invoke(dx, dy);
    }

    private void ReleasePress(int x, int y)
    {
        Part pressed = _pressed;
        bool dragged = _dragging;
        _pressed = Part.None;
        _dragging = false;
        PInvoke.ReleaseCapture();
        if (dragged || y < 0 || y >= _bounds.Height || x < 0 || x >= _bounds.Width || PartAt(x) != pressed)
        {
            return;
        }

        if (pressed == Part.EditButton)
        {
            EditClicked?.Invoke();
        }
        else if (pressed == Part.FillButton)
        {
            FillClicked?.Invoke();
        }
    }

    private void Layout()
    {
        if (_lens.IsEmpty || _screen.IsEmpty)
        {
            return;
        }

        _scale = Monitors.ScaleFor(_lens);
        EnsureFont();
        int height = Scaled(HeightDip);
        int buttons = 2 * Scaled(ButtonWidthDip);
        int padding = Scaled(PaddingDip);
        int label = Math.Max(MeasureText() + (2 * padding), Scaled(MinimumLabelDip));
        int width = Math.Min(label + buttons, Math.Max(Scaled(MaximumWidthDip), buttons + Scaled(MinimumLabelDip)));
        width = Math.Min(width, _screen.Width);

        int x = Math.Clamp(_lens.X, _screen.X, Math.Max(_screen.X, _screen.Right - width));
        int above = _lens.Y - height;
        int y = above >= _screen.Y ? above : _lens.Y;
        _bounds = new PixelRect(x, y, width, height);

        SET_WINDOW_POS_FLAGS flags = SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE;
        if (_wanted)
        {
            flags |= SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW;
        }
        else
        {
            flags |= SET_WINDOW_POS_FLAGS.SWP_HIDEWINDOW | SET_WINDOW_POS_FLAGS.SWP_NOZORDER;
        }

        _shown = _wanted;
        PInvoke.SetWindowPos(Hwnd, _devMode ? HWND.HWND_TOP : HWND.HWND_TOPMOST, x, y, width, height, flags);
        Repaint();
    }

    private void Repaint() => PInvoke.InvalidateRect(Hwnd, (RECT*)null, false);

    private void EnsureFont()
    {
        if (!_font.IsNull && _fontScale == _scale)
        {
            return;
        }

        Release();
        _fontScale = _scale;
        const int semiBold = 600;
        _font = PInvoke.CreateFont(
            -Scaled(FontHeightDip),
            0,
            0,
            0,
            semiBold,
            0,
            0,
            0,
            FONT_CHARSET.DEFAULT_CHARSET,
            FONT_OUTPUT_PRECISION.OUT_DEFAULT_PRECIS,
            FONT_CLIP_PRECISION.CLIP_DEFAULT_PRECIS,
            FONT_QUALITY.CLEARTYPE_QUALITY,
            0,
            "Segoe UI");
    }

    private int MeasureText()
    {
        if (_text.Length == 0 || _font.IsNull)
        {
            return 0;
        }

        HDC dc = PInvoke.GetDC(Hwnd);
        if (dc.IsNull)
        {
            return 0;
        }

        try
        {
            HGDIOBJ previous = PInvoke.SelectObject(dc, _font);
            var box = new RECT(0, 0, 0, 0);
            fixed (char* text = _text)
            {
                PInvoke.DrawText(
                    dc,
                    text,
                    _text.Length,
                    ref box,
                    DRAW_TEXT_FORMAT.DT_CALCRECT | DRAW_TEXT_FORMAT.DT_SINGLELINE | DRAW_TEXT_FORMAT.DT_NOPREFIX);
            }

            PInvoke.SelectObject(dc, previous);
            return box.right - box.left;
        }
        finally
        {
            _ = PInvoke.ReleaseDC(Hwnd, dc);
        }
    }

    private void Paint()
    {
        HDC dc = PInvoke.BeginPaint(Hwnd, out PAINTSTRUCT paint);
        if (dc.IsNull)
        {
            return;
        }

        HBRUSH background = PInvoke.CreateSolidBrush(ToColorRef(_colours.Background));
        HBRUSH text = PInvoke.CreateSolidBrush(ToColorRef(_colours.Text));
        HBRUSH accent = PInvoke.CreateSolidBrush(ToColorRef(_colours.Accent));
        try
        {
            int width = _bounds.Width;
            int height = _bounds.Height;
            int button = Scaled(ButtonWidthDip);
            int line = Scaled(2);

            Fill(dc, 0, 0, width, height, background);
            Frame(dc, 0, 0, width, height, line, accent);

            if (_text.Length > 0 && !_font.IsNull)
            {
                HGDIOBJ previous = PInvoke.SelectObject(dc, _font);
                PInvoke.SetBkMode(dc, BACKGROUND_MODE.TRANSPARENT);
                PInvoke.SetTextColor(dc, ToColorRef(_colours.Text));
                var box = new RECT(Scaled(PaddingDip), 0, width - (2 * button), height);
                fixed (char* label = _text)
                {
                    PInvoke.DrawText(
                        dc,
                        label,
                        _text.Length,
                        ref box,
                        DRAW_TEXT_FORMAT.DT_SINGLELINE | DRAW_TEXT_FORMAT.DT_VCENTER | DRAW_TEXT_FORMAT.DT_LEFT
                        | DRAW_TEXT_FORMAT.DT_END_ELLIPSIS | DRAW_TEXT_FORMAT.DT_NOPREFIX);
                }

                PInvoke.SelectObject(dc, previous);
            }

            // Edit mode: a rectangle with a grip on each corner.
            int editCentre = width - (2 * button) + (button / 2);
            int half = Scaled(8);
            int grip = Scaled(5);
            HBRUSH editBrush = _editOn ? accent : text;
            Frame(dc, editCentre - half, (height / 2) - half, 2 * half, 2 * half, line, editBrush);
            foreach ((int sx, int sy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
            {
                Fill(dc, editCentre + (sx * half) - (grip / 2), (height / 2) + (sy * half) - (grip / 2), grip, grip, editBrush);
            }

            // Fill this screen: a screen outline with a solid top bar.
            int fillCentre = width - (button / 2);
            int halfWide = Scaled(10);
            int halfHigh = Scaled(7);
            HBRUSH fillBrush = _fillOn ? accent : text;
            Frame(dc, fillCentre - halfWide, (height / 2) - halfHigh, 2 * halfWide, 2 * halfHigh, line, fillBrush);
            Fill(dc, fillCentre - halfWide, (height / 2) - halfHigh, 2 * halfWide, Scaled(4), fillBrush);
        }
        finally
        {
            PInvoke.DeleteObject(background);
            PInvoke.DeleteObject(text);
            PInvoke.DeleteObject(accent);
            PInvoke.EndPaint(Hwnd, in paint);
        }
    }

    private static void Fill(HDC dc, int x, int y, int width, int height, HBRUSH brush)
    {
        var rect = new RECT(x, y, x + width, y + height);
        _ = PInvoke.FillRect(dc, in rect, brush);
    }

    private static void Frame(HDC dc, int x, int y, int width, int height, int line, HBRUSH brush)
    {
        Fill(dc, x, y, width, line, brush);
        Fill(dc, x, y + height - line, width, line, brush);
        Fill(dc, x, y, line, height, brush);
        Fill(dc, x + width - line, y, line, height, brush);
    }
}
