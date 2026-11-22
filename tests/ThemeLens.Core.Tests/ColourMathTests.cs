// SPDX-License-Identifier: GPL-3.0-or-later
using System.Numerics;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using ThemeLens.Core.Colour;

namespace ThemeLens.Core.Tests;

public class ColourMathTests
{
    [Theory]
    [InlineData("#000000", 0, 0, 0)]
    [InlineData("#FFFFFF", 255, 255, 255)]
    [InlineData("#C9C4B8", 201, 196, 184)]
    [InlineData("#1a73e8", 26, 115, 232)]
    public void Hex_is_parsed(string hex, byte r, byte g, byte b) => Rgb8.FromHex(hex).ShouldBe(new Rgb8(r, g, b));

    [Theory]
    [InlineData("C9C4B8")]
    [InlineData("#C9C4B")]
    [InlineData("#C9C4B8FF")]
    [InlineData("#GGGGGG")]
    [InlineData("")]
    public void Malformed_hex_is_rejected(string hex)
    {
        Rgb8.TryParseHex(hex, out _).ShouldBeFalse();
        Should.Throw<FormatException>(() => Rgb8.FromHex(hex));
    }

    [Property]
    public bool Hex_round_trips(byte r, byte g, byte b)
    {
        var colour = new Rgb8(r, g, b);
        return Rgb8.FromHex(colour.ToHex()) == colour;
    }

    [Property]
    public bool Srgb_encoding_round_trips_every_8_bit_colour(byte r, byte g, byte b)
    {
        var colour = new Rgb8(r, g, b);
        return Srgb.FromLinear(Srgb.ToLinear(colour)) == colour;
    }

    [Property]
    public bool Oklab_round_trips_every_8_bit_colour(byte r, byte g, byte b)
    {
        var colour = new Rgb8(r, g, b);
        return Srgb.FromLinear(Oklab.FromRgb8(colour).ToLinear()) == colour;
    }

    [Property]
    public bool LumLab_round_trips_every_8_bit_colour(byte r, byte g, byte b)
    {
        var colour = new Rgb8(r, g, b);
        return Srgb.FromLinear(LumLab.FromRgb8(colour).ToLinear()).MaxChannelDifference(colour) <= 1;
    }

    [Property]
    public bool LumLab_lightness_is_the_cube_root_of_luminance(byte r, byte g, byte b)
    {
        var colour = new Rgb8(r, g, b);
        LumLab lab = LumLab.FromRgb8(colour);
        return MathF.Abs(lab.Luminance - Wcag.Luminance(colour)) < 1e-4f;
    }

    [Property]
    public Property LumLab_delivers_the_luminance_it_was_asked_for() =>
        Prop.ForAll(
            Gen.Choose(0, 1000).Select(i => i / 1000f).ToArbitrary(),
            Gen.Choose(-150, 150).Select(i => i / 1000f).ToArbitrary(),
            Gen.Choose(-150, 150).Select(i => i / 1000f).ToArbitrary(),
            (j, a, b) =>
            {
                // Whatever hue and chroma, the result has luminance J cubed once fitted into gamut.
                Vector3 linear = Gamut.Fit(new LumLab(j, a, b).ToLinear());
                return MathF.Abs(Wcag.Luminance(linear) - (j * j * j)) < 2e-3f;
            });

    [Fact]
    public void Oklab_matches_published_reference_values()
    {
        // From Björn Ottosson's article: white is L 1, and the sRGB primaries have these hue angles.
        Oklab white = Oklab.FromRgb8(Rgb8.White);
        white.L.ShouldBe(1f, 1e-3f);
        white.Chroma.ShouldBeLessThan(1e-3f);

        Oklab.FromRgb8(new Rgb8(255, 0, 0)).HueDegrees.ShouldBe(29.2f, 0.5f);
        Oklab.FromRgb8(new Rgb8(0, 255, 0)).HueDegrees.ShouldBe(142.5f, 0.5f);
        Oklab.FromRgb8(new Rgb8(0, 0, 255)).HueDegrees.ShouldBe(264.1f, 0.5f);
        Oklab.FromRgb8(new Rgb8(255, 0, 0)).L.ShouldBe(0.628f, 2e-3f);
    }

    [Theory]
    [InlineData("#FFFFFF", "#000000", 21.0)]
    [InlineData("#C9C4B8", "#000000", 12.07)]
    [InlineData("#767676", "#FFFFFF", 4.54)]
    [InlineData("#0000C8", "#FFFFFF", 11.45)]
    public void Wcag_contrast_matches_known_values(string a, string b, double expected) =>
        Wcag.Contrast(Rgb8.FromHex(a), Rgb8.FromHex(b)).ShouldBe((float)expected, 0.02f);

    [Property]
    public bool Contrast_is_symmetric_and_at_least_one(byte a, byte b)
    {
        var x = new Rgb8(a, a, a);
        var y = new Rgb8(b, b, b);
        float c = Wcag.Contrast(x, y);
        return c >= 1f && c <= 21.01f && MathF.Abs(c - Wcag.Contrast(y, x)) < 1e-5f;
    }

    [Property]
    public Property Luminance_at_contrast_inverts_contrast() =>
        Prop.ForAll(
            Gen.Choose(0, 1000).Select(i => i / 1000f).ToArbitrary(),
            Gen.Choose(100, 2000).Select(i => i / 100f).ToArbitrary(),
            (reference, contrast) =>
            {
                float lighter = Wcag.LuminanceAtContrast(reference, contrast, lighter: true);
                float darker = Wcag.LuminanceAtContrast(reference, contrast, lighter: false);
                bool lighterOk = lighter > 1f || MathF.Abs(Wcag.Contrast(lighter, reference) - contrast) < 0.01f;
                bool darkerOk = darker < 0f || MathF.Abs(Wcag.Contrast(darker, reference) - contrast) < 0.01f;
                return lighterOk && darkerOk;
            });

    [Property]
    public Property Gamut_fit_keeps_luminance_and_lands_in_range() =>
        Prop.ForAll(
            Gen.Choose(-500, 1500).Select(i => i / 1000f).ToArbitrary(),
            Gen.Choose(-500, 1500).Select(i => i / 1000f).ToArbitrary(),
            Gen.Choose(-500, 1500).Select(i => i / 1000f).ToArbitrary(),
            (r, g, b) =>
            {
                var input = new Vector3(r, g, b);
                Vector3 fitted = Gamut.Fit(input);
                float wanted = Math.Clamp(Wcag.Luminance(input), 0f, 1f);
                bool inRange = fitted.X is >= 0f and <= 1f && fitted.Y is >= 0f and <= 1f && fitted.Z is >= 0f and <= 1f;
                return inRange && MathF.Abs(Wcag.Luminance(fitted) - wanted) < 1e-3f;
            });

    [Fact]
    public void Simulated_dichromacy_confuses_red_and_green_but_not_blue_and_yellow()
    {
        // A red and a green of equal luminance are hard to tell apart for a protanope or deuteranope...
        var red = new Rgb8(200, 60, 40);
        Rgb8 green = Srgb.FromLinear(Gamut.WithLuminance(Srgb.ToLinear(new Rgb8(40, 160, 60)), Wcag.Luminance(red)));
        float normal = Oklab.Distance(Oklab.FromRgb8(red), Oklab.FromRgb8(green));
        foreach (ColourVisionDeficiency d in (ColourVisionDeficiency[])[ColourVisionDeficiency.Protanopia, ColourVisionDeficiency.Deuteranopia])
        {
            float seen = Oklab.Distance(Oklab.FromRgb8(ColourVision.Simulate(red, d)), Oklab.FromRgb8(ColourVision.Simulate(green, d)));
            seen.ShouldBeLessThan(normal * 0.6f);

            // ...while blue and yellow stay far apart.
            float blueYellow = Oklab.Distance(
                Oklab.FromRgb8(ColourVision.Simulate(new Rgb8(40, 80, 220), d)),
                Oklab.FromRgb8(ColourVision.Simulate(new Rgb8(230, 200, 40), d)));
            blueYellow.ShouldBeGreaterThan(0.3f);
        }
    }

    [Property]
    public bool Simulation_leaves_greys_grey(byte v)
    {
        var grey = new Rgb8(v, v, v);
        return ColourVision.Simulate(grey, ColourVisionDeficiency.Protanopia).MaxChannelDifference(grey) <= 1
            && ColourVision.Simulate(grey, ColourVisionDeficiency.Deuteranopia).MaxChannelDifference(grey) <= 1
            && ColourVision.Simulate(grey, ColourVisionDeficiency.Tritanopia).MaxChannelDifference(grey) <= 1;
    }
}
