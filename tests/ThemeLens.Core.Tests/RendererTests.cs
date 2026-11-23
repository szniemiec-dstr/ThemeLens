// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Core.Analysis;
using ThemeLens.Core.Colour;
using ThemeLens.Core.Imaging;
using ThemeLens.Core.Mapping;
using ThemeLens.Core.Measurement;
using ThemeLens.Core.Rendering;
using ThemeLens.Core.Themes;
using ThemeLens.Testing;

namespace ThemeLens.Core.Tests;

public class RendererTests
{
    private static readonly Theme SoftDark = BuiltInThemes.ById("soft-dark");

    [Fact]
    public void An_identity_look_returns_the_frame_untouched()
    {
        ImageRgb8 source = Fixtures.Draw("photo-on-white.png").Image;
        ImageRgb8 output = ReferenceRenderer.Render(source, TableBuilder.Build(BuiltInThemes.ById("passthrough")));
        output.ContentEquals(source).ShouldBeTrue();
        output.ShouldNotBeSameAs(source);
    }

    [Fact]
    public void A_flat_white_screen_becomes_exactly_the_page_colour_without_any_analysis()
    {
        ImageRgb8 output = ReferenceRenderer.Render(ImageRgb8.Filled(200, 120, Rgb8.White), TableBuilder.Build(SoftDark));
        output.ContentEquals(ImageRgb8.Filled(200, 120, Rgb8.Black)).ShouldBeTrue();
    }

    [Fact]
    public void A_flat_black_screen_also_becomes_the_page_colour()
    {
        ImageRgb8 output = ReferenceRenderer.Render(ImageRgb8.Filled(200, 120, Rgb8.Black), TableBuilder.Build(SoftDark));
        output.ContentEquals(ImageRgb8.Filled(200, 120, Rgb8.Black)).ShouldBeTrue();
    }

    [Theory]
    [InlineData("clear-white")]
    [InlineData("soft-paper")]
    public void On_a_light_theme_both_flat_screens_become_the_light_page(string id)
    {
        Theme theme = BuiltInThemes.ById(id);
        LensTables tables = TableBuilder.Build(theme);
        ImageRgb8 page = ImageRgb8.Filled(200, 120, theme.RequireRoles().Page);
        ReferenceRenderer.Render(ImageRgb8.Filled(200, 120, Rgb8.Black), tables).ContentEquals(page).ShouldBeTrue();
        ReferenceRenderer.Render(ImageRgb8.Filled(200, 120, Rgb8.White), tables).ContentEquals(page).ShouldBeTrue();
    }

    [Fact]
    public void Where_a_light_and_a_dark_region_meet_there_is_no_glowing_band()
    {
        // Left half white, right half near-black: each side belongs to its own region, and the boundary between
        // them must come out as page colour on both sides.
        var c = new Canvas(400, 200, Rgb8.White);
        c.Fill(200, 0, 200, 200, Rgb8.FromHex("#1E1E1E"));
        ImageRgb8 output = ReferenceRenderer.Render(c.Image, TableBuilder.Build(SoftDark));
        Measure.MaxLuminance(output).ShouldBeLessThan(0.02f);
    }

    [Fact]
    public void The_area_guard_holds_even_when_the_table_is_wrong()
    {
        // A table that leaves everything as it is, under a dark theme's rules: the worst case of a stale or
        // broken table. A white page must still not get through; text on it must stay readable.
        FrameParameters dark = TableBuilder.Build(SoftDark).Parameters;
        Lut3D identity = Lut3D.Identity();
        Fixture document = Fixtures.Draw("light-document.png");

        ImageRgb8 output = ReferenceRenderer.Render(document.Image, identity, identity, dark);

        Measure.LargestAreaShare(output, dark).ShouldBeLessThanOrEqualTo(0.005f);
        Measure.MedianLuminanceWhere(output, document.Image, document.Page)!.Value.ShouldBeLessThan(0.01f);
        Measure.MedianLuminanceWhere(output, document.Image, document.Text)!.Value.ShouldBeGreaterThan(0.2f);
    }

    [Fact]
    public void With_the_guard_off_the_same_wrong_table_lets_the_white_page_through()
    {
        FrameParameters off = TableBuilder.Build(SoftDark with { KeepBackground = KeepBackgroundMode.Off }).Parameters;
        Lut3D identity = Lut3D.Identity();
        ImageRgb8 output = ReferenceRenderer.Render(Fixtures.Draw("light-document.png").Image, identity, identity, off);
        Measure.LargestAreaShare(output, off).ShouldBeGreaterThan(0.3f);
    }

    [Fact]
    public void Balanced_lets_an_image_like_area_through_that_strict_would_flatten()
    {
        // Again with a table that changes nothing, so the photo arrives at the guard as bright as it was.
        Fixture photo = Fixtures.Draw("photo-on-white.png");
        Rect region = photo.Regions["photo"];
        Lut3D identity = Lut3D.Identity();
        FrameParameters strict = TableBuilder.Build(SoftDark).Parameters;
        FrameParameters balanced = TableBuilder.Build(SoftDark with { KeepBackground = KeepBackgroundMode.Balanced }).Parameters;

        float strictMean = MeanLuminance(ReferenceRenderer.Render(photo.Image, identity, identity, strict), region);
        float balancedMean = MeanLuminance(ReferenceRenderer.Render(photo.Image, identity, identity, balanced), region);

        strictMean.ShouldBeLessThan(0.08f);
        balancedMean.ShouldBeGreaterThan(strictMean * 3f);
    }

    [Fact]
    public void A_small_solid_shape_is_content_to_recolour_not_a_region_of_its_own()
    {
        // A dark icon, a checkbox or a thick rule on a light page: recoloured like text, solid, not hollowed out.
        var c = new Canvas(640, 400, Rgb8.White);
        c.Fill(100, 100, 14, 14, Rgb8.FromHex("#222222"));      // an icon
        c.Fill(200, 200, 300, 12, Rgb8.FromHex("#222222"));     // a thick rule
        ImageRgb8 output = Pipeline.Run(SoftDark, c.Image).Output;

        Wcag.Luminance(output[107, 107]).ShouldBeGreaterThan(0.4f, "the middle of the icon");
        Wcag.Luminance(output[350, 206]).ShouldBeGreaterThan(0.4f, "the middle of the rule");
        Wcag.Luminance(output[50, 50]).ShouldBeLessThan(0.01f, "the page");
    }

    [Fact]
    public void A_large_solid_shape_of_the_other_polarity_is_a_region_and_becomes_page()
    {
        var c = new Canvas(640, 400, Rgb8.White);
        c.Fill(200, 100, 240, 200, Rgb8.FromHex("#1E1E1E"));
        ImageRgb8 output = Pipeline.Run(SoftDark, c.Image).Output;
        Wcag.Luminance(output[320, 200]).ShouldBeLessThan(0.01f);
        Wcag.Luminance(output[50, 50]).ShouldBeLessThan(0.01f);
    }

    [Theory]
    [InlineData(12, false)]
    [InlineData(15, false)]
    [InlineData(16, true)]
    [InlineData(20, true)]
    [InlineData(31, true)]
    public void A_dark_band_is_content_or_a_region_by_its_thickness_wherever_it_lies_on_the_grid(int thickness, bool region)
    {
        // The lens works on 4×4 blocks. The answer must not depend on where a shape falls on that grid, or a
        // button would flicker between light and dark while the page scrolls under the lens.
        for (int offset = 0; offset < 4; offset++)
        {
            var c = new Canvas(320, 200, Rgb8.White);
            c.Fill(40, 80 + offset, 240, thickness, Rgb8.FromHex("#1E1E1E"));
            c.Fill(100 + offset, 20, thickness, 40, Rgb8.FromHex("#1E1E1E"));

            ImageRgb8 output = Pipeline.Run(SoftDark, c.Image).Output;

            foreach ((int x, int y) in new[] { (160, 80 + offset + (thickness / 2)), (100 + offset + (thickness / 2), 40) })
            {
                float middle = Wcag.Luminance(output[x, y]);
                string what = $"a band {thickness} pixels thick, {offset} pixels off the grid, at ({x}, {y})";
                if (region)
                {
                    middle.ShouldBeLessThan(0.02f, what + " is a dark region and stays dark");
                }
                else
                {
                    middle.ShouldBeGreaterThan(0.4f, what + " is content and turns light, as text does");
                }
            }
        }
    }

    [Theory]
    [InlineData(20)]
    [InlineData(28)]
    [InlineData(40)]
    public void A_dark_button_on_a_light_page_is_one_colour_and_keeps_its_label(int height)
    {
        Rgb8 fill = Rgb8.FromHex("#111111");
        var c = new Canvas(320, 200, Rgb8.White);
        c.Fill(60, 70, height * 3, height, fill);
        c.Text(68, 70 + (height / 2) + 4, "Buy", 9f, 1.4f, Rgb8.White);

        ImageRgb8 output = Pipeline.Run(SoftDark, c.Image).Output;

        float darkest = 1f;
        float lightest = 0f;
        var label = new List<float>();
        for (int y = 70; y < 70 + height; y++)
        {
            for (int x = 60; x < 60 + (height * 3); x++)
            {
                float luminance = Wcag.Luminance(output[x, y]);
                if (c.Image[x, y] == fill)
                {
                    darkest = MathF.Min(darkest, luminance);
                    lightest = MathF.Max(lightest, luminance);
                }
                else if (c.Image[x, y] == Rgb8.White)
                {
                    label.Add(luminance);
                }
            }
        }

        lightest.ShouldBeLessThan(0.02f, "the whole button is a dark region; no part of it turns light");
        (lightest - darkest).ShouldBeLessThan(0.01f, "and it is one colour throughout");
        label.Sort();
        label[label.Count / 2].ShouldBeGreaterThan(0.3f, "the label belongs to the button and stays light");
    }

    [Fact]
    public void The_gap_between_two_dark_shapes_on_a_light_page_is_page()
    {
        // Chips in a row, tiles in a grid: the gaps are far from open page, but they are connected to it.
        var c = new Canvas(640, 400, Rgb8.White);
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                c.Fill(60 + (column * 128), 40 + (row * 108), 120, 100, Rgb8.FromHex("#1E1E1E"));
            }
        }

        ImageRgb8 output = Pipeline.Run(SoftDark, c.Image).Output;

        Wcag.Luminance(output[184, 200]).ShouldBeLessThan(0.02f, "a gap between two tiles in the middle row");
        Wcag.Luminance(output[300, 144]).ShouldBeLessThan(0.02f, "a gap between two rows");
        Wcag.Luminance(output[312, 252]).ShouldBeLessThan(0.02f, "where four tiles meet");
    }

    [Fact]
    public void A_light_bar_inside_a_dark_panel_is_content_and_stays_light()
    {
        // A scrollbar thumb or a progress bar: light, but connected to no light region.
        var c = new Canvas(640, 400, Rgb8.White);
        c.Fill(100, 60, 400, 280, Rgb8.FromHex("#1E1E1E"));
        c.Fill(470, 100, 12, 120, Rgb8.White);
        c.Fill(140, 300, 200, 8, Rgb8.White);

        ImageRgb8 output = Pipeline.Run(SoftDark, c.Image).Output;

        Wcag.Luminance(output[476, 160]).ShouldBeGreaterThan(0.4f, "the thumb");
        Wcag.Luminance(output[240, 304]).ShouldBeGreaterThan(0.4f, "the bar");
        Wcag.Luminance(output[300, 200]).ShouldBeLessThan(0.02f, "the panel");
        Wcag.Luminance(output[50, 200]).ShouldBeLessThan(0.02f, "the page");
    }

    [Fact]
    public void No_bright_line_appears_where_a_text_filled_light_region_meets_a_dark_one()
    {
        // The light half is full of text right up to the boundary, so it casts far fewer votes than the clean
        // dark half. The boundary must still be drawn where it is.
        Fixture mixed = Fixtures.Draw("mixed-halves.png");
        ImageRgb8 output = Pipeline.Run(SoftDark, mixed.Image).Output;
        int boundary = mixed.Image.Width / 2;
        int bright = 0;
        for (int y = 0; y < mixed.Image.Height; y++)
        {
            for (int x = boundary - 20; x < boundary + 20; x++)
            {
                // Page pixels only: white on the light side, the editor's background on the dark side.
                Rgb8 page = x < boundary ? Rgb8.White : Rgb8.FromHex("#1E1E1E");
                if (mixed.Image[x, y] == page && Wcag.Luminance(output[x, y]) > 0.1f)
                {
                    bright++;
                }
            }
        }

        // A line down the boundary would be hundreds of pixels. A few pixels are tolerated at the one corner
        // where the light page, the dark half and a mid-tone title bar all meet.
        bright.ShouldBeLessThan(16);
    }

    [Theory]
    [InlineData(12, false)]
    [InlineData(16, false)]
    [InlineData(20, true)]
    [InlineData(40, true)]
    [InlineData(80, true)]
    public void The_guard_leaves_small_shapes_alone_and_catches_areas(int side, bool guarded)
    {
        // A table that changes nothing under a dark theme's rules, so a white square arrives at the guard as white.
        FrameParameters dark = TableBuilder.Build(SoftDark).Parameters;
        Lut3D identity = Lut3D.Identity();
        var c = new Canvas(640, 400, Rgb8.Black);
        c.Fill(300, 180, side, side, Rgb8.White);

        ImageRgb8 output = ReferenceRenderer.Render(c.Image, identity, identity, dark);

        float middle = Wcag.Luminance(output[300 + (side / 2), 180 + (side / 2)]);
        if (guarded)
        {
            middle.ShouldBeLessThan(0.02f, "an area is pulled back to the page colour");
        }
        else
        {
            middle.ShouldBeGreaterThan(0.5f, "a checkbox, a dot or a badge is not an area");
        }
    }

    [Fact]
    public void The_guard_catches_a_long_band_even_though_it_is_not_tall()
    {
        FrameParameters dark = TableBuilder.Build(SoftDark).Parameters;
        Lut3D identity = Lut3D.Identity();
        var c = new Canvas(640, 400, Rgb8.Black);
        c.Fill(0, 190, 640, 20, Rgb8.White);        // a toolbar that came out bright
        c.Fill(0, 300, 640, 6, Rgb8.White);         // a line
        ImageRgb8 output = ReferenceRenderer.Render(c.Image, identity, identity, dark);
        Wcag.Luminance(output[320, 200]).ShouldBeLessThan(0.02f);
        Wcag.Luminance(output[320, 303]).ShouldBeGreaterThan(0.5f);
        Measure.LargestAreaShare(output, dark).ShouldBeLessThanOrEqualTo(0.005f);
    }

    [Theory]
    [InlineData(160, 120)]
    [InlineData(320, 200)]
    [InlineData(640, 400)]
    [InlineData(1280, 800)]
    public void Whatever_the_lens_size_no_bright_area_is_left(int width, int height)
    {
        // FR-10 with a table that is plain wrong: white squares of every size from a dot to a panel, at
        // every position on the 4-pixel grid. An area is an area whatever the size of the lens.
        FrameParameters dark = TableBuilder.Build(SoftDark).Parameters;
        Lut3D identity = Lut3D.Identity();
        int step = 0;
        for (int side = 8; side <= Math.Min(width, height) / 2; side += 3)
        {
            var c = new Canvas(width, height, Rgb8.Black);
            int left = ((width - side) / 2) + (step % 4);
            int top = ((height - side) / 2) + ((step / 4) % 4);
            step += 5;
            c.Fill(left, top, side, side, Rgb8.White);

            ImageRgb8 output = ReferenceRenderer.Render(c.Image, identity, identity, dark);

            Measure.LargestAreaShare(output, dark).ShouldBe(0f, $"a square of {side} pixels at ({left}, {top}) on a lens of {width} by {height}");
        }
    }

    [Fact]
    public void Text_and_thin_lines_are_never_treated_as_areas()
    {
        Fixture editor = Fixtures.Draw("dark-editor.png");
        LensResult result = Pipeline.Run(SoftDark, editor.Image);
        float text = Measure.MedianLuminanceWhere(result.Output, editor.Image, editor.Text)!.Value;
        text.ShouldBeGreaterThan(0.45f, "text is far above the area limit and must be left alone");
    }

    [Theory]
    [InlineData(SharpenMode.Standard)]
    [InlineData(SharpenMode.Strong)]
    public void Sharpening_a_flat_picture_changes_nothing(SharpenMode mode)
    {
        Theme theme = BuiltInThemes.ById("less-blue") with { Sharpen = new SharpenSettings(1f, mode) };
        LensTables tables = TableBuilder.Build(theme);
        ImageRgb8 flat = ImageRgb8.Filled(120, 80, new Rgb8(90, 140, 200));
        ImageRgb8 sharpened = ReferenceRenderer.Render(flat, tables);
        ImageRgb8 plain = ReferenceRenderer.Render(flat, TableBuilder.Build(BuiltInThemes.ById("less-blue")));
        sharpened.ContentEquals(plain).ShouldBeTrue();
    }

    [Fact]
    public void Sharpening_changes_lightness_only()
    {
        // A soft grey edge on the keep base: after sharpening every pixel is still grey.
        var c = new Canvas(120, 80, new Rgb8(60, 60, 60));
        for (int x = 0; x < 120; x++)
        {
            byte v = (byte)Math.Clamp(60 + ((x - 50) * 30), 60, 180);
            c.Fill(x, 0, 1, 80, new Rgb8(v, v, v));
        }

        Theme theme = BuiltInThemes.ById("passthrough") with { Sharpen = new SharpenSettings(1f, SharpenMode.Strong) };
        ImageRgb8 output = ReferenceRenderer.Render(c.Image, TableBuilder.Build(theme));
        for (int x = 0; x < 120; x++)
        {
            Rgb8 p = output[x, 40];
            p.R.ShouldBe(p.G);
            p.G.ShouldBe(p.B);
        }

        Measure.EdgeStrength(output, c.Image).ShouldBeGreaterThan(Measure.EdgeStrength(c.Image, c.Image));
    }

    [Fact]
    public void Strong_sharpening_limits_its_halos()
    {
        // A hard black-to-white edge: the result may overshoot the local range only by the small allowance.
        var c = new Canvas(120, 80, new Rgb8(64, 64, 64));
        c.Fill(60, 0, 60, 80, new Rgb8(160, 160, 160));
        Theme theme = BuiltInThemes.ById("passthrough") with { Sharpen = new SharpenSettings(1f, SharpenMode.Strong) };
        ImageRgb8 output = ReferenceRenderer.Render(c.Image, TableBuilder.Build(theme));
        int allowance = (int)MathF.Ceiling(LensConstants.HaloAllowance * 255f) + 1;
        for (int x = 0; x < 120; x++)
        {
            ((int)output[x, 40].R).ShouldBeInRange(64 - allowance, 160 + allowance);
        }
    }

    [Fact]
    public void The_neighbourhood_levels_shrink_by_four_each_time_and_cover_odd_sizes()
    {
        Neighbourhood n = Neighbourhood.Build(ImageRgb8.Filled(643, 401, Rgb8.White));
        (n.FineWidth, n.FineHeight).ShouldBe((161, 101));
        (n.CoarseWidth, n.CoarseHeight).ShouldBe((41, 26));
        (n.WideWidth, n.WideHeight).ShouldBe((11, 7));
        n.PolarityAt(642, 400, 0.5f, 1f).ShouldBe(1f, 0.01f);
        n.SoftFractionAt(10, 10).ShouldBe(0f);
    }

    [Theory]
    [InlineData(640, 400, 160, 100)]
    [InlineData(1920, 1080, 160, 90)]
    [InlineData(2560, 1440, 160, 90)]
    [InlineData(3840, 2160, 160, 90)]
    [InlineData(400, 300, 100, 75)]
    public void The_analysis_picture_is_about_160_by_90(int width, int height, int expectedWidth, int expectedHeight)
    {
        Thumbnail thumbnail = Neighbourhood.Build(ImageRgb8.Filled(width, height, Rgb8.White)).ToThumbnail();
        (thumbnail.Width, thumbnail.Height).ShouldBe((expectedWidth, expectedHeight));
    }

    [Fact]
    public void The_analysis_picture_holds_untouched_screen_colours()
    {
        Fixture document = Fixtures.Draw("light-document.png");
        Thumbnail thumbnail = Neighbourhood.Build(document.Image).ToThumbnail();
        thumbnail.Pixels.ShouldContain(document.Page);
        thumbnail.Pixels.ShouldContain(document.Text);
        thumbnail.Pixels.ShouldContain(Rgb8.FromHex("#1A73E8"));
    }

    [Fact]
    public void The_largest_area_measure_ignores_lines_and_counts_areas_at_full_size()
    {
        FrameParameters dark = TableBuilder.Build(BuiltInThemes.ById("high-contrast-dark")).Parameters;
        var c = new Canvas(400, 250, Rgb8.Black);
        c.Fill(0, 20, 400, 6, Rgb8.White);                     // a long line: 2.4% of the picture, but not an area
        Measure.LargestAreaShare(c.Image, dark).ShouldBe(0f);

        c.Fill(100, 60, 80, 50, Rgb8.White);                   // an area: 4% of the picture
        Measure.LargestAreaShare(c.Image, dark).ShouldBe(0.04f, 0.001f);

        FrameParameters light = TableBuilder.Build(BuiltInThemes.ById("clear-white")).Parameters;
        Measure.LargestAreaShare(ImageRgb8.Filled(100, 100, Rgb8.Black), light).ShouldBe(1f);
        Measure.LargestAreaShare(ImageRgb8.Filled(100, 100, Rgb8.White), light).ShouldBe(0f);
    }

    [Fact]
    public void Png_files_round_trip()
    {
        ImageRgb8 photo = Fixtures.Draw("photo-on-white.png").Image;
        Png.Decode(Png.Encode(photo)).ContentEquals(photo).ShouldBeTrue();
    }

    private static float MeanLuminance(ImageRgb8 image, Rect region)
    {
        double sum = 0;
        for (int y = region.Y; y < region.Y + region.Height; y++)
        {
            for (int x = region.X; x < region.X + region.Width; x++)
            {
                sum += Wcag.Luminance(image[x, y]);
            }
        }

        return (float)(sum / (region.Width * region.Height));
    }
}
