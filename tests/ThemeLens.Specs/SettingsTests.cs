// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Core.Themes;
using ThemeLens.Engine;

namespace ThemeLens.Specs;

public class SettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "themelens-settings-" + Guid.NewGuid().ToString("N"));

    private string File => Path.Combine(_folder, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Without_a_file_the_app_starts_with_first_run_and_the_dark_default()
    {
        AppSettings settings = new SettingsStore(File).Load();
        settings.FirstRunDone.ShouldBeFalse();
        settings.Base.ShouldBe(ThemeBase.Dark);
        settings.ThemeId.ShouldBe("soft-dark");
        settings.CheckForUpdates.ShouldBeTrue();
        settings.LensWasOn.ShouldBeFalse();
    }

    [Fact]
    public void Settings_survive_a_restart()
    {
        var store = new SettingsStore(File);
        var layout = "1920x1080@0,0";
        AppSettings saved = new AppSettings
        {
            FirstRunDone = true,
            Base = ThemeBase.Light,
            ThemeId = "soft-paper",
            LensWasOn = true,
            StartWithWindows = true,
            CheckForUpdates = false,
            Language = "pl-PL",
        }
        .WithAdjustments("soft-paper", new ThemeAdjustments { Warmth = 0.4f, SharpenAmount = 0.5f, SharpenMode = SharpenMode.Strong })
        .WithPlacement(layout, new LensPlacement(new PixelRect(100, 80, 800, 600), FillsScreen: true));

        store.Save(saved);
        AppSettings loaded = new SettingsStore(File).Load();

        loaded.ThemeId.ShouldBe("soft-paper");
        loaded.Base.ShouldBe(ThemeBase.Light);
        loaded.LensWasOn.ShouldBeTrue();
        loaded.StartWithWindows.ShouldBeTrue();
        loaded.CheckForUpdates.ShouldBeFalse();
        loaded.Language.ShouldBe("pl-PL");
        loaded.AdjustmentsFor("soft-paper").ShouldBe(saved.AdjustmentsFor("soft-paper"));
        loaded.PlacementFor(layout).ShouldBe(new LensPlacement(new PixelRect(100, 80, 800, 600), true));
        loaded.PlacementFor("another layout").ShouldBeNull();
    }

    [Fact]
    public void A_damaged_file_gives_defaults_and_is_kept_aside()
    {
        Directory.CreateDirectory(_folder);
        System.IO.File.WriteAllText(File, "{ this is not json");
        var store = new SettingsStore(File);

        AppSettings settings = store.Load();

        settings.ThemeId.ShouldBe("soft-dark");
        settings.FirstRunDone.ShouldBeFalse();
        settings.Adjustments.ShouldBeEmpty();
        store.LastLoadWasDamaged.ShouldBeTrue();
        System.IO.File.Exists(File + ".damaged").ShouldBeTrue();
    }

    [Fact]
    public void A_file_from_a_newer_version_is_not_misread()
    {
        Directory.CreateDirectory(_folder);
        System.IO.File.WriteAllText(File, """{ "version": 99, "themeId": "from-the-future" }""");
        new SettingsStore(File).Load().ThemeId.ShouldBe("soft-dark");
    }

    [Fact]
    public void Adjustments_are_laid_over_the_theme_and_remembered_per_theme()
    {
        Theme soft = BuiltInThemes.ById("soft-dark");
        var adjustments = new ThemeAdjustments
        {
            BrightnessLimit = 0.3f,
            Warmth = 0.6f,
            ContrastAmount = 0.5f,
            ContrastMode = ContrastMode.Local,
            KeepBackground = KeepBackgroundMode.Balanced,
        };

        Theme adjusted = adjustments.ApplyTo(soft);

        adjusted.Limits.MaxLuminance.ShouldBe(0.3f);
        adjusted.Limits.MaxChroma.ShouldBe(soft.Limits.MaxChroma);
        adjusted.Warmth.ShouldBe(0.6f);
        adjusted.AuthoredWarmth.ShouldBe(soft.AuthoredWarmth);
        adjusted.Contrast.ShouldBe(new ContrastSettings(0.5f, ContrastMode.Local));
        adjusted.Sharpen.ShouldBe(soft.Sharpen);
        adjusted.KeepBackground.ShouldBe(KeepBackgroundMode.Balanced);
        adjusted.Roles.ShouldBe(soft.Roles);

        new ThemeAdjustments().ApplyTo(soft).ShouldBe(soft);
        new ThemeAdjustments().IsEmpty.ShouldBeTrue();

        AppSettings settings = new AppSettings().WithAdjustments("soft-dark", adjustments);
        settings.AdjustmentsFor("soft-dark").ShouldBe(adjustments);
        settings.AdjustmentsFor("night-warm").IsEmpty.ShouldBeTrue();
        settings.WithAdjustments("soft-dark", new ThemeAdjustments()).Adjustments.ShouldBeEmpty();
    }

    [Fact]
    public void Keep_background_does_not_apply_to_themes_that_keep_the_apps_colours()
    {
        Theme passthrough = BuiltInThemes.ById("passthrough");
        new ThemeAdjustments { KeepBackground = KeepBackgroundMode.Strict }.ApplyTo(passthrough).KeepBackground.ShouldBe(passthrough.KeepBackground);
    }

    [Fact]
    public void Out_of_range_values_in_a_hand_edited_file_are_clamped()
    {
        Theme adjusted = new ThemeAdjustments { BrightnessLimit = 7f, Warmth = -3f, SharpenAmount = 2f }.ApplyTo(BuiltInThemes.ById("soft-dark"));
        adjusted.Limits.MaxLuminance.ShouldBe(1f);
        adjusted.Warmth.ShouldBe(0f);
        adjusted.Sharpen.Amount.ShouldBe(1f);
    }
}
