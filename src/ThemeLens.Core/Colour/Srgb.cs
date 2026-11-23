// SPDX-License-Identifier: GPL-3.0-or-later
using System.Numerics;

namespace ThemeLens.Core.Colour;

/// <summary>
/// The sRGB transfer function. "Encoded" values are what is stored in a frame (0..1 for 0..255);
/// "linear" values are proportional to light.
/// </summary>
public static class Srgb
{
    private const int EncodeSteps = 4096;

    private static readonly float[] DecodeTable = BuildDecodeTable();
    private static readonly float[] EncodeTable = BuildEncodeTable();

    private static float[] BuildEncodeTable()
    {
        float[] table = new float[EncodeSteps + 2];
        for (int i = 0; i < table.Length; i++)
        {
            table[i] = Encode(MathF.Min(1f, (float)i / EncodeSteps));
        }

        return table;
    }

    /// <summary>
    /// sRGB encoding from a table with linear interpolation, clamped to 0..1. Accurate to well under a
    /// thousandth of an 8-bit step, and several times faster than the power function: table building and
    /// fades encode hundreds of thousands of values at a time.
    /// </summary>
    public static float EncodeFast(float linear)
    {
        float scaled = Math.Clamp(linear, 0f, 1f) * EncodeSteps;
        int i = (int)scaled;
        float t = scaled - i;
        return EncodeTable[i] + ((EncodeTable[i + 1] - EncodeTable[i]) * t);
    }

    public static Vector3 EncodeFast(Vector3 linear) =>
        new(EncodeFast(linear.X), EncodeFast(linear.Y), EncodeFast(linear.Z));

    private static float[] BuildDecodeTable()
    {
        float[] table = new float[256];
        for (int i = 0; i < table.Length; i++)
        {
            table[i] = Decode(i / 255f);
        }

        return table;
    }

    public static float Decode(float encoded) =>
        encoded <= 0.04045f
            ? encoded / 12.92f
            : MathF.Pow((encoded + 0.055f) / 1.055f, 2.4f);

    public static float Encode(float linear) =>
        linear <= 0.0031308f
            ? linear * 12.92f
            : (1.055f * MathF.Pow(linear, 1f / 2.4f)) - 0.055f;

    public static Vector3 Decode(Vector3 encoded) =>
        new(Decode(encoded.X), Decode(encoded.Y), Decode(encoded.Z));

    public static Vector3 Encode(Vector3 linear) =>
        new(Encode(linear.X), Encode(linear.Y), Encode(linear.Z));

    public static Vector3 ToLinear(Rgb8 colour) =>
        new(DecodeTable[colour.R], DecodeTable[colour.G], DecodeTable[colour.B]);

    public static Vector3 ToEncoded(Rgb8 colour) =>
        new(colour.R / 255f, colour.G / 255f, colour.B / 255f);

    /// <summary>Clamps to the displayable range, encodes and rounds to 8 bits.</summary>
    public static Rgb8 FromLinear(Vector3 linear) =>
        FromEncoded(Encode(Vector3.Clamp(linear, Vector3.Zero, Vector3.One)));

    public static Rgb8 FromEncoded(Vector3 encoded) =>
        new(ToByte(encoded.X), ToByte(encoded.Y), ToByte(encoded.Z));

    public static byte ToByte(float encoded) =>
        (byte)Math.Clamp((int)MathF.Floor((encoded * 255f) + 0.5f), 0, 255);
}
