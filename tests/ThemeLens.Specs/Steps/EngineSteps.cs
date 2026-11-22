// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Core.Colour;
using ThemeLens.Core.Imaging;
using ThemeLens.Core.Measurement;
using ThemeLens.Core.Themes;
using ThemeLens.Engine;
using ThemeLens.Specs.Support;
using ThemeLens.Testing;

namespace ThemeLens.Specs.Steps;

/// <summary>Steps that run the lens over time: the engine on a fake clock with a fake frame source.</summary>
[Binding]
public sealed class EngineSteps(LensWorld world)
{
    private const string DefaultScreen = "light-document.png";

    private string? _recording;
    private int _recordingSeconds;
    private Fixture? _screenAfterChange;

    [Given("the lens has shown {string} for {int} seconds")]
    public void GivenTheLensHasShown(string fixture, int seconds)
    {
        EngineHarness engine = Start(fixture);
        engine.RunFor(TimeSpan.FromSeconds(seconds));
        engine.Controller.State.ShouldBe(LensState.Live);
    }

    [Given("the lens is live with the theme {string}")]
    public void GivenTheLensIsLiveWithTheTheme(string theme) => GivenTheLensIsLiveWithTheThemeOn(theme, DefaultScreen);

    [Given("the lens is live with the theme {string} on {string}")]
    public void GivenTheLensIsLiveWithTheThemeOn(string theme, string fixture)
    {
        world.Theme = LensWorld.ThemeNamed(theme);
        EngineHarness engine = Start(fixture);
        engine.RunFor(TimeSpan.FromSeconds(1));
        engine.Controller.State.ShouldBe(LensState.Live);
    }

    [Given("a renderer that needs {int} ms for each frame")]
    public void GivenARendererThatNeeds(int milliseconds) => world.RenderCost = TimeSpan.FromMilliseconds(milliseconds);

    [Given("screen capture is blocked")]
    public void GivenScreenCaptureIsBlocked()
    {
        world.Fixture ??= Repository.LoadFixture(DefaultScreen);
        EngineHarness engine = world.StartEngine(world.RequireTheme());
        engine.Source.Blocked = "Screen capture is turned off by your organisation.";
        engine.Source.Show(world.Fixture.Image);
    }

    [Given("a {int} second recording {string}")]
    public void GivenARecording(int seconds, string recording)
    {
        _recordingSeconds = seconds;
        _recording = recording;
    }

    [When("the screen changes to {string}")]
    public void WhenTheScreenChangesTo(string fixture)
    {
        EngineHarness engine = world.RequireEngine();
        _screenAfterChange = Repository.LoadFixture(fixture);
        Mark(engine);
        engine.Source.Show(_screenAfterChange.Image);
        engine.Tick();
    }

    [When("the screen changes from {string} to {string}")]
    public void WhenTheScreenChangesFromTo(string before, string after)
    {
        EngineHarness engine = Start(before);
        engine.RunFor(TimeSpan.FromSeconds(1));
        _screenAfterChange = Repository.LoadFixture(after);
        Mark(engine);
        engine.Source.Show(_screenAfterChange.Image);
        engine.RunFor(TimeSpan.FromSeconds(1));
    }

    [When("the frame source reports a fault")]
    public void WhenTheFrameSourceReportsAFault()
    {
        EngineHarness engine = world.RequireEngine();
        Mark(engine);
        engine.Source.Fail("The capture session was closed.");
        engine.Tick();
    }

    [When("the display changes")]
    public void WhenTheDisplayChanges()
    {
        EngineHarness engine = world.RequireEngine();
        Mark(engine);
        engine.Controller.NotifyDisplayChanged();
    }

    [When("the frame loop stalls for {int} ms")]
    public void WhenTheFrameLoopStallsFor(int milliseconds)
    {
        EngineHarness engine = world.RequireEngine();
        Mark(engine);
        engine.Clock.Advance(TimeSpan.FromMilliseconds(milliseconds));
        engine.Controller.CheckWatchdog();
    }

    [When("the frame loop runs again")]
    public void WhenTheFrameLoopRunsAgain()
    {
        EngineHarness engine = world.RequireEngine();
        engine.Tick();
        engine.Source.Show(world.RequireFixture().Image);
        engine.RunFor(TimeSpan.FromMilliseconds(500));
    }

    [When("I turn the lens on")]
    public void WhenITurnTheLensOn()
    {
        EngineHarness engine;
        if (world.Engine is null)
        {
            world.Fixture ??= Repository.LoadFixture(DefaultScreen);
            engine = world.StartEngine(world.RequireTheme());
            engine.Source.Show(world.Fixture.Image);
        }
        else
        {
            engine = world.Engine;
        }

        Mark(engine);
        engine.Controller.TurnOn();
    }

    [When("I turn the lens off")]
    public void WhenITurnTheLensOff()
    {
        EngineHarness engine = world.RequireEngine();
        Mark(engine);
        engine.Controller.TurnOff();
    }

    [When("I press the everything-off shortcut")]
    public void WhenIPressTheEverythingOffShortcut()
    {
        EngineHarness engine = world.RequireEngine();
        Mark(engine);
        engine.Controller.EverythingOff();
    }

    [When("I enter edit mode")]
    public void WhenIEnterEditMode() => world.RequireEngine().Controller.SetEditMode(true);

    [When("I leave edit mode")]
    public void WhenILeaveEditMode() => world.RequireEngine().Controller.SetEditMode(false);

    [When("I switch to the theme {string}")]
    public void WhenISwitchToTheTheme(string theme)
    {
        EngineHarness engine = world.RequireEngine();
        Mark(engine);
        world.Theme = LensWorld.ThemeNamed(theme);
        engine.Controller.SetTheme(world.Theme);
        engine.RunFor(TimeSpan.FromMilliseconds(500));
    }

    [When("the lens processes the recording")]
    public void WhenTheLensProcessesTheRecording()
    {
        string recording = _recording ?? throw new InvalidOperationException("No recording was given.");
        world.Fixture = Repository.LoadFixture(DefaultScreen);
        EngineHarness engine = world.StartEngine(world.RequireTheme());
        engine.Source.Show(Fixtures.RecordingFrame(recording, 0));
        engine.Controller.TurnOn();

        // Let the lens settle on the first frame, then play the recording at 30 frames a second.
        engine.RunFor(TimeSpan.FromSeconds(1));
        Mark(engine);
        const int framesPerSecond = 30;
        for (int i = 0; i < _recordingSeconds * framesPerSecond; i++)
        {
            engine.Source.Show(Fixtures.RecordingFrame(recording, (double)i / framesPerSecond));
            engine.Tick();
            engine.Tick();
        }
    }

    [Then("the very next frame has no large bright area")]
    public void ThenTheVeryNextFrameHasNoLargeBrightArea()
    {
        Presented next = NextFrame();
        Measure.LargestAreaShare(next.Picture!, next.Parameters!).ShouldBeLessThanOrEqualTo(0.005f);
    }

    [Then("text in that frame is lighter than its background")]
    public void ThenTextInThatFrameIsLighterThanItsBackground()
    {
        Presented next = NextFrame();
        Fixture screen = _screenAfterChange ?? world.RequireFixture();
        float text = Measure.MedianLuminanceWhere(next.Picture!, screen.Image, screen.Text)!.Value;
        float page = Measure.MedianLuminanceWhere(next.Picture!, screen.Image, screen.Page)!.Value;
        text.ShouldBeGreaterThan(page + 0.1f);
    }

    [Then("the next presented frame is entirely the theme's page colour")]
    [Then("the first presented frame is entirely the theme's page colour")]
    public void ThenTheNextPresentedFrameIsEntirelyThePageColour()
    {
        EngineHarness engine = world.RequireEngine();
        Presented next = engine.ShownSince(world.Mark).First();
        next.Kind.ShouldBeOneOf(PresentedKind.Fill, PresentedKind.EmergencyFill);
        Rgb8 page = engine.Controller.Look.Parameters.PageColour;
        ImageRgb8.Filled(next.Picture!.Width, next.Picture.Height, page).ContentEquals(next.Picture).ShouldBeTrue();
    }

    [Then("the status is {string}")]
    public void ThenTheStatusIs(string status) => world.RequireEngine().Controller.Status.Text.ShouldBe(status);

    [Then("the status becomes {string} after the first fresh frame")]
    public void ThenTheStatusBecomesAfterTheFirstFreshFrame(string status)
    {
        EngineHarness engine = world.RequireEngine();
        engine.Controller.Status.Text.ShouldBe("Holding background");
        engine.Tick();
        engine.Controller.Status.Text.ShouldBe(status);
    }

    [Then("the lens state is {string}")]
    public void ThenTheLensStateIs(string state) =>
        world.RequireEngine().Controller.State.ToString().ShouldBe(state.Replace(" ", string.Empty, StringComparison.Ordinal), StringCompareShould.IgnoreCase);

    [Then("sharpen is still on")]
    public void ThenSharpenIsStillOn()
    {
        EngineHarness engine = world.RequireEngine();
        engine.Controller.Theme.Sharpen.IsOn.ShouldBeTrue();
        (engine.Controller.Look.Parameters.Sharpen + engine.Controller.Look.Parameters.StrongSharpen).ShouldBeGreaterThan(0f);
    }

    [Then("the option named as slowest is {string}")]
    public void ThenTheOptionNamedAsSlowestIs(string option) =>
        world.RequireEngine().Controller.Status.SlowOption.ToString().ShouldBe(option.Replace(" ", string.Empty, StringComparison.Ordinal), StringCompareShould.IgnoreCase);

    [Then("no presented frame has a large bright area")]
    public void ThenNoPresentedFrameHasALargeBrightArea()
    {
        foreach (Presented shown in world.RequireEngine().Surface.Shown.Where(s => s.Kind == PresentedKind.Frame))
        {
            Measure.LargestAreaShare(shown.Picture!, shown.Parameters!).ShouldBeLessThanOrEqualTo(0.005f);
        }
    }

    [Then("every presented frame since then is a complete frame or the page colour")]
    public void ThenEveryPresentedFrameIsComplete()
    {
        foreach (Presented shown in world.RequireEngine().ShownSince(world.Mark))
        {
            shown.Kind.ShouldNotBe(PresentedKind.Hidden);
            shown.Picture.ShouldNotBeNull();
        }
    }

    [Then("no role changes its cluster")]
    public void ThenNoRoleChangesItsCluster()
    {
        EngineHarness engine = world.RequireEngine();
        engine.Mappings.Count.ShouldBeGreaterThan(10, "the recording should have been analysed many times");
        LensMapping first = engine.Mappings[^1];
        foreach (LensMapping mapping in engine.Mappings.Skip(2))
        {
            LensAnalyser.SameAnchors(first.Roles, mapping.Roles).ShouldBeTrue("the mapping should stay the same for the whole recording");
        }
    }

    [Then("mean lens luminance changes by less than {int}% between any two frames")]
    public void ThenMeanLensLuminanceChangesByLessThan(int percent)
    {
        List<float> luminance = world.RequireEngine().ShownSince(world.Mark)
            .Where(s => s.Kind == PresentedKind.Frame)
            .Select(s => Measure.MeanLuminance(s.Picture!))
            .ToList();
        luminance.Count.ShouldBeGreaterThan(30);
        for (int i = 1; i < luminance.Count; i++)
        {
            MathF.Abs(luminance[i] - luminance[i - 1]).ShouldBeLessThan(percent / 100f);
        }
    }

    [Then("mean lens luminance moves only towards its new value")]
    public void ThenMeanLensLuminanceMovesOnlyTowardsItsNewValue()
    {
        List<float> luminance = world.RequireEngine().ShownSince(world.Mark)
            .Where(s => s.Kind == PresentedKind.Frame)
            .Select(s => Measure.MeanLuminance(s.Picture!))
            .ToList();
        luminance.Count.ShouldBeGreaterThan(3, "the change should be a fade, not a jump");
        Measure.IsMonotonic(luminance, tolerance: 0.0015f).ShouldBeTrue(
            "brightness should never overshoot: " + string.Join(" ", luminance.Select(l => l.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture))));
    }

    [Then("the change completes within {int} ms")]
    public void ThenTheChangeCompletesWithin(int milliseconds)
    {
        EngineHarness engine = world.RequireEngine();
        List<Presented> frames = engine.ShownSince(world.Mark).Where(s => s.Kind == PresentedKind.Frame).ToList();
        ImageRgb8 final = frames[^1].Picture!;
        Presented firstFinal = frames.First(f => f.Picture!.ContentEquals(final));
        (firstFinal.At - world.MarkTime).ShouldBeLessThanOrEqualTo(TimeSpan.FromMilliseconds(milliseconds) + EngineHarness.Frame);
    }

    [Then("no lens is shown")]
    public void ThenNoLensIsShown() => world.RequireEngine().Surface.Visible.ShouldBeFalse();

    [Then("the lens is shown")]
    public void ThenTheLensIsShown() => world.RequireEngine().Surface.Visible.ShouldBeTrue();

    [Then("the app explains that capture is unavailable")]
    public void ThenTheAppExplainsThatCaptureIsUnavailable()
    {
        LensStatus status = world.RequireEngine().Controller.Status;
        status.State.ShouldBe(LensState.Off);
        status.CaptureProblem.ShouldNotBeNullOrWhiteSpace();
    }

    [Then("nothing is captured")]
    public void ThenNothingIsCaptured() => world.RequireEngine().Source.Running.ShouldBeFalse();

    [Then("capture has been restarted")]
    public void ThenCaptureHasBeenRestarted() => world.RequireEngine().Source.Starts.ShouldBeGreaterThan(1);

    private EngineHarness Start(string fixture)
    {
        world.Fixture = Repository.LoadFixture(fixture);
        EngineHarness engine = world.StartEngine(world.RequireTheme());
        engine.Source.Show(world.Fixture.Image);
        engine.Controller.TurnOn();
        return engine;
    }

    private void Mark(EngineHarness engine)
    {
        world.Mark = engine.Surface.Shown.Count;
        world.MarkTime = engine.Clock.Now;
    }

    private Presented NextFrame() =>
        world.RequireEngine().ShownSince(world.Mark).First(s => s.Kind == PresentedKind.Frame);
}
