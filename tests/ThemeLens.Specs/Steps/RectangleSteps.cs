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
    [Given("the theme {string} with rectangles only from {float}% to {float}% of the screen at {int}% brightness")]
    public void GivenTheThemeWithRectanglesOnly(string name, float smallest, float full, int brightness)
    {
        Theme theme = LensWorld.ThemeNamed(name);
        world.Theme = theme with
        {
            Rectangles = new RectangleSettings
            {
                On = true,
                SmallestArea = smallest / 100f,
                FullArea = full / 100f,
                Brightness = brightness / 100f,
            }.For(theme.Base),
        };
    }

    [Given("sharpen at {int}%")]
    public void GivenSharpenAt(int percent) =>
        world.Theme = world.RequireTheme() with { Sharpen = new SharpenSettings(percent / 100f, SharpenMode.Standard) };

    [Given("the screen is {int} times the size of the lens")]
    public void GivenTheScreenIsTimesTheSizeOfTheLens(int times) =>
        world.ScreenPixels = (long)Fixtures.Width * Fixtures.Height * times;

    /// <summary>
    /// One flat rectangle on a desktop of the opposite kind: a dark desktop under a dark theme, where the
    /// option looks for bright rectangles, and a light desktop under a light theme.
    /// </summary>
    [Given("the screen shows a rectangle {int}% bright that covers {float}% of the screen")]
    public void GivenTheScreenShowsARectangle(int brightness, float size)
    {
        bool lightDesktop = world.RequireTheme().Base == ThemeBase.Light;
        Rgb8 desktop = lightDesktop ? Rgb8.FromHex("#E4DBCF") : Rgb8.FromHex("#1B2430");
        byte level = (byte)Math.Round(brightness * 255 / 100.0);
        var colour = new Rgb8(level, level, level);

        // Sixteen wide to ten high like the screen, on whole 4-pixel blocks.
        double pixels = size / 100.0 * Fixtures.Width * Fixtures.Height;
        int height = (int)Math.Round(Math.Sqrt(pixels / 1.6) / 4) * 4;
        int width = (int)Math.Round(pixels / height / 4) * 4;
        var canvas = new Canvas(Fixtures.Width, Fixtures.Height, desktop);
        canvas.Fill(40, 40, width, height, colour);
        world.Fixture = new Fixture("one-rectangle", canvas.Image, desktop, desktop, new Dictionary<string, Rect>
        {
            ["rectangle"] = new(40, 40, width, height),
            ["desktop"] = new(Fixtures.Width - 48, Fixtures.Height - 40, 40, 32),
        });
    }

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
        world.Theme = theme with { Rectangles = (theme.Rectangles.On ? theme.Rectangles : RectangleSettings.DefaultFor(theme.Base)) with { On = on } };
        EngineHarness engine = world.RequireEngine();
        engine.Controller.SetTheme(world.Theme);
        engine.RunFor(TimeSpan.FromMilliseconds(400));
    }

    [Then("no frame so far shows the {string} brighter than the theme allows")]
    public void ThenNoFrameSoFarShowsTheAreaBrighter(string area)
    {
        EngineHarness engine = world.RequireEngine();
        Rect rect = Region(area);
        float limit = world.RequireTheme().Limits.MaxLuminance;
        engine.Surface.Shown.Count(s => s.Kind == PresentedKind.Frame).ShouldBeGreaterThan(0, "the lens should have shown the screen by now");
        foreach (Presented shown in engine.Surface.Shown)
        {
            if (shown.Picture is not { } picture)
            {
                continue;
            }

            for (int y = rect.Y; y < rect.Y + rect.Height; y++)
            {
                for (int x = rect.X; x < rect.X + rect.Width; x++)
                {
                    Wcag.Luminance(picture[x, y]).ShouldBeLessThanOrEqualTo(
                        limit + 0.004f, $"a pixel of the {area} was shown too bright {shown.At.TotalMilliseconds:0} ms in");
                }
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

    [Then("after {int} second the {string} is shown close to the page colour")]
    public void ThenAfterTheAreaIsShownCloseToThePageColour(int seconds, string area)
    {
        EngineHarness engine = world.RequireEngine();
        engine.RunFor(TimeSpan.FromSeconds(seconds));
        ImageRgb8 picture = LastPicture(engine);
        Rect rect = Region(area);
        var shown = new List<float>();
        for (int y = rect.Y; y < rect.Y + rect.Height; y++)
        {
            for (int x = rect.X; x < rect.X + rect.Width; x++)
            {
                shown.Add(Wcag.Luminance(picture[x, y]));
            }
        }

        shown.Sort();
        Wcag.Contrast(shown[shown.Count / 2], engine.Controller.Look.Parameters.PageLuminance).ShouldBeLessThanOrEqualTo(1.5f);
    }

    [Then("after {int} second the picture on screen is the screen as it is")]
    public void ThenAfterThePictureIsTheScreenAsItIs(int seconds)
    {
        EngineHarness engine = world.RequireEngine();
        engine.RunFor(TimeSpan.FromSeconds(seconds));
        LastPicture(engine).ContentEquals(engine.Surface.LastSource!).ShouldBeTrue(
            "with no bright rectangle left on screen, the lens should change nothing");
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
