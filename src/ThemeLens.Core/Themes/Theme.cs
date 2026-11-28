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

/// <summary>What the "rectangles only" option looks for (section 5.6).</summary>
public enum RectangleTarget
{
    /// <summary>Bright rectangles, to turn them dark: light avoidance.</summary>
    Bright,

    /// <summary>Dark rectangles, to turn them light: darkness avoidance.</summary>
    Dark,
}

/// <summary>
/// The three values of the "rectangles only" rule (section 5.6) for one target. How bright (or dark) a
/// rectangle has to be depends on its size; sizes are the side of a square of the same area, in screen pixels.
/// </summary>
public sealed record RectangleSettings
{
    /// <summary>The smallest size that can be set: a square of 10 by 10 pixels.</summary>
    public const float LeastSide = 10f;

    /// <summary>No setting is larger than this, whatever the screen. The control window stops at half the screen.</summary>
    public const float MostSide = 8192f;

    /// <summary>Bright rectangles: the threshold can be set from here up to 100%.</summary>
    public const float LowestBrightThreshold = 0.50f;

    /// <summary>Dark rectangles: the threshold can be set from 0% up to here.</summary>
    public const float HighestDarkThreshold = 0.50f;

    /// <summary>
    /// Side of a square, in pixels. A rectangle of this area is changed only when it is pure white (pure black
    /// when dark rectangles are looked for). Smaller rectangles are never changed.
    /// </summary>
    public float SmallestSide { get; init; } = 200f;

    /// <summary>Side of a square, in pixels. From this area on, the brightness threshold alone decides.</summary>
    public float FullSide { get; init; } = 640f;

    /// <summary>
    /// Brightness, 0..1. A large bright rectangle is changed when it is at least this bright; a large dark one
    /// when it is at most this bright.
    /// </summary>
    public float Brightness { get; init; } = 0.75f;

    /// <summary>What the rule starts with: bright rectangles from 75% up, dark ones from 25% down.</summary>
    public static RectangleSettings DefaultFor(RectangleTarget target) =>
        target == RectangleTarget.Dark ? new RectangleSettings { Brightness = 0.25f } : new RectangleSettings();

    /// <summary>The side of the square that covers half of a screen: the largest size the control window offers.</summary>
    public static float HalfScreenSide(long screenPixels) =>
        Math.Clamp(MathF.Sqrt(Math.Max(screenPixels, 0) / 2f), LeastSide, MostSide);

    /// <summary>The same values with each one inside the range the rule works in for <paramref name="target"/>.</summary>
    public RectangleSettings For(RectangleTarget target)
    {
        float smallest = Math.Clamp(float.IsFinite(SmallestSide) ? SmallestSide : LeastSide, LeastSide, MostSide);
        float full = Math.Clamp(float.IsFinite(FullSide) ? FullSide : MostSide, smallest, MostSide);
        float brightness = float.IsFinite(Brightness) ? Brightness : DefaultFor(target).Brightness;
        brightness = target == RectangleTarget.Dark
            ? Math.Clamp(brightness, 0f, HighestDarkThreshold)
            : Math.Clamp(brightness, LowestBrightThreshold, 1f);
        return this with { SmallestSide = smallest, FullSide = full, Brightness = brightness };
    }

    /// <summary>
    /// How strongly a rectangle has to be of the kind looked for before it is changed, 0..1: its brightness for
    /// bright rectangles, its darkness (1 − brightness) for dark ones.
    /// </summary>
    public float Strength(RectangleTarget target) => target == RectangleTarget.Dark ? 1f - Brightness : Brightness;
}

/// <summary>
/// The "rectangles only" option as the lens runs it (section 5.6). With it on, the lens leaves the screen as it
/// is and changes only the rectangles it finds.
/// </summary>
/// <remarks>
/// This is the user's setting, resolved for the selected theme. It is not part of a theme file; the app lays it
/// over the theme the same way it lays the slider values over it.
/// </remarks>
public sealed record RectangleOption
{
    public static readonly RectangleOption Off = new();

    public bool On { get; init; }

    public RectangleTarget Target { get; init; } = RectangleTarget.Bright;

    /// <summary>
    /// False (the default): a found rectangle is inverted, whatever the theme. True: it is shown in the theme's
    /// colours, as the whole lens would show it. Only possible when the theme turns the target round itself:
    /// bright rectangles under a dark theme, dark ones under a light theme.
    /// </summary>
    public bool InTheme { get; init; }

    /// <summary>
    /// How a rectangle is inverted. False (the default): the plain negative, in which every colour becomes its
    /// opposite and blue turns orange. True: light and dark are swapped and hues are kept, so blue stays blue.
    /// </summary>
    public bool KeepHues { get; init; }

    public RectangleSettings Values { get; init; } = new();
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

    /// <summary>The "rectangles only" option. Not stored in theme files; see <see cref="RectangleOption"/>.</summary>
    public RectangleOption Rectangles { get; init; } = RectangleOption.Off;

    /// <summary>True when the base remaps lightness onto role colours.</summary>
    public bool HasRoles => Base != ThemeBase.Keep;

    /// <summary>True when the lens changes only the rectangles it finds and leaves the rest of the screen alone.</summary>
    public bool RectanglesOnly => Rectangles.On;

    /// <summary>True when found rectangles are shown in this theme's colours and not plainly inverted.</summary>
    public bool RectanglesInTheme =>
        Rectangles is { On: true, InTheme: true }
        && ((Base == ThemeBase.Dark && Rectangles.Target == RectangleTarget.Bright)
            || (Base == ThemeBase.Light && Rectangles.Target == RectangleTarget.Dark));

    /// <summary>
    /// True when the lens would show the screen exactly as it is (Passthrough with contrast and sharpen at zero).
    /// In that state the lens body is hidden and nothing is captured.
    /// </summary>
    public bool ChangesNothing =>
        !Rectangles.On
        && Base == ThemeBase.Keep
        && Warmth <= 0f
        && ColourVision == ColourVisionMode.None
        && Limits.MaxLuminance >= 1f
        && Limits.MaxChroma is null
        && !Contrast.IsOn
        && !Sharpen.IsOn;

    public ThemeRoles RequireRoles() =>
        Roles ?? throw new InvalidOperationException($"Theme '{Id}' has base '{Base}' and so has no role colours.");
}
