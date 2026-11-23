// SPDX-License-Identifier: GPL-3.0-or-later
using System.Numerics;
using ThemeLens.Core.Analysis;
using ThemeLens.Core.Colour;
using ThemeLens.Core.Mapping;
using ThemeLens.Core.Themes;
using ThemeLens.Testing;

namespace ThemeLens.Core.Tests;

public class TableBuilderTests
{
    public static TheoryData<string> BuiltInIds => new(BuiltInThemes.All.Select(t => t.Id));

    public static TheoryData<string> RoleThemeIds => new(BuiltInThemes.All.Where(t => t.HasRoles).Select(t => t.Id));

    public static TheoryData<string, string> RoleThemesAndScreens
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (Theme theme in BuiltInThemes.All.Where(t => t.HasRoles))
            {
                foreach (string fixture in (string[])["light-document.png", "dark-editor.png", "web-buttons.png", "terminal.png", "mixed-halves.png"])
                {
                    data.Add(theme.Id, fixture);
                }
            }

            return data;
        }
    }

    private static IEnumerable<Vector3> Entries(Lut3D lut)
    {
        for (int i = 0; i < lut.Data.Length; i += 3)
        {
            yield return new Vector3(lut.Data[i], lut.Data[i + 1], lut.Data[i + 2]);
        }
    }

    [Fact]
    public void Passthrough_is_the_identity()
    {
        LensTables tables = TableBuilder.Build(BuiltInThemes.ById("passthrough"));
        tables.Parameters.Identity.ShouldBeTrue();
        tables.Map(new Rgb8(12, 200, 99), RegionPolarity.Light).ShouldBe(new Rgb8(12, 200, 99));
    }

    [Fact]
    public void The_identity_table_returns_every_8_bit_colour_unchanged()
    {
        Lut3D identity = Lut3D.Identity();
        for (int v = 0; v < 256; v++)
        {
            var colour = new Rgb8((byte)v, (byte)(255 - v), (byte)((v * 7) % 256));
            identity.Map(colour).ShouldBe(colour);
        }
    }

    [Theory]
    [MemberData(nameof(BuiltInIds))]
    public void Building_a_table_is_deterministic(string id)
    {
        Theme theme = BuiltInThemes.ById(id);
        (SceneAnalysis scene, RoleMap roles) = Pipeline.Analyse(Fixtures.Draw("web-buttons.png").Image);
        LensTables a = TableBuilder.Build(theme, roles, scene);
        LensTables b = TableBuilder.Build(theme, roles, scene);
        a.Light.Data.AsSpan().SequenceEqual(b.Light.Data).ShouldBeTrue();
        a.Dark.Data.AsSpan().SequenceEqual(b.Dark.Data).ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(RoleThemesAndScreens))]
    public void No_entry_of_any_table_is_brighter_than_the_theme_allows(string id, string fixture)
    {
        Theme theme = BuiltInThemes.ById(id);
        (SceneAnalysis scene, RoleMap roles) = Pipeline.Analyse(Fixtures.Draw(fixture).Image);
        LensTables tables = TableBuilder.Build(theme, roles, scene);
        foreach (Lut3D lut in (Lut3D[])[tables.Light, tables.Dark])
        {
            float brightest = Entries(lut).Max(e => Wcag.Luminance(Srgb.Decode(e)));
            brightest.ShouldBeLessThanOrEqualTo(theme.Limits.MaxLuminance + 1e-3f);
            Entries(lut).ShouldAllBe(e => e.X >= 0f && e.X <= 1f && e.Y >= 0f && e.Y <= 1f && e.Z >= 0f && e.Z <= 1f);
        }
    }

    [Theory]
    [MemberData(nameof(RoleThemesAndScreens))]
    public void A_chroma_limit_holds_for_every_entry(string id, string fixture)
    {
        Theme theme = BuiltInThemes.ById(id);
        if (theme.Limits.MaxChroma is not { } maxChroma)
        {
            return;
        }

        (SceneAnalysis scene, RoleMap roles) = Pipeline.Analyse(Fixtures.Draw(fixture).Image);
        LensTables tables = TableBuilder.Build(theme, roles, scene);
        foreach (Lut3D lut in (Lut3D[])[tables.Light, tables.Dark])
        {
            // The limit is applied in the working space, whose chroma differs from OKLab's by a few per cent.
            Entries(lut).Max(e => LumLab.FromLinear(Srgb.Decode(e)).Chroma).ShouldBeLessThanOrEqualTo(maxChroma * 1.06f);
        }
    }

    [Theory]
    [MemberData(nameof(RoleThemesAndScreens))]
    public void Lightness_never_reverses_along_the_grey_axis(string id, string fixture)
    {
        // The property that keeps anti-aliased text edges smooth, checked for every grey and both regions.
        Theme theme = BuiltInThemes.ById(id);
        (SceneAnalysis scene, RoleMap roles) = Pipeline.Analyse(Fixtures.Draw(fixture).Image);
        LensTables tables = TableBuilder.Build(theme, roles, scene);
        foreach (RegionPolarity region in (RegionPolarity[])[RegionPolarity.Light, RegionPolarity.Dark])
        {
            // Each region maps the greys on the text side of its page; start the walk at the page.
            float page = roles.Anchor(region, Role.Page)?.Cluster.Luminance ?? (region == RegionPolarity.Light ? 0.85f : 0.02f);
            var luminance = new List<float>();
            for (int v = 0; v < 256; v++)
            {
                float source = Wcag.Luminance(new Rgb8((byte)v, (byte)v, (byte)v));
                bool textSide = region == RegionPolarity.Light ? source <= page : source >= page;
                if (textSide)
                {
                    luminance.Add(Wcag.Luminance(tables.For(region).Map(new Rgb8((byte)v, (byte)v, (byte)v))));
                }
            }

            // Two display steps of slack: table entries beside the grey axis carry a little colour, and the
            // interpolation between them can wobble by that much where the curve is flat.
            Measurement.Measure.IsMonotonic(luminance, tolerance: 0.008f).ShouldBeTrue($"{region} region");
        }
    }

    [Theory]
    [MemberData(nameof(RoleThemeIds))]
    public void The_page_and_text_of_the_screen_are_shown_as_the_page_and_text_of_the_theme(string id)
    {
        Theme theme = BuiltInThemes.ById(id);
        ThemeRoles themed = theme.RequireRoles();
        foreach (string name in (string[])["light-document.png", "dark-editor.png"])
        {
            Fixture fixture = Fixtures.Draw(name);
            (SceneAnalysis scene, RoleMap roles) = Pipeline.Analyse(fixture.Image);
            LensTables tables = TableBuilder.Build(theme, roles, scene);
            RegionPolarity region = roles.Entries.Single(e => e.Role == Role.Page).Region;
            tables.Map(fixture.Page, region).MaxChannelDifference(themed.Page).ShouldBeLessThanOrEqualTo(2, $"page of {name}");
            tables.Map(fixture.Text, region).MaxChannelDifference(themed.Text).ShouldBeLessThanOrEqualTo(3, $"text of {name}");
            tables.Parameters.PageColour.ShouldBe(themed.Page);
        }
    }

    [Fact]
    public void A_region_nothing_is_known_about_is_still_shown_on_the_theme_page()
    {
        // No analysis at all: a white screen and a black screen both come out as the dark page.
        LensTables tables = TableBuilder.Build(BuiltInThemes.ById("soft-dark"));
        tables.Map(Rgb8.White, RegionPolarity.Light).ShouldBe(Rgb8.Black);
        tables.Map(Rgb8.Black, RegionPolarity.Dark).ShouldBe(Rgb8.Black);
        Wcag.Luminance(tables.Map(Rgb8.Black, RegionPolarity.Light)).ShouldBeGreaterThan(0.3f, "dark text on a light page becomes light text");
        Wcag.Luminance(tables.Map(Rgb8.White, RegionPolarity.Dark)).ShouldBeGreaterThan(0.3f, "light text on a dark page stays light");
    }

    [Fact]
    public void Source_contrast_carries_over_to_the_theme()
    {
        // Grey text with 4.5:1 on white should not collapse to something unreadable on black.
        Fixture fixture = Fixtures.Draw("light-document.png");
        (SceneAnalysis scene, RoleMap roles) = Pipeline.Analyse(fixture.Image);
        LensTables tables = TableBuilder.Build(BuiltInThemes.ById("soft-dark"), roles, scene);
        Rgb8 grey = Rgb8.FromHex("#767676");
        Wcag.Contrast(grey, Rgb8.White).ShouldBe(4.54f, 0.02f);
        Wcag.Contrast(tables.Map(grey, RegionPolarity.Light), Rgb8.Black).ShouldBeGreaterThan(4.0f);
    }

    [Fact]
    public void A_link_is_shown_in_the_theme_link_colour_and_nearby_blues_blend_towards_it()
    {
        var c = new Canvas(640, 400, Rgb8.White);
        Rgb8 link = Rgb8.FromHex("#1A73E8");
        for (int line = 0; line < 12; line++)
        {
            c.Text(20, 40 + (line * 26), "the quick brown fox jumps over the lazy dog", 9, 1.5f, line % 3 == 0 ? link : Rgb8.FromHex("#222222"));
        }

        (SceneAnalysis scene, RoleMap roles) = Pipeline.Analyse(c.Image);
        Theme theme = BuiltInThemes.ById("soft-dark");
        LensTables tables = TableBuilder.Build(theme, roles, scene);
        roles.Entries.Single(e => e.Cluster.Colour == link).Role.ShouldBe(Role.Link);
        tables.Map(link, RegionPolarity.Light).MaxChannelDifference(theme.RequireRoles().Link).ShouldBeLessThanOrEqualTo(3);

        // A slightly different blue lands near the link colour, not somewhere unrelated.
        Oklab near = Oklab.FromRgb8(tables.Map(Rgb8.FromHex("#2A7BEA"), RegionPolarity.Light));
        Oklab.Distance(near, Oklab.FromRgb8(theme.RequireRoles().Link)).ShouldBeLessThan(0.06f);
    }

    [Fact]
    public void A_pinned_colour_is_shown_as_its_pinned_role()
    {
        Fixture fixture = Fixtures.Draw("status-dashboard.png");
        Theme theme = BuiltInThemes.ById("soft-dark");
        var c = new Canvas(640, 400, Rgb8.White);
        Rgb8 purple = Rgb8.FromHex("#8E44AD");
        for (int line = 0; line < 10; line++)
        {
            c.Text(20, 40 + (line * 30), "errors are shown in purple here", 9, 1.6f, purple);
        }

        (SceneAnalysis scene, RoleMap automatic) = Pipeline.Analyse(c.Image);
        automatic.Entries.Single(e => e.Cluster.Colour == purple).Role.ShouldBe(Role.OtherAccent);

        RoleMap pinned = RoleAssigner.Assign(scene, [new Pin(purple, RegionPolarity.Light, Role.Bad)]);
        LensTables tables = TableBuilder.Build(theme, pinned, scene);
        tables.Map(purple, RegionPolarity.Light).MaxChannelDifference(theme.RequireRoles().Bad).ShouldBeLessThanOrEqualTo(3);
        fixture.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("soft-dark")]
    [InlineData("night-warm")]
    [InlineData("evening-paper")]
    public void At_its_own_warmth_a_theme_shows_exactly_the_colours_in_its_file(string id)
    {
        Theme theme = BuiltInThemes.ById(id);
        theme.Warmth.ShouldBeGreaterThan(0f);
        LensTables tables = TableBuilder.Build(theme);
        tables.Parameters.PageColour.ShouldBe(theme.RequireRoles().Page);
        tables.Parameters.TextColour.MaxChannelDifference(theme.RequireRoles().Text).ShouldBeLessThanOrEqualTo(1);
    }

    [Fact]
    public void More_warmth_means_less_blue_and_the_page_stays_the_hold_colour()
    {
        Theme paper = BuiltInThemes.ById("evening-paper");
        Rgb8 mild = TableBuilder.Build(paper with { Warmth = 0.3f }).Parameters.PageColour;
        Rgb8 strong = TableBuilder.Build(paper with { Warmth = 0.9f }).Parameters.PageColour;
        strong.B.ShouldBeLessThan(mild.B);
        strong.R.ShouldBe(mild.R);

        // What the fallback fill shows is what a page pixel is shown as.
        LensTables tables = TableBuilder.Build(paper with { Warmth = 0.9f });
        tables.Map(Rgb8.White, RegionPolarity.Light).MaxChannelDifference(strong).ShouldBeLessThanOrEqualTo(1);
    }

    [Fact]
    public void Greyscale_keeps_luminance_and_dimmer_scales_it()
    {
        LensTables grey = TableBuilder.Build(BuiltInThemes.ById("greyscale"));
        var red = new Rgb8(217, 48, 37);
        Rgb8 shown = grey.Map(red, RegionPolarity.Light);
        Oklab.FromRgb8(shown).Chroma.ShouldBeLessThan(0.01f);
        Wcag.Luminance(shown).ShouldBe(Wcag.Luminance(red), 0.01f);

        Theme dimmer = BuiltInThemes.ById("dimmer");
        LensTables dim = TableBuilder.Build(dimmer);
        Wcag.Luminance(dim.Map(Rgb8.White, RegionPolarity.Light)).ShouldBe(dimmer.Limits.MaxLuminance, 0.01f);
        Wcag.Luminance(dim.Map(new Rgb8(128, 128, 128), RegionPolarity.Light))
            .ShouldBe(Wcag.Luminance(new Rgb8(128, 128, 128)) * dimmer.Limits.MaxLuminance, 0.01f);
    }

    [Fact]
    public void Red_green_assist_moves_red_towards_yellow_and_green_towards_blue()
    {
        LensTables tables = TableBuilder.Build(BuiltInThemes.ById("red-green-assist"));
        Oklab red = Oklab.FromRgb8(tables.Map(Rgb8.FromHex("#D93025"), RegionPolarity.Light));
        Oklab green = Oklab.FromRgb8(tables.Map(Rgb8.FromHex("#2E9E44"), RegionPolarity.Light));
        red.HueDegrees.ShouldBeInRange(50f, 100f);
        green.HueDegrees.ShouldBeInRange(210f, 280f);
        red.L.ShouldBeGreaterThan(green.L + 0.1f);

        // Greys are left alone.
        tables.Map(new Rgb8(128, 128, 128), RegionPolarity.Light).MaxChannelDifference(new Rgb8(128, 128, 128)).ShouldBeLessThanOrEqualTo(1);
    }

    [Fact]
    public void Standard_contrast_pushes_quiet_text_and_panels_apart_but_leaves_page_and_text_alone()
    {
        Fixture fixture = Fixtures.Draw("light-document.png");
        (SceneAnalysis scene, RoleMap roles) = Pipeline.Analyse(fixture.Image);
        Theme theme = BuiltInThemes.ById("soft-dark");
        LensTables plain = TableBuilder.Build(theme, roles, scene);
        LensTables boosted = TableBuilder.Build(theme with { Contrast = new ContrastSettings(1f, ContrastMode.Standard) }, roles, scene);

        boosted.Map(fixture.Page, RegionPolarity.Light).ShouldBe(plain.Map(fixture.Page, RegionPolarity.Light));
        boosted.Map(fixture.Text, RegionPolarity.Light).MaxChannelDifference(plain.Map(fixture.Text, RegionPolarity.Light)).ShouldBeLessThanOrEqualTo(2);

        Rgb8 panel = Rgb8.FromHex("#F1F3F5");
        Wcag.Luminance(boosted.Map(panel, RegionPolarity.Light)).ShouldBeLessThanOrEqualTo(Wcag.Luminance(plain.Map(panel, RegionPolarity.Light)));
        Rgb8 quiet = Rgb8.FromHex("#555555");
        Wcag.Luminance(boosted.Map(quiet, RegionPolarity.Light)).ShouldBeGreaterThan(Wcag.Luminance(plain.Map(quiet, RegionPolarity.Light)));
    }

    [Fact]
    public void A_coloured_fill_keeps_its_hue_family_and_stays_under_the_area_limit()
    {
        Fixture fixture = Fixtures.Draw("web-buttons.png");
        (SceneAnalysis scene, RoleMap roles) = Pipeline.Analyse(fixture.Image);
        Theme theme = BuiltInThemes.ById("soft-dark");
        LensTables tables = TableBuilder.Build(theme, roles, scene);
        Rgb8 button = tables.Map(Rgb8.FromHex("#1A73E8"), RegionPolarity.Light);
        RoleAssigner.HueFamily(Oklab.FromRgb8(button).HueDegrees).ShouldBe(Role.Link);
        Wcag.Contrast(button, Rgb8.Black).ShouldBeLessThan(theme.Limits.MaxAreaContrast * 0.8f);
        Wcag.Contrast(button, Rgb8.Black).ShouldBeGreaterThan(2.5f, "the label on it, now in the page colour, has to stay readable");
    }

    [Fact]
    public void A_fade_moves_every_colour_in_a_straight_line_of_light()
    {
        LensTables from = TableBuilder.Build(BuiltInThemes.ById("soft-dark"));
        LensTables to = TableBuilder.Build(BuiltInThemes.ById("clear-white"));
        var fade = new TableFade(from, to);
        fade.At(0f).ShouldBeSameAs(from);
        fade.At(1f).ShouldBeSameAs(to);

        var colour = new Rgb8(240, 240, 240);
        float start = Wcag.Luminance(from.Map(colour, RegionPolarity.Light));
        float end = Wcag.Luminance(to.Map(colour, RegionPolarity.Light));
        float previous = start;
        for (int step = 1; step <= 10; step++)
        {
            float now = Wcag.Luminance(fade.At(step / 10f).Light.Map(colour));
            now.ShouldBeGreaterThanOrEqualTo(previous - 0.003f);
            now.ShouldBe(start + ((end - start) * step / 10f), 0.01f);
            previous = now;
        }
    }
}
