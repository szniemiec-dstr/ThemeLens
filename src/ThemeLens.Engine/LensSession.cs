// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Core.Analysis;
using ThemeLens.Core.Themes;

namespace ThemeLens.Engine;

/// <summary>
/// What the user can do with the lens, independent of any window: choose a base and a theme, adjust it live,
/// correct the mapping, save the result as a theme, move the lens, use the shortcuts. It keeps the settings
/// file up to date and restores the last state on start (FR-06, FR-09, FR-14 to FR-18).
/// </summary>
/// <remarks>
/// The first-run window, the control window and the tray icon are thin views over this class, so everything
/// they do can be tested without a desktop. Call it from the UI thread; <see cref="Changed"/> may be raised
/// from the frame loop's thread when the mapping or status changes.
/// </remarks>
public sealed class LensSession : IDisposable
{
    private readonly LensController _controller;
    private readonly ThemeCatalogue _catalogue;
    private readonly SettingsStore _store;
    private readonly Func<IReadOnlyList<ScreenInfo>> _screens;
    private readonly Lock _gate = new();
    private IReadOnlyList<Pin> _pins = [];
    private IReadOnlyList<MappingRow> _rows = [];
    private LensMapping? _mapping;
    private bool _dirty;

    public LensSession(LensController controller, ThemeCatalogue catalogue, SettingsStore store, Func<IReadOnlyList<ScreenInfo>> screens)
    {
        _controller = controller;
        _catalogue = catalogue;
        _store = store;
        _screens = screens;
        Settings = store.Load();

        // A theme id that no longer exists (a user theme was deleted) falls back to the default of its base.
        SelectedTheme = catalogue.Find(Settings.ThemeId) ?? BuiltInThemes.DefaultFor(Settings.Base);
        Placement = Settings.PlacementFor(LensGeometry.LayoutKey(screens())) ?? DefaultPlacement();

        _controller.SetTheme(EffectiveTheme);
        _controller.SetArea(Placement.Current(screens()));
        _controller.MappingChanged += OnMappingChanged;
        _controller.StatusChanged += OnStatusChanged;
    }

    /// <summary>Raised after anything a view shows has changed.</summary>
    public event Action? Changed;

    /// <summary>Raised when the lens should be moved or resized on screen.</summary>
    public event Action<LensPlacement>? PlacementChanged;

    /// <summary>Raised when the user asks for the control window (shortcut or tray).</summary>
    public event Action? ControlWindowRequested;

    public AppSettings Settings { get; private set; }

    public LensController Controller => _controller;

    public ThemeCatalogue Catalogue => _catalogue;

    public LensStatus Status => _controller.Status;

    /// <summary>The theme chosen from the catalogue, without the user's adjustments.</summary>
    public Theme SelectedTheme { get; private set; }

    /// <summary>The user's values for the selected theme.</summary>
    public ThemeAdjustments Adjustments => Settings.AdjustmentsFor(SelectedTheme.Id);

    /// <summary>The selected theme with the user's values laid over it: what the lens shows.</summary>
    public Theme EffectiveTheme => Adjustments.ApplyTo(SelectedTheme);

    public LensPlacement Placement { get; private set; }

    /// <summary>True until the first-run choice has been made.</summary>
    public bool NeedsFirstRun => !Settings.FirstRunDone;

    /// <summary>The rows of "Colours on screen now, and what you see".</summary>
    public IReadOnlyList<MappingRow> Rows
    {
        get
        {
            lock (_gate)
            {
                return _rows;
            }
        }
    }

    public IReadOnlyList<Pin> Pins => _pins;

    public IReadOnlyDictionary<ShortcutAction, Shortcut?> Shortcuts => Engine.Shortcuts.Resolve(Settings.Shortcuts);

    /// <summary>The themes offered for a base, built-in ones first.</summary>
    public IReadOnlyList<Theme> ThemesFor(ThemeBase themeBase) => _catalogue.All.Where(t => t.Base == themeBase).ToList();

    // ---------------------------------------------------------------------------------------------------------
    // First run (section 4.2, 4.3)
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Finishes first run: remembers the base and theme, and whether to start with Windows. The caller turns
    /// the lens on ("Start lens") and applies the start-with-Windows setting to the system.
    /// </summary>
    public void CompleteFirstRun(ThemeBase themeBase, string themeId, bool startWithWindows)
    {
        Theme theme = _catalogue.Find(themeId) is { } found && found.Base == themeBase ? found : BuiltInThemes.DefaultFor(themeBase);
        Update(Settings with { FirstRunDone = true, Base = themeBase, StartWithWindows = startWithWindows });
        SelectTheme(theme.Id);
    }

    // ---------------------------------------------------------------------------------------------------------
    // Theme and live adjustments (FR-14, FR-15)
    // ---------------------------------------------------------------------------------------------------------

    public void SelectTheme(string themeId)
    {
        Theme theme = _catalogue.Find(themeId) ?? throw new KeyNotFoundException($"There is no theme with the id '{themeId}'.");
        SelectedTheme = theme;

        // Pins belong to what was on screen under the old theme's roles; a new theme starts from the automatic mapping.
        Update(Settings with { ThemeId = theme.Id, Base = theme.Base });
        _controller.SetTheme(EffectiveTheme);
        Changed?.Invoke();
    }

    /// <summary>The next theme of the current base, wrapping round. This is what the "next theme" shortcut does.</summary>
    public void NextTheme()
    {
        IReadOnlyList<Theme> themes = ThemesFor(SelectedTheme.Base);
        int index = 0;
        for (int i = 0; i < themes.Count; i++)
        {
            if (themes[i].Id == SelectedTheme.Id)
            {
                index = i;
                break;
            }
        }

        SelectTheme(themes[(index + 1) % themes.Count].Id);
    }

    /// <summary>Changes the user's values for the selected theme, live. They are remembered per theme.</summary>
    public void Adjust(Func<ThemeAdjustments, ThemeAdjustments> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        Update(Settings.WithAdjustments(SelectedTheme.Id, change(Adjustments)));
        _controller.SetTheme(EffectiveTheme);
        Changed?.Invoke();
    }

    /// <summary>Returns the selected theme to the values in its file.</summary>
    public void ResetAdjustments() => Adjust(static _ => new ThemeAdjustments());

    /// <summary>Saves what the lens shows now as a named theme and selects it (FR-15).</summary>
    public Theme SaveAsTheme(string name)
    {
        Theme saved = _catalogue.SaveAs(EffectiveTheme, name);
        SelectTheme(saved.Id);
        return saved;
    }

    // ---------------------------------------------------------------------------------------------------------
    // Mapping (FR-08, FR-09)
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>Gives a row another role. The change is pinned until <see cref="ResetMapping"/>.</summary>
    public void PinRow(MappingRow row, Role role)
    {
        _pins = MappingRows.Pin(_pins, row, role);
        _controller.SetPins(_pins);
        Changed?.Invoke();
    }

    public void UnpinRow(MappingRow row)
    {
        _pins = MappingRows.Unpin(_pins, row);
        _controller.SetPins(_pins);
        Changed?.Invoke();
    }

    public void ResetMapping()
    {
        _pins = [];
        _controller.SetPins(_pins);
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------------------------------------------------
    // Lens on screen (section 4.5, FR-05, FR-17, FR-18)
    // ---------------------------------------------------------------------------------------------------------

    public void TurnOn()
    {
        _controller.SetArea(Placement.Current(_screens()));
        _controller.TurnOn();
        Update(Settings with { LensWasOn = _controller.State != LensState.Off });
        Changed?.Invoke();
    }

    public void TurnOff()
    {
        _controller.TurnOff();
        Update(Settings with { LensWasOn = false });
        Changed?.Invoke();
    }

    public void ToggleLens()
    {
        if (_controller.State == LensState.Off)
        {
            TurnOn();
        }
        else
        {
            TurnOff();
        }
    }

    /// <summary>Removes the lens at once, from any state.</summary>
    public void EverythingOff()
    {
        _controller.EverythingOff();
        Update(Settings with { LensWasOn = false });
        Changed?.Invoke();
    }

    public void ToggleEditMode()
    {
        if (_controller.State == LensState.Off)
        {
            return;
        }

        _controller.SetEditMode(_controller.State != LensState.Edit);
        Changed?.Invoke();
    }

    /// <summary>Moves or resizes the lens; remembered for the current monitor layout.</summary>
    public void SetPlacement(LensPlacement placement)
    {
        IReadOnlyList<ScreenInfo> screens = _screens();
        Placement = placement;
        Update(Settings.WithPlacement(LensGeometry.LayoutKey(screens), placement));
        _controller.SetArea(placement.Current(screens));
        PlacementChanged?.Invoke(placement);
        Changed?.Invoke();
    }

    /// <summary>"Fill this screen", or back to the previous rectangle.</summary>
    public void ToggleFillScreen() => SetPlacement(Placement.ToggleFill());

    /// <summary>Puts the lens on the given screen, filling it. The control window's Area list uses this.</summary>
    public void FillScreen(ScreenInfo screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        PixelRect window = Placement.FillsScreen || LensGeometry.ScreenFor(Placement.Window, _screens()) != screen
            ? LensGeometry.DefaultFor(screen.Bounds)
            : Placement.Window;
        SetPlacement(new LensPlacement(window, FillsScreen: true));
    }

    /// <summary>
    /// Call when monitors were added, removed or rearranged: the lens takes the rectangle and mode it last had
    /// on this layout, and passes through Holding background.
    /// </summary>
    public void DisplaysChanged()
    {
        IReadOnlyList<ScreenInfo> screens = _screens();
        Placement = Settings.PlacementFor(LensGeometry.LayoutKey(screens)) ?? new LensPlacement(
            LensGeometry.Fit(Placement.Window, LensGeometry.ScreenFor(Placement.Window, screens).Bounds),
            Placement.FillsScreen);
        _controller.SetArea(Placement.Current(screens));
        _controller.NotifyDisplayChanged();
        PlacementChanged?.Invoke(Placement);
        Changed?.Invoke();
    }

    /// <summary>
    /// Call once at start, after first run if there was one: turns the lens back on if it was on when the app
    /// last closed. Position, mode and theme were restored by the constructor (FR-18).
    /// </summary>
    public void RestoreLens()
    {
        if (Settings.LensWasOn && !NeedsFirstRun)
        {
            TurnOn();
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // Shortcuts and other settings (FR-16)
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>What a shortcut, or the same entry in the tray menu, does.</summary>
    public void Handle(ShortcutAction action)
    {
        switch (action)
        {
            case ShortcutAction.ToggleLens:
                ToggleLens();
                break;
            case ShortcutAction.EditMode:
                ToggleEditMode();
                break;
            case ShortcutAction.FillScreen:
                ToggleFillScreen();
                break;
            case ShortcutAction.NextTheme:
                NextTheme();
                break;
            case ShortcutAction.OpenControlWindow:
                ControlWindowRequested?.Invoke();
                break;
            case ShortcutAction.EverythingOff:
                EverythingOff();
                break;
        }
    }

    /// <summary>Changes a shortcut; null removes it ("not set").</summary>
    public void SetShortcut(ShortcutAction action, Shortcut? shortcut)
    {
        var copy = new Dictionary<string, string>(Settings.Shortcuts);
        if (shortcut is { } s && s == Engine.Shortcuts.Defaults[action])
        {
            copy.Remove(action.ToString());
        }
        else
        {
            copy[action.ToString()] = shortcut?.ToString() ?? string.Empty;
        }

        Update(Settings with { Shortcuts = copy });
        Changed?.Invoke();
    }

    public void SetStartWithWindows(bool on)
    {
        Update(Settings with { StartWithWindows = on });
        Changed?.Invoke();
    }

    public void SetCheckForUpdates(bool on)
    {
        Update(Settings with { CheckForUpdates = on });
        Changed?.Invoke();
    }

    public void SetLanguage(string? culture)
    {
        Update(Settings with { Language = string.IsNullOrWhiteSpace(culture) ? null : culture });
        Changed?.Invoke();
    }

    /// <summary>
    /// Writes the settings file if anything changed since the last write. Sliders change settings many times a
    /// second, so the app calls this from a slow timer and on exit instead of writing on every change.
    /// </summary>
    public void Flush()
    {
        if (!_dirty)
        {
            return;
        }

        _dirty = false;
        try
        {
            _store.Save(Settings);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Settings that cannot be written are not worth a crash; try again at the next flush.
            _dirty = true;
        }
    }

    public void Dispose()
    {
        _controller.MappingChanged -= OnMappingChanged;
        _controller.StatusChanged -= OnStatusChanged;
        Flush();
    }

    private LensPlacement DefaultPlacement()
    {
        IReadOnlyList<ScreenInfo> screens = _screens();
        ScreenInfo screen = screens.FirstOrDefault(static s => s.IsPrimary) ?? screens[0];
        return new LensPlacement(LensGeometry.DefaultFor(screen.Bounds), FillsScreen: false);
    }

    private void Update(AppSettings settings)
    {
        Settings = settings;
        _dirty = true;
    }

    private void OnMappingChanged(LensMapping mapping)
    {
        lock (_gate)
        {
            _mapping = mapping;
            _rows = MappingRows.From(mapping);
        }

        Changed?.Invoke();
    }

    private void OnStatusChanged(LensStatus status) => Changed?.Invoke();
}
