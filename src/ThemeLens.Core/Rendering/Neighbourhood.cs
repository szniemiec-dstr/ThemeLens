// SPDX-License-Identifier: GPL-3.0-or-later
using System.Numerics;
using ThemeLens.Core.Analysis;
using ThemeLens.Core.Colour;
using ThemeLens.Core.Imaging;

namespace ThemeLens.Core.Rendering;

/// <summary>
/// The "small averaged copies" of a frame (figure 7): shrunken levels that tell each pixel whether it lies in a
/// light region, in a dark region, or is content of whatever surrounds it. On the GPU these are a few tiny render
/// passes; this class is their CPU twin and follows the same arithmetic, including the rounding to 8 bits
/// between passes.
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>Fine: per 4×4 block, the share of clearly light and of clearly dark pixels.</item>
/// <item>Regions: per block, whether it belongs to a light or a dark region (<see cref="RegionMap"/>).</item>
/// <item>Coarse and wide: per 16 and per 64 pixels, which kind of region is nearby, for content far from any.</item>
/// </list>
/// </remarks>
public sealed class Neighbourhood
{
    private Neighbourhood(int sourceWidth, int sourceHeight)
    {
        SourceWidth = sourceWidth;
        SourceHeight = sourceHeight;
    }

    public int SourceWidth { get; }

    public int SourceHeight { get; }

    /// <summary>Quarter-size level: one texel per 4×4 block of source pixels.</summary>
    public int FineWidth { get; private init; }

    public int FineHeight { get; private init; }

    /// <summary>Share of the block's pixels that are clearly light.</summary>
    public byte[] FineLight { get; private init; } = [];

    /// <summary>Share of the block's pixels that are clearly dark. Mid-tones are neither.</summary>
    public byte[] FineDark { get; private init; } = [];

    /// <summary>Luma range (max − min) inside the block.</summary>
    public byte[] FineRange { get; private init; } = [];

    /// <summary>One untouched source pixel per block, RGB. Used for the analysis thumbnail.</summary>
    public byte[] FinePoint { get; private init; } = [];

    /// <summary>Quarter-size: which blocks belong to a light region, which to a dark one.</summary>
    public RegionMap Regions { get; private init; } = null!;

    /// <summary>Sixteenth-size level: one texel per 16×16 block of source pixels.</summary>
    public int CoarseWidth { get; private init; }

    public int CoarseHeight { get; private init; }

    /// <summary>The light part of each cell's verdict on the regions in it; see <see cref="LensConstants.Verdict"/>.</summary>
    public byte[] CoarseLight { get; private init; } = [];

    public byte[] CoarseDark { get; private init; } = [];

    /// <summary>Share of the cell made of "soft" blocks; see <see cref="LensConstants.SoftIndicator"/>.</summary>
    public byte[] CoarseSoft { get; private init; } = [];

    /// <summary>Sixty-fourth-size level: one texel per 64×64 block. Asked where the coarse level has no region in sight.</summary>
    public int WideWidth { get; private init; }

    public int WideHeight { get; private init; }

    public byte[] WideLight { get; private init; } = [];

    public byte[] WideDark { get; private init; } = [];

    public static Neighbourhood Build(ImageRgb8 source)
    {
        ArgumentNullException.ThrowIfNull(source);
        const int block = LensConstants.BlockSize;
        int w = source.Width;
        int h = source.Height;
        int fw = (w + block - 1) / block;
        int fh = (h + block - 1) / block;

        // Fine: what each block is made of.
        byte[] fineLight = new byte[fw * fh];
        byte[] fineDark = new byte[fw * fh];
        byte[] fineRange = new byte[fw * fh];
        byte[] finePoint = new byte[fw * fh * 3];
        Parallel.For(0, fh, j =>
        {
            for (int i = 0; i < fw; i++)
            {
                float light = 0;
                float dark = 0;
                float lo = float.MaxValue;
                float hi = float.MinValue;
                for (int dy = 0; dy < block; dy++)
                {
                    for (int dx = 0; dx < block; dx++)
                    {
                        Rgb8 p = source.Clamped((i * block) + dx, (j * block) + dy);
                        float luminance = Wcag.Luminance(p);
                        light += LensConstants.StrongLight(luminance);
                        dark += LensConstants.StrongDark(luminance);
                        float luma = LensConstants.Luma(Srgb.ToEncoded(p));
                        lo = MathF.Min(lo, luma);
                        hi = MathF.Max(hi, luma);
                    }
                }

                int index = (j * fw) + i;
                fineLight[index] = Srgb.ToByte(light / (block * block));
                fineDark[index] = Srgb.ToByte(dark / (block * block));
                fineRange[index] = Srgb.ToByte(hi - lo);
                Rgb8 centre = source.Clamped((i * block) + (block / 2), (j * block) + (block / 2));
                finePoint[index * 3] = centre.R;
                finePoint[(index * 3) + 1] = centre.G;
                finePoint[(index * 3) + 2] = centre.B;
            }
        });

        RegionMap regions = RegionMap.Build(fineLight, fineDark, fw, fh, LensConstants.CoreRadius);
        byte[] regionLight = regions.HomeLight;
        byte[] regionDark = regions.HomeDark;

        // Coarse and wide: which kind of region is nearby.
        int cw = (fw + block - 1) / block;
        int ch = (fh + block - 1) / block;
        byte[] coarseLight = new byte[cw * ch];
        byte[] coarseDark = new byte[cw * ch];
        byte[] coarseSoft = new byte[cw * ch];
        for (int j = 0; j < ch; j++)
        {
            for (int i = 0; i < cw; i++)
            {
                float light = 0;
                float dark = 0;
                float soft = 0;
                for (int dy = 0; dy < block; dy++)
                {
                    for (int dx = 0; dx < block; dx++)
                    {
                        int fx = Math.Min((i * block) + dx, fw - 1);
                        int fy = Math.Min((j * block) + dy, fh - 1);
                        int fi = (fy * fw) + fx;
                        light += regionLight[fi] / 255f;
                        dark += regionDark[fi] / 255f;
                        soft += LensConstants.SoftIndicator(fineRange[fi] / 255f);
                    }
                }

                (float lightVerdict, float darkVerdict) = LensConstants.Verdict(light / (block * block), dark / (block * block));
                coarseLight[(j * cw) + i] = Srgb.ToByte(lightVerdict);
                coarseDark[(j * cw) + i] = Srgb.ToByte(darkVerdict);
                coarseSoft[(j * cw) + i] = Srgb.ToByte(soft / (block * block));
            }
        }

        int ww = (cw + block - 1) / block;
        int wh = (ch + block - 1) / block;
        byte[] wideLight = new byte[ww * wh];
        byte[] wideDark = new byte[ww * wh];
        for (int j = 0; j < wh; j++)
        {
            for (int i = 0; i < ww; i++)
            {
                float light = 0;
                float dark = 0;
                for (int dy = 0; dy < block; dy++)
                {
                    for (int dx = 0; dx < block; dx++)
                    {
                        int ci = (Math.Min((j * block) + dy, ch - 1) * cw) + Math.Min((i * block) + dx, cw - 1);
                        light += coarseLight[ci] / 255f;
                        dark += coarseDark[ci] / 255f;
                    }
                }

                (float lightVerdict, float darkVerdict) = LensConstants.Verdict(light / (block * block), dark / (block * block));
                wideLight[(j * ww) + i] = Srgb.ToByte(lightVerdict);
                wideDark[(j * ww) + i] = Srgb.ToByte(darkVerdict);
            }
        }

        return new Neighbourhood(w, h)
        {
            FineWidth = fw,
            FineHeight = fh,
            FineLight = fineLight,
            FineDark = fineDark,
            FineRange = fineRange,
            FinePoint = finePoint,
            Regions = regions,
            CoarseWidth = cw,
            CoarseHeight = ch,
            CoarseLight = coarseLight,
            CoarseDark = coarseDark,
            CoarseSoft = coarseSoft,
            WideWidth = ww,
            WideHeight = wh,
            WideLight = wideLight,
            WideDark = wideDark,
        };
    }

    /// <summary>
    /// The table a source pixel is recoloured with: 1 for the light-region table, 0 for the dark-region one;
    /// see <see cref="LensConstants.Polarity"/>.
    /// </summary>
    public float PolarityAt(int x, int y, float wholeLens, float luminance)
    {
        int block = (Math.Min(y / LensConstants.BlockSize, FineHeight - 1) * FineWidth) + Math.Min(x / LensConstants.BlockSize, FineWidth - 1);
        RegionMap r = Regions;
        float fraction = LensConstants.LightFraction(
            wholeLens,
            Bilinear.Sample(WideLight, WideWidth, WideHeight, 1, 0, x, y, LensConstants.WideScale),
            Bilinear.Sample(WideDark, WideWidth, WideHeight, 1, 0, x, y, LensConstants.WideScale),
            Bilinear.Sample(CoarseLight, CoarseWidth, CoarseHeight, 1, 0, x, y, LensConstants.CoarseScale),
            Bilinear.Sample(CoarseDark, CoarseWidth, CoarseHeight, 1, 0, x, y, LensConstants.CoarseScale),
            r.LocalLight[block] / 255f,
            r.LocalDark[block] / 255f);
        return LensConstants.Polarity(
            LensConstants.Surroundings(fraction, luminance),
            r.HomeLight[block] / 255f,
            r.HomeDark[block] / 255f,
            r.EdgeLight[block] / 255f,
            r.EdgeDark[block] / 255f,
            luminance);
    }

    /// <summary>
    /// How much of the lens's work the pixel gets with the "rectangles only" option; see
    /// <see cref="LensConstants.Change"/>. <paramref name="polarity"/> is what <see cref="PolarityAt"/> gave
    /// for it, <paramref name="luma"/> the Rec. 709 luma of its encoded colour.
    /// </summary>
    public static float ChangeAt(int x, int y, float polarity, float luma, Mapping.FrameParameters p)
    {
        float kind = LensConstants.RectangleKind(p.RectangleTarget, polarity);
        float selected = 0f;
        if (p.RectanglesOnly > 0f && p.Mask is { IsEmpty: false } mask)
        {
            selected = mask.Selected(x, y, LensConstants.RectangleStrength(p.RectangleTarget, luma), out bool askRegions);
            if (askRegions)
            {
                // A dark button on the white page, with whatever is written on it, stays as it is.
                selected *= kind;
            }
        }

        return LensConstants.Change(p.RectanglesOnly, p.RectangleBaseline, kind, selected);
    }

    public float SoftFractionAt(int x, int y) =>
        Bilinear.Sample(CoarseSoft, CoarseWidth, CoarseHeight, 1, 0, x, y, LensConstants.CoarseScale);

    /// <summary>
    /// The picture handed to the analysis: untouched source pixels on a regular grid, about 160 by 90 of them
    /// (section 6.1). Sampling keeps true colours, which averaging would smear into the page colour.
    /// </summary>
    /// <param name="withDetail">True to add the picture the "rectangles only" option needs; see <see cref="Thumbnail.Detail"/>.</param>
    public Thumbnail ToThumbnail(bool withDetail = false)
    {
        Thumbnail? detail = withDetail ? Sample(Thumbnail.DetailStrideFor(FineWidth, FineHeight), null) : null;
        return Sample(Thumbnail.StrideFor(FineWidth, FineHeight), detail);
    }

    private Thumbnail Sample(int stride, Thumbnail? detail)
    {
        int tw = (FineWidth + stride - 1) / stride;
        int th = (FineHeight + stride - 1) / stride;
        var pixels = new Rgb8[tw * th];
        byte[] range = new byte[tw * th];
        for (int j = 0; j < th; j++)
        {
            for (int i = 0; i < tw; i++)
            {
                int fx = Math.Min((i * stride) + (stride / 2), FineWidth - 1);
                int fy = Math.Min((j * stride) + (stride / 2), FineHeight - 1);
                int fi = (fy * FineWidth) + fx;
                pixels[(j * tw) + i] = new Rgb8(FinePoint[fi * 3], FinePoint[(fi * 3) + 1], FinePoint[(fi * 3) + 2]);
                range[(j * tw) + i] = FineRange[fi];
            }
        }

        return new Thumbnail(tw, th, pixels, range, stride * LensConstants.BlockSize) { Detail = detail };
    }
}

/// <summary>Bilinear sampling with clamp addressing, written to match a GPU sampler texel for texel.</summary>
public static class Bilinear
{
    /// <summary>
    /// Samples one channel of a byte texture at the centre of source pixel (<paramref name="x"/>, <paramref name="y"/>),
    /// where each texel covers <paramref name="scale"/> source pixels. Returns 0..1.
    /// </summary>
    public static float Sample(byte[] texture, int width, int height, int channels, int channel, float x, float y, int scale)
    {
        float u = ((x + 0.5f) / scale) - 0.5f;
        float v = ((y + 0.5f) / scale) - 0.5f;
        int x0 = (int)MathF.Floor(u);
        int y0 = (int)MathF.Floor(v);
        float fx = u - x0;
        float fy = v - y0;
        int xa = Math.Clamp(x0, 0, width - 1);
        int xb = Math.Clamp(x0 + 1, 0, width - 1);
        int ya = Math.Clamp(y0, 0, height - 1);
        int yb = Math.Clamp(y0 + 1, 0, height - 1);

        float p00 = texture[(((ya * width) + xa) * channels) + channel];
        float p10 = texture[(((ya * width) + xb) * channels) + channel];
        float p01 = texture[(((yb * width) + xa) * channels) + channel];
        float p11 = texture[(((yb * width) + xb) * channels) + channel];
        float top = p00 + ((p10 - p00) * fx);
        float bottom = p01 + ((p11 - p01) * fx);
        return (top + ((bottom - top) * fy)) / 255f;
    }

    public static Vector3 SampleRgb(byte[] texture, int width, int height, int channels, float x, float y, int scale) =>
        new(
            Sample(texture, width, height, channels, 0, x, y, scale),
            Sample(texture, width, height, channels, 1, x, y, scale),
            Sample(texture, width, height, channels, 2, x, y, scale));
}
