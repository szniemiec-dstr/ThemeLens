// SPDX-License-Identifier: GPL-3.0-or-later
using System.Numerics;
using ThemeLens.Core.Colour;
using ThemeLens.Core.Imaging;
using ThemeLens.Core.Mapping;

namespace ThemeLens.Core.Measurement;

/// <summary>
/// The measurements the executable specifications are written in (section 9.2). They live in Core so that the
/// same definitions can back a self-check in the app.
/// </summary>
public static class Measure
{
    /// <summary>
    /// An "area" is something at least this many pixels across in both directions. Narrower shapes are lines and
    /// text, which are allowed to differ from the page; that is the whole point of text. The number is the
    /// smallest square that fills the lens's own 20-pixel window to more than LensConstants.CoreShareHigh.
    /// </summary>
    public const int AreaMinimumWidth = 18;

    /// <summary>
    /// Largest connected area that differs from the page by more than the theme allows for an area, as a share
    /// of lens pixels. On a dark base this is the largest bright area; on a light base, the largest dark one.
    /// </summary>
    public static float LargestAreaShare(ImageRgb8 image, FrameParameters p, int minimumWidth = AreaMinimumWidth)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(p);
        if (p.Direction == 0f)
        {
            return 0f;
        }

        int w = image.Width;
        int h = image.Height;
        bool[] beyond = new bool[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float lum = Wcag.Luminance(image[x, y]);
                float contrast = p.Direction > 0f
                    ? (lum + Wcag.Flare) / (p.PageLuminance + Wcag.Flare)
                    : (p.PageLuminance + Wcag.Flare) / (lum + Wcag.Flare);
                beyond[(y * w) + x] = contrast > p.MaxAreaContrast;
            }
        }

        // Keep only the cores of shapes that are wide enough to be areas (an erosion with a square window).
        int[] sat = SummedArea(beyond, w, h);
        int before = (minimumWidth - 1) / 2;
        int after = minimumWidth / 2;
        bool[] core = new bool[w * h];
        for (int y = before; y < h - after; y++)
        {
            for (int x = before; x < w - after; x++)
            {
                core[(y * w) + x] = Sum(sat, w, x - before, y - before, x + after + 1, y + after + 1) == minimumWidth * minimumWidth;
            }
        }

        // Label the cores, then count each area at its full size again (core grown back by the same window).
        int[] label = new int[w * h];
        var stack = new Stack<int>();
        int best = 0;
        int next = 0;
        for (int start = 0; start < core.Length; start++)
        {
            if (!core[start] || label[start] != 0)
            {
                continue;
            }

            next++;
            label[start] = next;
            stack.Push(start);
            var member = new List<int>();
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                member.Add(i);
                int x = i % w;
                int y = i / w;
                Visit(x - 1, y);
                Visit(x + 1, y);
                Visit(x, y - 1);
                Visit(x, y + 1);
            }

            bool[] grown = new bool[w * h];
            foreach (int i in member)
            {
                int cx = i % w;
                int cy = i / w;
                for (int y = cy - before; y <= cy + after; y++)
                {
                    for (int x = cx - before; x <= cx + after; x++)
                    {
                        grown[(y * w) + x] = true;
                    }
                }
            }

            int size = 0;
            for (int i = 0; i < grown.Length; i++)
            {
                if (grown[i] && beyond[i])
                {
                    size++;
                }
            }

            best = Math.Max(best, size);

            void Visit(int x, int y)
            {
                if (x < 0 || y < 0 || x >= w || y >= h)
                {
                    return;
                }

                int j = (y * w) + x;
                if (core[j] && label[j] == 0)
                {
                    label[j] = next;
                    stack.Push(j);
                }
            }
        }

        return (float)best / (w * h);
    }

    public static float MeanLuminance(ImageRgb8 image)
    {
        ArgumentNullException.ThrowIfNull(image);
        double sum = 0;
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                sum += Wcag.Luminance(image[x, y]);
            }
        }

        return (float)(sum / image.PixelCount);
    }

    public static float MaxLuminance(ImageRgb8 image)
    {
        ArgumentNullException.ThrowIfNull(image);
        float max = 0;
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                max = MathF.Max(max, Wcag.Luminance(image[x, y]));
            }
        }

        return max;
    }

    /// <summary>The blue channel's share of total linear-light output.</summary>
    public static float BlueShare(ImageRgb8 image)
    {
        ArgumentNullException.ThrowIfNull(image);
        double r = 0;
        double g = 0;
        double b = 0;
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                Vector3 linear = Srgb.ToLinear(image[x, y]);
                r += linear.X;
                g += linear.Y;
                b += linear.Z;
            }
        }

        double total = r + g + b;
        return total <= 0 ? 0f : (float)(b / total);
    }

    /// <summary>
    /// Mean lightness step across edges in <paramref name="output"/>. An edge is a pair of neighbouring pixels
    /// that differ clearly in <paramref name="source"/>, so two renderings of the same frame are compared at the same places.
    /// </summary>
    public static float EdgeStrength(ImageRgb8 output, ImageRgb8 source)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(source);
        double sum = 0;
        long count = 0;
        for (int y = 0; y < source.Height - 1; y++)
        {
            for (int x = 0; x < source.Width - 1; x++)
            {
                float s = Luma(source[x, y]);
                float o = Luma(output[x, y]);
                if (MathF.Abs(s - Luma(source[x + 1, y])) > 0.1f)
                {
                    sum += MathF.Abs(o - Luma(output[x + 1, y]));
                    count++;
                }

                if (MathF.Abs(s - Luma(source[x, y + 1])) > 0.1f)
                {
                    sum += MathF.Abs(o - Luma(output[x, y + 1]));
                    count++;
                }
            }
        }

        return count == 0 ? 0f : (float)(sum / count);
    }

    /// <summary>
    /// Median luminance of the output at the pixels where the source has exactly the given colour: what a flat
    /// patch of that colour really came out as, after every pass.
    /// </summary>
    public static float? MedianLuminanceWhere(ImageRgb8 output, ImageRgb8 source, Rgb8 sourceColour)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(source);
        var values = new List<float>();
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                if (source[x, y] == sourceColour)
                {
                    values.Add(Wcag.Luminance(output[x, y]));
                }
            }
        }

        if (values.Count == 0)
        {
            return null;
        }

        values.Sort();
        return values[values.Count / 2];
    }

    /// <summary>True when the sequence never reverses direction by more than <paramref name="tolerance"/>.</summary>
    public static bool IsMonotonic(IReadOnlyList<float> values, float tolerance = 0f)
    {
        ArgumentNullException.ThrowIfNull(values);
        bool up = true;
        bool down = true;
        for (int i = 1; i < values.Count; i++)
        {
            float d = values[i] - values[i - 1];
            up &= d >= -tolerance;
            down &= d <= tolerance;
        }

        return up || down;
    }

    private static float Luma(Rgb8 c) => Vector3.Dot(Srgb.ToEncoded(c), Wcag.LuminanceWeights);

    private static int[] SummedArea(bool[] mask, int w, int h)
    {
        int[] sat = new int[(w + 1) * (h + 1)];
        for (int y = 0; y < h; y++)
        {
            int row = 0;
            for (int x = 0; x < w; x++)
            {
                row += mask[(y * w) + x] ? 1 : 0;
                sat[((y + 1) * (w + 1)) + x + 1] = sat[(y * (w + 1)) + x + 1] + row;
            }
        }

        return sat;
    }

    private static int Sum(int[] sat, int w, int x0, int y0, int x1, int y1) =>
        sat[(y1 * (w + 1)) + x1] - sat[(y0 * (w + 1)) + x1] - sat[(y1 * (w + 1)) + x0] + sat[(y0 * (w + 1)) + x0];
}
