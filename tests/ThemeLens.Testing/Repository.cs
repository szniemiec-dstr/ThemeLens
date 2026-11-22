// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Core.Imaging;

namespace ThemeLens.Testing;

/// <summary>Finds files in the source tree from wherever a test happens to run.</summary>
public static class Repository
{
    private static readonly Lazy<string> RootPath = new(FindRoot);

    public static string Root => RootPath.Value;

    public static string FixturesDirectory => Path.Combine(Root, "tests", "ThemeLens.Specs", "Fixtures");

    public static string GoldensDirectory => Path.Combine(Root, "tests", "ThemeLens.Specs", "Goldens");

    public static string ThemesDirectory => Path.Combine(Root, "themes");

    /// <summary>Set THEMELENS_UPDATE_FIXTURES=1 to rewrite the fixture PNG files from the drawing code.</summary>
    public static bool UpdateFixtures => IsSet("THEMELENS_UPDATE_FIXTURES");

    /// <summary>Set THEMELENS_UPDATE_GOLDENS=1 to rewrite the golden images from the current renderer.</summary>
    public static bool UpdateGoldens => IsSet("THEMELENS_UPDATE_GOLDENS");

    /// <summary>Loads a committed fixture picture together with the facts the drawing code knows about it.</summary>
    public static Fixture LoadFixture(string fileName)
    {
        Fixture drawn = Fixtures.Draw(fileName);
        string path = Path.Combine(FixturesDirectory, fileName);
        ImageRgb8 image = File.Exists(path) ? Png.Read(path) : drawn.Image;
        return drawn with { Image = image };
    }

    private static bool IsSet(string variable) =>
        Environment.GetEnvironmentVariable(variable) is "1" or "true" or "TRUE";

    private static string FindRoot()
    {
        for (DirectoryInfo? d = new(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "ThemeLens.slnx")))
            {
                return d.FullName;
            }
        }

        throw new InvalidOperationException("Could not find ThemeLens.slnx above " + AppContext.BaseDirectory);
    }
}
