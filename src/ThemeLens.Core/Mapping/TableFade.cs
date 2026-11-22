// SPDX-License-Identifier: GPL-3.0-or-later
using System.Numerics;
using ThemeLens.Core.Colour;

namespace ThemeLens.Core.Mapping;

/// <summary>
/// A cross-fade from one look of the lens to another (section 6.4). The fade is done by blending the tables
/// themselves, in linear light: every output colour then moves in a straight line in light, so the brightness of
/// any pixel, and of the lens as a whole, only ever moves towards its target. Nothing overshoots (FR-13).
/// </summary>
public sealed class TableFade
{
    private static readonly float[] EncodeTable = BuildEncodeTable();

    private readonly Vector3[] _fromLight;
    private readonly Vector3[] _fromDark;
    private readonly Vector3[] _toLight;
    private readonly Vector3[] _toDark;
    private readonly bool _sameTables;

    public TableFade(LensTables from, LensTables to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        From = from;
        To = to;
        _fromLight = Decode(from.Light);
        _toLight = Decode(to.Light);
        _sameTables = ReferenceEquals(from.Light, from.Dark) && ReferenceEquals(to.Light, to.Dark);
        _fromDark = _sameTables ? _fromLight : Decode(from.Dark);
        _toDark = _sameTables ? _toLight : Decode(to.Dark);
    }

    public LensTables From { get; }

    public LensTables To { get; }

    /// <summary>The look at <paramref name="progress"/> between 0 (the old look) and 1 (the new one).</summary>
    public LensTables At(float progress)
    {
        if (progress <= 0f)
        {
            return From;
        }

        if (progress >= 1f)
        {
            return To;
        }

        Lut3D light = Blend(_fromLight, _toLight, progress);
        Lut3D dark = _sameTables ? light : Blend(_fromDark, _toDark, progress);
        return new LensTables(light, dark, FrameParameters.Lerp(From.Parameters, To.Parameters, progress), To.Theme, To.Roles);
    }

    private static Vector3[] Decode(Lut3D lut)
    {
        var linear = new Vector3[Lut3D.EntryCount];
        for (int n = 0; n < linear.Length; n++)
        {
            int i = n * 3;
            linear[n] = Srgb.Decode(new Vector3(lut.Data[i], lut.Data[i + 1], lut.Data[i + 2]));
        }

        return linear;
    }

    private static Lut3D Blend(Vector3[] from, Vector3[] to, float t)
    {
        var lut = new Lut3D();
        float[] data = lut.Data;
        for (int n = 0; n < from.Length; n++)
        {
            Vector3 linear = Vector3.Lerp(from[n], to[n], t);
            int i = n * 3;
            data[i] = EncodeFast(linear.X);
            data[i + 1] = EncodeFast(linear.Y);
            data[i + 2] = EncodeFast(linear.Z);
        }

        return lut;
    }

    private const int EncodeSteps = 4096;

    private static float[] BuildEncodeTable()
    {
        float[] table = new float[EncodeSteps + 2];
        for (int i = 0; i < table.Length; i++)
        {
            table[i] = Srgb.Encode(MathF.Min(1f, (float)i / EncodeSteps));
        }

        return table;
    }

    /// <summary>sRGB encoding from a table. A fade rebuilds two tables every frame, so the power function is too slow here.</summary>
    private static float EncodeFast(float linear)
    {
        float scaled = Math.Clamp(linear, 0f, 1f) * EncodeSteps;
        int i = (int)scaled;
        float t = scaled - i;
        return EncodeTable[i] + ((EncodeTable[i + 1] - EncodeTable[i]) * t);
    }
}
