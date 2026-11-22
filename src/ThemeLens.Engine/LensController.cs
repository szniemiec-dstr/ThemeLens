// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Core.Analysis;
using ThemeLens.Core.Colour;
using ThemeLens.Core.Mapping;
using ThemeLens.Core.Themes;

namespace ThemeLens.Engine;

/// <summary>What the control window shows: the clusters found, their roles, and the look built from them.</summary>
public sealed record LensMapping(SceneAnalysis Scene, RoleMap Roles, LensTables Tables);

/// <summary>
/// The lens state machine and frame loop (figures 7 and 8). It talks to interfaces only, so the same code runs
/// against Windows and against the fake clock, fake frame source and reference renderer of the specifications.
/// </summary>
/// <remarks>
/// <para>
/// Only two things ever change what the lens shows: a new look, which is always cross-faded, and a fill with
/// the plain page colour when the picture cannot be trusted. The lens passes through
/// <see cref="LensState.HoldingBackground"/> on every way to <see cref="LensState.Live"/>, so the first thing
/// ever shown is the user's background.
/// </para>
/// <para>
/// <see cref="Tick"/> is called by the frame loop once per display refresh. Commands may come from any thread.
/// <see cref="CheckWatchdog"/> is called from a timer on another thread.
/// </para>
/// </remarks>
public sealed class LensController : IDisposable
{
    /// <summary>Theme and mapping changes are cross-faded over this long (150 to 250 ms in the specification).</summary>
    public static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(200);

    /// <summary>On the way to Off the lens fades out over this long.</summary>
    public static readonly TimeSpan TurnOffFade = TimeSpan.FromMilliseconds(150);

    /// <summary>Analyses run at most this often: eight times a second.</summary>
    public static readonly TimeSpan AnalysisInterval = TimeSpan.FromMilliseconds(125);

    /// <summary>When the frame loop has not run for this long while the lens is live, the watchdog puts up the page colour.</summary>
    public static readonly TimeSpan StallAfter = TimeSpan.FromMilliseconds(500);

    /// <summary>A frame that takes longer than this many refresh intervals puts the lens past the Standard budget.</summary>
    private const double SlowFrameFactor = 1.25;

    private const int FastFramesToClearSlow = 3;

    private readonly Lock _gate = new();
    private readonly IClock _clock;
    private readonly IFrameSource _source;
    private readonly ILensSurface _surface;
    private readonly IAnalysisRunner _runner;
    private readonly LensAnalyser _analyser;

    private LensState _state = LensState.Off;
    private HoldReason _holdReason;
    private string? _captureProblem;
    private PixelRect _area;
    private LensTables _target;
    private TableFade? _fade;
    private TimeSpan _fadeStart;
    private IFrame? _lastFrame;
    private bool _frameDirty;
    private bool _lookDirty;
    private bool _hiddenForIdentity;
    private bool _sourceRunning;
    private bool _editWanted;
    private int _framesSinceAnalysis;
    private TimeSpan _lastAnalysisRequest = TimeSpan.MinValue;
    private bool _slow;
    private int _fastFrames;
    private double _delayMs;
    private long _lastTickTicks;
    private int _stalled;
    private LensStatus _status = LensStatus.Off;

    public LensController(IClock clock, IFrameSource source, ILensSurface surface, IAnalysisRunner runner, Theme theme)
    {
        _clock = clock;
        _source = source;
        _surface = surface;
        _runner = runner;
        _analyser = new LensAnalyser(theme);
        _target = _analyser.Tables;
    }

    public event Action<LensStatus>? StatusChanged;

    public event Action<LensMapping>? MappingChanged;

    public LensStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    public LensState State => Status.State;

    public Theme Theme => _analyser.Theme;

    /// <summary>The look the lens is heading for (the end of the current fade, if one is running).</summary>
    public LensTables Look
    {
        get
        {
            lock (_gate)
            {
                return _target;
            }
        }
    }

    public PixelRect Area
    {
        get
        {
            lock (_gate)
            {
                return _area;
            }
        }
    }

    /// <summary>Sets the part of the screen the lens covers. While the lens is on, capture restarts through Holding background.</summary>
    public void SetArea(PixelRect area)
    {
        lock (_gate)
        {
            if (_area == area)
            {
                return;
            }

            _area = area;
            _frameDirty = true;
        }
    }

    public void TurnOn()
    {
        lock (_gate)
        {
            if (_state != LensState.Off)
            {
                return;
            }

            _captureProblem = null;
            _analyser.Reset();
            _target = _analyser.Tables;
            _fade = null;
            _hiddenForIdentity = false;
            Volatile.Write(ref _lastTickTicks, _clock.Now.Ticks);

            if (_target.Parameters.Identity)
            {
                // Passthrough with contrast and sharpen at zero: the lens body is hidden and nothing is captured.
                _state = _editWanted ? LensState.Edit : LensState.Live;
                _hiddenForIdentity = true;
                _surface.Hide(TimeSpan.Zero);
                Publish();
                return;
            }

            // Background first: the page colour is on screen before capture has even started (FR-12).
            EnterHold(HoldReason.Starting);
            StartSource();
        }
    }

    /// <summary>Turns the lens off with a short fade.</summary>
    public void TurnOff() => TurnOff(TurnOffFade);

    /// <summary>Removes the lens instantly, from any state (FR-17).</summary>
    public void EverythingOff()
    {
        lock (_gate)
        {
            _editWanted = false;
        }

        TurnOff(TimeSpan.Zero);
    }

    private void TurnOff(TimeSpan fade)
    {
        lock (_gate)
        {
            if (_state == LensState.Off)
            {
                return;
            }

            _state = LensState.Off;
            _holdReason = HoldReason.None;
            StopSource();
            _surface.Hide(fade);
            _lastFrame?.Dispose();
            _lastFrame = null;
            _fade = null;
            _slow = false;
            Publish();
        }
    }

    /// <summary>Switches between locked and edit mode (move and resize). The content stays themed while editing.</summary>
    public void SetEditMode(bool edit)
    {
        lock (_gate)
        {
            _editWanted = edit;
            if (_state == LensState.Live && edit)
            {
                _state = LensState.Edit;
            }
            else if (_state == LensState.Edit && !edit)
            {
                _state = LensState.Live;
            }

            Publish();
        }
    }

    public void SetTheme(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        lock (_gate)
        {
            TimeSpan now = _clock.Now;
            LensTables current = CurrentLook(now, out _);
            LensTables next = _analyser.SetTheme(theme);

            if (_state == LensState.Off)
            {
                _target = next;
                return;
            }

            bool wasIdentity = _target.Parameters.Identity;
            _target = next;
            if (_state == LensState.HoldingBackground)
            {
                ShowHold();
            }
            else if (wasIdentity && !next.Parameters.Identity)
            {
                // Coming out of Passthrough the lens appears, so it goes through the background first.
                _hiddenForIdentity = false;
                _fade = null;
                EnterHold(HoldReason.Starting);
                StartSource();
            }
            else
            {
                _fade = new TableFade(current, next);
                _fadeStart = now;
                _lookDirty = true;
            }

            Publish();
            MappingChanged?.Invoke(new LensMapping(SceneAnalysis.Empty, _analyser.Roles, next));
        }
    }

    /// <summary>Applies the user's corrections to the mapping (FR-09). An empty list is "Reset mapping".</summary>
    public void SetPins(IReadOnlyList<Pin> pins)
    {
        ArgumentNullException.ThrowIfNull(pins);
        lock (_gate)
        {
            _analyser.SetPins(pins);
            _framesSinceAnalysis = Math.Max(_framesSinceAnalysis, 1);
            _lastAnalysisRequest = TimeSpan.MinValue;
        }
    }

    /// <summary>
    /// Tells the engine that the display changed under it: sleep and resume, a monitor connected or removed,
    /// a new resolution or DPI. The lens goes through Holding background (FR-19).
    /// </summary>
    public void NotifyDisplayChanged()
    {
        lock (_gate)
        {
            if (_state is LensState.Off || _hiddenForIdentity)
            {
                return;
            }

            EnterHold(HoldReason.DisplayChanged);
            StopSource();
            StartSource();
        }
    }

    /// <summary>One turn of the frame loop.</summary>
    public void Tick()
    {
        lock (_gate)
        {
            TimeSpan now = _clock.Now;
            Volatile.Write(ref _lastTickTicks, now.Ticks);
            if (_state == LensState.Off)
            {
                return;
            }

            if (Interlocked.Exchange(ref _stalled, 0) != 0)
            {
                // The watchdog covered the lens while this loop was stuck. Come back the usual way.
                EnterHold(HoldReason.Stalled);
            }

            if (_runner.TryTake(out AnalysisResult? analysis) && analysis is not null)
            {
                ApplyAnalysis(analysis, now);
            }

            if (_hiddenForIdentity)
            {
                return;
            }

            bool newFrame = false;
            if (_sourceRunning)
            {
                FramePoll poll = _source.Poll(out IFrame? frame, out _);
                if (poll == FramePoll.Fault)
                {
                    frame?.Dispose();
                    EnterHold(HoldReason.CaptureFault);
                    StopSource();
                    StartSource();
                    return;
                }

                if (poll == FramePoll.NewFrame && frame is not null)
                {
                    _lastFrame?.Dispose();
                    _lastFrame = frame;
                    _frameDirty = true;
                    _framesSinceAnalysis++;
                    newFrame = true;
                }
            }

            if (_state == LensState.HoldingBackground)
            {
                if (!newFrame)
                {
                    return;
                }

                // First fresh frame: fade from the plain page colour into the picture.
                _state = _editWanted ? LensState.Edit : LensState.Live;
                _holdReason = HoldReason.None;
                _fade = new TableFade(Solid(_target), _target);
                _fadeStart = now;
            }

            LensTables look = CurrentLook(now, out bool fading);
            if (look.Parameters.Identity && !fading)
            {
                _hiddenForIdentity = true;
                _surface.Hide(TimeSpan.Zero);
                StopSource();
                Publish();
                return;
            }

            if (_lastFrame is not null && (_frameDirty || fading || _lookDirty))
            {
                TimeSpan started = _clock.Now;
                _surface.ShowFrame(_lastFrame, look);
                TimeSpan finished = _clock.Now;
                _frameDirty = false;
                _lookDirty = fading;
                Measure(finished - started, finished - _lastFrame.CapturedAt);
            }

            if (_framesSinceAnalysis > 0 && !_runner.IsBusy && now - _lastAnalysisRequest >= AnalysisInterval)
            {
                Thumbnail? thumbnail = _surface.TryReadThumbnail();
                if (thumbnail is not null)
                {
                    _lastAnalysisRequest = now;
                    _framesSinceAnalysis = 0;
                    LensAnalyser analyser = _analyser;
                    _runner.Submit(() => analyser.Process(thumbnail));
                }
            }

            Publish();
        }
    }

    /// <summary>
    /// Call from a timer on a thread other than the frame loop's. If the loop has stalled while the lens is
    /// live, the lens is covered with the page colour at once, without waiting for the loop (FR-11).
    /// </summary>
    public void CheckWatchdog()
    {
        // Deliberately lock-free: the frame loop may be stuck while holding the lock.
        LensStatus status = Volatile.Read(ref _status);
        if (status.State is not (LensState.Live or LensState.Edit))
        {
            return;
        }

        LensTables target = Volatile.Read(ref _target);
        if (target.Parameters.Identity || target.Parameters.Direction == 0f)
        {
            return;
        }

        TimeSpan sinceTick = _clock.Now - TimeSpan.FromTicks(Volatile.Read(ref _lastTickTicks));
        if (sinceTick < StallAfter || Interlocked.Exchange(ref _stalled, 1) != 0)
        {
            return;
        }

        _surface.ShowEmergencyFill(target.Parameters.PageColour);
        var held = status with { State = LensState.HoldingBackground, HoldReason = HoldReason.Stalled };
        Volatile.Write(ref _status, held);
        StatusChanged?.Invoke(held);
    }

    public void Dispose()
    {
        EverythingOff();
        _source.Dispose();
    }

    private void StartSource()
    {
        try
        {
            _source.Start(_area);
            _sourceRunning = true;
        }
        catch (CaptureUnavailableException e)
        {
            // Capture is blocked or unsupported: say why, and do not leave a lens on screen (FR-20).
            _sourceRunning = false;
            _state = LensState.Off;
            _holdReason = HoldReason.None;
            _captureProblem = e.Message;
            _surface.Hide(TimeSpan.Zero);
            Publish();
        }
    }

    private void StopSource()
    {
        if (_sourceRunning)
        {
            _sourceRunning = false;
            _source.Stop();
        }
    }

    private void EnterHold(HoldReason reason)
    {
        _state = LensState.HoldingBackground;
        _holdReason = reason;
        _fade = null;
        _slow = false;
        ShowHold();
        Publish();
    }

    private void ShowHold()
    {
        FrameParameters p = _target.Parameters;
        if (p.Direction != 0f)
        {
            _surface.ShowFill(p.PageColour);
        }
        else if (_lastFrame is not null && !p.Identity)
        {
            // Keep base: there is no page colour to fall back on, so the last good frame is shown, dimmed.
            _surface.ShowFrame(_lastFrame, Dimmed(_target));
        }
        else
        {
            _surface.Hide(TimeSpan.Zero);
        }
    }

    private void ApplyAnalysis(AnalysisResult analysis, TimeSpan now)
    {
        if (analysis.LookChanged && !ReferenceEquals(analysis.Tables, _target))
        {
            if (_state is LensState.Live or LensState.Edit)
            {
                _fade = new TableFade(CurrentLook(now, out _), analysis.Tables);
                _fadeStart = now;
                _lookDirty = true;
            }

            _target = analysis.Tables;
        }

        MappingChanged?.Invoke(new LensMapping(analysis.Scene, analysis.Roles, analysis.Tables));
    }

    private LensTables CurrentLook(TimeSpan now, out bool fading)
    {
        fading = false;
        if (_fade is null)
        {
            return _target;
        }

        double progress = (now - _fadeStart) / FadeDuration;
        if (progress >= 1.0)
        {
            _fade = null;
            return _target;
        }

        fading = true;
        float t = (float)Math.Clamp(progress, 0.0, 1.0);
        return _fade.At(Smooth.Step(0f, 1f, t));
    }

    private void Measure(TimeSpan frameCost, TimeSpan delay)
    {
        _delayMs = _delayMs <= 0 ? delay.TotalMilliseconds : (_delayMs * 0.9) + (delay.TotalMilliseconds * 0.1);
        bool slowFrame = frameCost > _surface.RefreshInterval * SlowFrameFactor;
        if (slowFrame)
        {
            _slow = true;
            _fastFrames = 0;
        }
        else if (_slow && ++_fastFrames >= FastFramesToClearSlow)
        {
            _slow = false;
        }
    }

    private SlowOption CostliestOption()
    {
        FrameParameters p = _target.Parameters;
        if (p.StrongSharpen > 0f)
        {
            return SlowOption.StrongSharpen;
        }

        if (p.LocalContrast > 0f)
        {
            return SlowOption.LocalContrast;
        }

        return _surface.IsSoftwareRendered ? SlowOption.CpuRendering : SlowOption.None;
    }

    private void Publish()
    {
        bool live = _state is LensState.Live or LensState.Edit;
        var status = new LensStatus
        {
            State = _state,
            HoldReason = _state == LensState.HoldingBackground ? _holdReason : HoldReason.None,
            ReducedSpeed = live && _slow,
            SlowOption = live && _slow ? CostliestOption() : SlowOption.None,
            Delay = live ? TimeSpan.FromMilliseconds(Math.Round(_delayMs)) : TimeSpan.Zero,
            CaptureProblem = _state == LensState.Off ? _captureProblem : null,
        };

        if (status == _status)
        {
            return;
        }

        Volatile.Write(ref _status, status);
        StatusChanged?.Invoke(status);
    }

    /// <summary>A look that shows nothing but the page colour: the starting point of the fade into the first frame.</summary>
    private static LensTables Solid(LensTables like)
    {
        var lut = new Lut3D();
        System.Numerics.Vector3 page = like.Parameters.PageEncoded;
        for (int i = 0; i < lut.Data.Length; i += 3)
        {
            lut.Data[i] = page.X;
            lut.Data[i + 1] = page.Y;
            lut.Data[i + 2] = page.Z;
        }

        return new LensTables(lut, lut, like.Parameters, like.Theme, like.Roles);
    }

    private static LensTables Dimmed(LensTables look)
    {
        const float dim = 0.5f;
        Lut3D lut = look.Light.Clone();
        for (int i = 0; i < lut.Data.Length; i++)
        {
            lut.Data[i] = Srgb.Encode(Srgb.Decode(lut.Data[i]) * dim);
        }

        return new LensTables(lut, lut, look.Parameters, look.Theme, look.Roles);
    }
}
