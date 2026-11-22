// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace ThemeLens.Core.Colour;

/// <summary>An sRGB colour with 8 bits per channel, as it is stored in a frame.</summary>
public readonly record struct Rgb8(byte R, byte G, byte B)
{
    public static readonly Rgb8 Black = new(0, 0, 0);
    public static readonly Rgb8 White = new(255, 255, 255);

    /// <summary>Parses <c>#RRGGBB</c> (the leading hash is required).</summary>
    public static Rgb8 FromHex(string hex) =>
        TryParseHex(hex, out Rgb8 value)
            ? value
            : throw new FormatException($"'{hex}' is not a colour in the form #RRGGBB.");

    public static bool TryParseHex([NotNullWhen(true)] string? hex, out Rgb8 value)
    {
        value = default;
        if (hex is null || hex.Length != 7 || hex[0] != '#')
        {
            return false;
        }

        if (!uint.TryParse(hex.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint rgb))
        {
            return false;
        }

        value = new Rgb8((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    public string ToHex() => string.Create(CultureInfo.InvariantCulture, $"#{R:X2}{G:X2}{B:X2}");

    public override string ToString() => ToHex();

    /// <summary>Largest per-channel difference to another colour, in 8-bit steps.</summary>
    public int MaxChannelDifference(Rgb8 other) =>
        Math.Max(Math.Abs(R - other.R), Math.Max(Math.Abs(G - other.G), Math.Abs(B - other.B)));
}
