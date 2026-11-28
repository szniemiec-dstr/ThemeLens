// SPDX-License-Identifier: GPL-3.0-or-later
using System.Numerics;
using ThemeLens.Core.Colour;
using ThemeLens.Core.Imaging;
using ThemeLens.Core.Mapping;

namespace ThemeLens.Core.Rendering;

/// <summary>
/// The lens on the CPU. This is the definition of what the lens shows: the GPU shader (<c>lens.hlsl</c>) has to
/// produce the same pixels within a small tolerance, and platform tests hold it to that. It is written pass by
/// pass, in the same order and with the same arithmetic as the shader, and it is not meant to be fast.
/// </summary>
/// <remarks>
/// Passes, in frame order (figure 7):
/// <list type="number">
/// <item>Neighbourhood: three shrunken copies of the source that say, for each pixel, whether its surroundings are light or dark.</item>
/// <item>Recolour: one table lookup per pixel. The polarity check chooses between the table for light regions and the one for dark regions.</item>
/// <item>Two shrunken copies of the recoloured picture and, for the guard, a map of where flagged pixels form an area.</item>
/// <item>Finish: area guard, local contrast, sharpen, then clamp to the theme's limits.</item>
/// </list>
/// When nothing in step 4 is switched on, step 2 writes the final picture and steps 3 and 4 are skipped.
/// With the "rectangles only" option on (section 5.6), every pixel outside the found rectangles leaves the
/// last step exactly as it came in.
/// </remarks>
public static class ReferenceRenderer
{
    private const int Fine = LensConstants.BlockSize;
    private const int Coarse = LensConstants.CoarseScale;

    public static ImageRgb8 Render(ImageRgb8 source, LensTables tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        return Render(source, tables.Light, tables.Dark, tables.Parameters);
    }

    public static ImageRgb8 Render(ImageRgb8 source, Lut3D light, Lut3D dark, FrameParameters p)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(light);
        ArgumentNullException.ThrowIfNull(dark);
        ArgumentNullException.ThrowIfNull(p);

        if (p.Identity)
        {
            return source.Clone();
        }

        int w = source.Width;
        int h = source.Height;
        Neighbourhood around = Neighbourhood.Build(source);
        bool oneTable = ReferenceEquals(light, dark);
        bool finish = p.NeedsFinishPass;
        bool only = p.SelectsPixels;

        // Pass 2: recolour. RGB is the recoloured pixel, A flags pixels that differ from the page by more than an area may.
        // With "rectangles only" on, a second output says how much of the lens's work each pixel gets.
        byte[] mapped = new byte[w * h * 4];
        byte[] change = only && finish ? new byte[w * h] : [];
        var result = new ImageRgb8(w, h);
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                Rgb8 pixel = source[x, y];
                Vector3 encoded = Srgb.ToEncoded(pixel);
                Vector3 o = light.Sample(encoded);
                float luminance = Wcag.Luminance(pixel);
                float polarity = 1f;
                if (!oneTable || only)
                {
                    polarity = around.PolarityAt(x, y, p.WholeLensLightFraction, luminance);
                }

                if (!oneTable)
                {
                    o = Vector3.Lerp(dark.Sample(encoded), o, polarity);
                }

                // Outside the found rectangles the pixel stays what it was.
                float share = only ? Neighbourhood.ChangeAt(x, y, polarity, LensConstants.Luma(encoded), p) : 1f;
                if (!finish)
                {
                    result[x, y] = Srgb.FromEncoded(Vector3.Lerp(encoded, FinishMath.Clamp(o, p), share));
                    continue;
                }

                int i = ((y * w) + x) * 4;
                mapped[i] = Srgb.ToByte(o.X);
                mapped[i + 1] = Srgb.ToByte(o.Y);
                mapped[i + 2] = Srgb.ToByte(o.Z);
                mapped[i + 3] = Srgb.ToByte(AreaFlag(o, p) * share);
                if (only)
                {
                    change[(y * w) + x] = Srgb.ToByte(share);
                }
            }
        });

        if (!finish)
        {
            return result;
        }

        // Pass 3: the recoloured picture at a quarter and a sixteenth of its size.
        byte[] mappedFine = Shrink(mapped, w, h, out int fw, out int fh);
        byte[] mappedCoarse = Shrink(mappedFine, fw, fh, out int cw, out int ch);

        // For the guard: where the flagged pixels form an area. An area is found the way a region is, with the
        // flagged pixels as one kind and all others as the other.
        RegionMap? areas = null;
        if (p.GuardOn)
        {
            byte[] flagged = new byte[fw * fh];
            byte[] clear = new byte[fw * fh];
            for (int i = 0; i < flagged.Length; i++)
            {
                flagged[i] = mappedFine[(i * 4) + 3];
                clear[i] = (byte)(255 - flagged[i]);
            }

            areas = RegionMap.Build(flagged, clear, fw, fh, LensConstants.CoreRadius);
        }

        // Strong sharpen needs a wider blur; its horizontal half is a pass of its own.
        float[]? blurredRows = p.StrongSharpen > 0f ? BlurRows(mapped, w, h) : null;

        // Pass 4: finish.
        float detailRange = MathF.Max(MathF.Abs(LensConstants.Luma(p.TextEncoded) - LensConstants.Luma(p.PageEncoded)), 0.1f);
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                Vector3 o = Texel(mapped, w, h, x, y);
                float luma = LensConstants.Luma(o);
                Vector3 colour = o;
                float guard = 0f;

                if (areas is not null)
                {
                    // A block that belongs to an area is guarded whole, text and all. A block that only touches
                    // one gives it its flagged pixels: that is the rim of the area.
                    int block = (Math.Min(y / Fine, fh - 1) * fw) + Math.Min(x / Fine, fw - 1);
                    float flag = mapped[(((y * w) + x) * 4) + 3] / 255f;
                    guard = p.GuardStrength * MathF.Max(areas.HomeLight[block] / 255f, areas.EdgeLight[block] / 255f * flag);
                    if (p.GuardSoftGate > 0f)
                    {
                        float soft = Smooth.Step(LensConstants.SoftGateLow, LensConstants.SoftGateHigh, around.SoftFractionAt(x, y));
                        guard *= 1f - (p.GuardSoftGate * soft);
                    }

                    // Inside a guarded area the area itself becomes the page colour; whatever stands out from its
                    // immediate surroundings (text, lines) is redrawn towards the text colour, so it stays readable.
                    Vector3 nearby = Bilinear.SampleRgb(mappedFine, fw, fh, 4, x, y, Fine);
                    float detail = Smooth.Saturate(LensConstants.GuardDetailGain * MathF.Abs(luma - LensConstants.Luma(nearby)) / detailRange);
                    Vector3 redrawn = Vector3.Lerp(p.PageEncoded, p.TextEncoded, detail);
                    colour = Vector3.Lerp(o, redrawn, guard);
                }

                float delta = 0f;
                if (p.LocalContrast > 0f)
                {
                    Vector3 blurred = Bilinear.SampleRgb(mappedCoarse, cw, ch, 4, x, y, Coarse);
                    delta += LensConstants.LocalContrastGain * p.LocalContrast * (luma - LensConstants.Luma(blurred));
                }

                if (p.Sharpen > 0f || p.StrongSharpen > 0f)
                {
                    float north = LensConstants.Luma(Texel(mapped, w, h, x, y - 1));
                    float south = LensConstants.Luma(Texel(mapped, w, h, x, y + 1));
                    float west = LensConstants.Luma(Texel(mapped, w, h, x - 1, y));
                    float east = LensConstants.Luma(Texel(mapped, w, h, x + 1, y));
                    float lo = MathF.Min(luma, MathF.Min(MathF.Min(north, south), MathF.Min(west, east)));
                    float hi = MathF.Max(luma, MathF.Max(MathF.Max(north, south), MathF.Max(west, east)));

                    if (p.Sharpen > 0f)
                    {
                        // Five samples, on lightness only. Strength falls where the edge is already crisp.
                        float crisp = Smooth.Step(0.35f, 0.85f, hi - lo);
                        float strength = LensConstants.SharpenGain * p.Sharpen * Smooth.Lerp(1f, 0.4f, crisp);
                        delta += strength * (luma - ((north + south + west + east) * 0.25f));
                    }

                    if (blurredRows is not null)
                    {
                        float blurred = 0f;
                        for (int k = -4; k <= 4; k++)
                        {
                            blurred += LensConstants.StrongKernel[Math.Abs(k)] * blurredRows[(Math.Clamp(y + k, 0, h - 1) * w) + x];
                        }

                        // Halo limiting: the result may leave the local range only by a small allowance.
                        float raw = luma + (LensConstants.StrongSharpenGain * p.StrongSharpen * (luma - blurred));
                        float limited = Math.Clamp(raw, lo - LensConstants.HaloAllowance, hi + LensConstants.HaloAllowance);
                        delta += limited - luma;
                    }
                }

                // Contrast and sharpen add to lightness only, so no colour fringes appear; they stay out of guarded areas.
                colour += new Vector3(delta * (1f - guard));

                // Clamp last. This is what guarantees that a sharpening halo can never exceed the brightness limit.
                Vector3 finished = FinishMath.Clamp(colour, p);
                if (only)
                {
                    finished = Vector3.Lerp(Srgb.ToEncoded(source[x, y]), finished, change[(y * w) + x] / 255f);
                }

                result[x, y] = Srgb.FromEncoded(finished);
            }
        });

        return result;
    }

    /// <summary>
    /// 1 where a recoloured pixel differs from the page by more than an area is allowed to, 0 where it is fine.
    /// A single flagged pixel is harmless (text is flagged all the time); the guard acts on regions of them.
    /// </summary>
    public static float AreaFlag(Vector3 encoded, FrameParameters p)
    {
        if (!p.GuardOn)
        {
            return 0f;
        }

        float y = Wcag.Luminance(Srgb.Decode(encoded));
        float contrast = p.Direction > 0f
            ? (y + Wcag.Flare) / (p.PageLuminance + Wcag.Flare)
            : (p.PageLuminance + Wcag.Flare) / (y + Wcag.Flare);
        return Smooth.Step(LensConstants.AreaFlagStart * p.MaxAreaContrast, p.MaxAreaContrast, contrast);
    }

    private static Vector3 Texel(byte[] rgba, int w, int h, int x, int y)
    {
        int i = ((Math.Clamp(y, 0, h - 1) * w) + Math.Clamp(x, 0, w - 1)) * 4;
        return new Vector3(rgba[i], rgba[i + 1], rgba[i + 2]) / 255f;
    }

    /// <summary>Averages 4×4 blocks of an RGBA texture, with clamp addressing at the right and bottom edges.</summary>
    private static byte[] Shrink(byte[] rgba, int w, int h, out int outWidth, out int outHeight)
    {
        int ow = (w + Fine - 1) / Fine;
        int oh = (h + Fine - 1) / Fine;
        byte[] output = new byte[ow * oh * 4];
        Parallel.For(0, oh, j =>
        {
            Span<float> sum = stackalloc float[4];
            for (int i = 0; i < ow; i++)
            {
                sum.Clear();
                for (int dy = 0; dy < Fine; dy++)
                {
                    int sy = Math.Min((j * Fine) + dy, h - 1);
                    for (int dx = 0; dx < Fine; dx++)
                    {
                        int sx = Math.Min((i * Fine) + dx, w - 1);
                        int s = ((sy * w) + sx) * 4;
                        sum[0] += rgba[s];
                        sum[1] += rgba[s + 1];
                        sum[2] += rgba[s + 2];
                        sum[3] += rgba[s + 3];
                    }
                }

                int o = ((j * ow) + i) * 4;
                for (int c = 0; c < 4; c++)
                {
                    output[o + c] = Srgb.ToByte(sum[c] / (Fine * Fine) / 255f);
                }
            }
        });

        outWidth = ow;
        outHeight = oh;
        return output;
    }

    private static float[] BlurRows(byte[] mapped, int w, int h)
    {
        float[] rows = new float[w * h];
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                float sum = 0f;
                for (int k = -4; k <= 4; k++)
                {
                    sum += LensConstants.StrongKernel[Math.Abs(k)] * LensConstants.Luma(Texel(mapped, w, h, x + k, y));
                }

                rows[(y * w) + x] = sum;
            }
        });
        return rows;
    }
}
