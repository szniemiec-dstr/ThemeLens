// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Core.Colour;
using ThemeLens.Core.Mapping;
using ThemeLens.Core.Themes;

namespace ThemeLens.Core.Tests;

public class ColourNameAndCatalogueTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "themelens-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData("#FFFFFF", "White")]
    [InlineData("#222222", "Near black")]
    [InlineData("#F1F3F5", "Light grey")]
    [InlineData("#1A73E8", "Blue")]
    [InlineData("#D93025", "Red")]
    [InlineData("#2E9E44", "Green")]
    [InlineData("#000000", "Black")]
    [InlineData("#C9C4B8", "Off-white")]
    [InlineData("#0E0E0D", "Charcoal")]
    [InlineData("#8FB4D9", "Pale blue")]
    [InlineData("#D98A7E", "Soft red")]
    [InlineData("#7FB08A", "Soft green")]
    public void Colours_are_named_as_in_the_control_window_wireframe(string hex, string expected) =>
        ColourName.Of(Rgb8.FromHex(hex)).ToEnglish().ShouldBe(expected);

    [Theory]
    [InlineData("#FFB000", ColourFamily.Orange)]
    [InlineData("#FFFF00", ColourFamily.Yellow)]
    [InlineData("#009688", ColourFamily.Teal)]
    [InlineData("#8E44AD", ColourFamily.Purple)]
    [InlineData("#0D47A1", ColourFamily.Blue)]
    [InlineData("#808080", ColourFamily.Grey)]
    [InlineData("#555555", ColourFamily.DarkGrey)]
    public void Every_colour_gets_a_family(string hex, ColourFamily family) =>
        ColourName.Of(Rgb8.FromHex(hex)).Family.ShouldBe(family);

    [Fact]
    public void A_dark_colour_says_so()
    {
        ColourName.Of(Rgb8.FromHex("#0D47A1")).ToEnglish().ShouldBe("Dark blue");
        ColourName.Of(Rgb8.FromHex("#808080")).IsNeutral.ShouldBeTrue();
        ColourName.Of(Rgb8.FromHex("#0D47A1")).IsNeutral.ShouldBeFalse();
    }

    [Fact]
    public void A_catalogue_without_a_folder_offers_the_built_in_themes()
    {
        var catalogue = new ThemeCatalogue(null);
        catalogue.All.Count().ShouldBe(BuiltInThemes.All.Count);
        catalogue.User.ShouldBeEmpty();
        catalogue.Find("soft-dark").ShouldNotBeNull();
        catalogue.IsBuiltIn("soft-dark").ShouldBeTrue();
        Should.Throw<InvalidOperationException>(() => catalogue.SaveAs(BuiltInThemes.ById("soft-dark"), "Mine"));
    }

    [Fact]
    public void The_current_settings_can_be_saved_as_a_named_theme_and_loaded_again()
    {
        var catalogue = new ThemeCatalogue(_folder);
        Theme adjusted = BuiltInThemes.ById("soft-dark") with
        {
            Sharpen = new SharpenSettings(0.5f, SharpenMode.Strong),
            KeepBackground = KeepBackgroundMode.Balanced,
            Limits = BuiltInThemes.ById("soft-dark").Limits with { MaxLuminance = 0.4f },
        };

        Theme saved = catalogue.SaveAs(adjusted, "Evening reading");

        saved.Id.ShouldBe("evening-reading");
        saved.Name.ShouldBe("Evening reading");
        File.Exists(Path.Combine(_folder, "evening-reading.theme.json")).ShouldBeTrue();

        var reopened = new ThemeCatalogue(_folder);
        Theme loaded = reopened.Find("evening-reading").ShouldNotBeNull();
        loaded.Sharpen.ShouldBe(new SharpenSettings(0.5f, SharpenMode.Strong));
        loaded.KeepBackground.ShouldBe(KeepBackgroundMode.Balanced);
        loaded.Limits.MaxLuminance.ShouldBe(0.4f);
        loaded.Roles.ShouldBe(adjusted.Roles);
        reopened.Problems.ShouldBeEmpty();
    }

    [Fact]
    public void Saving_under_the_same_name_replaces_the_theme_and_a_built_in_name_gets_its_own_id()
    {
        var catalogue = new ThemeCatalogue(_folder);
        catalogue.SaveAs(BuiltInThemes.ById("soft-dark"), "Mine");
        catalogue.SaveAs(BuiltInThemes.ById("night-warm"), "mine");
        catalogue.User.Count.ShouldBe(1);
        catalogue.User[0].RequireRoles().Text.ShouldBe(BuiltInThemes.ById("night-warm").RequireRoles().Text);

        Theme copy = catalogue.SaveAs(BuiltInThemes.ById("soft-dark"), "Soft dark");
        copy.Id.ShouldBe("soft-dark-2");
        catalogue.Find("soft-dark")!.ShouldBe(BuiltInThemes.ById("soft-dark"));
    }

    [Fact]
    public void A_theme_saved_at_a_different_warmth_looks_the_same_when_loaded()
    {
        var catalogue = new ThemeCatalogue(_folder);
        Theme warmer = BuiltInThemes.ById("soft-dark") with { Warmth = 0.7f };
        Rgb8 shownBefore = TableBuilder.Build(warmer).Parameters.TextColour;

        Theme saved = catalogue.SaveAs(warmer, "Warmer");

        saved.Warmth.ShouldBe(0.7f);
        saved.AuthoredWarmth.ShouldBe(0.7f);
        TableBuilder.Build(saved).Parameters.TextColour.MaxChannelDifference(shownBefore).ShouldBeLessThanOrEqualTo(2);
        saved.RequireRoles().Text.MaxChannelDifference(shownBefore).ShouldBeLessThanOrEqualTo(2);
    }

    [Fact]
    public void A_broken_user_theme_is_reported_and_does_not_hide_the_others()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "broken.theme.json"), """{"id":"broken","name":"Broken","base":"dark"}""");
        File.WriteAllText(Path.Combine(_folder, "clash.theme.json"), ThemeJson.Serialize(BuiltInThemes.ById("soft-dark")));
        File.WriteAllText(Path.Combine(_folder, "fine.theme.json"), ThemeJson.Serialize(BuiltInThemes.ById("dimmer") with { Id = "fine", Name = "Fine" }));

        var catalogue = new ThemeCatalogue(_folder);

        catalogue.User.Select(t => t.Id).ShouldBe(["fine"]);
        catalogue.Problems.Select(p => p.File).ShouldBe(["broken.theme.json", "clash.theme.json"], ignoreOrder: true);
        catalogue.Problems.Single(p => p.File == "broken.theme.json").Message.ShouldContain("roles");
    }

    [Fact]
    public void A_user_theme_can_be_deleted_but_a_built_in_one_cannot()
    {
        var catalogue = new ThemeCatalogue(_folder);
        Theme saved = catalogue.SaveAs(BuiltInThemes.ById("dimmer"), "Dim");
        catalogue.Delete("soft-dark").ShouldBeFalse();
        catalogue.Delete(saved.Id).ShouldBeTrue();
        catalogue.User.ShouldBeEmpty();
        catalogue.Delete(saved.Id).ShouldBeFalse();
    }

    [Theory]
    [InlineData("Evening reading", "evening-reading")]
    [InlineData("  Żółty   motyw! ", "zoty-motyw")]
    [InlineData("Red–green assist (mine)", "red-green-assist-mine")]
    [InlineData("!!!", "theme")]
    public void A_name_becomes_a_file_safe_id(string name, string id) => ThemeCatalogue.Slug(name).ShouldBe(id);
}
