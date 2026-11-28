// SPDX-License-Identifier: GPL-3.0-or-later
using System.Numerics;
using ThemeLens.Core.Analysis;
using ThemeLens.Core.Colour;
using ThemeLens.Core.Imaging;
using ThemeLens.Core.Mapping;
using ThemeLens.Core.Rendering;
using ThemeLens.Core.Themes;
using ThemeLens.Testing;

namespace ThemeLens.Core.Tests;

/// <summary>The "rectangles only" option (section 5.6): finding the rectangles, inverting them, and changing nothing else.</summary>
public class RectangleTests
{
    private static readonly Rgb8 Desktop = Rgb8.FromHex("#1B2430");

    /// <summary>Values that suit the 640 by 400 test screens: from a square of 100 pixels to one of 300.</summary>
    private static readonly RectangleSettings Bright = new() { SmallestSide = 100f, FullSide = 300f, Brightness = 0.75f };

    private static readonly RectangleSettings Dark = Bright with { Brightness = 0.25f };

    private static FoundRectangles Find(ImageRgb8 screen, RectangleTarget target = RectangleTarget.Bright, RectangleSettings? values = null) =>
        RectangleFinder.Find(
            Neighbourhood.Build(screen).ToThumbnail(withDetail: true).Detail!,
            values ?? (target == RectangleTarget.Bright ? Bright : Dark),
            target);

    private static Theme With(string themeId, RectangleTarget target, RectangleSettings? values = null, bool inTheme = false) =>
        BuiltInThemes.ById(themeId) with
        {
            Rectangles = new RectangleOption
            {
                On = true,
                Target = target,
                InTheme = inTheme,
                Values = values ?? (target == RectangleTarget.Bright ? Bright : Dark),
            },
        };

    /// <summary>True when the middle of the area lies inside one of the rectangles.</summary>
    private static bool Covers(FoundRectangles found, Rect area)
    {
        int x = (area.X + (area.Width / 2)) / found.Pitch;
        int y = (area.Y + (area.Height / 2)) / found.Pitch;
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
        found.Items.Count.ShouldBe(2, "the window is judged by its outline: the dark picture in it does not cut it up");
        found.Pitch.ShouldBe(4, "rectangles are found in the picture with a sample for every 4 by 4 block");
    }

    [Fact]
    public void Mirrored_the_same_windows_are_found_as_dark_rectangles()
    {
        Fixture desktop = Fixtures.Draw("desktop-windows-light.png");
        FoundRectangles found = Find(desktop.Image, RectangleTarget.Dark);

        Covers(found, desktop.Regions["large-window"]).ShouldBeTrue();
        Covers(found, desktop.Regions["small-dialog"]).ShouldBeTrue();
        Covers(found, desktop.Regions["grey-panel"]).ShouldBeFalse();
        Covers(found, desktop.Regions["tiny-note"]).ShouldBeFalse();
        Covers(found, desktop.Regions["desktop"]).ShouldBeFalse();
    }

    [Theory]
    [InlineData("light-document.png", RectangleTarget.Bright, true)]
    [InlineData("light-spreadsheet.png", RectangleTarget.Bright, true)]
    [InlineData("dark-buttons.png", RectangleTarget.Bright, true)]
    [InlineData("dark-editor.png", RectangleTarget.Bright, false)]
    [InlineData("terminal.png", RectangleTarget.Bright, false)]
    [InlineData("dark-editor.png", RectangleTarget.Dark, true)]
    [InlineData("terminal.png", RectangleTarget.Dark, true)]
    [InlineData("light-document.png", RectangleTarget.Dark, false)]
    public void Text_does_not_count_against_a_rectangle_and_does_not_make_one(string fixture, RectangleTarget target, bool expected)
    {
        ImageRgb8 screen = Fixtures.Draw(fixture).Image;
        FoundRectangles found = Find(screen, target);

        // The middle of the screen is page in all of these.
        Covers(found, new Rect(0, 0, screen.Width, screen.Height)).ShouldBe(expected, $"{fixture}, looking for {target} rectangles");
        if (!expected)
        {
            found.Items.ShouldBeEmpty("light text on a dark page must not add up to a bright rectangle, nor the other way round");
        }
    }

    [Theory]
    [InlineData(300, 300, 0.76, true)]
    [InlineData(450, 200, 0.76, true)]      // the same area as a square of 300
    [InlineData(300, 300, 0.72, false)]
    [InlineData(200, 200, 0.90, true)]
    [InlineData(200, 200, 0.80, false)]
    [InlineData(104, 104, 1.00, true)]
    [InlineData(104, 104, 0.92, false)]
    [InlineData(80, 80, 1.00, false)]
    public void The_brightness_a_rectangle_needs_falls_as_it_gets_larger(int width, int height, double brightness, bool expected)
    {
        var canvas = new Canvas(960, 540, Desktop);
        var rectangle = new Rect(81, 62, width, height);
        byte level = (byte)Math.Round(brightness * 255);
        canvas.Fill(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height, new Rgb8(level, level, level));

        Covers(Find(canvas.Image), rectangle).ShouldBe(expected);
    }

    [Fact]
    public void A_square_of_ten_pixels_is_found_wherever_it_lies_on_the_grid_of_blocks()
    {
        RectangleSettings smallest = Bright with { SmallestSide = RectangleSettings.LeastSide };
        for (int offset = 0; offset < 16; offset++)
        {
            int left = 60 + (offset % 4);
            int top = 40 + (offset / 4);
            var canvas = new Canvas(200, 120, Desktop);
            canvas.Fill(left, top, 10, 10, Rgb8.White);

            Find(canvas.Image, values: smallest).Items.Count.ShouldBe(1, $"the square at ({left}, {top})");
            Find(canvas.Image, values: smallest with { SmallestSide = 24f }).Items.ShouldBeEmpty("with the smallest size at 24 pixels a square of 10 is too small");

            // All of it is inverted, and nothing around it.
            ImageRgb8 output = Pipeline.Run(With("passthrough", RectangleTarget.Bright, smallest), canvas.Image).Output;
            for (int y = top - 6; y < top + 16; y++)
            {
                for (int x = left - 6; x < left + 16; x++)
                {
                    bool inside = x >= left && x < left + 10 && y >= top && y < top + 10;
                    output[x, y].ShouldBe(inside ? Rgb8.Black : Desktop, $"pixel ({x}, {y}) with the square at ({left}, {top})");
                }
            }
        }
    }

    [Fact]
    public void A_window_that_fills_the_lens_reaches_its_edges()
    {
        var canvas = new Canvas(960, 540, Rgb8.White);
        FoundRectangles found = Find(canvas.Image);
        LensRectangle r = found.Items.ShouldHaveSingleItem();
        (r.Left, r.Top, r.Right, r.Bottom).ShouldBe((0, 0, found.Width, found.Height));
    }

    [Fact]
    public void A_window_with_another_window_over_one_corner_is_two_rectangles()
    {
        var canvas = new Canvas(960, 540, Desktop);
        canvas.Fill(100, 76, 600, 400, Rgb8.White);
        canvas.Fill(500, 52, 352, 200, Rgb8.FromHex("#101418"));

        FoundRectangles found = Find(canvas.Image);
        found.Items.Count.ShouldBe(2);
        Covers(found, new Rect(150, 100, 50, 50)).ShouldBeTrue("the part beside the other window");
        Covers(found, new Rect(550, 350, 50, 50)).ShouldBeTrue("the part below the other window");
        Covers(found, new Rect(550, 100, 50, 50)).ShouldBeFalse("the other window itself");
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
        var canvas = new Canvas(960, 540, Desktop);
        canvas.Fill(100, 52, 700, 400, Rgb8.White);
        canvas.Fill(784, 152, 8, 100, Rgb8.FromHex("#444444"));     // scroll bar thumb near the right edge
        canvas.Fill(100, 240, 700, 2, Rgb8.FromHex("#333333"));     // a rule right across

        FoundRectangles found = Find(canvas.Image);
        found.Items.Count.ShouldBe(1);
        Covers(found, new Rect(740, 180, 20, 20)).ShouldBeTrue();
    }

    [Fact]
    public void A_page_with_dark_buttons_is_one_rectangle_and_the_buttons_keep_what_is_written_on_them()
    {
        Fixture page = Fixtures.Draw("dark-buttons.png");
        Find(page.Image).Items.Count.ShouldBe(1, "the buttons lie inside the page; they do not cut it up");

        LensResult result = Pipeline.Run(With("passthrough", RectangleTarget.Bright), page.Image);
        result.Output[4, 4].ShouldBe(Rgb8.Black, "the white page is inverted");

        // Away from its rounded corners, where the page shows, nothing of the button changes.
        Rect button = page.Regions["first-button"];
        var middle = new Rect(button.X + 8, button.Y + 8, button.Width - 16, button.Height - 16);
        Changed(result, middle).ShouldBe(0, "a dark button on the page, and its white label, stay as they are");
    }

    [Fact]
    public void Values_outside_the_range_of_the_rule_are_pulled_into_it()
    {
        var wild = new RectangleSettings { SmallestSide = 500f, FullSide = 100f, Brightness = 0.2f };
        RectangleSettings bright = wild.For(RectangleTarget.Bright);
        bright.FullSide.ShouldBe(500f, "the larger size is never below the smallest");
        bright.Brightness.ShouldBe(RectangleSettings.LowestBrightThreshold);
        (wild with { Brightness = 0.9f }).For(RectangleTarget.Dark).Brightness.ShouldBe(RectangleSettings.HighestDarkThreshold);
        (new RectangleSettings { SmallestSide = float.NaN, FullSide = -1f }).For(RectangleTarget.Bright).SmallestSide.ShouldBe(RectangleSettings.LeastSide);
        (new RectangleSettings { SmallestSide = 2f }).For(RectangleTarget.Bright).SmallestSide.ShouldBe(10f, "a square of 10 by 10 pixels is the smallest");
        RectangleSettings.HalfScreenSide(1920L * 1080L).ShouldBe(1018.2f, 0.1f, "the side of the square that covers half a full-HD screen");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Outside_the_rectangles_every_pixel_is_exactly_what_it_was(bool inTheme, bool withFinishPass)
    {
        Fixture desktop = Fixtures.Draw("desktop-windows.png");
        Theme theme = With("soft-dark", RectangleTarget.Bright, inTheme: inTheme) with
        {
            Sharpen = withFinishPass ? new SharpenSettings(0.6f, SharpenMode.Standard) : SharpenSettings.Off,
            Contrast = withFinishPass ? new ContrastSettings(0.5f, ContrastMode.Local) : ContrastSettings.Off,
            KeepBackground = withFinishPass ? KeepBackgroundMode.Strict : KeepBackgroundMode.Off,
        };
        LensResult result = Pipeline.Run(theme, desktop.Image);
        result.Tables.Parameters.RectanglesOnly.ShouldBe(1f);
        result.Tables.Parameters.NeedsFinishPass.ShouldBe(withFinishPass);

        foreach (string area in (string[])["desktop", "grey-panel", "tiny-note", "dark-window", "picture"])
        {
            Changed(result, desktop.Regions[area]).ShouldBe(0, $"the {area} is not part of a bright rectangle, or is a dark area inside one");
        }

        // The page of the large window is dark, right up to the window's edge. Plainly inverted, white is black;
        // in the theme it is the theme's page colour, which for Soft dark is black as well.
        Rgb8 page = inTheme ? result.Tables.Parameters.PageColour : Rgb8.Black;
        result.Output[390, 150].ShouldBe(page);
        result.Output[399, 150].ShouldBe(page, "the last column of the window");
        result.Output[400, 150].ShouldBe(desktop.Image[400, 150], "the first column of the desktop");
        result.Output[20, 300].ShouldBe(page, "the first column of the window");
        result.Output[19, 300].ShouldBe(desktop.Image[19, 300]);
    }

    [Fact]
    public void By_default_a_found_rectangle_is_inverted_whatever_the_theme()
    {
        Fixture desktop = Fixtures.Draw("desktop-windows.png");
        ImageRgb8 underSoftDark = Pipeline.Run(With("soft-dark", RectangleTarget.Bright), desktop.Image).Output;
        foreach (string themeId in (string[])["clear-white", "wake-up-sky", "passthrough", "less-blue", "night-warm"])
        {
            LensTables tables = Pipeline.Run(With(themeId, RectangleTarget.Bright), desktop.Image).Tables;
            tables.Parameters.Identity.ShouldBeFalse($"{themeId}: with the option on there is something to show");
            ReferenceEquals(tables.Light, tables.Dark).ShouldBeTrue("plain inversion is one table");
            Pipeline.Run(With(themeId, RectangleTarget.Bright), desktop.Image).Output.ContentEquals(underSoftDark).ShouldBeTrue(
                $"under {themeId} the picture is the same as under any other theme");
        }
    }

    [Fact]
    public void In_the_theme_is_only_possible_where_the_theme_turns_that_kind_of_rectangle_round()
    {
        With("soft-dark", RectangleTarget.Bright, inTheme: true).RectanglesInTheme.ShouldBeTrue();
        With("clear-white", RectangleTarget.Dark, inTheme: true).RectanglesInTheme.ShouldBeTrue();
        With("clear-white", RectangleTarget.Bright, inTheme: true).RectanglesInTheme.ShouldBeFalse("a light theme has no dark page to give a bright rectangle");
        With("soft-dark", RectangleTarget.Dark, inTheme: true).RectanglesInTheme.ShouldBeFalse();
        With("passthrough", RectangleTarget.Bright, inTheme: true).RectanglesInTheme.ShouldBeFalse();

        // Where it is not possible the rectangle is inverted all the same.
        Fixture desktop = Fixtures.Draw("desktop-windows.png");
        Pipeline.Run(With("clear-white", RectangleTarget.Bright, inTheme: true), desktop.Image).Output[390, 150].ShouldBe(Rgb8.Black);
    }

    [Fact]
    public void With_no_rectangle_found_the_lens_shows_the_screen_as_it_is()
    {
        ImageRgb8 editor = Fixtures.Draw("dark-editor.png").Image;
        LensResult result = Pipeline.Run(With("soft-dark", RectangleTarget.Bright), editor);
        result.Output.ContentEquals(editor).ShouldBeTrue();
    }

    [Fact]
    public void Before_the_lens_has_looked_it_inverts_every_region_of_the_kind_and_leaves_the_others()
    {
        // The look as the engine shows it until the first rectangles are in: the option not yet in force.
        Fixture desktop = Fixtures.Draw("desktop-windows.png");
        LensTables look = TableBuilder.Build(With("soft-dark", RectangleTarget.Bright));
        look.Parameters.RectangleBaseline.ShouldBe(0f);
        ImageRgb8 output = ReferenceRenderer.Render(desktop.Image, look.With(look.Parameters with { RectanglesOnly = 0f }));

        output[390, 150].ShouldBe(Rgb8.Black, "the white window is never shown white");
        output[470, 320].ShouldBe(Rgb8.Black, "nor is the small note, which will be let go once the lens has looked");
        Changed(desktop.Image, output, desktop.Regions["desktop"]).ShouldBe(0, "what is dark already is not inverted");
        Changed(desktop.Image, output, desktop.Regions["dark-window"]).ShouldBe(0);
    }

    [Fact]
    public void The_plain_inversion_keeps_every_contrast_and_every_hue()
    {
        Lut3D table = TableBuilder.InversionTable;
        table.Map(Rgb8.White).ShouldBe(Rgb8.Black);
        table.Map(Rgb8.Black).ShouldBe(Rgb8.White);

        foreach (string hex in (string[])["#222222", "#666666", "#1A73E8", "#D93025", "#2E9E44", "#F1F3F5", "#8A8F98"])
        {
            Rgb8 colour = Rgb8.FromHex(hex);
            Rgb8 inverted = table.Map(colour);
            float before = Wcag.Contrast(colour, Rgb8.White);
            float after = Wcag.Contrast(inverted, Rgb8.Black);
            after.ShouldBe(before, before * 0.06f, $"{hex} had {before:0.0} to 1 against white and has {after:0.0} to 1 against black");
        }

        Rgb8 blue = table.Map(Rgb8.FromHex("#1A73E8"));
        blue.B.ShouldBeGreaterThan(blue.R, "a blue link is still blue");
        Rgb8 red = table.Map(Rgb8.FromHex("#D93025"));
        red.R.ShouldBeGreaterThan(red.B, "a red warning is still red");
        TableBuilder.InvertedLuminance(TableBuilder.InvertedLuminance(0.3f)).ShouldBe(0.3f, 1e-4f, "inverting twice gives the colour back");
    }

    [Fact]
    public void The_mask_tells_the_inside_of_a_rectangle_from_its_rim()
    {
        LensRectangle[] rectangles = [new LensRectangle(10, 10, 20, 16, Level: 0.9f)];
        RectangleMask mask = RectangleMask.Build(40, 30, 4, rectangles);

        mask.Selected(60, 52, strength: 0f, out bool ask).ShouldBe(1f, "well inside, every pixel belongs");
        ask.ShouldBeFalse();
        mask.Selected(41, 41, strength: 0.95f, out _).ShouldBe(1f, "in the outermost ring of cells a bright pixel belongs");
        mask.Selected(41, 41, strength: 0.5f, out _).ShouldBe(0f, "and a pixel that is not bright enough does not");
        mask.Selected(37, 41, strength: 0.95f, out _).ShouldBe(1f, "the same in the ring of cells around the rectangle");
        mask.Selected(37, 41, strength: 0.5f, out _).ShouldBe(0f);
        mask.Selected(30, 41, strength: 1f, out _).ShouldBe(0f, "further out nothing belongs");

        RectangleMask fading = RectangleMask.Build(40, 30, 4, [rectangles[0] with { Weight = 0.25f }]);
        fading.Selected(60, 52, 0f, out _).ShouldBe(0.25f, 0.003f, "a rectangle on its way out counts for less");

        // Two rectangles side by side are one area: the seam between them is inside.
        RectangleMask two = RectangleMask.Build(40, 30, 4, [rectangles[0], new LensRectangle(20, 10, 30, 16, 0.9f)]);
        two.Selected(80, 52, 0f, out _).ShouldBe(1f);

        // A rectangle that reaches the edge of the lens is inside right up to it.
        RectangleMask whole = RectangleMask.Build(40, 30, 4, [new LensRectangle(0, 0, 40, 30, 0.9f)]);
        whole.Selected(0, 0, 0f, out _).ShouldBe(1f);
        whole.Selected(159, 119, 0f, out _).ShouldBe(1f);

        // Where something of the other kind lies in the rectangle, the caller is told to ask the regions.
        byte[] other = new byte[40 * 30];
        other[(12 * 40) + 14] = 1;
        RectangleMask.Build(40, 30, 4, rectangles, other).Selected(58, 50, 0f, out bool asked).ShouldBe(1f);
        asked.ShouldBeTrue();

        RectangleMask.Empty.Selected(5, 5, 1f, out _).ShouldBe(0f);
        RectangleMask.Build(40, 30, 4, []).Selected(5, 5, 1f, out _).ShouldBe(0f);
    }

    [Fact]
    public void A_pixel_is_changed_by_what_the_rectangles_give_it_once_the_option_is_in_force()
    {
        LensConstants.Change(0f, 1f, kind: 0f, selected: 0f).ShouldBe(1f, "without the option everything is changed");
        LensConstants.Change(1f, 1f, kind: 1f, selected: 0f).ShouldBe(0f, "outside every rectangle nothing is changed");
        LensConstants.Change(1f, 0f, kind: 0f, selected: 1f).ShouldBe(1f, "inside one, the pixel is");
        LensConstants.Change(0f, 0f, kind: 1f, selected: 0f).ShouldBe(1f, "plain inversion before the first look: a light region is inverted");
        LensConstants.Change(0f, 0f, kind: 0f, selected: 0f).ShouldBe(0f, "and a dark region is not");
        LensConstants.Change(0.5f, 1f, kind: 0f, selected: 0f).ShouldBe(0.5f, "half way into the option");
        LensConstants.RectangleKind(1f, 0.8f).ShouldBe(0.8f);
        LensConstants.RectangleKind(-1f, 0.8f).ShouldBe(0.2f, 1e-6f);
        LensConstants.RectangleStrength(1f, 0.9f).ShouldBe(0.9f);
        LensConstants.RectangleStrength(-1f, 0.9f).ShouldBe(0.1f, 1e-6f);
    }

    [Fact]
    public void A_fade_between_two_looks_carries_the_option_along()
    {
        FrameParameters whole = TableBuilder.Build(BuiltInThemes.ById("soft-dark")).Parameters;
        var mask = RectangleMask.Build(4, 4, 4, [new LensRectangle(0, 0, 2, 2, 0.75f)]);
        FrameParameters only = TableBuilder.Build(With("soft-dark", RectangleTarget.Dark)).Parameters with { Mask = mask };
        whole.SelectsPixels.ShouldBeFalse();
        only.SelectsPixels.ShouldBeTrue();

        FrameParameters half = FrameParameters.Lerp(whole, only, 0.5f);
        half.RectanglesOnly.ShouldBe(0.5f);
        half.RectangleBaseline.ShouldBe(0.5f);
        half.RectangleTarget.ShouldBe(-1f);
        half.Mask.ShouldBeSameAs(mask);
        FrameParameters back = FrameParameters.Lerp(only, whole, 0.5f);
        back.Mask.ShouldBeSameAs(mask, "on the way out the rectangles are still needed");
        back.RectangleTarget.ShouldBe(-1f);
    }

    [Fact]
    public void The_detailed_picture_has_a_sample_for_every_block_until_the_lens_gets_very_large()
    {
        Thumbnail plain = Neighbourhood.Build(Fixtures.Draw("light-document.png").Image).ToThumbnail();
        plain.Detail.ShouldBeNull("nobody asked for it");

        Thumbnail detailed = Neighbourhood.Build(Fixtures.Draw("light-document.png").Image).ToThumbnail(withDetail: true);
        (detailed.Width, detailed.Height).ShouldBe((plain.Width, plain.Height));
        (detailed.Detail!.Width, detailed.Detail.Height, detailed.Detail.SourcePixelsPerSample).ShouldBe((160, 100, 4));

        Thumbnail.DetailStrideFor(480, 270).ShouldBe(1, "full HD: every block");
        Thumbnail.DetailStrideFor(640, 360).ShouldBe(1, "2560 by 1440: every block");
        Thumbnail.DetailStrideFor(960, 540).ShouldBe(2, "4K: every second block");
    }

    private static int Changed(LensResult result, Rect area) => Changed(result.Source, result.Output, area);

    private static int Changed(ImageRgb8 source, ImageRgb8 output, Rect area)
    {
        int changed = 0;
        for (int y = area.Y; y < area.Y + area.Height; y++)
        {
            for (int x = area.X; x < area.X + area.Width; x++)
            {
                if (output[x, y] != source[x, y])
                {
                    changed++;
                }
            }
        }

        return changed;
    }
}
