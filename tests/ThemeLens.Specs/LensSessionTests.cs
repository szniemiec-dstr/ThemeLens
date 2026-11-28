// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Core.Themes;
using ThemeLens.Engine;
using ThemeLens.Specs.Support;
using ThemeLens.Testing;

namespace ThemeLens.Specs;

/// <summary>
/// What the first-run window, control window and tray do, tested without any window: the session on a fake
/// clock, a fake screen and a temporary settings folder.
/// </summary>
public sealed class LensSessionTests : IDisposable
{
    private static readonly ScreenInfo Primary = new(1, new PixelRect(0, 0, 1920, 1080), IsPrimary: true);
    private static readonly ScreenInfo Second = new(2, new PixelRect(1920, 0, 2560, 1440), IsPrimary: false);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "themelens-session-" + Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _disposables = [];
    private List<ScreenInfo> _screens = [Primary];

    public void Dispose()
    {
        foreach (IDisposable d in _disposables)
        {
            d.Dispose();
        }

        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    /// <summary>Starts the app's logic as a fresh process would: settings and themes are read from the folder.</summary>
    private (LensSession Session, EngineHarness Engine) Start()
    {
        var engine = new EngineHarness(BuiltInThemes.DefaultFor(ThemeBase.Dark), Fixtures.Width, Fixtures.Height);
        engine.Source.Show(Fixtures.Draw("light-document.png").Image);
        var session = new LensSession(
            engine.Controller,
            new ThemeCatalogue(Path.Combine(_folder, "themes")),
            new SettingsStore(Path.Combine(_folder, "settings.json")),
            () => _screens);
        _disposables.Add(session);
        _disposables.Add(engine);
        return (session, engine);
    }

    [Fact]
    public void A_new_user_is_asked_first_and_nothing_changes_until_they_choose()
    {
        (LensSession session, EngineHarness engine) = Start();
        session.NeedsFirstRun.ShouldBeTrue();
        session.RestoreLens();
        engine.Controller.State.ShouldBe(LensState.Off);
        engine.Surface.Shown.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(ThemeBase.Dark, "Soft dark", 5)]
    [InlineData(ThemeBase.Light, "Clear white", 6)]
    [InlineData(ThemeBase.Keep, "Passthrough", 5)]
    public void First_run_offers_three_bases_and_then_the_themes_of_the_chosen_one(ThemeBase themeBase, string defaultName, int count)
    {
        (LensSession session, _) = Start();
        IReadOnlyList<Theme> themes = session.ThemesFor(themeBase);
        themes.Count.ShouldBe(count);
        themes[0].Name.ShouldBe(defaultName, "the default for the base comes first");
        themes.ShouldAllBe(t => t.Base == themeBase);
    }

    [Fact]
    public void Finishing_first_run_remembers_the_choice_and_the_next_start_goes_straight_to_the_lens()
    {
        (LensSession session, EngineHarness engine) = Start();
        session.CompleteFirstRun(ThemeBase.Light, "soft-paper", startWithWindows: true);
        session.TurnOn();
        engine.RunFor(TimeSpan.FromMilliseconds(300));
        engine.Controller.State.ShouldBe(LensState.Live);
        engine.Controller.Theme.Id.ShouldBe("soft-paper");
        session.Dispose();

        (LensSession next, EngineHarness nextEngine) = Start();
        next.NeedsFirstRun.ShouldBeFalse();
        next.SelectedTheme.Id.ShouldBe("soft-paper");
        next.Settings.Base.ShouldBe(ThemeBase.Light);
        next.Settings.StartWithWindows.ShouldBeTrue();

        // FR-18: the lens was on when the app closed, so it comes back on, through the page colour.
        next.RestoreLens();
        nextEngine.Controller.State.ShouldBe(LensState.HoldingBackground);
        nextEngine.Surface.Shown[0].Kind.ShouldBe(PresentedKind.Fill);
        nextEngine.Surface.Shown[0].Picture![0, 0].ShouldBe(BuiltInThemes.ById("soft-paper").RequireRoles().Page);
    }

    [Fact]
    public void A_theme_that_does_not_belong_to_the_chosen_base_is_replaced_by_that_bases_default()
    {
        (LensSession session, _) = Start();
        session.CompleteFirstRun(ThemeBase.Dark, "clear-white", startWithWindows: false);
        session.SelectedTheme.Id.ShouldBe("soft-dark");
    }

    [Fact]
    public void Sliders_change_the_lens_live_and_are_remembered_per_theme()
    {
        (LensSession session, EngineHarness engine) = Start();
        session.CompleteFirstRun(ThemeBase.Dark, "soft-dark", false);
        session.TurnOn();
        engine.RunFor(TimeSpan.FromMilliseconds(500));

        session.Adjust(a => a with { BrightnessLimit = 0.30f, Warmth = 0.6f, SharpenAmount = 0.5f });
        engine.RunFor(TimeSpan.FromMilliseconds(500));

        engine.Controller.Look.Parameters.MaxLuminance.ShouldBe(0.30f);
        engine.Controller.Look.Parameters.Sharpen.ShouldBe(0.5f);
        Core.Measurement.Measure.MaxLuminance(engine.Surface.Shown[^1].Picture!).ShouldBeLessThanOrEqualTo(0.305f);

        // Another theme has its own values; coming back brings these ones back.
        session.SelectTheme("night-warm");
        session.Adjustments.IsEmpty.ShouldBeTrue();
        session.EffectiveTheme.Limits.MaxLuminance.ShouldBe(BuiltInThemes.ById("night-warm").Limits.MaxLuminance);
        session.SelectTheme("soft-dark");
        session.EffectiveTheme.Limits.MaxLuminance.ShouldBe(0.30f);
        session.EffectiveTheme.Warmth.ShouldBe(0.6f);

        session.ResetAdjustments();
        session.EffectiveTheme.ShouldBe(BuiltInThemes.ById("soft-dark"));
    }

    [Fact]
    public void Rectangles_only_works_under_any_theme_and_has_values_for_bright_and_for_dark_rectangles()
    {
        (LensSession session, EngineHarness engine) = Start();
        session.CompleteFirstRun(ThemeBase.Dark, "soft-dark", false);
        session.Rectangles.ShouldBe(new RectanglePreferences(), "the option starts switched off, looking for bright rectangles, inverting plainly");
        session.TurnOn();
        engine.RunFor(TimeSpan.FromMilliseconds(500));
        engine.Controller.Look.Parameters.SelectsPixels.ShouldBeFalse();

        session.SetRectangles(r => r with { On = true });
        session.SetRectangleValues(v => v with { SmallestSide = 120f, FullSide = 400f, Brightness = 0.80f });
        engine.RunFor(TimeSpan.FromMilliseconds(500));
        engine.Controller.Look.Parameters.RectanglesOnly.ShouldBe(1f);
        engine.Controller.Look.Parameters.RectangleBaseline.ShouldBe(0f, "plain inversion, whatever the theme");
        engine.Controller.Theme.Rectangles.Values.Brightness.ShouldBe(0.80f);

        // The option does not belong to a theme: it stays as it is under every one of them.
        session.SelectTheme("clear-white");
        session.RectangleOption.Target.ShouldBe(RectangleTarget.Bright);
        session.RectangleOption.Values.FullSide.ShouldBe(400f);
        session.EffectiveTheme.RectanglesInTheme.ShouldBeFalse();
        session.SelectTheme("passthrough");
        session.EffectiveTheme.RectanglesOnly.ShouldBeTrue();
        session.EffectiveTheme.ChangesNothing.ShouldBeFalse("even a theme that changes nothing shows inverted rectangles");

        // Dark rectangles have values of their own.
        session.SetRectangles(r => r with { Target = RectangleTarget.Dark });
        session.RectangleOption.Values.ShouldBe(RectangleSettings.DefaultFor(RectangleTarget.Dark));
        session.SetRectangleValues(v => v with { Brightness = 0.9f });
        session.RectangleOption.Values.Brightness.ShouldBe(RectangleSettings.HighestDarkThreshold, "for dark rectangles the threshold is a darkness");
        session.Dispose();

        (LensSession next, EngineHarness nextEngine) = Start();
        next.Rectangles.On.ShouldBeTrue();
        next.Rectangles.Target.ShouldBe(RectangleTarget.Dark);
        next.Rectangles.Bright.ShouldBe(new RectangleSettings { SmallestSide = 120f, FullSide = 400f, Brightness = 0.80f });
        nextEngine.Controller.Theme.RectanglesOnly.ShouldBeTrue();
    }

    [Fact]
    public void The_two_preview_settings_can_be_switched_off_one_by_one()
    {
        (LensSession session, _) = Start();
        session.CompleteFirstRun(ThemeBase.Dark, "soft-dark", false);
        session.SetRectangles(r => r with { On = true, Target = RectangleTarget.Dark });
        session.Rectangles.OwnTarget.ShouldBeTrue();
        session.Rectangles.PlainInversion.ShouldBeTrue();

        // Without the first, the theme decides what is looked for.
        session.SetRectangles(r => r with { OwnTarget = false });
        session.RectangleOption.Target.ShouldBe(RectangleTarget.Bright, "a dark theme looks for bright rectangles");
        session.EffectiveTheme.RectanglesInTheme.ShouldBeFalse("the rectangle is still plainly inverted");
        session.SelectTheme("clear-white");
        session.RectangleOption.Target.ShouldBe(RectangleTarget.Dark, "a light theme looks for dark ones");
        session.SelectTheme("less-blue");
        session.RectangleOption.Target.ShouldBe(RectangleTarget.Dark, "a theme that leaves light and dark alone has no say: the user's choice stands");

        // Without the second, a found rectangle is shown in the theme's colours where the theme has them.
        session.SetRectangles(r => r with { PlainInversion = false });
        session.EffectiveTheme.RectanglesInTheme.ShouldBeFalse("Less blue has no colours of its own");
        session.SelectTheme("soft-dark");
        session.EffectiveTheme.RectanglesInTheme.ShouldBeTrue();

        // The first on again, the second still off: bright or dark is the user's choice, the colours the theme's.
        session.SetRectangles(r => r with { OwnTarget = true });
        session.RectangleOption.Target.ShouldBe(RectangleTarget.Dark);
        session.EffectiveTheme.RectanglesInTheme.ShouldBeFalse("a dark theme cannot turn a dark rectangle round, so it is inverted");
    }

    [Fact]
    public void A_saved_theme_does_not_carry_the_rectangles_option()
    {
        (LensSession session, _) = Start();
        session.CompleteFirstRun(ThemeBase.Dark, "soft-dark", false);
        session.SetRectangles(r => r with { On = true });

        Theme saved = session.SaveAsTheme("Mine");

        saved.Rectangles.ShouldBe(RectangleOption.Off, "the option is the user's setting, not part of a theme file");
        session.EffectiveTheme.RectanglesOnly.ShouldBeTrue("the option stays on: it does not depend on the theme");
    }

    [Fact]
    public void Adjustments_survive_a_restart()
    {
        (LensSession session, _) = Start();
        session.CompleteFirstRun(ThemeBase.Dark, "soft-dark", false);
        session.Adjust(a => a with { KeepBackground = KeepBackgroundMode.Balanced, ContrastAmount = 0.4f, ContrastMode = ContrastMode.Local });
        session.Dispose();

        (LensSession next, EngineHarness engine) = Start();
        next.EffectiveTheme.KeepBackground.ShouldBe(KeepBackgroundMode.Balanced);
        next.EffectiveTheme.Contrast.ShouldBe(new ContrastSettings(0.4f, ContrastMode.Local));
        engine.Controller.Theme.KeepBackground.ShouldBe(KeepBackgroundMode.Balanced);
    }

    [Fact]
    public void The_current_settings_can_be_saved_as_a_theme_that_is_then_selected()
    {
        (LensSession session, _) = Start();
        session.CompleteFirstRun(ThemeBase.Dark, "soft-dark", false);
        session.Adjust(a => a with { BrightnessLimit = 0.4f });

        Theme saved = session.SaveAsTheme("Reading at night");

        saved.Id.ShouldBe("reading-at-night");
        session.SelectedTheme.Id.ShouldBe("reading-at-night");
        session.Adjustments.IsEmpty.ShouldBeTrue("the saved theme carries the values itself");
        session.EffectiveTheme.Limits.MaxLuminance.ShouldBe(0.4f);
        session.ThemesFor(ThemeBase.Dark).Select(t => t.Id).ShouldContain("reading-at-night");
        session.Dispose();

        (LensSession next, _) = Start();
        next.SelectedTheme.Id.ShouldBe("reading-at-night");
    }

    [Fact]
    public void A_deleted_user_theme_falls_back_to_the_default_of_its_base()
    {
        (LensSession session, _) = Start();
        session.CompleteFirstRun(ThemeBase.Light, "clear-white", false);
        Theme saved = session.SaveAsTheme("Mine");
        session.Dispose();
        File.Delete(Path.Combine(_folder, "themes", saved.Id + ThemeJson.FileExtension));

        (LensSession next, _) = Start();
        next.SelectedTheme.Id.ShouldBe("clear-white");
    }

    [Fact]
    public void Changing_a_rows_role_pins_it_until_the_mapping_is_reset()
    {
        (LensSession session, EngineHarness engine) = Start();
        session.CompleteFirstRun(ThemeBase.Dark, "soft-dark", false);
        session.TurnOn();
        engine.RunFor(TimeSpan.FromSeconds(1));
        MappingRow red = session.Rows.Single(r => r.Role == Role.Bad);

        session.PinRow(red, Role.Warning);
        engine.RunFor(TimeSpan.FromSeconds(1));

        MappingRow pinned = session.Rows.Single(r => r.Found == red.Found);
        pinned.Role.ShouldBe(Role.Warning);
        pinned.Pinned.ShouldBeTrue();
        pinned.Shown.ShouldNotBe(red.Shown);

        session.ResetMapping();
        engine.RunFor(TimeSpan.FromSeconds(1));
        session.Rows.Single(r => r.Found == red.Found).Role.ShouldBe(Role.Bad);
        session.Rows.ShouldAllBe(r => !r.Pinned);
    }

    [Fact]
    public void The_mapping_table_keeps_up_with_the_screen()
    {
        (LensSession session, EngineHarness engine) = Start();
        session.CompleteFirstRun(ThemeBase.Dark, "soft-dark", false);
        int changes = 0;
        session.Changed += () => changes++;
        session.TurnOn();
        engine.RunFor(TimeSpan.FromSeconds(1));

        session.Rows.Count.ShouldBeInRange(4, 8);
        session.Rows.Single(r => r.Role == Role.Page).FoundHex.ShouldBe("#FFFFFF");
        changes.ShouldBeGreaterThan(0);

        engine.Source.Show(Fixtures.Draw("dark-editor.png").Image);
        engine.RunFor(TimeSpan.FromSeconds(1));
        session.Rows.Single(r => r.Role == Role.Page).FoundHex.ShouldBe("#1E1E1E");
    }

    [Fact]
    public void Each_shortcut_does_what_section_4_7_says()
    {
        (LensSession session, EngineHarness engine) = Start();
        session.CompleteFirstRun(ThemeBase.Dark, "soft-dark", false);
        bool controlWindowAsked = false;
        session.ControlWindowRequested += () => controlWindowAsked = true;

        session.Handle(ShortcutAction.ToggleLens);
        engine.RunFor(TimeSpan.FromMilliseconds(300));
        engine.Controller.State.ShouldBe(LensState.Live);

        session.Handle(ShortcutAction.EditMode);
        engine.Controller.State.ShouldBe(LensState.Edit);
        session.Handle(ShortcutAction.EditMode);
        engine.Controller.State.ShouldBe(LensState.Live);

        session.Handle(ShortcutAction.FillScreen);
        session.Placement.FillsScreen.ShouldBeTrue();
        engine.Controller.Area.ShouldBe(Primary.Bounds);
        session.Handle(ShortcutAction.FillScreen);
        session.Placement.FillsScreen.ShouldBeFalse();

        session.Handle(ShortcutAction.NextTheme);
        session.SelectedTheme.Id.ShouldBe("high-contrast-dark");

        session.Handle(ShortcutAction.OpenControlWindow);
        controlWindowAsked.ShouldBeTrue();

        session.Handle(ShortcutAction.EditMode);
        session.Handle(ShortcutAction.EverythingOff);
        engine.Controller.State.ShouldBe(LensState.Off);
        engine.Surface.Visible.ShouldBeFalse();
        session.Settings.LensWasOn.ShouldBeFalse();
    }

    [Fact]
    public void Next_theme_stays_within_the_base_and_wraps_round()
    {
        (LensSession session, _) = Start();
        session.CompleteFirstRun(ThemeBase.Dark, "wake-up-dark", false);
        session.NextTheme();
        session.SelectedTheme.Id.ShouldBe("soft-dark");
        session.SelectedTheme.Base.ShouldBe(ThemeBase.Dark);
    }

    [Fact]
    public void The_lens_remembers_its_rectangle_and_mode_per_monitor_layout()
    {
        (LensSession session, _) = Start();
        session.CompleteFirstRun(ThemeBase.Dark, "soft-dark", false);
        session.Placement.Window.ShouldBe(LensGeometry.DefaultFor(Primary.Bounds));

        var onOneScreen = new LensPlacement(new PixelRect(100, 100, 700, 500), FillsScreen: false);
        session.SetPlacement(onOneScreen);

        // A second monitor is plugged in: a layout the lens has not seen keeps the lens where it is.
        _screens = [Primary, Second];
        session.DisplaysChanged();
        session.Placement.ShouldBe(onOneScreen);

        session.FillScreen(Second);
        session.Placement.FillsScreen.ShouldBeTrue();
        session.Controller.Area.ShouldBe(Second.Bounds);
        session.Dispose();

        // Next start with both monitors: fills the second screen again.
        (LensSession both, _) = Start();
        both.Placement.FillsScreen.ShouldBeTrue();
        both.Controller.Area.ShouldBe(Second.Bounds);
        both.Dispose();

        // Next start with the laptop alone: back to the rectangle used on that layout.
        _screens = [Primary];
        (LensSession alone, _) = Start();
        alone.Placement.ShouldBe(onOneScreen);
    }

    [Fact]
    public void What_the_host_did_by_itself_is_remembered_without_being_done_again()
    {
        // The Windows runtime moves the lens when its handle is dragged, and switches it on and off on a
        // shortcut, without asking the session first. The session is told afterwards.
        (LensSession session, EngineHarness engine) = Start();
        session.CompleteFirstRun(ThemeBase.Dark, "soft-dark", false);
        PixelRect areaBefore = session.Controller.Area;
        int moves = 0;
        session.PlacementChanged += _ => moves++;

        var dragged = new LensPlacement(new PixelRect(300, 200, 640, 480), FillsScreen: false);
        session.NotePlacement(dragged);
        session.NoteLensSwitched(on: true);

        session.Placement.ShouldBe(dragged);
        moves.ShouldBe(0, "the lens is where the host put it already; it must not be moved again");
        session.Controller.Area.ShouldBe(areaBefore, "the host owns the lens area while it moves the lens itself");
        engine.Controller.State.ShouldBe(LensState.Off, "noting that the lens is on does not switch it on");
        session.Dispose();

        // The next start finds the lens where it was left, and knows it was on.
        (LensSession next, _) = Start();
        next.Placement.ShouldBe(dragged);
        next.Settings.LensWasOn.ShouldBeTrue();
    }

    [Fact]
    public void A_display_change_while_the_lens_is_on_goes_through_the_page_colour()
    {
        (LensSession session, EngineHarness engine) = Start();
        session.CompleteFirstRun(ThemeBase.Dark, "soft-dark", false);
        session.TurnOn();
        engine.RunFor(TimeSpan.FromMilliseconds(500));
        int before = engine.Surface.Shown.Count;

        _screens = [Primary, Second];
        session.DisplaysChanged();

        engine.Controller.State.ShouldBe(LensState.HoldingBackground);
        engine.Surface.Shown[before].Kind.ShouldBe(PresentedKind.Fill);
    }

    [Fact]
    public void Shortcuts_can_be_changed_removed_and_put_back()
    {
        (LensSession session, _) = Start();
        Shortcut.TryParse("Ctrl+Shift+F9", out Shortcut f9).ShouldBeTrue();

        session.SetShortcut(ShortcutAction.ToggleLens, f9);
        session.SetShortcut(ShortcutAction.NextTheme, null);
        session.Shortcuts[ShortcutAction.ToggleLens].ShouldBe(f9);
        session.Shortcuts[ShortcutAction.NextTheme].ShouldBeNull();
        session.Dispose();

        (LensSession next, _) = Start();
        next.Shortcuts[ShortcutAction.ToggleLens].ShouldBe(f9);
        next.Shortcuts[ShortcutAction.NextTheme].ShouldBeNull();
        next.Shortcuts[ShortcutAction.EverythingOff].ShouldBe(Shortcuts.Defaults[ShortcutAction.EverythingOff]);

        next.SetShortcut(ShortcutAction.ToggleLens, Shortcuts.Defaults[ShortcutAction.ToggleLens]);
        next.Settings.Shortcuts.ContainsKey("ToggleLens").ShouldBeFalse("a default is not stored");
    }

    [Fact]
    public void The_area_list_offers_the_window_and_every_screen_by_number_and_position()
    {
        _screens = [Primary, Second];
        (LensSession session, _) = Start();

        IReadOnlyList<AreaChoice> choices = session.AreaChoices;
        choices.Count.ShouldBe(3);
        choices[0].IsWindow.ShouldBeTrue();
        (choices[1].Screen!.Number, choices[1].Position).ShouldBe((1, ScreenPosition.Primary));
        (choices[2].Screen!.Number, choices[2].Position).ShouldBe((2, ScreenPosition.Right));
        session.CurrentArea.IsWindow.ShouldBeTrue();

        session.SelectArea(choices[2]);
        session.CurrentArea.ShouldBe(choices[2]);
        session.Controller.Area.ShouldBe(Second.Bounds);

        PixelRect before = session.Placement.Window;
        session.SelectArea(choices[0]);
        session.CurrentArea.IsWindow.ShouldBeTrue();
        session.Placement.Window.ShouldBe(before, "going back to Window restores the rectangle");
        session.Controller.Area.ShouldBe(before);
    }

    [Fact]
    public void One_step_returns_the_heavier_options_to_standard_modes_without_switching_them_off()
    {
        (LensSession session, _) = Start();
        session.UsesHeavierModes.ShouldBeFalse();
        session.Adjust(a => a with { SharpenAmount = 0.8f, SharpenMode = SharpenMode.Strong, ContrastAmount = 0.5f, ContrastMode = ContrastMode.Local });
        session.UsesHeavierModes.ShouldBeTrue();

        session.ReturnToStandardModes();

        session.UsesHeavierModes.ShouldBeFalse();
        session.EffectiveTheme.Sharpen.ShouldBe(new SharpenSettings(0.8f, SharpenMode.Standard));
        session.EffectiveTheme.Contrast.ShouldBe(new ContrastSettings(0.5f, ContrastMode.Standard));
    }

    [Fact]
    public void Settings_are_written_on_flush_not_on_every_change()
    {
        (LensSession session, _) = Start();
        string file = Path.Combine(_folder, "settings.json");
        session.SetCheckForUpdates(false);
        session.SetLanguage("pl-PL");
        File.Exists(file).ShouldBeFalse();

        session.Flush();

        File.Exists(file).ShouldBeTrue();
        AppSettings saved = new SettingsStore(file).Load();
        saved.CheckForUpdates.ShouldBeFalse();
        saved.Language.ShouldBe("pl-PL");
    }
}
