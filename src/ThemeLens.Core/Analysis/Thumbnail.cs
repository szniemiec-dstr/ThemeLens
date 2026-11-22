// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Core.Colour;

namespace ThemeLens.Core.Analysis;

/// <summary>
/// What the analysis sees: a regular grid of untouched pixels from the lens area, each with the luma range of
/// the 4×4 block it came from (large for text and edges, zero for flat areas).
/// </summary>
public sealed class Thumbnail
{
    /// <summary>The analysis aims for about this many samples (160 × 90).</summary>
    public const int TargetSamples = 14400;

    public Thumbnail(int width, int height, Rgb8[] pixels, byte[] range, int sourcePixelsPerSample)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentNullException.ThrowIfNull(range);
        if (pixels.Length != width * height || range.Length != pixels.Length)
        {
            throw new ArgumentException("Pixel and range buffers must each hold width × height entries.");
        }

        Width = width;
        Height = height;
        Pixels = pixels;
        Range = range;
        SourcePixelsPerSample = sourcePixelsPerSample;
    }

    public int Width { get; }

    public int Height { get; }

    public Rgb8[] Pixels { get; }

    public byte[] Range { get; }

    /// <summary>Distance between neighbouring samples, in lens pixels.</summary>
    public int SourcePixelsPerSample { get; }

    /// <summary>How many quarter-size texels to step over so the thumbnail lands near <see cref="TargetSamples"/>.</summary>
    public static int StrideFor(int fineWidth, int fineHeight) =>
        Math.Max(1, (int)MathF.Round(MathF.Sqrt((float)fineWidth * fineHeight / TargetSamples)));
}
