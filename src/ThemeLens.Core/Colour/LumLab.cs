// SPDX-License-Identifier: GPL-3.0-or-later
using System.Numerics;

namespace ThemeLens.Core.Colour;

/// <summary>
/// The working space of the lookup-table builder: OKLab rescaled so that its first coordinate, <see cref="J"/>,
/// is exactly the cube root of WCAG relative luminance.
/// </summary>
/// <remarks>
/// Every guarantee the engine makes about readability is a WCAG contrast ratio, and every brightness limit is a
/// WCAG luminance. In plain OKLab those are only approximately functions of L once a colour has some chroma.
/// Here two colours with equal J have equal luminance by construction, so a tone curve defined on J delivers
/// exact contrast ratios whatever hue the colour ends up with. For greys J equals OKLab L.
/// </remarks>
public readonly record struct LumLab(float J, float A, float B)
{
    public float Chroma => MathF.Sqrt((A * A) + (B * B));

    public float Luminance => J * J * J;

    public float HueDegrees => new Oklab(J, A, B).HueDegrees;

    public static LumLab FromLinear(Vector3 linear)
    {
        float y = Wcag.Luminance(linear);
        if (y <= 1e-7f)
        {
            return default;
        }

        Oklab ok = Oklab.FromLinear(linear);
        float j = MathF.Cbrt(y);
        if (ok.L <= 1e-6f)
        {
            return new LumLab(j, 0, 0);
        }

        float scale = j / ok.L;
        return new LumLab(j, ok.A * scale, ok.B * scale);
    }

    public static LumLab FromRgb8(Rgb8 colour) => FromLinear(Srgb.ToLinear(colour));

    public static LumLab Grey(float luminance) => new(MathF.Cbrt(MathF.Max(luminance, 0)), 0, 0);

    /// <summary>
    /// Converts to linear sRGB with exactly the luminance J³. The result can lie outside 0..1;
    /// pass it through <see cref="Gamut.Fit"/> before display.
    /// </summary>
    public Vector3 ToLinear()
    {
        if (J <= 0)
        {
            return Vector3.Zero;
        }

        float target = J * J * J;
        Vector3 shape = new Oklab(J, A, B).ToLinear();
        float y = Wcag.Luminance(shape);
        if (y <= 1e-7f)
        {
            return new Vector3(target);
        }

        return shape * (target / y);
    }

    public LumLab WithChromaScaled(float factor) => new(J, A * factor, B * factor);

    public static LumLab Lerp(LumLab x, LumLab y, float t) =>
        new(x.J + ((y.J - x.J) * t), x.A + ((y.A - x.A) * t), x.B + ((y.B - x.B) * t));
}
