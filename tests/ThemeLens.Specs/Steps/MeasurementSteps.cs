// SPDX-License-Identifier: GPL-3.0-or-later
using System.Numerics;
using ThemeLens.Core.Analysis;
using ThemeLens.Core.Colour;
using ThemeLens.Core.Imaging;
using ThemeLens.Core.Mapping;
using ThemeLens.Core.Measurement;
using ThemeLens.Specs.Support;
using ThemeLens.Testing;

namespace ThemeLens.Specs.Steps;

/// <summary>Steps that measure a rendered picture, using the definitions of section 9.2.</summary>
[Binding]
public sealed class MeasurementSteps(LensWorld world)
{
    /// <summary>Two colours are "the same on screen" within this many 8-bit steps per channel (table interpolation and rounding).</summary>
    private const int SameColourSteps = 3;

    /// <summary>Half an 8-bit step of luminance near the limit: the limit is applied before rounding to 8 bits.</summary>
    private const float RoundingLuminance = 0.004f;

    private Oklab _first;
    private Oklab _second;

    [Then("the page colour on screen is the theme's page colour")]
    public void ThenThePageColourOnScreenIsTheThemesPageColour()
    {
        LensResult result = world.RequireResult();
        Rgb8 sourcePage = world.RequireFixture().Page;
        Rgb8 expected = result.Tables.Parameters.PageColour;

        int total = 0;
        int matching = 0;
        for (int y = 0; y < result.Source.Height; y++)
        {
            for (int x = 0; x < result.Source.Width; x++)
            {
                if (result.Source[x, y] != sourcePage)
                {
                    continue;
                }

                total++;
                if (result.Output[x, y].MaxChannelDifference(expected) <= SameColourSteps)
                {
                    matching++;
                }
            }
        }

        total.ShouldBeGreaterThan(0, "the fixture should contain its page colour");
        ((double)matching / total).ShouldBeGreaterThanOrEqualTo(
            0.97,
            $"page pixels should be shown as {expected}; only {matching} of {total} are");
    }

    [Then("the largest bright area is at most {float}% of the lens")]
    [Then("the largest dark area is at most {float}% of the lens")]
    public void ThenTheLargestAreaIsAtMost(float percent)
    {
        LensResult result = world.RequireResult();
        float share = Measure.LargestAreaShare(result.Output, result.Tables.Parameters);
        (share * 100f).ShouldBeLessThanOrEqualTo(percent);
    }

    [Then("text contrast is at least {float} to 1")]
    public void ThenTextContrastIsAtLeast(float ratio) =>
        LensWorld.TextContrast(world.RequireResult(), world.RequireFixture()).ShouldBeGreaterThanOrEqualTo(ratio);

    [Then("text contrast is at least as high as with contrast off")]
    public void ThenTextContrastIsAtLeastAsHighAsWithContrastOff()
    {
        Fixture fixture = world.RequireFixture();
        float with = LensWorld.TextContrast(world.RequireResult(), fixture);
        float without = LensWorld.TextContrast(world.Baseline!, fixture);
        with.ShouldBeGreaterThanOrEqualTo(without - 0.05f);
    }

    [Then("text edges stay smooth")]
    public void ThenTextEdgesStaySmooth()
    {
        // Output lightness has to be monotonic along a ramp from the source page colour to the source text colour:
        // these are the in-between colours of anti-aliased text.
        LensResult result = world.RequireResult();
        Fixture fixture = world.RequireFixture();
        Lut3D table = result.Tables.For(LensWorld.PageRegion(result, fixture));
        Vector3 page = Srgb.ToEncoded(fixture.Page);
        Vector3 text = Srgb.ToEncoded(fixture.Text);

        // Lightness is taken at display precision (8 bits per channel), and a reversal has to be more than
        // one display step to count: anything smaller is rounding, not a kink in the curve.
        var lightness = new List<float>();
        for (int i = 0; i <= 255; i++)
        {
            Vector3 mapped = result.Tables.Parameters.Identity ? Vector3.Lerp(page, text, i / 255f) : table.Sample(Vector3.Lerp(page, text, i / 255f));
            lightness.Add(Vector3.Dot(Srgb.ToEncoded(Srgb.FromEncoded(mapped)), Wcag.LuminanceWeights));
        }

        Measure.IsMonotonic(lightness, tolerance: 1.01f / 255f).ShouldBeTrue("lightness should never reverse between page and text");
    }

    [Then("the {string} and {string} markers differ by at least {float} in OKLab")]
    public void ThenTheMarkersDifferBy(string first, string second, float distance)
    {
        _first = MarkerColour(first);
        _second = MarkerColour(second);
        Oklab.Distance(_first, _second).ShouldBeGreaterThanOrEqualTo(distance);
    }

    [Then("their lightness differs by at least {float}")]
    public void ThenTheirLightnessDiffersBy(float difference) =>
        MathF.Abs(_first.L - _second.L).ShouldBeGreaterThanOrEqualTo(difference);

    [Then("the blue share is at most {int}% of the source's")]
    public void ThenTheBlueShareIsAtMost(int percent)
    {
        LensResult result = world.RequireResult();
        float source = Measure.BlueShare(result.Source);
        float output = Measure.BlueShare(result.Output);
        (output / source * 100f).ShouldBeLessThanOrEqualTo(percent);
    }

    [Then("the lens picture is identical to the screen")]
    public void ThenTheLensPictureIsIdenticalToTheScreen()
    {
        LensResult result = world.RequireResult();
        result.Output.ContentEquals(result.Source).ShouldBeTrue("every output pixel should equal the input pixel");
    }

    [Then("edges are stronger than with sharpen off")]
    public void ThenEdgesAreStrongerThanWithSharpenOff()
    {
        LensResult result = world.RequireResult();
        float with = Measure.EdgeStrength(result.Output, result.Source);
        float without = Measure.EdgeStrength(world.Baseline!.Output, result.Source);
        with.ShouldBeGreaterThan(without);
    }

    [Then("no pixel is brighter than the theme's brightness limit")]
    public void ThenNoPixelIsBrighterThanTheLimit()
    {
        LensResult result = world.RequireResult();
        Measure.MaxLuminance(result.Output).ShouldBeLessThanOrEqualTo(world.RequireTheme().Limits.MaxLuminance + RoundingLuminance);
    }

    [Then("no pixel is brighter than the page")]
    public void ThenNoPixelIsBrighterThanThePage()
    {
        LensResult result = world.RequireResult();
        Measure.MaxLuminance(result.Output).ShouldBeLessThanOrEqualTo(result.Tables.Parameters.PageLuminance + RoundingLuminance);
    }

    [Then("the picture has no colour left")]
    public void ThenThePictureHasNoColourLeft()
    {
        ImageRgb8 output = world.RequireResult().Output;
        float worst = 0f;
        for (int y = 0; y < output.Height; y++)
        {
            for (int x = 0; x < output.Width; x++)
            {
                worst = MathF.Max(worst, Oklab.FromRgb8(output[x, y]).Chroma);
            }
        }

        worst.ShouldBeLessThan(0.012f);
    }

    [Then("the lens finds at most {int} colour clusters and gives each a role")]
    public void ThenTheLensFindsAtMostClusters(int count)
    {
        LensResult result = world.RequireResult();
        result.Scene.Clusters.Count.ShouldBeInRange(1, count);
        result.Roles.Entries.Count.ShouldBe(result.Scene.Clusters.Count);
    }

    [Then("the colour {string} is used as {string}")]
    public void ThenTheColourIsUsedAs(string hex, string role)
    {
        Rgb8 colour = Rgb8.FromHex(hex);
        RoleEntry entry = world.RequireResult().Roles.Entries
            .OrderBy(e => Oklab.Distance(e.Cluster.Lab, Oklab.FromRgb8(colour)))
            .First();
        Oklab.Distance(entry.Cluster.Lab, Oklab.FromRgb8(colour)).ShouldBeLessThan(0.03f, $"a cluster near {hex} should have been found");
        entry.Role.ToString().ShouldBe(role.Replace(" ", string.Empty, StringComparison.Ordinal), StringCompareShould.IgnoreCase);
    }

    [Then("the colour {string} is shown as the theme's {string} colour")]
    public void ThenTheColourIsShownAsTheThemes(string hex, string role)
    {
        LensResult result = world.RequireResult();
        Rgb8 colour = Rgb8.FromHex(hex);
        RoleEntry entry = result.Roles.Entries
            .OrderBy(e => Oklab.Distance(e.Cluster.Lab, Oklab.FromRgb8(colour)))
            .First();
        var wanted = Enum.Parse<Core.Themes.Role>(role.Replace(" ", string.Empty, StringComparison.Ordinal), ignoreCase: true);
        Rgb8 expected = world.RequireTheme().RequireRoles()[wanted];
        result.Tables.Map(entry.Cluster.Colour, entry.Region).MaxChannelDifference(expected).ShouldBeLessThanOrEqualTo(SameColourSteps);
    }

    private Oklab MarkerColour(string name)
    {
        Rect region = world.RequireFixture().Regions[name];
        ImageRgb8 picture = world.Viewed ?? world.RequireResult().Output;
        Vector3 sum = Vector3.Zero;
        for (int y = region.Y; y < region.Y + region.Height; y++)
        {
            for (int x = region.X; x < region.X + region.Width; x++)
            {
                sum += Srgb.ToLinear(picture[x, y]);
            }
        }

        return Oklab.FromLinear(sum / (region.Width * region.Height));
    }
}
