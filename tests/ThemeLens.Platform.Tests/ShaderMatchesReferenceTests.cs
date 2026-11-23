// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Core.Analysis;
using ThemeLens.Core.Colour;
using ThemeLens.Core.Imaging;
using ThemeLens.Core.Mapping;
using ThemeLens.Core.Rendering;
using ThemeLens.Core.Themes;
using ThemeLens.Testing;

namespace ThemeLens.Platform.Tests;

/// <summary>
/// The GPU shader gives the same pixels as the reference renderer (section 9.1), on the software rasteriser,
/// which also proves the CPU-only path gives the same picture (section 9.4, FR-24).
/// </summary>
public sealed class ShaderMatchesReferenceTests(SoftwareRendererFixture graphics) : IClassFixture<SoftwareRendererFixture>
{
    /// <summary>
    /// The looks to compare: built-in themes of every base, and themes with each per-frame option switched on,
    /// so that every pass of the shader is exercised. "+" adds an option to a built-in theme.
    /// </summary>
    private static readonly string[] Looks =
    [
        "soft-dark",
        "high-contrast-dark",
        "clear-white",
        "soft-paper",
        "night-warm",
        "red-green-assist",
        "less-blue",
        "soft-dark+balanced",
        "soft-dark+guard-off",
        "soft-dark+local-contrast",
        "soft-dark+sharpen",
        "soft-dark+strong-sharpen",
        "clear-white+balanced+strong-sharpen",
        "less-blue+sharpen",
        "soft-paper+balanced+local-contrast+strong-sharpen",
    ];

    public static TheoryData<string, string> LooksAndFixtures()
    {
        var cases = new TheoryData<string, string>();
        foreach (string look in Looks)
        {
            foreach (string fixture in Fixtures.Names)
            {
                cases.Add(look, fixture);
            }
        }

        return cases;
    }

    [WindowsTheory]
    [MemberData(nameof(LooksAndFixtures))]
    public void The_shader_draws_what_the_reference_renderer_draws(string look, string fixture)
    {
        ImageRgb8 source = Fixtures.Draw(fixture).Image;
        LensResult reference = Pipeline.Run(LookNamed(look), source);

        ImageRgb8 drawn = graphics.Renderer.RenderToImage(source, reference.Tables);

        AssertSamePicture(reference.Output, drawn, look, fixture);
    }

    /// <summary>
    /// A lens whose width and height are not multiples of 4, 16 or 64: the shrunken levels are rounded up, and
    /// their last row and column cover pixels that are not there. The shader has to clamp exactly as the reference does.
    /// </summary>
    [WindowsTheory]
    [InlineData("soft-dark", "mixed-halves.png", 637, 395)]
    [InlineData("soft-paper+balanced+local-contrast+strong-sharpen", "web-buttons.png", 601, 333)]
    [InlineData("clear-white+balanced+strong-sharpen", "dark-editor.png", 130, 67)]
    public void The_shader_matches_at_sizes_that_do_not_divide_evenly(string look, string fixture, int width, int height)
    {
        ImageRgb8 source = Crop(Fixtures.Draw(fixture).Image, width, height);
        LensResult reference = Pipeline.Run(LookNamed(look), source);

        ImageRgb8 drawn = graphics.Renderer.RenderToImage(source, reference.Tables);

        AssertSamePicture(reference.Output, drawn, look, $"{fixture} cropped to {width}×{height}");
    }

    /// <summary>Tables built for one screen, used on another: what the lens shows right after the screen changes (section 6.5).</summary>
    [WindowsTheory]
    [InlineData("soft-dark", "light-document.png", "dark-editor.png")]
    [InlineData("clear-white", "dark-editor.png", "light-document.png")]
    public void The_shader_matches_with_tables_built_for_another_screen(string look, string analysed, string shown)
    {
        ImageRgb8 source = Fixtures.Draw(shown).Image;
        LensResult reference = Pipeline.RunWithStaleTables(LookNamed(look), Fixtures.Draw(analysed).Image, source);

        ImageRgb8 drawn = graphics.Renderer.RenderToImage(source, reference.Tables);

        AssertSamePicture(reference.Output, drawn, look, $"{shown} with tables for {analysed}");
    }

    /// <summary>FR-21: with the Passthrough theme, what comes out is what went in, to the last bit.</summary>
    [WindowsFact]
    public void Passthrough_gives_back_every_pixel_unchanged()
    {
        ImageRgb8 source = Fixtures.Draw("photo-on-white.png").Image;
        LensTables tables = TableBuilder.Build(BuiltInThemes.ById(BuiltInThemes.DefaultKeepId));
        tables.Parameters.Identity.ShouldBeTrue();

        ImageRgb8 drawn = graphics.Renderer.RenderToImage(source, tables);

        PictureComparison comparison = PictureComparison.Compare(source, drawn);
        comparison.IsIdentical.ShouldBeTrue("Passthrough changed pixels. " + comparison.Describe());
    }

    /// <summary>The analysis picture read back from the GPU is the one the reference builds from the same frame.</summary>
    [WindowsTheory]
    [InlineData("light-document.png", 640, 400)]
    [InlineData("status-dashboard.png", 640, 400)]
    [InlineData("photo-on-white.png", 637, 395)]
    [InlineData("chart.png", 130, 67)]
    public void The_thumbnail_read_back_is_the_one_the_reference_builds(string fixture, int width, int height)
    {
        ImageRgb8 source = Crop(Fixtures.Draw(fixture).Image, width, height);
        Thumbnail expected = Neighbourhood.Build(source).ToThumbnail();
        graphics.Renderer.RenderToImage(source, Pipeline.Run(LookNamed("soft-dark"), source).Tables);

        Thumbnail actual = WaitForThumbnail();

        string where = $"Fixture '{fixture}' at {width}×{height}: ";
        (actual.Width, actual.Height, actual.SourcePixelsPerSample).ShouldBe(
            (expected.Width, expected.Height, expected.SourcePixelsPerSample), where + "the thumbnail has another shape.");

        int wrongPixels = 0;
        int rangeOffByOne = 0;
        int rangeWrong = 0;
        string? first = null;
        for (int i = 0; i < expected.Pixels.Length; i++)
        {
            int x = i % expected.Width;
            int y = i / expected.Width;
            if (actual.Pixels[i] != expected.Pixels[i])
            {
                wrongPixels++;
                first ??= $"sample ({x}, {y}) expected {expected.Pixels[i]} got {actual.Pixels[i]}";
            }

            int rangeDifference = Math.Abs(actual.Range[i] - expected.Range[i]);
            if (rangeDifference == 1)
            {
                rangeOffByOne++;
            }
            else if (rangeDifference > 1)
            {
                rangeWrong++;
                first ??= $"sample ({x}, {y}) expected range {expected.Range[i]} got {actual.Range[i]}";
            }
        }

        // The samples are untouched screen pixels and must match exactly. A range is a rounded difference of two
        // lumas, and a value that lands exactly between two bytes may round the other way on the GPU: off by one, rarely.
        string summary = $"{where}{wrongPixels} of {expected.Pixels.Length} samples have the wrong colour, {rangeWrong} a range off by more than 1, "
            + $"{rangeOffByOne} a range off by exactly 1 (allowed: 1%). First: {first ?? "none"}.";
        (wrongPixels == 0 && rangeWrong == 0 && rangeOffByOne <= expected.Pixels.Length / 100).ShouldBeTrue(summary);
    }

    /// <summary>The fallback fill of FR-11 and FR-12 is exactly the colour asked for, not something near it.</summary>
    [WindowsTheory]
    [InlineData("#000000")]
    [InlineData("#0E0E0D")]
    [InlineData("#C9C4B8")]
    [InlineData("#F4ECD8")]
    [InlineData("#FFFFFF")]
    [InlineData("#12A0FE")]
    public void A_fill_is_exactly_the_colour_asked_for(string hex)
    {
        Rgb8 colour = Rgb8.FromHex(hex);

        ImageRgb8 filled = graphics.Renderer.FillToImage(96, 64, colour);

        PictureComparison comparison = PictureComparison.Compare(ImageRgb8.Filled(96, 64, colour), filled);
        comparison.IsIdentical.ShouldBeTrue($"A fill with {hex} did not read back as {hex}. " + comparison.Describe());
    }

    private static void AssertSamePicture(ImageRgb8 expected, ImageRgb8 actual, string look, string fixture)
    {
        PictureComparison comparison = PictureComparison.Compare(expected, actual);
        if (!comparison.Passes)
        {
            Assert.Fail($"Theme '{look}', fixture '{fixture}': the shader and the reference renderer disagree. {comparison.Describe()}");
        }
    }

    private static Theme LookNamed(string look)
    {
        string[] parts = look.Split('+');
        Theme theme = BuiltInThemes.ById(parts[0]);
        foreach (string option in parts.Skip(1))
        {
            theme = option switch
            {
                "balanced" => theme with { KeepBackground = KeepBackgroundMode.Balanced },
                "guard-off" => theme with { KeepBackground = KeepBackgroundMode.Off },
                "local-contrast" => theme with { Contrast = new ContrastSettings(0.6f, ContrastMode.Local) },
                "sharpen" => theme with { Sharpen = new SharpenSettings(0.7f, SharpenMode.Standard) },
                "strong-sharpen" => theme with { Sharpen = new SharpenSettings(0.7f, SharpenMode.Strong) },
                _ => throw new ArgumentException($"There is no option called '{option}'.", nameof(look)),
            };
        }

        return theme;
    }

    private static ImageRgb8 Crop(ImageRgb8 image, int width, int height)
    {
        if (width == image.Width && height == image.Height)
        {
            return image;
        }

        var cropped = new ImageRgb8(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                cropped[x, y] = image[x, y];
            }
        }

        return cropped;
    }

    /// <summary>The read-back never waits for the GPU, so the first calls may come back empty. Ask until it is there.</summary>
    private Thumbnail WaitForThumbnail()
    {
        for (int attempt = 0; attempt < 500; attempt++)
        {
            Thumbnail? thumbnail = graphics.Renderer.TryReadThumbnail();
            if (thumbnail is not null)
            {
                return thumbnail;
            }

            Thread.Sleep(2);
        }

        throw new TimeoutException("The thumbnail did not come back from the GPU within a second.");
    }
}
