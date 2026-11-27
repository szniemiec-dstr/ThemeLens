// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Core.Themes;
using ThemeLens.Engine;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace ThemeLens.Platform.Windows;

/// <summary>What the app decides before the lens exists.</summary>
public sealed record LensRuntimeOptions
{
    /// <summary>
    /// Development mode (<c>--dev</c>, section 10.2): the lens is not topmost and covers at most a quarter of
    /// the screen, so stopping at a breakpoint cannot leave a frozen rectangle over the editor.
    /// </summary>
    public bool DevMode { get; init; }

    /// <summary>Capture the screen, or show the test pattern (<c>--source pattern</c>).</summary>
    public FrameSourceKind Source { get; init; } = FrameSourceKind.Auto;

    /// <summary>The theme the lens starts with. Soft dark when not given.</summary>
    public Theme? InitialTheme { get; init; }

    /// <summary>Where the lens starts. The middle of the primary screen when not given.</summary>
    public LensPlacement? InitialPlacement { get; init; }

    /// <summary>The text on the handle, for example "Lens: Soft dark" in the user's language.</summary>
    public string HandleText { get; init; } = string.Empty;

    /// <summary>The global shortcuts to register. The defaults of section 4.7 when not given; an empty list for none.</summary>
    public IReadOnlyList<HotkeyBinding>? Hotkeys { get; init; }

    /// <summary>True to draw the thin outline around the locked lens (figure 3). Edit mode always shows its outline.</summary>
    public bool ShowOutline { get; init; } = true;

    /// <summary>The colours of the handle and the outline.</summary>
    public HandleColours HandleColours { get; init; } = HandleColours.Default;

    /// <summary>True to render on the CPU even when a GPU is present.</summary>
    public bool ForceSoftwareRendering { get; init; }
}

/// <summary>
/// The lens, assembled and running: the one class of this layer the app talks to. It owns the clock, the frame
/// source, the surface and its windows, the <see cref="LensController"/>, the frame loop and the watchdog.
/// </summary>
/// <remarks>
/// <para>
/// <b>Threads.</b> Every member may be called from any thread. The lens has three threads of its own: the frame
/// loop, which calls <see cref="LensController.Tick"/> once per display refresh and sleeps in between; the
/// window thread, which owns the lens window, its handle and outline and receives the global shortcuts; and
/// the small thread of the emergency fill. A timer calls <see cref="LensController.CheckWatchdog"/> ten times a
/// second.
/// </para>
/// <para>
/// <b>Events</b> are raised on the window thread, or on the thread that made the change. A user interface
/// marshals them to its own thread and does not block in the handler.
/// </para>
/// <para>
/// <b>Shortcuts.</b> The lens carries out "lens on or off", "edit mode", "fill this screen" and "everything
/// off" itself, so they work even while the app's own window is busy, and then raises
/// <see cref="HotkeyPressed"/> for every shortcut. "Next theme" and "open control window" are the app's to do.
/// </para>
/// </remarks>
public sealed class LensRuntime : IDisposable
{
    private static readonly TimeSpan IdleWait = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan WatchdogPeriod = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan DeviceCheckPeriod = TimeSpan.FromSeconds(1);

    private readonly LensRuntimeOptions _options;
    private readonly SystemClock _clock = new();
    private readonly D3D11LensRenderer _renderer;
    private readonly WindowThread _windows;
    private readonly WindowsLensSurface _surface;
    private readonly IFrameSource _source;
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _frameLoop;
    private readonly Timer _watchdog;
    private readonly Lock _placementGate = new();

    private HandleWindow _handle = null!;
    private OutlineWindow _outline = null!;
    private SystemEventsWindow _events = null!;
    private IReadOnlyList<HotkeyRegistration> _hotkeys = [];
    private IReadOnlyList<ScreenInfo> _screens;
    private LensPlacement _placement;
    private PixelRect _lens;
    private PixelRect _screen;
    private PixelRect _dragOrigin;
    private string _handleText;
    private nint _controlWindow;
    private double _framesPerSecond;
    private int _windowState = -1;
    private volatile bool _editWanted;
    private volatile bool _stopping;
    private bool _disposed;

    public LensRuntime(LensRuntimeOptions? options = null)
    {
        _options = options ?? new LensRuntimeOptions();
        _handleText = _options.HandleText;
        _screens = Monitors.Enumerate();
        if (_screens.Count == 0)
        {
            throw new InvalidOperationException("Windows reports no monitor to put the lens on.");
        }

        ScreenInfo primary = _screens.FirstOrDefault(static s => s.IsPrimary) ?? _screens[0];
        _placement = _options.InitialPlacement ?? new LensPlacement(LensGeometry.DefaultFor(primary.Bounds), FillsScreen: false);
        _lens = Effective(_placement, out ScreenInfo screen);
        _screen = screen.Bounds;

        Theme theme = _options.InitialTheme ?? BuiltInThemes.ById(BuiltInThemes.DefaultDarkId);
        _renderer = new D3D11LensRenderer(_options.ForceSoftwareRendering);
        try
        {
            _windows = new WindowThread("ThemeLens windows");
            _surface = new WindowsLensSurface(_renderer, _windows, ownsWindowThread: false, _lens, _screen, _options.DevMode);
            _surface.Raised += OnLensRaised;
            _windows.Invoke(CreateWindows);

            _source = FrameSources.Create(_options.Source, _renderer, _clock);
            FrameSources.Notify(_source, () => _wake.Set());

            Controller = new LensController(_clock, _source, _surface, new BackgroundAnalysisRunner(), theme);
            Controller.SetScreens([.. _screens.Select(static s => s.Bounds)]);
            Controller.SetArea(_lens);
            Controller.StatusChanged += OnStatusChanged;

            _frameLoop = new Thread(RunFrameLoop) { Name = "ThemeLens frame loop", IsBackground = true };
            _frameLoop.SetApartmentState(ApartmentState.MTA);
            _frameLoop.Start();
            _watchdog = new Timer(static state => ((LensController)state!).CheckWatchdog(), Controller, WatchdogPeriod, WatchdogPeriod);
        }
        catch
        {
            _stopping = true;
            _surface?.Dispose();
            _source?.Dispose();
            _windows?.Dispose();
            _renderer.Dispose();
            throw;
        }
    }

    /// <summary>A global shortcut was pressed. See the class remarks for which ones the lens has already carried out.</summary>
    public event Action<HotkeyAction>? HotkeyPressed;

    /// <summary>The lens was moved or resized, or fill-screen was switched.</summary>
    public event Action? PlacementChanged;

    /// <summary>Monitors were connected, removed or rearranged.</summary>
    public event Action? ScreensChanged;

    /// <summary>Edit mode was switched on or off.</summary>
    public event Action? EditModeChanged;

    /// <summary>The lens state machine: on and off, themes, pins, status.</summary>
    public LensController Controller { get; }

    /// <summary>The monitors, numbered as Windows shows them.</summary>
    public IReadOnlyList<ScreenInfo> Screens => Volatile.Read(ref _screens);

    /// <summary>The lens rectangle, the fill-screen switch and the rectangle to go back to.</summary>
    public LensPlacement Placement => Volatile.Read(ref _placement);

    /// <summary>The rectangle the lens covers right now, in physical pixels.</summary>
    public PixelRect LensBounds
    {
        get
        {
            lock (_placementGate)
            {
                return _lens;
            }
        }
    }

    /// <summary>True when the lens is rendered on the CPU, because there is no usable GPU.</summary>
    public bool IsSoftwareRendered => _renderer.IsSoftwareRendered;

    public bool IsDevMode => _options.DevMode;

    /// <summary>Each shortcut and whether Windows let us have it. One that is not registered shows as "not set".</summary>
    public IReadOnlyList<HotkeyRegistration> HotkeyStatus => Volatile.Read(ref _hotkeys);

    /// <summary>True when the user has asked for edit mode. The lens body takes input once it is also showing.</summary>
    public bool IsEditMode => _editWanted;

    /// <summary>Pictures shown per second, measured over the last second.</summary>
    public double FramesPerSecond => Volatile.Read(ref _framesPerSecond);

    /// <summary>Moves or resizes the lens, or switches fill-screen. The rectangle is fitted to the screen it is mostly on.</summary>
    public void SetPlacement(LensPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ApplyPlacement(placement, screensChanged: false);
    }

    /// <summary>"Fill this screen": snaps the lens to the whole monitor it is mostly on; the same again restores the previous rectangle.</summary>
    public void ToggleFillScreen() => SetPlacement(Placement.ToggleFill());

    /// <summary>Turns the lens on if it is off, and off (with its short fade) if it is on.</summary>
    public void ToggleLens()
    {
        if (Controller.State == LensState.Off)
        {
            Controller.TurnOn();
        }
        else
        {
            Controller.TurnOff();
        }
    }

    /// <summary>Removes the lens at once, from any state, and leaves edit mode (FR-17).</summary>
    public void EverythingOff()
    {
        bool wasEditing = _editWanted;
        _editWanted = false;
        Controller.EverythingOff();
        ApplyWindowState(Controller.Status);
        if (wasEditing)
        {
            EditModeChanged?.Invoke();
        }
    }

    /// <summary>Edit mode (section 4.5): the lens body takes the pointer and the keyboard so it can be moved and resized.</summary>
    public void SetEditMode(bool edit)
    {
        if (_editWanted == edit)
        {
            return;
        }

        _editWanted = edit;
        Controller.SetEditMode(edit);
        ApplyWindowState(Controller.Status);
        EditModeChanged?.Invoke();
    }

    public void ToggleEditMode() => SetEditMode(!_editWanted);

    /// <summary>Changes the text on the handle, for example after the theme changed.</summary>
    public void SetHandleText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _handleText = text;
        _windows.Post(() => _handle.SetText(text));
    }

    /// <summary>
    /// Tells the lens about the app's control window: it is kept out of capture, so the lens never recolours it,
    /// and it is put back above the lens whenever the lens comes to the front (section 4.6).
    /// </summary>
    public void AttachControlWindow(nint windowHandle)
    {
        CaptureExclusion.Apply(windowHandle);
        Volatile.Write(ref _controlWindow, windowHandle);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stopping = true;
        _wake.Set();
        if (Thread.CurrentThread != _frameLoop)
        {
            _frameLoop.Join(TimeSpan.FromSeconds(2));
        }

        using (var stopped = new ManualResetEvent(false))
        {
            if (_watchdog.Dispose(stopped))
            {
                stopped.WaitOne(TimeSpan.FromSeconds(1));
            }
        }

        Controller.StatusChanged -= OnStatusChanged;
        Controller.Dispose();
        _surface.Raised -= OnLensRaised;
        try
        {
            _windows.Invoke(DestroyWindows);
        }
        catch (ObjectDisposedException)
        {
        }

        _surface.Dispose();
        _windows.Dispose();
        _renderer.Dispose();
        _wake.Dispose();
    }

    private PixelRect Effective(LensPlacement placement, out ScreenInfo screen)
    {
        IReadOnlyList<ScreenInfo> screens = Screens;
        PixelRect lens = placement.Current(screens);
        screen = LensGeometry.ScreenFor(lens, screens);
        return _options.DevMode ? LensGeometry.LimitForDevMode(lens, screen.Bounds) : lens;
    }

    private void ApplyPlacement(LensPlacement placement, bool screensChanged)
    {
        PixelRect lens;
        PixelRect screenBounds;
        bool moved;
        bool otherScreen;
        lock (_placementGate)
        {
            Volatile.Write(ref _placement, placement);
            lens = Effective(placement, out ScreenInfo screen);
            screenBounds = screen.Bounds;
            moved = lens != _lens;
            otherScreen = screenBounds != _screen;
            _lens = lens;
            _screen = screenBounds;
            if (moved || otherScreen || screensChanged)
            {
                _surface.SetBounds(lens, screenBounds);
                Controller.SetScreens([.. Screens.Select(static s => s.Bounds)]);
                Controller.SetArea(lens);
            }
        }

        if (otherScreen || screensChanged)
        {
            // Another monitor, or the same one changed under the lens: capture starts again, through the background (FR-19).
            Controller.NotifyDisplayChanged();
        }

        bool fills = placement.FillsScreen;
        bool edit = _editWanted;
        _windows.Post(() =>
        {
            _handle.Follow(lens, screenBounds);
            _handle.SetState(edit, fills);
            _outline.Follow(lens);
        });
        _wake.Set();
        PlacementChanged?.Invoke();
    }

    private void CreateWindows()
    {
        _handle = new HandleWindow(_options.DevMode, _options.HandleColours);
        _handle.SetText(_handleText);
        _handle.Follow(_lens, _screen);
        _handle.DragStarted += OnHandleDragStarted;
        _handle.Dragged += OnHandleDragged;
        _handle.EditClicked += ToggleEditMode;
        _handle.FillClicked += ToggleFillScreen;

        _outline = new OutlineWindow(_options.DevMode, _options.HandleColours.Accent);
        _outline.Follow(_lens);

        _surface.Window.BoundsRequested += OnLensBoundsRequested;
        _surface.Window.Nudged += OnLensNudged;
        _surface.Window.EditFinished += OnLensEditFinished;

        _events = new SystemEventsWindow();
        _events.HotkeyPressed += OnHotkey;
        _events.DisplayChanged += OnDisplayChanged;
        Volatile.Write(ref _hotkeys, Hotkeys.Register(_events.Hwnd, _options.Hotkeys ?? Hotkeys.Defaults));
    }

    private void DestroyWindows()
    {
        Hotkeys.Unregister(_events.Hwnd, HotkeyStatus);
        _events.Destroy();
        _outline.Destroy();
        _handle.Release();
        _handle.Destroy();
    }

    private void OnHotkey(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.ToggleLens:
                ToggleLens();
                break;
            case HotkeyAction.ToggleEditMode:
                ToggleEditMode();
                break;
            case HotkeyAction.ToggleFillScreen:
                ToggleFillScreen();
                break;
            case HotkeyAction.EverythingOff:
                EverythingOff();
                break;
            default:
                // Next theme and the control window are the app's to do.
                break;
        }

        HotkeyPressed?.Invoke(action);
    }

    private void OnDisplayChanged()
    {
        IReadOnlyList<ScreenInfo> screens = Monitors.Enumerate();
        if (screens.Count > 0)
        {
            Volatile.Write(ref _screens, screens);
        }

        ApplyPlacement(Placement, screensChanged: true);
        ScreensChanged?.Invoke();
    }

    private void OnHandleDragStarted()
    {
        // Dragging a lens that fills the screen first lets go of the screen, back to the rectangle it had.
        if (Placement.FillsScreen)
        {
            ToggleFillScreen();
        }

        _dragOrigin = LensBounds;
    }

    private void OnHandleDragged(int dx, int dy) =>
        SetPlacement(Placement.MovedTo(LensGeometry.Move(_dragOrigin, dx, dy, Screens)));

    private void OnLensBoundsRequested(PixelRect requested)
    {
        IReadOnlyList<ScreenInfo> screens = Screens;
        SetPlacement(Placement.MovedTo(LensGeometry.Fit(requested, LensGeometry.ScreenFor(requested, screens).Bounds)));
    }

    private void OnLensNudged(int dx, int dy, bool resize)
    {
        IReadOnlyList<ScreenInfo> screens = Screens;
        PixelRect lens = LensBounds;
        SetPlacement(Placement.MovedTo(resize ? LensGeometry.Resize(lens, dx, dy, screens) : LensGeometry.Move(lens, dx, dy, screens)));
    }

    private void OnLensEditFinished() => SetEditMode(false);

    /// <summary>The lens came to the front of the topmost band: put what belongs above it back above it.</summary>
    private void OnLensRaised()
    {
        _outline.Raise();
        _handle.Raise();
        nint control = Volatile.Read(ref _controlWindow);
        if (control != 0 && !_options.DevMode)
        {
            // Another thread owns that window: ask, do not wait.
            PInvoke.SetWindowPos(
                new HWND(control),
                HWND.HWND_TOPMOST,
                0,
                0,
                0,
                0,
                SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE
                | SET_WINDOW_POS_FLAGS.SWP_ASYNCWINDOWPOS);
        }
    }

    /// <summary>Runs with the controller's lock held, on whatever thread changed the state: only hand work on, never wait.</summary>
    private void OnStatusChanged(LensStatus status)
    {
        // While the lens is showing, the engine's state says whether edit mode is on; follow it.
        if (status.State == LensState.Edit)
        {
            _editWanted = true;
        }
        else if (status.State == LensState.Live)
        {
            _editWanted = false;
        }

        _wake.Set();
        ApplyWindowState(status);
    }

    private void ApplyWindowState(LensStatus status)
    {
        bool on = status.State != LensState.Off;
        bool edit = on && _editWanted;
        bool fills = Placement.FillsScreen;
        bool outline = on && (edit || _options.ShowOutline);

        // The status changes many times a second while the lens is live (its delay is part of it); the windows
        // only need to hear about it when something they show has changed.
        int state = (on ? 1 : 0) | (edit ? 2 : 0) | (fills ? 4 : 0) | (outline ? 8 : 0);
        if (Interlocked.Exchange(ref _windowState, state) == state)
        {
            return;
        }

        _surface.SetEditMode(edit);
        _windows.Post(() =>
        {
            _handle.SetVisible(on);
            _handle.SetState(edit, fills);
            _outline.SetEditMode(edit);
            _outline.SetVisible(outline);
        });
    }

    private void RunFrameLoop()
    {
        bool fineTimer = false;
        long countedFrames = _surface.PresentedFrames;
        TimeSpan countedAt = _clock.Now;
        TimeSpan deviceCheckedAt = countedAt;
        try
        {
            while (!_stopping)
            {
                TimeSpan started = _clock.Now;
                try
                {
                    Controller.Tick();
                }
                catch (Exception e)
                {
                    // The loop must outlive any single bad frame. The watchdog covers the lens if this keeps happening.
                    PlatformLog.Warn("A turn of the frame loop failed", e);
                }

                TimeSpan now = _clock.Now;
                if (now - deviceCheckedAt >= DeviceCheckPeriod || _renderer.IsDeviceLost)
                {
                    deviceCheckedAt = now;
                    RecoverDeviceIfLost();
                }

                if (now - countedAt >= TimeSpan.FromSeconds(1))
                {
                    long frames = _surface.PresentedFrames;
                    Volatile.Write(ref _framesPerSecond, (frames - countedFrames) / (now - countedAt).TotalSeconds);
                    countedFrames = frames;
                    countedAt = now;
                }

                // Off: nothing to do until something changes. On: once per refresh, sooner when a frame arrives.
                bool active = Controller.State != LensState.Off;
                if (active != fineTimer)
                {
                    fineTimer = active;
                    SetFineTimer(active);
                }

                TimeSpan wait = IdleWait;
                if (active)
                {
                    wait = _surface.RefreshInterval - (_clock.Now - started);
                    if (wait < TimeSpan.FromMilliseconds(1))
                    {
                        wait = TimeSpan.FromMilliseconds(1);
                    }
                }

                _wake.WaitOne(wait);
            }
        }
        finally
        {
            if (fineTimer)
            {
                SetFineTimer(false);
            }
        }
    }

    /// <summary>
    /// The waits of the frame loop are a few milliseconds long, and the default Windows timer only ticks every
    /// 15.6 ms. A finer timer is asked for while the lens is on, and given back when it is off.
    /// </summary>
    private static void SetFineTimer(bool on)
    {
        const uint milliseconds = 1;
        _ = on ? PInvoke.timeBeginPeriod(milliseconds) : PInvoke.timeEndPeriod(milliseconds);
    }

    private void RecoverDeviceIfLost()
    {
        try
        {
            if (_renderer.CheckDevice() && !_surface.NeedsRecovery)
            {
                return;
            }

            // The GPU device is gone (driver update, adapter removed, a reset). Build everything again, then let
            // the engine come back the way it always does: background first.
            _surface.RecoverDevice();
            Controller.NotifyDisplayChanged();
        }
        catch (Exception e)
        {
            PlatformLog.Warn("Replacing the lost graphics device failed; it will be tried again", e);
        }
    }
}
