// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ThemeLens.Core.Themes;

namespace ThemeLens.Engine;

/// <summary>The values a user changed on a theme in the control window. They are remembered per theme (section 5.4).</summary>
public sealed record ThemeAdjustments
{
    /// <summary>Brightness limit, 0..1. Null leaves the theme's own value.</summary>
    public float? BrightnessLimit { get; init; }

    public float? Warmth { get; init; }

    public float? ContrastAmount { get; init; }

    public ContrastMode? ContrastMode { get; init; }

    public float? SharpenAmount { get; init; }

    public SharpenMode? SharpenMode { get; init; }

    public KeepBackgroundMode? KeepBackground { get; init; }

    [JsonIgnore]
    public bool IsEmpty => this == new ThemeAdjustments();

    /// <summary>The theme with the user's values laid over its own.</summary>
    public Theme ApplyTo(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return theme with
        {
            Limits = BrightnessLimit is { } limit ? theme.Limits with { MaxLuminance = Math.Clamp(limit, 0.02f, 1f) } : theme.Limits,
            Warmth = Math.Clamp(Warmth ?? theme.Warmth, 0f, 1f),
            Contrast = new ContrastSettings(
                Math.Clamp(ContrastAmount ?? theme.Contrast.Amount, 0f, 1f),
                ContrastMode ?? theme.Contrast.Mode),
            Sharpen = new SharpenSettings(
                Math.Clamp(SharpenAmount ?? theme.Sharpen.Amount, 0f, 1f),
                SharpenMode ?? theme.Sharpen.Mode),
            KeepBackground = theme.HasRoles ? KeepBackground ?? theme.KeepBackground : theme.KeepBackground,
        };
    }
}

/// <summary>
/// What the user chose for the "rectangles only" option (section 5.6): on or off, what to look for, the values
/// of the rule for bright and for dark rectangles, and two settings that are on while the option is a preview.
/// </summary>
public sealed record RectanglePreferences
{
    public bool On { get; init; }

    /// <summary>What to look for while <see cref="OwnTarget"/> is on.</summary>
    public RectangleTarget Target { get; init; } = RectangleTarget.Bright;

    /// <summary>
    /// On (the default): the user chooses bright or dark rectangles, whatever the theme. Off: a dark theme looks
    /// for bright rectangles and a light theme for dark ones.
    /// </summary>
    public bool OwnTarget { get; init; } = true;

    /// <summary>
    /// On (the default): a found rectangle is plainly inverted, whatever the theme. Off: it is shown in the
    /// theme's colours, where the theme turns that kind of rectangle round.
    /// </summary>
    public bool PlainInversion { get; init; } = true;

    public RectangleSettings Bright { get; init; } = RectangleSettings.DefaultFor(RectangleTarget.Bright);

    public RectangleSettings Dark { get; init; } = RectangleSettings.DefaultFor(RectangleTarget.Dark);

    /// <summary>What is looked for under <paramref name="theme"/>. A theme that leaves light and dark alone has no say.</summary>
    public RectangleTarget TargetFor(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return OwnTarget || !theme.HasRoles
            ? Target
            : theme.Base == ThemeBase.Dark ? RectangleTarget.Bright : RectangleTarget.Dark;
    }

    public RectangleSettings ValuesFor(RectangleTarget target) =>
        ((target == RectangleTarget.Dark ? Dark : Bright) ?? RectangleSettings.DefaultFor(target)).For(target);

    public RectanglePreferences WithValues(RectangleTarget target, RectangleSettings values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return target == RectangleTarget.Dark ? this with { Dark = values.For(target) } : this with { Bright = values.For(target) };
    }

    /// <summary>The option as the lens runs it under <paramref name="theme"/>.</summary>
    public RectangleOption For(Theme theme)
    {
        RectangleTarget target = TargetFor(theme);
        return new RectangleOption { On = On, Target = target, InTheme = !PlainInversion, Values = ValuesFor(target) };
    }
}

/// <summary>Where the lens was on one arrangement of monitors.</summary>
public sealed record SavedPlacement(int X, int Y, int Width, int Height, bool FillsScreen)
{
    public LensPlacement ToPlacement() => new(new PixelRect(X, Y, Width, Height), FillsScreen);

    public static SavedPlacement From(LensPlacement placement) =>
        new(placement.Window.X, placement.Window.Y, placement.Window.Width, placement.Window.Height, placement.FillsScreen);
}

/// <summary>
/// Everything the app remembers between runs. Stored as one small JSON file per user; it holds settings only,
/// never anything that was on screen.
/// </summary>
public sealed record AppSettings
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    /// <summary>False until the first-run choice (dark, light, keep my colours) has been made.</summary>
    public bool FirstRunDone { get; init; }

    public ThemeBase Base { get; init; } = ThemeBase.Dark;

    public string ThemeId { get; init; } = BuiltInThemes.DefaultDarkId;

    /// <summary>Whether the lens was on when the app last closed, so it can be restored (FR-18).</summary>
    public bool LensWasOn { get; init; }

    public bool StartWithWindows { get; init; }

    /// <summary>The only network call the app makes; it can be turned off (NFR-03).</summary>
    public bool CheckForUpdates { get; init; } = true;

    /// <summary>UI language as a culture name, or null to follow Windows.</summary>
    public string? Language { get; init; }

    /// <summary>The user's values per theme id.</summary>
    public IReadOnlyDictionary<string, ThemeAdjustments> Adjustments { get; init; } = new Dictionary<string, ThemeAdjustments>();

    /// <summary>The "rectangles only" option (section 5.6).</summary>
    public RectanglePreferences Rectangles { get; init; } = new();

    /// <summary>The last lens rectangle and mode per monitor layout (key from <see cref="LensGeometry.LayoutKey"/>).</summary>
    public IReadOnlyDictionary<string, SavedPlacement> Placements { get; init; } = new Dictionary<string, SavedPlacement>();

    /// <summary>Shortcuts the user changed, by action name. A missing action uses its default; an empty string means "not set".</summary>
    public IReadOnlyDictionary<string, string> Shortcuts { get; init; } = new Dictionary<string, string>();

    public ThemeAdjustments AdjustmentsFor(string themeId) =>
        Adjustments.TryGetValue(themeId, out ThemeAdjustments? a) ? a : new ThemeAdjustments();

    public AppSettings WithAdjustments(string themeId, ThemeAdjustments adjustments)
    {
        var copy = new Dictionary<string, ThemeAdjustments>(Adjustments);
        if (adjustments.IsEmpty)
        {
            copy.Remove(themeId);
        }
        else
        {
            copy[themeId] = adjustments;
        }

        return this with { Adjustments = copy };
    }

    public AppSettings WithPlacement(string layoutKey, LensPlacement placement)
    {
        var copy = new Dictionary<string, SavedPlacement>(Placements) { [layoutKey] = SavedPlacement.From(placement) };
        return this with { Placements = copy };
    }

    public LensPlacement? PlacementFor(string layoutKey) =>
        Placements.TryGetValue(layoutKey, out SavedPlacement? p) && p.Width > 0 && p.Height > 0 ? p.ToPlacement() : null;
}

/// <summary>Loads and saves <see cref="AppSettings"/>. A missing or damaged file gives default settings, never a crash.</summary>
public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public string Path { get; } = path;

    /// <summary>True when the last <see cref="Load"/> found a file it could not read and fell back to defaults.</summary>
    public bool LastLoadWasDamaged { get; private set; }

    public AppSettings Load()
    {
        LastLoadWasDamaged = false;
        try
        {
            if (!File.Exists(Path))
            {
                return new AppSettings();
            }

            AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path), Options);
            if (settings is null || settings.Version > AppSettings.CurrentVersion)
            {
                // Written by a newer version: do not guess, and do not overwrite it with something older.
                LastLoadWasDamaged = settings is null;
                return new AppSettings();
            }

            return settings;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            LastLoadWasDamaged = true;
            TryKeepDamagedFile();
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string? directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        // Write next to the file and swap, so a crash in the middle never leaves half a file.
        string temporary = Path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Options) + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporary, Path, overwrite: true);
    }

    private void TryKeepDamagedFile()
    {
        try
        {
            File.Copy(Path, Path + ".damaged", overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Nothing more to do: the app starts with defaults either way.
        }
    }
}
