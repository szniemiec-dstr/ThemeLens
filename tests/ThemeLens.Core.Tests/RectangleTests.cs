// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Core.Analysis;
using ThemeLens.Core.Colour;
using ThemeLens.Core.Imaging;
using ThemeLens.Core.Mapping;
using ThemeLens.Core.Rendering;
using ThemeLens.Core.Themes;
using ThemeLens.Testing;

namespace ThemeLens.Core.Tests;

/// <summary>The "rectangles only" option (section 5.6): finding the rectangles, and changing nothing else.</summary>
public class RectangleTests
{
    private static readonly RectangleSettings On = new() { On = true };
    private static readonly Rgb8 Desktop = Rgb8.FromHex("#1B2430");

    private static FoundRectangles Find(ImageRgb8 screen, RectangleSettings? settings = null, ThemeBase themeBase = ThemeBase.Dark, long? screenPixels = null) =>
        RectangleFinder.Find(
            Neighbourhood.Build(screen).ToThumbnail(),
            settings ?? RectangleSettings.DefaultFor(themeBase) with { On = true },
            themeBase,
            screenPixels ?? ((long)screen.Width * screen.Height));

    /// <summary>True when the middle of the area lies inside one of the rectangles.</summary>
    private static bool Covers(FoundRectangles found, Rect area)
    {
        int x = area.X + (area.Width / 2);
        int y = area.Y + (area.Height / 2);
        return found.Items.Any(r => x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom);
    }

    [Fact]
    public void On_the_desktop_the_large_window_and_the_white_dialog_are_found_and_nothing_else()
    {
        Fixture desktop = Fixtures.Draw("desktop-windows.png");
        FoundRectangles found = Find(desktop.Image);

        Covers(found, desktop.Regions["large-window"]).ShouldBeTrue();
        Covers(found, desktop.Regions["title-bar"]).ShouldBeTrue("the light title bar is part of the window");
        Covers(found, desktop.Regions["small-dialog"]).ShouldBeTrue("a small rectangle counts when it is pure white");
        Covers(found, desktop.Regions["grey-panel"]).ShouldBeFalse("a small rectangle that is only light grey does not count");
        Covers(found, desktop.Regions["tiny-note"]).ShouldBeFalse("below the smallest size nothing counts");
        Covers(found, desktop.Regions["dark-window"]).ShouldBeFalse();
        Covers(found, desktop.Regions["desktop"]).ShouldBeFalse();
        Covers(found, desktop.Regions["picture"]).ShouldBeFalse("a dark picture larger than the smallest size is left out of the page around it");
        found.Items.Count.ShouldBeLessThanOrEqualTo(LensConstants.MaxRectangles);
        found.Margin.ShouldBe(8, "the fixture is small enough for one sample per 4-pixel block, and the margin is two samples");
    }

    [Fact]
    public void Mirrored_the_same_windows_are_found_as_dark_rectangles_under_a_light_theme()
    {
        Fixture desktop = Fixtures.Draw("desktop-windows-light.png");
        FoundRectangles found = Find(desktop.Image, themeBase: ThemeBase.Light);

        Covers(found, desktop.Regions["large-window"]).ShouldBeTrue();
        Covers(found, desktop.Regions["small-dialog"]).ShouldBeTrue();
        Covers(found, desktop.Regions["grey-panel"]).ShouldBeFalse();
        Covers(found, desktop.Regions["tiny-note"]).ShouldBeFalse();
        Covers(found, desktop.Regions["desktop"]).ShouldBeFalse();
    }

    [Theory]
    [InlineData("light-document.png", ThemeBase.Dark, true)]
    [InlineData("light-spreadsheet.png", ThemeBase.Dark, true)]
    [InlineData("dark-editor.png", ThemeBase.Dark, false)]
    [InlineData("terminal.png", ThemeBase.Dark, false)]
    [InlineData("dark-editor.png", ThemeBase.Light, true)]
    [InlineData("terminal.png", ThemeBase.Light, true)]
    [InlineData("light-document.png", ThemeBase.Light, false)]
    public void Text_does_not_count_against_a_rectangle_and_does_not_make_one(string fixture, ThemeBase themeBase, bool expected)
    {
        ImageRgb8 screen = Fixtures.Draw(fixture).Image;
        FoundRectangles found = Find(screen, themeBase: themeBase);

        // The middle of the screen is page in all of these.
        bool middle = Covers(found, new Rect(0, 0, screen.Width, screen.Height));
        middle.ShouldBe(expected, $"{fixture} under a {themeBase} theme");
        if (!expected)
        {
            found.Items.ShouldBeEmpty("light text on a dark page must not add up to a bright rectangle, nor the other way round");
        }
    }

    [Theory]
    [InlineData(0.25, 0.76, true)]
    [InlineData(0.25, 0.72, false)]
    [InlineData(0.12, 0.92, true)]
    [InlineData(0.12, 0.80, false)]
    [InlineData(0.03, 1.00, true)]
    [InlineData(0.03, 0.92, false)]
    [InlineData(0.015, 1.00, false)]
    public void The_brightness_a_rectangle_needs_falls_as_it_gets_larger(double share, double brightness, bool expected)
    {
        var canvas = new Canvas(960, 540, Desktop);
        Rect rectangle = Sized(canvas, share, 16.0 / 9.0);
        byte level = (byte)Math.Round(brightness * 255);
        canvas.Fill(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height, new Rgb8(level, level, level));

        FoundRectangles found = Find(canvas.Image);
        Covers(found, rectangle).ShouldBe(expected);
        found.Margin.ShouldBe(16, "a lens of this size is sampled every 8 pixels, and the margin is two samples");
    }

    [Fact]
    public void A_found_rectangle_stops_short_of_its_edges_and_the_margin_reaches_past_them()
    {
        var canvas = new Canvas(1920, 1080, Desktop);
        var window = new Rect(301, 203, 1001, 603);
        canvas.Fill(window.X, window.Y, window.Width, window.Height, Rgb8.White);

        FoundRectangles found = Find(canvas.Image);
        found.Items.Count.ShouldBe(1);
        LensRectangle r = found.Items[0];

        // Inside the rectangle every pixel is changed without looking, so it must not reach outside the window.
        r.Left.ShouldBeGreaterThanOrEqualTo(window.X);
        r.Top.ShouldBeGreaterThanOrEqualTo(window.Y);
        r.Right.ShouldBeLessThanOrEqualTo(window.X + window.Width);
        r.Bottom.ShouldBeLessThanOrEqualTo(window.Y + window.Height);

        // The margin is where the renderer settles the edge, so it has to hold the true edge.
        (r.Left - found.Margin).ShouldBeLessThanOrEqualTo(window.X);
        (r.Top - found.Margin).ShouldBeLessThanOrEqualTo(window.Y);
        (r.Right + found.Margin).ShouldBeGreaterThanOrEqualTo(window.X + window.Width);
        (r.Bottom + found.Margin).ShouldBeGreaterThanOrEqualTo(window.Y + window.Height);
    }

    [Fact]
    public void A_window_that_fills_the_lens_reaches_its_edges()
    {
        var canvas = new Canvas(960, 540, Rgb8.White);
        LensRectangle r = Find(canvas.Image).Items.ShouldHaveSingleItem();
        (r.Left, r.Top).ShouldBe((0, 0));
        r.Right.ShouldBeGreaterThanOrEqualTo(960);
        r.Bottom.ShouldBeGreaterThanOrEqualTo(540);
    }

    [Fact]
    public void A_window_with_another_window_over_one_corner_is_two_rectangles()
    {
        var canvas = new Canvas(1920, 1080, Desktop);
        canvas.Fill(200, 150, 1200, 800, Rgb8.White);
        canvas.Fill(1000, 100, 700, 400, Rgb8.FromHex("#101418"));

        FoundRectangles found = Find(canvas.Image);
        found.Items.Count.ShouldBe(2);
        Covers(found, new Rect(300, 200, 100, 100)).ShouldBeTrue("the part beside the other window");
        Covers(found, new Rect(1100, 700, 100, 100)).ShouldBeTrue("the part below the other window");
        Covers(found, new Rect(1100, 200, 100, 100)).ShouldBeFalse("the other window itself");
    }

    [Fact]
    public void A_bright_shape_that_is_not_made_of_rectangles_is_left_alone()
    {
        var canvas = new Canvas(960, 540, Desktop);
        canvas.Disc(480, 270, 210, Rgb8.White);
        Find(canvas.Image).Items.ShouldBeEmpty("a disc is large and bright, but four rectangles do not cover it");
    }

    [Fact]
    public void A_scroll_bar_or_a_thin_rule_does_not_split_a_rectangle()
    {
        var canvas = new Canvas(1920, 1080, Desktop);
        canvas.Fill(200, 100, 1400, 800, Rgb8.White);
        canvas.Fill(1580, 300, 12, 200, Rgb8.FromHex("#444444"));  // scroll bar thumb at the right edge
        canvas.Fill(200, 480, 1400, 2, Rgb8.FromHex("#333333"));    // a rule right across

        FoundRectangles found = Find(canvas.Image);
        Covers(found, new Rect(1500, 380, 40, 40)).ShouldBeTrue("left of the scroll bar, on both sides of the rule");
        Covers(found, new Rect(1500, 560, 40, 40)).ShouldBeTrue();

        // The thumb is seen as a bite out of the window's edge. The strip of page between the thumb and the
        // edge is too narrow for the analysis to see at all; the margin picks it up, block by block.
        LensConstants.Selected(found.Items, found.Margin, 1596, 400, blockStrength: 1f).ShouldBe(1f, "the white strip beside the thumb");
        LensConstants.Selected(found.Items, found.Margin, 1584, 400, blockStrength: 0.27f).ShouldBe(0f, "the thumb itself");
        LensConstants.Selected(found.Items, found.Margin, 1610, 400, blockStrength: 0.14f).ShouldBe(0f, "the desktop beyond the window");
    }

    [Fact]
    public void Areas_are_shares_of_the_screen_not_of_the_lens()
    {
        var canvas = new Canvas(960, 540, Desktop);
        canvas.Fill(100, 100, 400, 250, Rgb8.FromHex("#D0D0D0"));    // 82% bright, 19% of this lens

        Find(canvas.Image, screenPixels: 960 * 540).Items.ShouldNotBeEmpty("on a screen the size of the lens it is nearly a fifth");
        Find(canvas.Image, screenPixels: 1920 * 1080).Items.ShouldBeEmpty("on a full-HD screen it is under 5%, and not bright enough for that size");
    }

    [Fact]
    public void Off_or_under_a_theme_that_keeps_lightness_nothing_is_found()
    {
        ImageRgb8 screen = Fixtures.Draw("light-document.png").Image;
        Find(screen, new RectangleSettings()).Items.ShouldBeEmpty();
        Find(screen, On, ThemeBase.Keep).Items.ShouldBeEmpty();
    }

    [Fact]
    public void Values_outside_the_range_of_the_option_are_pulled_into_it()
    {
        var wild = new RectangleSettings { On = true, SmallestArea = 0.5f, FullArea = 0.1f, Brightness = 0.4f };
        RectangleSettings dark = wild.For(ThemeBase.Dark);
        dark.FullArea.ShouldBe(0.5f, "the larger size is never below the smallest");
        dark.Brightness.ShouldBe(RectangleSettings.LowestBrightThreshold);
        wild.For(ThemeBase.Light).Brightness.ShouldBe(RectangleSettings.HighestDarkThreshold);
        wild.For(ThemeBase.Keep).On.ShouldBeFalse();
        (new RectangleSettings { SmallestArea = float.NaN, FullArea = -1f }).For(ThemeBase.Dark).SmallestArea.ShouldBe(RectangleSettings.LeastArea);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Outside_the_rectangles_every_pixel_is_exactly_what_it_was(bool withFinishPass)
    {
        Fixture desktop = Fixtures.Draw("desktop-windows.png");
        Theme theme = BuiltInThemes.ById("soft-dark") with
        {
            Rectangles = On,
            Sharpen = withFinishPass ? new SharpenSettings(0.6f, SharpenMode.Standard) : SharpenSettings.Off,
            Contrast = withFinishPass ? new ContrastSettings(0.5f, ContrastMode.Local) : ContrastSettings.Off,
        };
        LensResult result = Pipeline.Run(theme, desktop.Image);
        result.Tables.Parameters.RectanglesOnly.ShouldBe(1f);
        result.Tables.Parameters.NeedsFinishPass.ShouldBeTrue("Soft dark keeps its background strictly, so the finish pass runs either way");

        foreach (string area in (string[])["desktop", "grey-panel", "tiny-note", "dark-window", "picture"])
        {
            Changed(result, desktop.Regions[area]).ShouldBe(0, $"the {area} is not part of a bright rectangle");
        }

        // The page of the large window is the theme's page colour, right up to the window's edge.
        Rgb8 page = result.Tables.Parameters.PageColour;
        result.Output[390, 150].ShouldBe(page);
        result.Output[399, 150].ShouldBe(page, "the last column of the window");
        result.Output[400, 150].ShouldBe(desktop.Image[400, 150], "the first column of the desktop");
        result.Output[20, 300].ShouldBe(page, "the first column of the window");
        result.Output[19, 300].ShouldBe(desktop.Image[19, 300]);
    }

    [Fact]
    public void Without_a_finish_pass_the_rest_of_the_screen_is_untouched_too()
    {
        Fixture desktop = Fixtures.Draw("desktop-windows.png");
        Theme theme = BuiltInThemes.ById("soft-dark") with { Rectangles = On, KeepBackground = KeepBackgroundMode.Off };
        LensResult result = Pipeline.Run(theme, desktop.Image);
        result.Tables.Parameters.NeedsFinishPass.ShouldBeFalse();

        Changed(result, desktop.Regions["desktop"]).ShouldBe(0);
        Changed(result, desktop.Regions["grey-panel"]).ShouldBe(0);
        result.Output[390, 150].ShouldBe(result.Tables.Parameters.PageColour);
    }

    [Fact]
    public void With_no_rectangle_found_the_lens_shows_the_screen_as_it_is()
    {
        ImageRgb8 editor = Fixtures.Draw("dark-editor.png").Image;
        LensResult result = Pipeline.Run(BuiltInThemes.ById("soft-dark") with { Rectangles = On }, editor);
        result.Tables.Parameters.Rectangles.ShouldBeEmpty();
        result.Output.ContentEquals(editor).ShouldBeTrue();
    }

    [Fact]
    public void In_the_margin_a_block_belongs_to_the_rectangle_only_when_it_is_bright_enough()
    {
        LensRectangle[] rectangles = [new LensRectangle(100, 100, 200, 200, Level: 0.9f)];
        LensConstants.Selected(rectangles, 12f, 150, 150, 0f).ShouldBe(1f, "inside, every pixel belongs");
        LensConstants.Selected(rectangles, 12f, 95, 150, 0.95f).ShouldBe(1f, "in the margin, a bright block belongs");
        LensConstants.Selected(rectangles, 12f, 95, 150, 0.85f).ShouldBe(0f, "in the margin, a block that is not bright enough does not");
        LensConstants.Selected(rectangles, 12f, 80, 150, 1f).ShouldBe(0f, "beyond the margin nothing belongs");
        LensConstants.Selected([rectangles[0] with { Weight = 0.25f }], 12f, 150, 150, 0f).ShouldBe(0.25f, "a rectangle on its way out counts for less");
    }

    [Fact]
    public void A_pixel_is_changed_only_where_it_is_selected_and_of_the_kind_looked_for()
    {
        LensConstants.Change(0f, 1f, selected: 0f, polarity: 0f).ShouldBe(1f, "with the option off everything is changed");
        LensConstants.Change(1f, 1f, selected: 1f, polarity: 1f).ShouldBe(1f, "dark theme: a light region inside a rectangle");
        LensConstants.Change(1f, 1f, selected: 1f, polarity: 0f).ShouldBe(0f, "dark theme: a dark region inside a rectangle stays");
        LensConstants.Change(1f, -1f, selected: 1f, polarity: 0f).ShouldBe(1f, "light theme: a dark region inside a rectangle");
        LensConstants.Change(1f, -1f, selected: 1f, polarity: 1f).ShouldBe(0f);
        LensConstants.Change(1f, 1f, selected: 0f, polarity: 1f).ShouldBe(0f, "outside every rectangle nothing is changed");
        LensConstants.Change(0.5f, 1f, selected: 0f, polarity: 1f).ShouldBe(0.5f, "half way through the fade into the option");
    }

    [Fact]
    public void A_fade_between_two_looks_carries_the_option_along()
    {
        FrameParameters whole = TableBuilder.Build(BuiltInThemes.ById("soft-dark")).Parameters;
        FrameParameters only = TableBuilder.Build(BuiltInThemes.ById("soft-dark") with { Rectangles = On }).Parameters with
        {
            Rectangles = [new LensRectangle(0, 0, 10, 10, 0.75f)],
            RectangleMargin = 12f,
        };

        FrameParameters half = FrameParameters.Lerp(whole, only, 0.5f);
        half.RectanglesOnly.ShouldBe(0.5f);
        half.Rectangles.Count.ShouldBe(1);
        half.RectangleMargin.ShouldBe(12f);
        FrameParameters.Lerp(only, whole, 0.5f).Rectangles.Count.ShouldBe(1, "on the way out the rectangles are still needed");
    }

    private static int Changed(LensResult result, Rect area)
    {
        int changed = 0;
        for (int y = area.Y; y < area.Y + area.Height; y++)
        {
            for (int x = area.X; x < area.X + area.Width; x++)
            {
                if (result.Output[x, y] != result.Source[x, y])
                {
                    changed++;
                }
            }
        }

        return changed;
    }

    /// <summary>A rectangle that covers the given share of the canvas, on whole 8-pixel samples, away from the edges.</summary>
    private static Rect Sized(Canvas canvas, double share, double aspect)
    {
        double pixels = share * canvas.Width * canvas.Height;
        int height = (int)Math.Round(Math.Sqrt(pixels / aspect) / 8) * 8;
        int width = (int)Math.Round(pixels / height / 8) * 8;
        return new Rect(80, 80, width, height);
    }
}
