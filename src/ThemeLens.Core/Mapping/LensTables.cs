// SPDX-License-Identifier: GPL-3.0-or-later
using System.Numerics;
using ThemeLens.Core.Analysis;
using ThemeLens.Core.Colour;
using ThemeLens.Core.Themes;

namespace ThemeLens.Core.Mapping;

/// <summary>
/// Everything a renderer needs besides the two lookup tables. It maps one-to-one onto the shader's constant
/// buffer, and every value can be cross-faded.
/// </summary>
public sealed record FrameParameters
{
    /// <summary>True when the lens shows the screen exactly as it is; renderers skip all work.</summary>
    public bool Identity { get; init; }

    /// <summary>+1 on a dark base (text is lighter than the page), −1 on a light base, 0 on the keep base.</summary>
    public float Direction { get; init; }

    /// <summary>The page colour as the lens shows it, after limits and warmth. Also the colour of the fallback fill.</summary>
    public Vector3 PageEncoded { get; init; }

    /// <summary>The text colour as the lens shows it. Detail inside a guarded area is drawn in this colour.</summary>
    public Vector3 TextEncoded { get; init; }

    public float PageLuminance { get; init; }

    public float MaxAreaContrast { get; init; } = 6f;

    /// <summary>0 guard off, 1 guard on.</summary>
    public float GuardStrength { get; init; }

    /// <summary>1 when the guard lets image-like areas through (Balanced), else 0.</summary>
    public float GuardSoftGate { get; init; }

    public float MaxLuminance { get; init; } = 1f;

    /// <summary>
    /// Share of light votes over the whole lens, from the latest analysis. It decides the polarity of places
    /// where no background is in sight, such as the middle of a large mid-tone area.
    /// </summary>
    public float WholeLensLightFraction { get; init; } = 0.5f;

    /// <summary>Strength of the local-contrast pass, 0 when off.</summary>
    public float LocalContrast { get; init; }

    /// <summary>Strength of the standard sharpen pass, 0 when off.</summary>
    public float Sharpen { get; init; }

    /// <summary>Strength of the strong sharpen pass, 0 when off.</summary>
    public float StrongSharpen { get; init; }

    /// <summary>
    /// 0 when the lens changes everything under it; 1 when the "rectangles only" option is on and it changes
    /// only what <see cref="Mask"/> selects (section 5.6). In between while one fades into the other, and 0
    /// while the option is on but the lens has not found its rectangles yet.
    /// </summary>
    public float RectanglesOnly { get; init; }

    /// <summary>
    /// What is changed while <see cref="RectanglesOnly"/> is still 0: 1 every pixel (the lens without the
    /// option, and the option with rectangles shown in the theme), 0 every pixel in a region of the kind the
    /// option looks for (the option with plain inversion: it must not invert what is dark already).
    /// </summary>
    public float RectangleBaseline { get; init; } = 1f;

    /// <summary>+1 when bright rectangles are looked for, −1 for dark ones. Only read with the option on.</summary>
    public float RectangleTarget { get; init; } = 1f;

    /// <summary>The rectangles found by the latest analysis, or null before the first one.</summary>
    public RectangleMask? Mask { get; init; }

    /// <summary>True when some pixels may be left as they are, so the renderer has to ask for each one.</summary>
    public bool SelectsPixels => RectanglesOnly > 0f || RectangleBaseline < 1f;

    public Rgb8 PageColour => Srgb.FromEncoded(PageEncoded);

    public Rgb8 TextColour => Srgb.FromEncoded(TextEncoded);

    public bool GuardOn => GuardStrength > 0f && Direction != 0f;

    /// <summary>True when the picture needs more than the single recolour pass.</summary>
    public bool NeedsFinishPass => GuardOn || LocalContrast > 0f || Sharpen > 0f || StrongSharpen > 0f;

    public static FrameParameters Lerp(FrameParameters a, FrameParameters b, float t)
    {
        static float Mix(float x, float y, float t) => x + ((y - x) * t);
        return new FrameParameters
        {
            Identity = false,
            Direction = t < 0.5f ? a.Direction : b.Direction,
            PageEncoded = Srgb.Encode(Vector3.Lerp(Srgb.Decode(a.PageEncoded), Srgb.Decode(b.PageEncoded), t)),
            TextEncoded = Srgb.Encode(Vector3.Lerp(Srgb.Decode(a.TextEncoded), Srgb.Decode(b.TextEncoded), t)),
            PageLuminance = Mix(a.PageLuminance, b.PageLuminance, t),
            MaxAreaContrast = Mix(a.MaxAreaContrast, b.MaxAreaContrast, t),
            GuardStrength = Mix(a.GuardStrength, b.GuardStrength, t),
            GuardSoftGate = Mix(a.GuardSoftGate, b.GuardSoftGate, t),
            MaxLuminance = Mix(a.MaxLuminance, b.MaxLuminance, t),
            WholeLensLightFraction = Mix(a.WholeLensLightFraction, b.WholeLensLightFraction, t),
            LocalContrast = Mix(a.LocalContrast, b.LocalContrast, t),
            Sharpen = Mix(a.Sharpen, b.Sharpen, t),
            StrongSharpen = Mix(a.StrongSharpen, b.StrongSharpen, t),
            RectanglesOnly = Mix(a.RectanglesOnly, b.RectanglesOnly, t),
            RectangleBaseline = Mix(a.RectangleBaseline, b.RectangleBaseline, t),
            RectangleTarget = b.SelectsPixels ? b.RectangleTarget : a.RectangleTarget,
            Mask = b.SelectsPixels ? b.Mask ?? a.Mask : a.Mask,
        };
    }
}

/// <summary>
/// The complete look of the lens at one moment: one table for light regions of the screen, one for dark
/// regions, and the parameters of the per-frame passes. Built by <see cref="TableBuilder"/>.
/// </summary>
public sealed class LensTables
{
    public LensTables(Lut3D light, Lut3D dark, FrameParameters parameters, Theme theme, RoleMap roles)
    {
        Light = light;
        Dark = dark;
        Parameters = parameters;
        Theme = theme;
        Roles = roles;
    }

    /// <summary>Used where the screen is light with dark content.</summary>
    public Lut3D Light { get; }

    /// <summary>Used where the screen is dark with light content.</summary>
    public Lut3D Dark { get; }

    public FrameParameters Parameters { get; }

    public Theme Theme { get; }

    public RoleMap Roles { get; }

    /// <summary>The same look with other parameters. The tables are shared, not copied.</summary>
    public LensTables With(FrameParameters parameters) => new(Light, Dark, parameters, Theme, Roles);

    /// <summary>The same look with the rectangles of the "rectangles only" option filled in.</summary>
    public LensTables WithRectangles(FoundRectangles found)
    {
        ArgumentNullException.ThrowIfNull(found);
        return With(Parameters with { Mask = found.ToMask() });
    }

    public Lut3D For(RegionPolarity region) => region == RegionPolarity.Light ? Light : Dark;

    /// <summary>The colour a flat area of <paramref name="source"/> is shown as: what the control window lists under "Shown as".</summary>
    public Rgb8 Map(Rgb8 source, RegionPolarity region) =>
        Parameters.Identity ? source : Srgb.FromEncoded(FinishMath.Clamp(For(region).Sample(Srgb.ToEncoded(source)), Parameters));
}

/// <summary>The last step of every frame: clamp to the theme's limits. Shared by the table preview and the renderer.</summary>
public static class FinishMath
{
    /// <summary>
    /// Clamps an encoded colour to the brightness limit and to the page colour, so that neither sharpening
    /// halos nor contrast can produce a pixel the theme would not allow (FR-23).
    /// </summary>
    public static Vector3 Clamp(Vector3 encoded, FrameParameters p)
    {
        encoded = Vector3.Clamp(encoded, Vector3.Zero, Vector3.One);
        if (p.Direction > 0f)
        {
            encoded = Vector3.Max(encoded, p.PageEncoded);
        }
        else if (p.Direction < 0f)
        {
            encoded = Vector3.Min(encoded, p.PageEncoded);
        }

        if (p.MaxLuminance < 1f)
        {
            Vector3 linear = Srgb.Decode(encoded);
            float y = Wcag.Luminance(linear);
            if (y > p.MaxLuminance)
            {
                encoded = Srgb.Encode(linear * (p.MaxLuminance / y));
            }
        }

        return encoded;
    }
}
