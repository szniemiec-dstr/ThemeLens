// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Core.Analysis;
using ThemeLens.Core.Colour;
using ThemeLens.Core.Imaging;
using ThemeLens.Core.Measurement;
using ThemeLens.Core.Themes;
using ThemeLens.Testing;

namespace ThemeLens.Specs.Support;

/// <summary>What a scenario knows so far. Reqnroll creates one per scenario and hands it to every step class.</summary>
public sealed class LensWorld : IDisposable
{
    public Theme? Theme { get; set; }

    public Fixture? Fixture { get; set; }

    public LensResult? Result { get; set; }

    /// <summary>The same frame with the optional setting under test switched off, for "stronger than" and "at least as high as".</summary>
    public LensResult? Baseline { get; set; }

    /// <summary>The picture a later step looks at; may be <see cref="Result"/>'s output seen through simulated colour vision.</summary>
    public ImageRgb8? Viewed { get; set; }

    public EngineHarness? Engine { get; set; }

    /// <summary>Index into the surface's history at the moment the "When" step ran.</summary>
    public int Mark { get; set; }

    public TimeSpan MarkTime { get; set; }

    public TimeSpan RenderCost { get; set; } = TimeSpan.FromMilliseconds(2);

    /// <summary>Size of the screen the lens is on, for the "rectangles only" option. Null: the lens fills the screen.</summary>
    public long? ScreenPixels { get; set; }

    public Theme RequireTheme() => Theme ?? throw new InvalidOperationException("No theme has been chosen in this scenario.");

    public Fixture RequireFixture() => Fixture ?? throw new InvalidOperationException("The screen shows nothing in this scenario.");

    public LensResult RequireResult() => Result ?? throw new InvalidOperationException("The lens has not rendered a frame in this scenario.");

    public EngineHarness RequireEngine() => Engine ?? throw new InvalidOperationException("The lens is not running in this scenario.");

    public static Theme ThemeNamed(string name) =>
        BuiltInThemes.FindByName(name) ?? throw new KeyNotFoundException($"There is no built-in theme called '{name}'.");

    public EngineHarness StartEngine(Theme theme)
    {
        Engine?.Dispose();
        Engine = new EngineHarness(theme, Fixtures.Width, Fixtures.Height);
        Engine.Surface.RenderCost = RenderCost;
        return Engine;
    }

    /// <summary>
    /// WCAG contrast between what the fixture's text colour and its page colour came out as, measured on the
    /// rendered picture so that every pass is included.
    /// </summary>
    public static float TextContrast(LensResult result, Fixture fixture)
    {
        float text = Measure.MedianLuminanceWhere(result.Output, result.Source, fixture.Text)
            ?? throw new InvalidOperationException($"{fixture.FileName} has no pixel in its text colour {fixture.Text}.");
        float page = Measure.MedianLuminanceWhere(result.Output, result.Source, fixture.Page)
            ?? throw new InvalidOperationException($"{fixture.FileName} has no pixel in its page colour {fixture.Page}.");
        return Wcag.Contrast(text, page);
    }

    /// <summary>The region of the screen the fixture's page lies in.</summary>
    public static RegionPolarity PageRegion(LensResult result, Fixture fixture)
    {
        RoleEntry? page = result.Roles.Entries.FirstOrDefault(e => e.Role == Role.Page && e.Cluster.Colour.MaxChannelDifference(fixture.Page) <= 2);
        return page?.Region ?? (Wcag.Luminance(fixture.Page) > 0.2f ? RegionPolarity.Light : RegionPolarity.Dark);
    }

    public void Dispose() => Engine?.Dispose();
}
