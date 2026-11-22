// SPDX-License-Identifier: GPL-3.0-or-later
using System.Numerics;
using ThemeLens.Core.Colour;

namespace ThemeLens.Core.Mapping;

/// <summary>
/// A 33×33×33 lookup table: an output colour for every possible input colour (section 6.3). Both sides are
/// sRGB-encoded 0..1. Entries are stored red-fastest, which is the layout of a 3D texture with red on x.
/// </summary>
public sealed class Lut3D
{
    public const int Size = 33;
    public const int EntryCount = Size * Size * Size;

    public Lut3D()
        : this(new float[EntryCount * 3])
    {
    }

    public Lut3D(float[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length != EntryCount * 3)
        {
            throw new ArgumentException($"A table holds exactly {EntryCount} RGB entries.", nameof(data));
        }

        Data = data;
    }

    public float[] Data { get; }

    public static int Index(int r, int g, int b) => ((((b * Size) + g) * Size) + r) * 3;

    public Vector3 this[int r, int g, int b]
    {
        get
        {
            int i = Index(r, g, b);
            return new Vector3(Data[i], Data[i + 1], Data[i + 2]);
        }

        set
        {
            int i = Index(r, g, b);
            Data[i] = value.X;
            Data[i + 1] = value.Y;
            Data[i + 2] = value.Z;
        }
    }

    public static Lut3D Identity()
    {
        var lut = new Lut3D();
        for (int b = 0; b < Size; b++)
        {
            for (int g = 0; g < Size; g++)
            {
                for (int r = 0; r < Size; r++)
                {
                    lut[r, g, b] = new Vector3(r, g, b) / (Size - 1);
                }
            }
        }

        return lut;
    }

    /// <summary>Trilinear interpolation, as a GPU sampler does it on a 3D texture.</summary>
    public Vector3 Sample(Vector3 encoded)
    {
        float fr = Math.Clamp(encoded.X, 0f, 1f) * (Size - 1);
        float fg = Math.Clamp(encoded.Y, 0f, 1f) * (Size - 1);
        float fb = Math.Clamp(encoded.Z, 0f, 1f) * (Size - 1);
        int r0 = Math.Min((int)fr, Size - 2);
        int g0 = Math.Min((int)fg, Size - 2);
        int b0 = Math.Min((int)fb, Size - 2);
        float tr = fr - r0;
        float tg = fg - g0;
        float tb = fb - b0;

        Vector3 c00 = Vector3.Lerp(this[r0, g0, b0], this[r0 + 1, g0, b0], tr);
        Vector3 c10 = Vector3.Lerp(this[r0, g0 + 1, b0], this[r0 + 1, g0 + 1, b0], tr);
        Vector3 c01 = Vector3.Lerp(this[r0, g0, b0 + 1], this[r0 + 1, g0, b0 + 1], tr);
        Vector3 c11 = Vector3.Lerp(this[r0, g0 + 1, b0 + 1], this[r0 + 1, g0 + 1, b0 + 1], tr);
        return Vector3.Lerp(Vector3.Lerp(c00, c10, tg), Vector3.Lerp(c01, c11, tg), tb);
    }

    public Rgb8 Map(Rgb8 colour) => Srgb.FromEncoded(Sample(Srgb.ToEncoded(colour)));

    public Lut3D Clone() => new((float[])Data.Clone());
}
