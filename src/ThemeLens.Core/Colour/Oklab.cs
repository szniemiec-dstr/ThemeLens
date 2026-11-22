// SPDX-License-Identifier: GPL-3.0-or-later
using System.Numerics;

namespace ThemeLens.Core.Colour;

/// <summary>
/// A colour in OKLab (Björn Ottosson, 2020): a space in which equal distances look roughly equally different.
/// L is lightness (0 black, 1 white); A is green to red; B is blue to yellow.
/// </summary>
public readonly record struct Oklab(float L, float A, float B)
{
    public float Chroma => MathF.Sqrt((A * A) + (B * B));

    /// <summary>Hue angle in degrees, 0 to 360. Red is near 29, yellow 110, green 142, blue 264.</summary>
    public float HueDegrees
    {
        get
        {
            float h = MathF.Atan2(B, A) * (180f / MathF.PI);
            return h < 0 ? h + 360f : h;
        }
    }

    public static Oklab FromLinear(Vector3 c)
    {
        float l = (0.4122214708f * c.X) + (0.5363325363f * c.Y) + (0.0514459929f * c.Z);
        float m = (0.2119034982f * c.X) + (0.6806995451f * c.Y) + (0.1073969566f * c.Z);
        float s = (0.0883024619f * c.X) + (0.2817188376f * c.Y) + (0.6299787005f * c.Z);

        float l3 = MathF.Cbrt(l);
        float m3 = MathF.Cbrt(m);
        float s3 = MathF.Cbrt(s);

        return new Oklab(
            (0.2104542553f * l3) + (0.7936177850f * m3) - (0.0040720468f * s3),
            (1.9779984951f * l3) - (2.4285922050f * m3) + (0.4505937099f * s3),
            (0.0259040371f * l3) + (0.7827717662f * m3) - (0.8086757660f * s3));
    }

    public static Oklab FromRgb8(Rgb8 colour) => FromLinear(Srgb.ToLinear(colour));

    /// <summary>Converts to linear sRGB. The result can lie outside 0..1 when the colour is out of gamut.</summary>
    public Vector3 ToLinear()
    {
        float l3 = L + (0.3963377774f * A) + (0.2158037573f * B);
        float m3 = L - (0.1055613458f * A) - (0.0638541728f * B);
        float s3 = L - (0.0894841775f * A) - (1.2914855480f * B);

        float l = l3 * l3 * l3;
        float m = m3 * m3 * m3;
        float s = s3 * s3 * s3;

        return new Vector3(
            (4.0767416621f * l) - (3.3077115913f * m) + (0.2309699292f * s),
            (-1.2684380046f * l) + (2.6097574011f * m) - (0.3413193965f * s),
            (-0.0041960863f * l) - (0.7034186147f * m) + (1.7076147010f * s));
    }

    public static float Distance(Oklab x, Oklab y)
    {
        float dl = x.L - y.L;
        float da = x.A - y.A;
        float db = x.B - y.B;
        return MathF.Sqrt((dl * dl) + (da * da) + (db * db));
    }
}
