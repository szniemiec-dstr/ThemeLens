// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Core.Analysis;
using ThemeLens.Core.Colour;
using ThemeLens.Core.Themes;
using ThemeLens.Engine;
using ThemeLens.Testing;

namespace ThemeLens.Specs;

/// <summary>What the control window shows (figure 5) and the shortcuts (section 4.7), without any window.</summary>
public class ControlWindowModelTests
{
    private static LensMapping MappingOf(string themeId, string screen, IReadOnlyList<Pin>? pins = null)
    {
        LensResult result = Pipeline.Run(BuiltInThemes.ById(themeId), Fixtures.Draw(screen).Image, pins);
        return new LensMapping(result.Scene, result.Roles, result.Tables);
    }

    [Fact]
    public void The_mapping_table_lists_every_cluster_with_name_hex_share_role_and_shown_colour()
    {
        IReadOnlyList<MappingRow> rows = MappingRows.From(MappingOf("soft-dark", "light-document.png"));

        rows.Count.ShouldBeInRange(4, 8);
        rows.Select(r => r.Share).ShouldBeInOrder(SortDirection.Descending);

        MappingRow page = rows.Single(r => r.Role == Role.Page);
        page.FoundHex.ShouldBe("#FFFFFF");
        page.FoundName.ToEnglish().ShouldBe("White");
        page.ShownHex.ShouldBe("#000000");
        page.ShownName.ToEnglish().ShouldBe("Black");
        page.SharePercent.ShouldBeInRange(60, 75);
        page.ContrastAgainstPage.ShouldBeNull();

        MappingRow text = rows.Single(r => r.Role == Role.Text);
        text.FoundName.ToEnglish().ShouldBe("Near black");
        text.ShownName.ToEnglish().ShouldBe("Off-white");
        text.ContrastAgainstPage!.Value.ShouldBe(12.1f, 0.2f);

        MappingRow panel = rows.First(r => r.Role == Role.Panel);
        panel.FoundName.ToEnglish().ShouldBe("Light grey");
        panel.ShownName.ToEnglish().ShouldBe("Charcoal");

        rows.ShouldAllBe(r => !r.Pinned);
        rows.ShouldAllBe(r => r.SharePercent >= 1);
    }

    [Fact]
    public void Changing_a_rows_role_pins_it_and_reset_unpins_everything()
    {
        IReadOnlyList<MappingRow> rows = MappingRows.From(MappingOf("soft-dark", "light-document.png"));
        MappingRow red = rows.Single(r => r.Role == Role.Bad);

        IReadOnlyList<Pin> pins = MappingRows.Pin([], red, Role.Warning);
        IReadOnlyList<MappingRow> pinnedRows = MappingRows.From(MappingOf("soft-dark", "light-document.png", pins));

        MappingRow pinned = pinnedRows.Single(r => r.Found == red.Found);
        pinned.Role.ShouldBe(Role.Warning);
        pinned.Pinned.ShouldBeTrue();
        pinnedRows.Count(r => r.Pinned).ShouldBe(1);

        // Choosing again replaces the pin instead of adding a second one for the same colour.
        MappingRows.Pin(pins, pinned, Role.Good).Count.ShouldBe(1);
        MappingRows.Pin(pins, pinned, Role.Good)[0].Role.ShouldBe(Role.Good);
        MappingRows.Unpin(pins, pinned).ShouldBeEmpty();
    }

    [Fact]
    public void A_theme_that_keeps_the_apps_colours_shows_no_contrast_figures()
    {
        IReadOnlyList<MappingRow> rows = MappingRows.From(MappingOf("less-blue", "light-document.png"));
        rows.ShouldAllBe(r => r.ContrastAgainstPage == null);
        rows.Single(r => r.Role == Role.Page).Shown.B.ShouldBeLessThan((byte)230);
    }

    [Fact]
    public void The_theme_picker_preview_shows_each_theme_on_its_own_page_colour()
    {
        foreach (Theme theme in BuiltInThemes.All.Where(t => t.HasRoles))
        {
            Core.Imaging.ImageRgb8 preview = ThemePreview.Render(theme);
            (preview.Width, preview.Height).ShouldBe((ThemePreview.DefaultWidth, ThemePreview.DefaultHeight));

            // The sample's white page becomes the theme's page, and its text stays clearly readable.
            Rgb8 page = theme.RequireRoles().Page;
            preview[preview.Width - 2, preview.Height / 5].MaxChannelDifference(page).ShouldBeLessThanOrEqualTo(3, theme.Id);
            float text = Core.Measurement.Measure.MedianLuminanceWhere(preview, ThemePreview.Sample(), Rgb8.FromHex("#222222"))!.Value;
            Wcag.Contrast(text, Wcag.Luminance(page)).ShouldBeGreaterThan(7f, theme.Id);
        }
    }

    [Fact]
    public void The_preview_of_passthrough_is_the_sample_itself()
    {
        ThemePreview.Render(BuiltInThemes.ById("passthrough")).ContentEquals(ThemePreview.Sample()).ShouldBeTrue();
    }

    [Fact]
    public void The_default_shortcuts_are_the_ones_proposed_and_none_uses_ctrl_alt()
    {
        Shortcuts.Defaults[ShortcutAction.ToggleLens].ToString().ShouldBe("Win+Alt+L");
        Shortcuts.Defaults[ShortcutAction.EditMode].ToString().ShouldBe("Win+Alt+E");
        Shortcuts.Defaults[ShortcutAction.FillScreen].ToString().ShouldBe("Win+Alt+F");
        Shortcuts.Defaults[ShortcutAction.NextTheme].ToString().ShouldBe("Win+Alt+T");
        Shortcuts.Defaults[ShortcutAction.OpenControlWindow].ToString().ShouldBe("Win+Alt+C");
        Shortcuts.Defaults[ShortcutAction.EverythingOff].ToString().ShouldBe("Win+Alt+X");

        Shortcuts.Defaults.Count.ShouldBe(Shortcuts.Actions.Count);
        Shortcuts.Defaults.Values.ShouldAllBe(s => !s.UsesCtrlAlt && s.IsUsable);
        Shortcuts.Defaults.Values.Distinct().Count().ShouldBe(Shortcuts.Defaults.Count);
    }

    [Theory]
    [InlineData("Win+Alt+L", "Win+Alt+L")]
    [InlineData("alt + win + l", "Win+Alt+L")]
    [InlineData("Ctrl+Shift+F9", "Ctrl+Shift+F9")]
    [InlineData("Control+Windows+7", "Win+Ctrl+7")]
    public void Shortcuts_are_read_and_written_as_text(string text, string expected)
    {
        Shortcut.TryParse(text, out Shortcut shortcut).ShouldBeTrue();
        shortcut.ToString().ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Win+Alt")]
    [InlineData("Win+Alt+L+K")]
    [InlineData("Win+Alt+F25")]
    [InlineData("Win+Alt+Enter")]
    public void Text_that_is_not_a_shortcut_is_refused(string text) => Shortcut.TryParse(text, out _).ShouldBeFalse();

    [Fact]
    public void Ctrl_alt_is_recognised_because_it_is_altgr_on_many_keyboards()
    {
        Shortcut.TryParse("Ctrl+Alt+L", out Shortcut shortcut).ShouldBeTrue();
        shortcut.UsesCtrlAlt.ShouldBeTrue();
        new Shortcut(ShortcutModifiers.Shift, "L").IsUsable.ShouldBeFalse("it would swallow a capital L");
    }

    [Fact]
    public void A_users_changes_are_laid_over_the_defaults()
    {
        var choices = new Dictionary<string, string>
        {
            ["ToggleLens"] = "Ctrl+Shift+F9",
            ["NextTheme"] = "",
            ["FillScreen"] = "not a shortcut",
        };

        IReadOnlyDictionary<ShortcutAction, Shortcut?> resolved = Shortcuts.Resolve(choices);

        resolved[ShortcutAction.ToggleLens]!.Value.ToString().ShouldBe("Ctrl+Shift+F9");
        resolved[ShortcutAction.NextTheme].ShouldBeNull("the user removed it");
        resolved[ShortcutAction.FillScreen].ShouldBe(Shortcuts.Defaults[ShortcutAction.FillScreen]);
        resolved[ShortcutAction.EverythingOff].ShouldBe(Shortcuts.Defaults[ShortcutAction.EverythingOff]);
    }
}
