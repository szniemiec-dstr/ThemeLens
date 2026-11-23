// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using ThemeLens.Engine;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace ThemeLens.Platform.Windows;

/// <summary>
/// The lens window (section 7.3): a borderless popup that is topmost, never takes focus and lets every click,
/// wheel turn, touch and drag fall through to the window underneath. It has no redirection bitmap; its picture
/// comes from a swap chain attached through DirectComposition by <see cref="WindowsLensSurface"/>.
/// </summary>
/// <remarks>
/// In edit mode (section 4.5) the window loses <c>WS_EX_TRANSPARENT</c>, so its body takes the pointer: drag
/// inside to move, drag near an edge or corner to resize. It also takes the keyboard for as long as the user
/// stays on it: arrow keys move, Shift+arrow keys resize, Enter or Esc lock it again.
/// </remarks>
internal sealed unsafe class LensWindow : NativeWindow
{
    /// <summary>The extended styles of the locked lens, exactly as listed in section 7.3.</summary>
    public const WINDOW_EX_STYLE LockedStyle =
        WINDOW_EX_STYLE.WS_EX_LAYERED
        | WINDOW_EX_STYLE.WS_EX_TRANSPARENT
        | WINDOW_EX_STYLE.WS_EX_TOPMOST
        | WINDOW_EX_STYLE.WS_EX_NOACTIVATE
        | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW
        | WINDOW_EX_STYLE.WS_EX_NOREDIRECTIONBITMAP;

    private const string ClassName = "ThemeLens.Lens";
    private const nuint FadeTimer = 1;
    private const uint FadeTickMilliseconds = 15;
    private const nuint OrderTimer = 2;
    private const uint OrderTickMilliseconds = 200;
    private const int OrderWalkLimit = 64;
    private const int GripDip = 12;
    private const int KeyStepDip = 16;

    private readonly bool _devMode;
    private PixelRect _bounds;
    private bool _visible;
    private bool _edit;
    private HWND _previousForeground;

    private bool _dragging;
    private Zone _dragZone;
    private System.Drawing.Point _dragStart;
    private PixelRect _dragBounds;

    private bool _fading;
    private long _fadeStarted;
    private TimeSpan _fadeDuration;

    /// <summary>A window the lens went above once and found above itself again: left alone until another one turns up.</summary>
    private HWND _keptBelow;

    /// <param name="devMode">True to leave the window out of the topmost band (section 10.2).</param>
    /// <param name="bounds">Where the window starts, in physical pixels. It starts hidden.</param>
    public LensWindow(bool devMode, PixelRect bounds)
    {
        _devMode = devMode;
        _bounds = bounds;
        WINDOW_EX_STYLE style = devMode ? LockedStyle & ~WINDOW_EX_STYLE.WS_EX_TOPMOST : LockedStyle;
        Create(ClassName, 0, "ThemeLens", WINDOW_STYLE.WS_POPUP, style, bounds);

        // A layered window stays invisible until its attributes have been set once.
        PInvoke.SetLayeredWindowAttributes(Hwnd, default, 255, LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_ALPHA);

        // No feedback loop: the capture sees the desktop as if the lens were not there. Without this the lens
        // would recolour its own picture again and again, so a refusal is a reason not to start at all.
        if (!CaptureExclusion.Apply(Handle) || !CaptureExclusion.IsApplied(Handle))
        {
            Destroy();
            throw new InvalidOperationException(PlatformStrings.LensCannotBeExcluded);
        }
    }

    [Flags]
    private enum Zone
    {
        Move = 0,
        Left = 1,
        Right = 2,
        Top = 4,
        Bottom = 8,
    }

    /// <summary>The user dragged the lens or one of its edges: the rectangle asked for, not yet fitted to a screen.</summary>
    public event Action<PixelRect>? BoundsRequested;

    /// <summary>An arrow key in edit mode: a step in pixels, and whether Shift was held (resize instead of move).</summary>
    public event Action<int, int, bool>? Nudged;

    /// <summary>Enter or Esc in edit mode.</summary>
    public event Action? EditFinished;

    /// <summary>Sets the opacity of the lens picture, 0 to 1. Supplied by the surface, which owns the composition visual.</summary>
    public Action<float>? ApplyOpacity { get; set; }

    /// <summary>
    /// The lens went back above a window that had come up over it. Supplied by the surface, which then raises
    /// what belongs above the lens (its outline, its handle, the control window).
    /// </summary>
    public Action? RaisedAgain { get; set; }

    public bool IsEditMode => _edit;

    public bool IsVisible => _visible;

    /// <summary>The extended styles Windows reports for the window right now.</summary>
    public WINDOW_EX_STYLE ExtendedStyle => (WINDOW_EX_STYLE)PInvoke.GetWindowLong(Hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);

    public void Show()
    {
        CancelFade();
        if (_visible)
        {
            return;
        }

        _visible = true;
        PInvoke.SetWindowPos(
            Hwnd,
            _devMode ? HWND.HWND_TOP : HWND.HWND_TOPMOST,
            _bounds.X,
            _bounds.Y,
            _bounds.Width,
            _bounds.Height,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);
        if (!_devMode)
        {
            _keptBelow = default;
            PInvoke.SetTimer(Hwnd, OrderTimer, OrderTickMilliseconds, null);
        }

        if (_edit)
        {
            TakeKeyboard();
        }
    }

    /// <summary>Takes the window off the screen, fading its picture out first unless Windows has animations turned off.</summary>
    public void Hide(TimeSpan fade)
    {
        if (!_visible)
        {
            return;
        }

        if (fade <= TimeSpan.Zero || ApplyOpacity is null || !AnimationsEnabled())
        {
            HideNow();
            return;
        }

        if (!_fading)
        {
            _fading = true;
            _fadeStarted = Stopwatch.GetTimestamp();
            _fadeDuration = fade;
            PInvoke.SetTimer(Hwnd, FadeTimer, FadeTickMilliseconds, null);
        }
    }

    public void Move(PixelRect bounds)
    {
        _bounds = bounds;
        PInvoke.SetWindowPos(
            Hwnd,
            default,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER);
    }

    /// <summary>Switches between locked (click-through) and edit mode (the body takes input).</summary>
    public void SetEditMode(bool edit)
    {
        if (_edit == edit)
        {
            return;
        }

        _edit = edit;
        WINDOW_EX_STYLE style = ExtendedStyle;
        style = edit ? style & ~WINDOW_EX_STYLE.WS_EX_TRANSPARENT : style | WINDOW_EX_STYLE.WS_EX_TRANSPARENT;
        PInvoke.SetWindowLong(Hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, (int)style);
        PInvoke.SetWindowPos(
            Hwnd,
            default,
            0,
            0,
            0,
            0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER
            | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED);

        if (edit)
        {
            _previousForeground = PInvoke.GetForegroundWindow();
            if (_visible)
            {
                TakeKeyboard();
            }
        }
        else
        {
            EndDrag();

            // Locked again: the lens must not sit on the keyboard focus (FR-03). Hand it back to whoever had it.
            if (PInvoke.GetForegroundWindow() == Hwnd && !_previousForeground.IsNull && PInvoke.IsWindow(_previousForeground))
            {
                PInvoke.SetForegroundWindow(_previousForeground);
            }

            _previousForeground = default;
        }
    }

    protected override bool OnMessage(uint message, WPARAM wParam, LPARAM lParam, out LRESULT result)
    {
        result = default;
        switch (message)
        {
            case PInvoke.WM_NCHITTEST:
                // Locked: not there at all, as far as the pointer is concerned (FR-02).
                result = new LRESULT(_edit ? (nint)PInvoke.HTCLIENT : PInvoke.HTTRANSPARENT);
                return true;

            case PInvoke.WM_MOUSEACTIVATE:
                // A click never activates the lens by itself; edit mode asks for the keyboard explicitly.
                result = new LRESULT((nint)PInvoke.MA_NOACTIVATE);
                return true;

            case PInvoke.WM_ERASEBKGND:
                result = new LRESULT(1);
                return true;

            case PInvoke.WM_SETCURSOR when _edit && LowWord(lParam.Value) == PInvoke.HTCLIENT:
                PInvoke.SetCursor(PInvoke.LoadCursor(default, CursorFor(_dragging ? _dragZone : ZoneUnderPointer())));
                result = new LRESULT(1);
                return true;

            case PInvoke.WM_LBUTTONDOWN when _edit:
                TakeKeyboard();
                BeginDrag(ZoneAt(LowWord(lParam.Value), HighWord(lParam.Value)));
                return true;

            case PInvoke.WM_MOUSEMOVE when _dragging:
                ContinueDrag();
                return true;

            case PInvoke.WM_LBUTTONUP when _dragging:
                ContinueDrag();
                EndDrag();
                return true;

            case PInvoke.WM_CAPTURECHANGED:
                _dragging = false;
                return false;

            case PInvoke.WM_KEYDOWN when _edit:
                return OnKey((VIRTUAL_KEY)(ushort)wParam.Value);

            case PInvoke.WM_TIMER when wParam.Value == FadeTimer:
                StepFade();
                return true;

            case PInvoke.WM_TIMER when wParam.Value == OrderTimer:
                KeepOnTop();
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// A menu, a tooltip or a drop-down list is a topmost window too, and one that opens after the lens lies
    /// above it, in its original colours. While the lens is shown it looks a few times a second whether another
    /// program's window has come up over it, and goes back on top if so. It stays click-through, and the menu
    /// is then seen through the lens like everything else.
    /// </summary>
    private void KeepOnTop()
    {
        if (!_visible || _devMode || _fading)
        {
            return;
        }

        HWND above = OtherWindowAbove();
        if (above == default)
        {
            _keptBelow = default;
            return;
        }

        if (above == _keptBelow)
        {
            // Tried already. Windows keeps some of its own above everything (the Start menu, the lock screen),
            // and another always-on-top program may insist: no tug of war.
            return;
        }

        _keptBelow = above;
        PInvoke.SetWindowPos(
            Hwnd,
            HWND.HWND_TOPMOST,
            0,
            0,
            0,
            0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
        RaisedAgain?.Invoke();
    }

    /// <summary>The nearest visible window of another program that lies above the lens and over its area, if any.</summary>
    private HWND OtherWindowAbove()
    {
        uint own = (uint)Environment.ProcessId;
        HWND window = Hwnd;
        for (int i = 0; i < OrderWalkLimit; i++)
        {
            window = PInvoke.GetWindow(window, GET_WINDOW_CMD.GW_HWNDPREV);
            if (window == default)
            {
                return default;
            }

            if (!PInvoke.IsWindowVisible(window))
            {
                continue;
            }

            uint process = 0;
            if (PInvoke.GetWindowThreadProcessId(window, &process) == 0 || process == own)
            {
                continue;
            }

            if (PInvoke.GetWindowRect(window, out RECT area)
                && area.right > _bounds.X && area.left < _bounds.Right
                && area.bottom > _bounds.Y && area.top < _bounds.Bottom)
            {
                return window;
            }
        }

        return default;
    }

    private static bool AnimationsEnabled()
    {
        // "None at all when Windows has animations turned off" (section 4.8).
        BOOL enabled = true;
        return !PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETCLIENTAREAANIMATION, 0, &enabled, 0) || enabled;
    }

    private static PCWSTR CursorFor(Zone zone) => zone switch
    {
        Zone.Left or Zone.Right => PInvoke.IDC_SIZEWE,
        Zone.Top or Zone.Bottom => PInvoke.IDC_SIZENS,
        Zone.Left | Zone.Top or Zone.Right | Zone.Bottom => PInvoke.IDC_SIZENWSE,
        Zone.Right | Zone.Top or Zone.Left | Zone.Bottom => PInvoke.IDC_SIZENESW,
        _ => PInvoke.IDC_SIZEALL,
    };

    private float Scale()
    {
        uint dpi = PInvoke.GetDpiForWindow(Hwnd);
        return dpi > 0 ? dpi / 96f : 1f;
    }

    private void TakeKeyboard()
    {
        if (PInvoke.GetForegroundWindow() != Hwnd)
        {
            PInvoke.SetForegroundWindow(Hwnd);
        }
    }

    private Zone ZoneUnderPointer()
    {
        if (!PInvoke.GetCursorPos(out System.Drawing.Point pointer) || !PInvoke.GetWindowRect(Hwnd, out RECT window))
        {
            return Zone.Move;
        }

        return ZoneAt(pointer.X - window.left, pointer.Y - window.top);
    }

    private Zone ZoneAt(int x, int y)
    {
        int grip = Math.Max(4, (int)MathF.Round(GripDip * Scale()));
        grip = Math.Min(grip, Math.Min(_bounds.Width, _bounds.Height) / 3);
        Zone zone = Zone.Move;
        if (x < grip)
        {
            zone |= Zone.Left;
        }
        else if (x >= _bounds.Width - grip)
        {
            zone |= Zone.Right;
        }

        if (y < grip)
        {
            zone |= Zone.Top;
        }
        else if (y >= _bounds.Height - grip)
        {
            zone |= Zone.Bottom;
        }

        return zone;
    }

    private void BeginDrag(Zone zone)
    {
        if (!PInvoke.GetCursorPos(out _dragStart))
        {
            return;
        }

        _dragZone = zone;
        _dragBounds = _bounds;
        _dragging = true;
        PInvoke.SetCapture(Hwnd);
    }

    private void ContinueDrag()
    {
        if (!_dragging || !PInvoke.GetCursorPos(out System.Drawing.Point pointer))
        {
            return;
        }

        int dx = pointer.X - _dragStart.X;
        int dy = pointer.Y - _dragStart.Y;
        int left = _dragBounds.X;
        int top = _dragBounds.Y;
        int right = _dragBounds.Right;
        int bottom = _dragBounds.Bottom;
        const int minimum = LensGeometry.MinimumSize;
        if (_dragZone == Zone.Move)
        {
            left += dx;
            right += dx;
            top += dy;
            bottom += dy;
        }
        else
        {
            if (_dragZone.HasFlag(Zone.Left))
            {
                left = Math.Min(left + dx, right - minimum);
            }

            if (_dragZone.HasFlag(Zone.Right))
            {
                right = Math.Max(right + dx, left + minimum);
            }

            if (_dragZone.HasFlag(Zone.Top))
            {
                top = Math.Min(top + dy, bottom - minimum);
            }

            if (_dragZone.HasFlag(Zone.Bottom))
            {
                bottom = Math.Max(bottom + dy, top + minimum);
            }
        }

        var requested = new PixelRect(left, top, right - left, bottom - top);
        if (requested != _bounds)
        {
            BoundsRequested?.Invoke(requested);
        }
    }

    private void EndDrag()
    {
        if (_dragging)
        {
            _dragging = false;
            PInvoke.ReleaseCapture();
        }
    }

    private bool OnKey(VIRTUAL_KEY key)
    {
        bool shift = PInvoke.GetKeyState((int)VIRTUAL_KEY.VK_SHIFT) < 0;
        bool fine = PInvoke.GetKeyState((int)VIRTUAL_KEY.VK_CONTROL) < 0;
        int step = fine ? 1 : Math.Max(1, (int)MathF.Round(KeyStepDip * Scale()));
        switch (key)
        {
            case VIRTUAL_KEY.VK_LEFT:
                Nudged?.Invoke(-step, 0, shift);
                return true;
            case VIRTUAL_KEY.VK_RIGHT:
                Nudged?.Invoke(step, 0, shift);
                return true;
            case VIRTUAL_KEY.VK_UP:
                Nudged?.Invoke(0, -step, shift);
                return true;
            case VIRTUAL_KEY.VK_DOWN:
                Nudged?.Invoke(0, step, shift);
                return true;
            case VIRTUAL_KEY.VK_RETURN:
            case VIRTUAL_KEY.VK_ESCAPE:
                EditFinished?.Invoke();
                return true;
            default:
                return false;
        }
    }

    private void StepFade()
    {
        if (!_fading)
        {
            PInvoke.KillTimer(Hwnd, FadeTimer);
            return;
        }

        double progress = Stopwatch.GetElapsedTime(_fadeStarted) / _fadeDuration;
        if (progress >= 1.0)
        {
            HideNow();
        }
        else
        {
            ApplyOpacity?.Invoke((float)(1.0 - progress));
        }
    }

    private void CancelFade()
    {
        if (_fading)
        {
            _fading = false;
            PInvoke.KillTimer(Hwnd, FadeTimer);
            ApplyOpacity?.Invoke(1f);
        }
    }

    private void HideNow()
    {
        bool wasFading = _fading;
        _fading = false;
        if (wasFading)
        {
            PInvoke.KillTimer(Hwnd, FadeTimer);
        }

        EndDrag();
        _visible = false;
        PInvoke.KillTimer(Hwnd, OrderTimer);
        PInvoke.ShowWindow(Hwnd, SHOW_WINDOW_CMD.SW_HIDE);
        if (wasFading)
        {
            // Opaque again for the next time it is shown; nobody sees this, the window is hidden.
            ApplyOpacity?.Invoke(1f);
        }
    }
}
