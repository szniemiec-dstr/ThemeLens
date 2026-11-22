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
    public float MaxAreaContrast { get; init; } = 6f;
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

    public ColourVisionMode ColourVision { get; init; } = ColourVisionMode.None;

    public KeepBackgroundMode KeepBackground { get; init; } = KeepBackgroundMode.Strict;

    public ContrastSettings Contrast { get; init; } = ContrastSettings.Off;

    public SharpenSettings Sharpen { get; init; } = SharpenSettings.Off;

    /// <summary>True when the base remaps lightness onto role colours.</summary>
    public bool HasRoles => Base != ThemeBase.Keep;

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
