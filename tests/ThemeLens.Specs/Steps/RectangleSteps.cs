// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Core.Colour;
using ThemeLens.Core.Imaging;
using ThemeLens.Core.Themes;
using ThemeLens.Engine;
using ThemeLens.Specs.Support;
using ThemeLens.Testing;

namespace ThemeLens.Specs.Steps;

/// <summary>Steps for the "rectangles only" option (section 5.6): still pictures and the lens over time.</summary>
[Binding]
public sealed class RectangleSteps(LensWorld world)
{
    [Given("the theme {string} with bright rectangles only, from {int} to {int} pixels at {int}% brightness")]
    public void GivenTheThemeWithBrightRectanglesOnly(string name, int smallest, int full, int brightness) =>
        world.Theme = LensWorld.ThemeNamed(name) with { Rectangles = Option(RectangleTarget.Bright, smallest, full, brightness) };

    [Given("the theme {string} with dark rectangles only, from {int} to {int} pixels at {int}% brightness")]
    public void GivenTheThemeWithDarkRectanglesOnly(string name, int smallest, int full, int brightness) =>
        world.Theme = LensWorld.ThemeNamed(name) with { Rectangles = Option(RectangleTarget.Dark, smallest, full, brightness) };

    [Given("found rectangles are shown in the theme's colours")]
    public void GivenFoundRectanglesAreShownInTheThemesColours()
    {
        Theme theme = world.RequireTheme();
        world.Theme = theme with { Rectangles = theme.Rectangles with { InTheme = true } };
    }

    [Given("hues are kept when a rectangle is inverted")]
    public void GivenHuesAreKept()
    {
        Theme theme = world.RequireTheme();
        world.Theme = theme with { Rectangles = theme.Rectangles with { KeepHues = true } };
    }

    [Given("sharpen at {int}%")]
    public void GivenSharpenAt(int percent) =>
        world.Theme = world.RequireTheme() with { Sharpen = new SharpenSettings(percent / 100f, SharpenMode.Standard) };

    /// <summary>
    /// One flat square on a desktop of the opposite kind: a dark desktop when bright rectangles are looked for,
    /// a light one for dark rectangles. It does not sit on the grid of 4-pixel blocks.
    /// </summary>
    [Given("the screen shows a rectangle {int}% bright as large as a square of {int} pixels")]
    public void GivenTheScreenShowsARectangle(int brightness, int side)
    {
        bool lightDesktop = world.RequireTheme().Rectangles.Target == RectangleTarget.Dark;
        Rgb8 desktop = lightDesktop ? Rgb8.FromHex("#E4DBCF") : Rgb8.FromHex("#1B2430");
        byte level = (byte)Math.Round(brightness * 255 / 100.0);
        var canvas = new Canvas(Fixtures.Width, Fixtures.Height, desktop);
        canvas.Fill(41, 38, side, side, new Rgb8(level, level, level));
        world.Fixture = new Fixture("one-rectangle", canvas.Image, desktop, desktop, new Dictionary<string, Rect>
        {
            ["rectangle"] = new(41, 38, side, side),
            ["desktop"] = new(Fixtures.Width - 48, Fixtures.Height - 40, 40, 32),
        });
    }

    [Then("the {string} is shown dark")]
    public void ThenTheAreaIsShownDark(string area) =>
        Ground(world.RequireResult().Source, world.RequireResult().Output, area).ShouldBeLessThanOrEqualTo(
            DarkAtMost, $"the {area} should have been turned dark");

    [Then("the {string} is shown light")]
    public void ThenTheAreaIsShownLight(string area) =>
        Ground(world.RequireResult().Source, world.RequireResult().Output, area).ShouldBeGreaterThanOrEqualTo(
            LightAtLeast, $"the {area} should have been turned light");

    [Then("the {string} is shown as it is on the screen")]
    public void ThenTheAreaIsShownAsItIsOnTheScreen(string area)
    {
        LensResult result = world.RequireResult();
        ShouldBeUntouched(result.Source, result.Output, area);
    }

    [Given("the lens is live over {string}")]
    public void GivenTheLensIsLiveOver(string fixture) => StartOver(fixture, TimeSpan.FromMilliseconds(100));

    [Given("the lens is live over {string} for {int} seconds")]
    public void GivenTheLensIsLiveOverFor(string fixture, int seconds) => StartOver(fixture, TimeSpan.FromSeconds(seconds));

    [When("I switch rectangles only on")]
    public void WhenISwitchRectanglesOnlyOn() => SwitchRectangles(on: true);

    [When("I switch rectangles only off")]
    public void WhenISwitchRectanglesOnlyOff() => SwitchRectangles(on: false);

    private void SwitchRectangles(bool on)
    {
        Theme theme = world.RequireTheme();
        RectangleOption option = theme.Rectangles.On ? theme.Rectangles : Option(RectangleTarget.Bright, 100, 300, 75);
        world.Theme = theme with { Rectangles = option with { On = on } };
        EngineHarness engine = world.RequireEngine();
        engine.Controller.SetTheme(world.Theme);
        engine.RunFor(TimeSpan.FromMilliseconds(400));
    }

    [Then("no frame so far shows the page of the {string} bright")]
    public void ThenNoFrameSoFarShowsThePageOfTheAreaBright(string area)
    {
        EngineHarness engine = world.RequireEngine();
        ImageRgb8 screen = world.RequireFixture().Image;
        engine.Surface.Shown.Count(s => s.Kind == PresentedKind.Frame).ShouldBeGreaterThan(0, "the lens should have shown the screen by now");
        foreach (Presented shown in engine.Surface.Shown)
        {
            if (shown.Picture is { } picture)
            {
                Ground(screen, picture, area).ShouldBeLessThanOrEqualTo(
                    0.1f, $"the page of the {area} was shown bright {shown.At.TotalMilliseconds:0} ms in");
            }
        }
    }

    [Then("after {int} second the {string} is shown as it is on the screen")]
    public void ThenAfterTheAreaIsShownAsItIs(int seconds, string area)
    {
        EngineHarness engine = world.RequireEngine();
        engine.RunFor(TimeSpan.FromSeconds(seconds));
        ShouldBeUntouched(engine.Surface.LastSource!, LastPicture(engine), area);
    }

    [Then("after {int} second the {string} is shown dark")]
    public void ThenAfterTheAreaIsShownDark(int seconds, string area)
    {
        EngineHarness engine = world.RequireEngine();
        engine.RunFor(TimeSpan.FromSeconds(seconds));
        Ground(engine.Surface.LastSource!, LastPicture(engine), area).ShouldBeLessThanOrEqualTo(DarkAtMost);
    }

    [Then("after {int} second the {string} is shown close to the page colour")]
    public void ThenAfterTheAreaIsShownCloseToThePageColour(int seconds, string area)
    {
        EngineHarness engine = world.RequireEngine();
        engine.RunFor(TimeSpan.FromSeconds(seconds));
        float shown = Ground(engine.Surface.LastSource!, LastPicture(engine), area);
        Wcag.Contrast(shown, engine.Controller.Look.Parameters.PageLuminance).ShouldBeLessThanOrEqualTo(1.5f);
    }

    [Then("after {int} second the picture on screen is the screen as it is")]
    public void ThenAfterThePictureIsTheScreenAsItIs(int seconds)
    {
        EngineHarness engine = world.RequireEngine();
        engine.RunFor(TimeSpan.FromSeconds(seconds));
        LastPicture(engine).ContentEquals(engine.Surface.LastSource!).ShouldBeTrue(
            "with no bright rectangle left on screen, the lens should change nothing");
    }

    /// <summary>Luminance at or below which an area counts as turned dark: white, light grey and 76% grey all end up below it.</summary>
    private const float DarkAtMost = 0.06f;

    /// <summary>Luminance at or above which an area counts as turned light.</summary>
    private const float LightAtLeast = 0.4f;

    private static RectangleOption Option(RectangleTarget target, int smallest, int full, int brightness) => new()
    {
        On = true,
        Target = target,
        Values = new RectangleSettings { SmallestSide = smallest, FullSide = full, Brightness = brightness / 100f }.For(target),
    };

    /// <summary>
    /// The luminance the area's own colour is shown with: the median over the pixels that have the colour most
    /// of the area has on the screen. What is written on it is left out.
    /// </summary>
    private float Ground(ImageRgb8 screen, ImageRgb8 picture, string area)
    {
        Rect rect = Region(area);
        var counts = new Dictionary<Rgb8, int>();
        for (int y = rect.Y; y < rect.Y + rect.Height; y++)
        {
            for (int x = rect.X; x < rect.X + rect.Width; x++)
            {
                counts[screen[x, y]] = counts.GetValueOrDefault(screen[x, y]) + 1;
            }
        }

        Rgb8 ground = counts.MaxBy(pair => pair.Value).Key;
        var shown = new List<float>();
        for (int y = rect.Y; y < rect.Y + rect.Height; y++)
        {
            for (int x = rect.X; x < rect.X + rect.Width; x++)
            {
                if (screen[x, y] == ground)
                {
                    shown.Add(Wcag.Luminance(picture[x, y]));
                }
            }
        }

        shown.Sort();
        return shown[shown.Count / 2];
    }

    private void StartOver(string fixture, TimeSpan time)
    {
        world.Fixture = Repository.LoadFixture(fixture);
        EngineHarness engine = world.StartEngine(world.RequireTheme());
        engine.Source.Show(world.Fixture.Image);
        engine.Controller.TurnOn();
        engine.RunFor(time);
        engine.Controller.State.ShouldBe(LensState.Live);
    }

    private Rect Region(string area)
    {
        world.RequireFixture().Regions.TryGetValue(area, out Rect rect).ShouldBeTrue($"the fixture has no area called '{area}'");
        return rect;
    }

    private void ShouldBeUntouched(ImageRgb8 source, ImageRgb8 output, string area)
    {
        Rect rect = Region(area);
        int changed = 0;
        for (int y = rect.Y; y < rect.Y + rect.Height; y++)
        {
            for (int x = rect.X; x < rect.X + rect.Width; x++)
            {
                if (output[x, y] != source[x, y])
                {
                    changed++;
                }
            }
        }

        changed.ShouldBe(0, $"the {area} should be left exactly as it is, but {changed} of its {rect.Width * rect.Height} pixels changed");
    }

    private static ImageRgb8 LastPicture(EngineHarness engine) =>
        engine.Surface.Shown.Last(s => s.Kind == PresentedKind.Frame).Picture!;
}
