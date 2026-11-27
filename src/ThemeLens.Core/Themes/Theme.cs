// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Core.Colour;

namespace ThemeLens.Core.Themes;

/// <summary>What the lens does to the lightness of the picture underneath it.</summary>
public enum ThemeBase
{
    /// <summary>Whatever the app's background is, it is shown as the theme's dark page colour.</summary>
    Dark,

    /// <summary>The mirror image: backgrounds are shown as the theme's light page colour.</summary>
    Light,

    /// <summary>Lightness stays as the app drew it; only hue, blue level, saturation or brightness change.</summary>
    Keep,
}

public enum ColourVisionMode
{
    None,

    /// <summary>Moves red–green differences onto lightness and the blue–yellow axis.</summary>
    RedGreen,
}

/// <summary>How hard the area guard works (section 6.5 of the specification).</summary>
public enum KeepBackgroundMode
{
    /// <summary>The guard is disabled.</summary>
    Off,

    /// <summary>Areas that look like images (many colours, soft edges) are allowed through.</summary>
    Balanced,

    /// <summary>No large area may differ from the page by more than the theme allows.</summary>
    Strict,
}

public enum ContrastMode
{
    Standard,
    Local,
}

public enum SharpenMode
{
    Standard,
    Strong,
}

/// <summary>Optional contrast boost. <see cref="Amount"/> 0 is off, 1 is 100%.</summary>
public readonly record struct ContrastSettings(float Amount, ContrastMode Mode)
{
    public static readonly ContrastSettings Off = new(0f, ContrastMode.Standard);

    public bool IsOn => Amount > 0f;
}

/// <summary>Optional sharpening. <see cref="Amount"/> 0 is off, 1 is 100%.</summary>
public readonly record struct SharpenSettings(float Amount, SharpenMode Mode)
{
    public static readonly SharpenSettings Off = new(0f, SharpenMode.Standard);

    public bool IsOn => Amount > 0f;
}

/// <summary>
/// The "rectangles only" option (section 5.6). With it on, the lens leaves the screen as it is and changes only
/// large rectangles of the wrong kind: bright ones under a dark theme, dark ones under a light theme. How bright
/// (or dark) a rectangle has to be depends on its size.
/// </summary>
/// <remarks>
/// This is the user's setting, kept once for dark themes and once for light themes. It is not part of a theme
/// file; the app lays it over the theme the same way it lays the slider values over it.
/// </remarks>
public sealed record RectangleSettings
{
    public static readonly RectangleSettings Off = new();

    /// <summary>Under a dark theme the brightness threshold can be set from here up to 100%.</summary>
    public const float LowestBrightThreshold = 0.70f;

    /// <summary>Under a light theme the threshold can be set from 0% up to here.</summary>
    public const float HighestDarkThreshold = 0.30f;

    /// <summary>The smallest share of the screen either area can be set to.</summary>
    public const float LeastArea = 0.005f;

    public bool On { get; init; }

    /// <summary>
    /// Share of the screen, 0..1. A rectangle of this size is changed only when it is pure white (pure black
    /// under a light theme). Smaller rectangles are never changed.
    /// </summary>
    public float SmallestArea { get; init; } = 0.02f;

    /// <summary>Share of the screen, 0..1. From this size on, the brightness threshold alone decides.</summary>
    public float FullArea { get; init; } = 0.20f;

    /// <summary>
    /// Brightness, 0..1. Under a dark theme a large rectangle is changed when it is at least this bright; under
    /// a light theme, when it is at most this bright.
    /// </summary>
    public float Brightness { get; init; } = 0.75f;

    /// <summary>What the option starts with for a base: bright rectangles from 75% up, dark ones from 25% down.</summary>
    public static RectangleSettings DefaultFor(ThemeBase themeBase) =>
        themeBase == ThemeBase.Light ? new RectangleSettings { Brightness = 0.25f } : new RectangleSettings();

    /// <summary>The same settings with every value inside the range the option works in for <paramref name="themeBase"/>.</summary>
    public RectangleSettings For(ThemeBase themeBase)
    {
        float smallest = Math.Clamp(float.IsFinite(SmallestArea) ? SmallestArea : LeastArea, LeastArea, 1f);
        float full = Math.Clamp(float.IsFinite(FullArea) ? FullArea : 1f, smallest, 1f);
        float brightness = float.IsFinite(Brightness) ? Brightness : DefaultFor(themeBase).Brightness;
        brightness = themeBase == ThemeBase.Light
            ? Math.Clamp(brightness, 0f, HighestDarkThreshold)
            : Math.Clamp(brightness, LowestBrightThreshold, 1f);
        return this with
        {
            On = On && themeBase != ThemeBase.Keep,
            SmallestArea = smallest,
            FullArea = full,
            Brightness = brightness,
        };
    }

    /// <summary>
    /// How strongly a rectangle has to be of the wrong kind before it is changed, 0..1: its brightness under a
    /// dark theme, its darkness (1 − brightness) under a light theme.
    /// </summary>
    public float Strength(ThemeBase themeBase) => themeBase == ThemeBase.Light ? 1f - Brightness : Brightness;
}

/// <summary>The colours a theme gives to each role. Other accent has no colour of its own: it keeps its hue.</summary>
public sealed record ThemeRoles(
    Rgb8 Page,
    Rgb8 Panel,
    Rgb8 Text,
    Rgb8 QuietText,
    Rgb8 Link,
    Rgb8 Good,
    Rgb8 Warning,
    Rgb8 Bad)
{
    public Rgb8 this[Role role] => role switch
    {
        Role.Page => Page,
        Role.Panel => Panel,
        Role.Text => Text,
        Role.QuietText => QuietText,
        Role.Link => Link,
        Role.Good => Good,
        Role.Warning => Warning,
        Role.Bad => Bad,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "This role has no colour of its own."),
    };
}

public sealed record ThemeLimits
{
    public static readonly ThemeLimits None = new();

    /// <summary>Largest WCAG relative luminance any pixel of the lens may have. 1 means no limit.</summary>
    public float MaxLuminance { get; init; } = 1f;

    /// <summary>Smallest contrast ratio against the page that text-like colours are given.</summary>
    public float MinTextContrast { get; init; } = 4.5f;

    /// <summary>Largest OKLab chroma. Null means no limit; 0 removes colour entirely.</summary>
    public float? MaxChroma { get; init; }

    /// <summary>
    /// Largest contrast ratio against the page that an <em>area</em> (as opposed to text) may have before the
    /// area guard treats it as a background that came out wrong. On a dark theme this is what stops a large
    /// bright rectangle; on a light theme, a large dark one.
    /// </summary>
    public float MaxAreaContrast { get; init; } = DefaultMaxAreaContrastDark;

    /// <summary>
    /// On a dark base anything much brighter than the page is glare, so the default is tight. On a light base a
    /// dark button is ordinary and harmless; only something close to a dark page needs to be caught.
    /// </summary>
    public const float DefaultMaxAreaContrastDark = 6f;
    public const float DefaultMaxAreaContrastLight = 12f;

    public static float DefaultMaxAreaContrast(ThemeBase themeBase) =>
        themeBase == ThemeBase.Light ? DefaultMaxAreaContrastLight : DefaultMaxAreaContrastDark;
}

/// <summary>
/// A theme: data only. Built-in themes ship as JSON files in <c>themes/</c>; see <see cref="ThemeJson"/>.
/// </summary>
public sealed record Theme
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public required ThemeBase Base { get; init; }

    /// <summary>Role colours. Required for the dark and light bases; null for the keep base.</summary>
    public ThemeRoles? Roles { get; init; }

    public ThemeLimits Limits { get; init; } = ThemeLimits.None;

    /// <summary>0 leaves the white point alone; 1 removes almost all blue.</summary>
    public float Warmth { get; init; }

    /// <summary>
    /// The warmth at which <see cref="Roles"/> were written down. Role colours are what the lens shows at this
    /// warmth, so a theme file is what-you-see: moving the warmth slider shifts the colours relative to it.
    /// </summary>
    public float AuthoredWarmth { get; init; }

    public ColourVisionMode ColourVision { get; init; } = ColourVisionMode.None;

    public KeepBackgroundMode KeepBackground { get; init; } = KeepBackgroundMode.Strict;

    public ContrastSettings Contrast { get; init; } = ContrastSettings.Off;

    public SharpenSettings Sharpen { get; init; } = SharpenSettings.Off;

    /// <summary>The "rectangles only" option. Not stored in theme files; see <see cref="RectangleSettings"/>.</summary>
    public RectangleSettings Rectangles { get; init; } = RectangleSettings.Off;

    /// <summary>True when the base remaps lightness onto role colours.</summary>
    public bool HasRoles => Base != ThemeBase.Keep;

    /// <summary>True when the lens changes only the rectangles it finds and leaves the rest of the screen alone.</summary>
    public bool RectanglesOnly => HasRoles && Rectangles.On;

    /// <summary>
    /// True when the lens would show the screen exactly as it is (Passthrough with contrast and sharpen at zero).
    /// In that state the lens body is hidden and nothing is captured.
    /// </summary>
    public bool ChangesNothing =>
        Base == ThemeBase.Keep
        && Warmth <= 0f
        && ColourVision == ColourVisionMode.None
        && Limits.MaxLuminance >= 1f
        && Limits.MaxChroma is null
        && !Contrast.IsOn
        && !Sharpen.IsOn;

    public ThemeRoles RequireRoles() =>
        Roles ?? throw new InvalidOperationException($"Theme '{Id}' has base '{Base}' and so has no role colours.");
}
