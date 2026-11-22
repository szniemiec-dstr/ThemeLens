// SPDX-License-Identifier: GPL-3.0-or-later
using System.Numerics;

namespace ThemeLens.Core.Colour;

public static class Gamut
{
    /// <summary>
    /// Brings a linear sRGB colour into 0..1 by mixing it towards the grey of the same luminance, which keeps
    /// luminance (and so every contrast ratio) unchanged and only gives up saturation.
    /// </summary>
    public static Vector3 Fit(Vector3 linear)
    {
        float y = Math.Clamp(Wcag.Luminance(linear), 0f, 1f);
        float t = 0f;
        t = MathF.Max(t, MixNeeded(linear.X, y));
        t = MathF.Max(t, MixNeeded(linear.Y, y));
        t = MathF.Max(t, MixNeeded(linear.Z, y));
        if (t <= 0f)
        {
            return linear;
        }

        Vector3 fitted = Vector3.Lerp(linear, new Vector3(y), MathF.Min(t, 1f));
        return Vector3.Clamp(fitted, Vector3.Zero, Vector3.One);
    }

    private static float MixNeeded(float channel, float grey)
    {
        if (channel > 1f)
        {
            return channel - grey <= 1e-9f ? 1f : (channel - 1f) / (channel - grey);
        }

        if (channel < 0f)
        {
            return grey - channel <= 1e-9f ? 1f : -channel / (grey - channel);
        }

        return 0f;
    }

    /// <summary>
    /// Scales a linear colour to the requested luminance without changing its chromaticity, then fits it into gamut.
    /// </summary>
    public static Vector3 WithLuminance(Vector3 linear, float luminance)
    {
        float y = Wcag.Luminance(linear);
        if (y <= 1e-7f)
        {
            return new Vector3(Math.Clamp(luminance, 0f, 1f));
        }

        return Fit(linear * (luminance / y));
    }
}
