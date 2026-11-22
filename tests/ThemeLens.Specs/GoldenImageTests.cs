// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Core.Colour;
using ThemeLens.Core.Imaging;
using ThemeLens.Core.Themes;
using ThemeLens.Testing;

namespace ThemeLens.Specs;

/// <summary>
/// Golden images (section 9.4): what each built-in theme makes of a light and a dark screen, compared with a
/// perceptual tolerance, so that an unintended colour change shows up as a picture a reviewer can look at.
/// </summary>
public class GoldenImageTests
{
    private static readonly string[] Screens = ["light-document.png", "dark-editor.png"];

    /// <summary>Mean colour difference (OKLab) a picture may drift by before the golden has to be reviewed.</summary>
    private const float MeanTolerance = 0.004f;

    /// <summary>No more than this share of pixels may differ clearly from the golden.</summary>
    private const float ClearlyDifferentShare = 0.005f;
    private const float ClearlyDifferent = 0.03f;

    public static TheoryData<string, string> Cases
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (Theme theme in BuiltInThemes.All)
            {
                foreach (string screen in Screens)
                {
                    data.Add(theme.Id, screen);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_theme_still_looks_the_way_it_was_approved(string themeId, string screen)
    {
        ImageRgb8 rendered = Pipeline.Run(BuiltInThemes.ById(themeId), Repository.LoadFixture(screen).Image).Output;
        string path = Path.Combine(Repository.GoldensDirectory, themeId, screen);
        if (Repository.UpdateGoldens)
        {
            Png.Write(path, rendered);
            return;
        }

        File.Exists(path).ShouldBeTrue($"No golden image for {themeId} on {screen}. Run the tests once with THEMELENS_UPDATE_GOLDENS=1 and review the picture.");
        ImageRgb8 golden = Png.Read(path);
        (golden.Width, golden.Height).ShouldBe((rendered.Width, rendered.Height));

        double sum = 0;
        int clearlyDifferent = 0;
        for (int y = 0; y < golden.Height; y++)
        {
            for (int x = 0; x < golden.Width; x++)
            {
                Rgb8 a = golden[x, y];
                Rgb8 b = rendered[x, y];
                if (a == b)
                {
                    continue;
                }

                float difference = Oklab.Distance(Oklab.FromRgb8(a), Oklab.FromRgb8(b));
                sum += difference;
                if (difference > ClearlyDifferent)
                {
                    clearlyDifferent++;
                }
            }
        }

        float mean = (float)(sum / golden.PixelCount);
        float share = (float)clearlyDifferent / golden.PixelCount;
        if (mean > MeanTolerance || share > ClearlyDifferentShare)
        {
            // Put the new picture next to the golden so the two can be compared. The name is git-ignored.
            Png.Write(Path.ChangeExtension(path, ".received.png"), rendered);
        }

        mean.ShouldBeLessThanOrEqualTo(MeanTolerance, $"{themeId} on {screen} has drifted from its golden image");
        share.ShouldBeLessThanOrEqualTo(ClearlyDifferentShare, $"{themeId} on {screen} differs clearly from its golden image in places");
    }
}
