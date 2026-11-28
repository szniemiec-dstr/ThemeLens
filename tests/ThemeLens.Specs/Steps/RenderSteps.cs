// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Core.Colour;
using ThemeLens.Core.Imaging;
using ThemeLens.Core.Themes;
using ThemeLens.Specs.Support;
using ThemeLens.Testing;

namespace ThemeLens.Specs.Steps;

/// <summary>Steps that take one still picture through the lens.</summary>
[Binding]
public sealed class RenderSteps(LensWorld world)
{
    [Given("the theme {string}")]
    public void GivenTheTheme(string name) => world.Theme = LensWorld.ThemeNamed(name);

    [Given("the theme {string} with keep background {string}")]
    public void GivenTheThemeWithKeepBackground(string name, string mode) =>
        world.Theme = LensWorld.ThemeNamed(name) with { KeepBackground = Enum.Parse<KeepBackgroundMode>(mode, ignoreCase: true) };

    [Given("the theme {string} with warmth {int}%")]
    public void GivenTheThemeWithWarmth(string name, int percent) =>
        world.Theme = LensWorld.ThemeNamed(name) with { Warmth = percent / 100f };

    [Given("the theme {string} with brightness limit {int}%")]
    public void GivenTheThemeWithBrightnessLimit(string name, int percent)
    {
        Theme theme = LensWorld.ThemeNamed(name);
        world.Theme = theme with { Limits = theme.Limits with { MaxLuminance = percent / 100f } };
    }

    [Given("the theme {string} with contrast {int}% and sharpen {int}%")]
    public void GivenTheThemeWithContrastAndSharpen(string name, int contrast, int sharpen) =>
        world.Theme = LensWorld.ThemeNamed(name) with
        {
            Contrast = new ContrastSettings(contrast / 100f, ContrastMode.Standard),
            Sharpen = new SharpenSettings(sharpen / 100f, SharpenMode.Standard),
        };

    [Given("the theme {string} with sharpen {int}% in {string} mode")]
    public void GivenTheThemeWithSharpen(string name, int percent, string mode) =>
        world.Theme = LensWorld.ThemeNamed(name) with
        {
            Sharpen = new SharpenSettings(percent / 100f, Enum.Parse<SharpenMode>(mode, ignoreCase: true)),
        };

    [Given("the theme {string} with contrast {int}% in {string} mode")]
    public void GivenTheThemeWithContrast(string name, int percent, string mode) =>
        world.Theme = LensWorld.ThemeNamed(name) with
        {
            Contrast = new ContrastSettings(percent / 100f, Enum.Parse<ContrastMode>(mode, ignoreCase: true)),
        };

    [Given("the screen shows {string}")]
    public void GivenTheScreenShows(string fixture) => world.Fixture = Repository.LoadFixture(fixture);

    [When("the lens renders the frame")]
    public void WhenTheLensRendersTheFrame()
    {
        Theme theme = world.RequireTheme();
        ImageRgb8 screen = world.RequireFixture().Image;
        world.Result = Pipeline.Run(theme, screen);
        world.Viewed = world.Result.Output;

        // The same frame with the optional settings off, for steps that compare against it.
        world.Baseline = theme.Contrast.IsOn || theme.Sharpen.IsOn
            ? Pipeline.Run(theme with { Contrast = ContrastSettings.Off, Sharpen = SharpenSettings.Off }, screen)
            : world.Result;
    }

    [When("the result is viewed with simulated {string}")]
    public void WhenTheResultIsViewedWithSimulated(string vision)
    {
        ColourVisionDeficiency deficiency = Enum.Parse<ColourVisionDeficiency>(vision, ignoreCase: true);
        ImageRgb8 output = world.RequireResult().Output;
        var seen = new ImageRgb8(output.Width, output.Height);
        for (int y = 0; y < output.Height; y++)
        {
            for (int x = 0; x < output.Width; x++)
            {
                seen[x, y] = ColourVision.Simulate(output[x, y], deficiency);
            }
        }

        world.Viewed = seen;
    }
}
