// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Core.Analysis;
using ThemeLens.Core.Colour;
using ThemeLens.Core.Imaging;
using ThemeLens.Core.Mapping;
using ThemeLens.Core.Rendering;
using ThemeLens.Core.Themes;
using ThemeLens.Engine;

namespace ThemeLens.Specs.Support;

public sealed class FakeClock : IClock
{
    public TimeSpan Now { get; private set; } = TimeSpan.FromSeconds(10);

    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>A frame source that shows whatever picture the scenario puts on the "screen".</summary>
public sealed class FakeFrameSource(FakeClock clock) : IFrameSource
{
    private ImageRgb8? _screen;
    private bool _changed;
    private string? _fault;

    /// <summary>When set, <see cref="Start"/> refuses, as Windows does when capture is blocked by policy.</summary>
    public string? Blocked { get; set; }

    public bool Running { get; private set; }

    public int Starts { get; private set; }

    public void Show(ImageRgb8 screen)
    {
        _screen = screen;
        _changed = true;
    }

    public void Fail(string reason) => _fault = reason;

    public void Start(PixelRect area)
    {
        if (Blocked is not null)
        {
            throw new CaptureUnavailableException(Blocked);
        }

        Running = true;
        Starts++;
        _changed = _screen is not null;
    }

    public void Stop() => Running = false;

    public FramePoll Poll(out IFrame? frame, out string? fault)
    {
        frame = null;
        fault = null;
        if (_fault is not null)
        {
            fault = _fault;
            _fault = null;
            return FramePoll.Fault;
        }

        if (_changed && _screen is not null)
        {
            _changed = false;
            frame = new ImageFrame(_screen, clock.Now);
            return FramePoll.NewFrame;
        }

        return FramePoll.NoChange;
    }

    public void Dispose()
    {
    }
}

public enum PresentedKind
{
    Fill,
    Frame,
    EmergencyFill,
    Hidden,
}

/// <summary>One thing the lens put on screen.</summary>
public sealed record Presented(PresentedKind Kind, TimeSpan At, ImageRgb8? Picture, FrameParameters? Parameters);

/// <summary>A lens "window" that renders with the CPU reference renderer and remembers everything it showed.</summary>
public sealed class ReferenceSurface(FakeClock clock, int width, int height) : ILensSurface
{
    private ImageRgb8? _lastSource;

    public List<Presented> Shown { get; } = [];

    /// <summary>The screen content of the frame shown most recently, untouched.</summary>
    public ImageRgb8? LastSource => _lastSource;

    /// <summary>How long each frame takes to render, on the fake clock.</summary>
    public TimeSpan RenderCost { get; set; } = TimeSpan.FromMilliseconds(2);

    public TimeSpan RefreshInterval => TimeSpan.FromSeconds(1.0 / 60.0);

    public bool IsSoftwareRendered { get; set; }

    public bool Visible { get; private set; }

    public void ShowFill(Rgb8 colour)
    {
        Visible = true;
        Shown.Add(new Presented(PresentedKind.Fill, clock.Now, ImageRgb8.Filled(width, height, colour), null));
    }

    public void ShowFrame(IFrame frame, LensTables look)
    {
        ImageRgb8 source = ((ImageFrame)frame).Image;
        ImageRgb8 picture = ReferenceRenderer.Render(source, look);
        clock.Advance(RenderCost);
        _lastSource = source;
        Visible = true;
        Shown.Add(new Presented(PresentedKind.Frame, clock.Now, picture, look.Parameters));
    }

    public void Hide(TimeSpan fade)
    {
        Visible = false;
        Shown.Add(new Presented(PresentedKind.Hidden, clock.Now, null, null));
    }

    public Thumbnail? TryReadThumbnail(bool withDetail = false) =>
        _lastSource is null ? null : Neighbourhood.Build(_lastSource).ToThumbnail(withDetail);

    public void ShowEmergencyFill(Rgb8 colour)
    {
        Visible = true;
        Shown.Add(new Presented(PresentedKind.EmergencyFill, clock.Now, ImageRgb8.Filled(width, height, colour), null));
    }
}

/// <summary>The whole engine on a fake clock: what the engine scenarios drive.</summary>
public sealed class EngineHarness : IDisposable
{
    public static readonly TimeSpan Frame = TimeSpan.FromSeconds(1.0 / 60.0);

    public EngineHarness(Theme theme, int width, int height)
    {
        Clock = new FakeClock();
        Source = new FakeFrameSource(Clock);
        Surface = new ReferenceSurface(Clock, width, height);
        Controller = new LensController(Clock, Source, Surface, new InlineAnalysisRunner(), theme);
        Controller.SetArea(new PixelRect(0, 0, width, height));
        Controller.StatusChanged += s => Statuses.Add(s);
        Controller.MappingChanged += m => Mappings.Add(m);
    }

    public FakeClock Clock { get; }

    public FakeFrameSource Source { get; }

    public ReferenceSurface Surface { get; }

    public LensController Controller { get; }

    public List<LensStatus> Statuses { get; } = [];

    public List<LensMapping> Mappings { get; } = [];

    /// <summary>Runs the frame loop once: one tick, then one display refresh passes.</summary>
    public void Tick()
    {
        Controller.Tick();
        Clock.Advance(Frame);
    }

    public void RunFor(TimeSpan duration)
    {
        TimeSpan end = Clock.Now + duration;
        while (Clock.Now < end)
        {
            Tick();
        }
    }

    public IEnumerable<Presented> ShownSince(int index) => Surface.Shown.Skip(index);

    public void Dispose() => Controller.Dispose();
}
