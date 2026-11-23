// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Core.Imaging;
using ThemeLens.Testing;

namespace ThemeLens.Specs;

/// <summary>
/// The fixture pictures in <c>Fixtures/</c> are the output of the drawing code in ThemeLens.Testing, committed so
/// that people can look at them. This keeps the two from drifting apart.
/// </summary>
public class FixtureFilesTests
{
    public static TheoryData<string> FixtureNames => new(Fixtures.Names);

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void The_committed_picture_is_what_the_drawing_code_draws(string name)
    {
        ImageRgb8 drawn = Fixtures.Draw(name).Image;
        string path = Path.Combine(Repository.FixturesDirectory, name);
        if (Repository.UpdateFixtures)
        {
            Png.Write(path, drawn);
            return;
        }

        File.Exists(path).ShouldBeTrue($"{name} is missing. Run the tests once with THEMELENS_UPDATE_FIXTURES=1.");
        ImageRgb8 committed = Png.Read(path);
        (committed.Width, committed.Height).ShouldBe((drawn.Width, drawn.Height));

        // One step of slack per channel: the drawing uses floating-point maths that differs in the last bit
        // between operating systems.
        int worst = 0;
        for (int i = 0; i < drawn.Data.Length; i++)
        {
            worst = Math.Max(worst, Math.Abs(drawn.Data[i] - committed.Data[i]));
        }

        worst.ShouldBeLessThanOrEqualTo(1, $"{name} no longer matches its drawing code. Regenerate it with THEMELENS_UPDATE_FIXTURES=1 and review the change.");
    }

    [Fact]
    public void There_are_no_stray_pictures_in_the_fixtures_folder()
    {
        Directory.GetFiles(Repository.FixturesDirectory, "*.png").Select(Path.GetFileName)
            .ShouldBe(Fixtures.Names, ignoreOrder: true);
    }
}
