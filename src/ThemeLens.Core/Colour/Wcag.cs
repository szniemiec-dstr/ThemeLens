// SPDX-License-Identifier: GPL-3.0-or-later
using System.Numerics;

namespace ThemeLens.Core.Colour;

/// <summary>Relative luminance and contrast ratio as defined by WCAG 2.x.</summary>
public static class Wcag
{
    public static readonly Vector3 LuminanceWeights = new(0.2126f, 0.7152f, 0.0722f);

    /// <summary>The flare term WCAG adds to both luminances before taking their ratio.</summary>
    public const float Flare = 0.05f;

    public static float Luminance(Vector3 linear) => Vector3.Dot(linear, LuminanceWeights);

    public static float Luminance(Rgb8 colour) => Luminance(Srgb.ToLinear(colour));

    public static float Contrast(float luminanceA, float luminanceB)
    {
        float hi = MathF.Max(luminanceA, luminanceB);
        float lo = MathF.Min(luminanceA, luminanceB);
        return (hi + Flare) / (lo + Flare);
    }

    public static float Contrast(Rgb8 a, Rgb8 b) => Contrast(Luminance(a), Luminance(b));

    /// <summary>
    /// The luminance that has the given contrast ratio against <paramref name="reference"/>, on the lighter side
    /// (<paramref name="lighter"/> true) or the darker side. The result is not clamped to 0..1.
    /// </summary>
    public static float LuminanceAtContrast(float reference, float contrast, bool lighter) =>
        lighter
            ? ((reference + Flare) * contrast) - Flare
            : ((reference + Flare) / contrast) - Flare;
}
