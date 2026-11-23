// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using Json.Schema;
using ThemeLens.Core.Colour;
using ThemeLens.Core.Themes;
using ThemeLens.Testing;

namespace ThemeLens.Core.Tests;

public class ThemeFilesTests
{
    private static readonly Lazy<JsonSchema> Schema = new(() =>
        JsonSchema.FromText(File.ReadAllText(Path.Combine(Repository.ThemesDirectory, "theme-1.schema.json"))));

    public static TheoryData<string> BuiltInIds => new(BuiltInThemes.All.Select(t => t.Id));

    [Fact]
    public void All_thirteen_built_in_themes_load_in_the_order_they_are_offered()
    {
        BuiltInThemes.All.Select(t => t.Name).ShouldBe(
        [
            "Soft dark", "High contrast dark", "Red–green assist dark", "Night warm",
            "Clear white", "Soft paper", "High contrast light", "Evening paper",
            "Passthrough", "Red–green assist", "Less blue", "Greyscale", "Dimmer",
        ]);
    }

    [Fact]
    public void Each_base_has_the_default_the_specification_names()
    {
        BuiltInThemes.DefaultFor(ThemeBase.Dark).Name.ShouldBe("Soft dark");
        BuiltInThemes.DefaultFor(ThemeBase.Light).Name.ShouldBe("Clear white");
        BuiltInThemes.DefaultFor(ThemeBase.Keep).Name.ShouldBe("Passthrough");
    }

    [Fact]
    public void Every_theme_file_in_the_themes_folder_is_a_built_in_theme_named_after_its_id()
    {
        string[] files = Directory.GetFiles(Repository.ThemesDirectory, "*" + ThemeJson.FileExtension);
        files.Length.ShouldBe(BuiltInThemes.All.Count);
        foreach (string file in files)
        {
            Theme theme = ThemeJson.Load(file);
            Path.GetFileName(file).ShouldBe(theme.Id + ThemeJson.FileExtension);
            BuiltInThemes.ById(theme.Id).ShouldBe(theme);
        }
    }

    [Theory]
    [MemberData(nameof(BuiltInIds))]
    public void Built_in_theme_files_satisfy_the_published_schema(string id)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repository.ThemesDirectory, id + ThemeJson.FileExtension)));
        EvaluationResults results = Schema.Value.Evaluate(document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        results.IsValid.ShouldBeTrue(string.Join("; ", (results.Details ?? []).Where(d => d.Errors is not null).SelectMany(d => d.Errors!.Values)));
    }

    [Theory]
    [InlineData("soft-dark", 12.1)]
    [InlineData("high-contrast-dark", 21.0)]
    [InlineData("red-green-assist-dark", 12.1)]
    [InlineData("night-warm", 11.5)]
    [InlineData("clear-white", 18.9)]
    [InlineData("soft-paper", 14.1)]
    [InlineData("high-contrast-light", 21.0)]
    [InlineData("evening-paper", 12.1)]
    public void Text_contrast_is_the_figure_in_the_specification(string id, double contrast)
    {
        ThemeRoles roles = BuiltInThemes.ById(id).RequireRoles();
        Wcag.Contrast(roles.Text, roles.Page).ShouldBe((float)contrast, 0.06f);
    }

    [Theory]
    [InlineData("high-contrast-dark", "#FFFF00", 19.6)]
    [InlineData("high-contrast-light", "#0000C8", 11.5)]
    public void High_contrast_links_are_the_ones_in_the_specification(string id, string link, double contrast)
    {
        ThemeRoles roles = BuiltInThemes.ById(id).RequireRoles();
        roles.Link.ShouldBe(Rgb8.FromHex(link));
        Wcag.Contrast(roles.Link, roles.Page).ShouldBe((float)contrast, 0.06f);
    }

    [Theory]
    [MemberData(nameof(BuiltInIds))]
    public void A_theme_obeys_its_own_limits(string id)
    {
        Theme theme = BuiltInThemes.ById(id);
        if (theme.Roles is not { } roles)
        {
            return;
        }

        foreach (Role role in (Role[])[Role.Page, Role.Panel, Role.Text, Role.QuietText, Role.Link, Role.Good, Role.Warning, Role.Bad])
        {
            Rgb8 colour = roles[role];
            Wcag.Luminance(colour).ShouldBeLessThanOrEqualTo(theme.Limits.MaxLuminance + 1e-3f, $"{role} is brighter than the theme's own limit");
            if (theme.Limits.MaxChroma is { } maxChroma)
            {
                Oklab.FromRgb8(colour).Chroma.ShouldBeLessThanOrEqualTo(maxChroma + 5e-3f, $"{role} is more saturated than the theme's own limit");
            }
        }

        Wcag.Contrast(roles.Text, roles.Page).ShouldBeGreaterThanOrEqualTo(theme.Limits.MinTextContrast);

        // Accents are read as text too, so each needs solid contrast against the page.
        foreach (Role role in (Role[])[Role.Link, Role.Good, Role.Warning, Role.Bad])
        {
            Wcag.Contrast(roles[role], roles.Page).ShouldBeGreaterThanOrEqualTo(4.5f, $"{role} is too close to the page");
        }
    }

    [Fact]
    public void The_red_green_pair_stays_distinct_under_simulated_colour_blindness()
    {
        ThemeRoles roles = BuiltInThemes.ById("red-green-assist-dark").RequireRoles();
        foreach (ColourVisionDeficiency d in (ColourVisionDeficiency[])[ColourVisionDeficiency.Protanopia, ColourVisionDeficiency.Deuteranopia])
        {
            Oklab good = Oklab.FromRgb8(ColourVision.Simulate(roles.Good, d));
            Oklab bad = Oklab.FromRgb8(ColourVision.Simulate(roles.Bad, d));
            Oklab.Distance(good, bad).ShouldBeGreaterThan(0.2f);
        }

        // The pair also differs in lightness for normal vision and under deuteranopia. Under simulated
        // protanopia the lightness gap of the specification's pair shrinks to about 0.03 (the orange darkens);
        // the two remain far apart on the blue-yellow axis. See docs/decisions.md.
        MathF.Abs(Oklab.FromRgb8(roles.Good).L - Oklab.FromRgb8(roles.Bad).L).ShouldBeGreaterThan(0.08f);
        MathF.Abs(
            Oklab.FromRgb8(ColourVision.Simulate(roles.Good, ColourVisionDeficiency.Deuteranopia)).L
            - Oklab.FromRgb8(ColourVision.Simulate(roles.Bad, ColourVisionDeficiency.Deuteranopia)).L).ShouldBeGreaterThan(0.08f);
    }

    [Fact]
    public void Passthrough_changes_nothing_and_every_other_theme_changes_something()
    {
        foreach (Theme theme in BuiltInThemes.All)
        {
            theme.ChangesNothing.ShouldBe(theme.Id == "passthrough", theme.Id);
        }
    }

    [Theory]
    [MemberData(nameof(BuiltInIds))]
    public void A_theme_survives_being_saved_and_loaded_again(string id)
    {
        Theme theme = BuiltInThemes.ById(id);
        ThemeJson.Parse(ThemeJson.Serialize(theme)).ShouldBe(theme);
    }

    [Fact]
    public void A_saved_theme_satisfies_the_schema_too()
    {
        Theme custom = BuiltInThemes.ById("soft-dark") with
        {
            Id = "my-theme",
            Name = "My theme",
            Sharpen = new SharpenSettings(0.4f, SharpenMode.Strong),
            Contrast = new ContrastSettings(0.25f, ContrastMode.Local),
            KeepBackground = KeepBackgroundMode.Balanced,
        };
        using JsonDocument document = JsonDocument.Parse(ThemeJson.Serialize(custom));
        Schema.Value.Evaluate(document.RootElement).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("{}", "\"id\" is required")]
    [InlineData("""{"id":"x","name":"X"}""", "\"base\" is required")]
    [InlineData("""{"id":"Bad Id","name":"X","base":"keep"}""", "\"id\" must be")]
    [InlineData("""{"id":"x","name":"X","base":"dark"}""", "must define \"roles\"")]
    [InlineData("""{"id":"x","name":"X","base":"keep","roles":{}}""", "must not define \"roles\"")]
    [InlineData("""{"id":"x","name":"X","base":"keep","warmth":2}""", "\"warmth\" must be between 0 and 1")]
    [InlineData("""{"id":"x","name":"X","base":"keep","limits":{"maxLuminance":0}}""", "\"limits.maxLuminance\" must be between")]
    [InlineData("""{"id":"x","name":"X","base":"keep","script":"calc.exe"}""", "not valid")]
    [InlineData("""{"id":"x","name":"X","base":"sideways"}""", "not valid")]
    [InlineData("not json", "not valid")]
    public void A_broken_theme_file_is_refused_with_a_message_for_its_author(string json, string expected) =>
        Should.Throw<ThemeFormatException>(() => ThemeJson.Parse(json)).Message.ShouldContain(expected);

    [Fact]
    public void A_dark_theme_must_have_a_page_darker_than_its_text()
    {
        string json = ThemeJson.Serialize(BuiltInThemes.ById("soft-dark")).Replace("\"base\": \"dark\"", "\"base\": \"light\"", StringComparison.Ordinal);
        Should.Throw<ThemeFormatException>(() => ThemeJson.Parse(json)).Message.ShouldContain("lighter than");
    }

    [Fact]
    public void A_bad_colour_is_named_in_the_message()
    {
        string json = ThemeJson.Serialize(BuiltInThemes.ById("soft-dark")).Replace("#8FB4D9", "blue", StringComparison.Ordinal);
        Should.Throw<ThemeFormatException>(() => ThemeJson.Parse(json)).Message.ShouldContain("roles.link");
    }
}
