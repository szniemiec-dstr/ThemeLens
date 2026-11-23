// SPDX-License-Identifier: GPL-3.0-or-later
using System.Numerics;
using ThemeLens.Core.Analysis;
using ThemeLens.Core.Colour;
using ThemeLens.Core.Imaging;

namespace ThemeLens.Core.Rendering;

/// <summary>
/// The "small averaged copies" of a frame (figure 7): two shrunken levels that tell each pixel what its
/// surroundings look like. On the GPU these are two tiny render passes; this class is their CPU twin and
/// follows the same arithmetic, including the rounding to 8 bits between passes.
/// </summary>
public sealed class Neighbourhood
{
    private Neighbourhood(
        int sourceWidth, int sourceHeight,
        int fineWidth, int fineHeight, byte[] fineLight, byte[] fineDark, byte[] fineRange, byte[] finePoint,
        int coarseWidth, int coarseHeight, byte[] coarseLight, byte[] coarseDark, byte[] coarseSoft,
        int wideWidth, int wideHeight, byte[] wideLight, byte[] wideDark)
    {
        SourceWidth = sourceWidth;
        SourceHeight = sourceHeight;
        FineWidth = fineWidth;
        FineHeight = fineHeight;
        FineLight = fineLight;
        FineDark = fineDark;
        FineRange = fineRange;
        FinePoint = finePoint;
        CoarseWidth = coarseWidth;
        CoarseHeight = coarseHeight;
        CoarseLight = coarseLight;
        CoarseDark = coarseDark;
        CoarseSoft = coarseSoft;
        WideWidth = wideWidth;
        WideHeight = wideHeight;
        WideLight = wideLight;
        WideDark = wideDark;
    }

    public int SourceWidth { get; }

    public int SourceHeight { get; }

    /// <summary>Quarter-size level: one texel per 4×4 block of source pixels.</summary>
    public int FineWidth { get; }

    public int FineHeight { get; }

    /// <summary>Share of the block's pixels that vote light.</summary>
    public byte[] FineLight { get; }

    /// <summary>Share of the block's pixels that vote dark. Mid-tones vote neither way.</summary>
    public byte[] FineDark { get; }

    /// <summary>Luma range (max − min) inside the block.</summary>
    public byte[] FineRange { get; }

    /// <summary>One untouched source pixel per block, RGB. Used for the analysis thumbnail.</summary>
    public byte[] FinePoint { get; }

    /// <summary>Sixteenth-size level: one texel per 16×16 block of source pixels.</summary>
    public int CoarseWidth { get; }

    public int CoarseHeight { get; }

    /// <summary>The light part of each cell's verdict; see <see cref="LensConstants.Verdict"/>.</summary>
    public byte[] CoarseLight { get; }

    public byte[] CoarseDark { get; }

    /// <summary>Share of the region made of "soft" blocks; see <see cref="LensConstants.SoftIndicator"/>.</summary>
    public byte[] CoarseSoft { get; }

    /// <summary>Sixty-fourth-size level: one texel per 64×64 block. Asked where the coarse level has too few votes.</summary>
    public int WideWidth { get; }

    public int WideHeight { get; }

    public byte[] WideLight { get; }

    public byte[] WideDark { get; }

    public static Neighbourhood Build(ImageRgb8 source)
    {
        ArgumentNullException.ThrowIfNull(source);
        const int block = LensConstants.BlockSize;
        int w = source.Width;
        int h = source.Height;
        int fw = (w + block - 1) / block;
        int fh = (h + block - 1) / block;

        byte[] fineLight = new byte[fw * fh];
        byte[] fineDark = new byte[fw * fh];
        byte[] fineRange = new byte[fw * fh];
        byte[] finePoint = new byte[fw * fh * 3];

        for (int j = 0; j < fh; j++)
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
                        light += LensConstants.LightVote(luminance);
                        dark += LensConstants.DarkVote(luminance);
                        float luma = LensConstants.Luma(Srgb.ToEncoded(p));
                        lo = MathF.Min(lo, luma);
                        hi = MathF.Max(hi, luma);
                    }
                }

                // Only flat blocks vote on what the background is.
                int index = (j * fw) + i;
                float flat = LensConstants.Flatness(hi - lo);
                fineLight[index] = Srgb.ToByte(flat * light / (block * block));
                fineDark[index] = Srgb.ToByte(flat * dark / (block * block));
                fineRange[index] = Srgb.ToByte(hi - lo);
                Rgb8 centre = source.Clamped((i * block) + (block / 2), (j * block) + (block / 2));
                finePoint[index * 3] = centre.R;
                finePoint[(index * 3) + 1] = centre.G;
                finePoint[(index * 3) + 2] = centre.B;
            }
        }

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
                        light += fineLight[fi] / 255f;
                        dark += fineDark[fi] / 255f;
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

        return new Neighbourhood(
            w, h,
            fw, fh, fineLight, fineDark, fineRange, finePoint,
            cw, ch, coarseLight, coarseDark, coarseSoft,
            ww, wh, wideLight, wideDark);
    }

    /// <summary>
    /// Share of the light votes that decides the region of a source pixel; see <see cref="LensConstants.LightFraction"/>.
    /// </summary>
    public float LightFractionAt(int x, int y, float wholeLens) =>
        LensConstants.LightFraction(
            wholeLens,
            Bilinear.Sample(WideLight, WideWidth, WideHeight, 1, 0, x, y, LensConstants.WideScale),
            Bilinear.Sample(WideDark, WideWidth, WideHeight, 1, 0, x, y, LensConstants.WideScale),
            Bilinear.Sample(CoarseLight, CoarseWidth, CoarseHeight, 1, 0, x, y, LensConstants.CoarseScale),
            Bilinear.Sample(CoarseDark, CoarseWidth, CoarseHeight, 1, 0, x, y, LensConstants.CoarseScale));

    public float SoftFractionAt(int x, int y) =>
        Bilinear.Sample(CoarseSoft, CoarseWidth, CoarseHeight, 1, 0, x, y, LensConstants.CoarseScale);

    /// <summary>
    /// The picture handed to the analysis: untouched source pixels on a regular grid, about 160 by 90 of them
    /// (section 6.1). Sampling keeps true colours, which averaging would smear into the page colour.
    /// </summary>
    public Thumbnail ToThumbnail()
    {
        int stride = Thumbnail.StrideFor(FineWidth, FineHeight);
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

        return new Thumbnail(tw, th, pixels, range, stride * LensConstants.BlockSize);
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
