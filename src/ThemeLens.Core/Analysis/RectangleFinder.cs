// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Core.Colour;
using ThemeLens.Core.Rendering;
using ThemeLens.Core.Themes;

namespace ThemeLens.Core.Analysis;

/// <summary>
/// One rectangle the lens changes while the "rectangles only" option is on, in lens pixels. Right and bottom
/// are the first pixels outside it.
/// </summary>
/// <param name="Level">
/// How strongly a block in the margin around the rectangle has to be of the rectangle's kind to belong to it;
/// see <see cref="LensConstants.BlockStrength"/>.
/// </param>
/// <param name="Weight">1 normally; between 1 and 0 while a rectangle that is no longer found fades out.</param>
public readonly record struct LensRectangle(int Left, int Top, int Right, int Bottom, float Level, float Weight = 1f)
{
    /// <summary>Stands for "up to the edge of the lens, wherever that is".</summary>
    public const int Far = 1 << 20;

    public long Area => (long)Math.Max(0, Right - Left) * Math.Max(0, Bottom - Top);

    public long IntersectionArea(LensRectangle other)
    {
        long w = Math.Min(Right, other.Right) - Math.Max(Left, other.Left);
        long h = Math.Min(Bottom, other.Bottom) - Math.Max(Top, other.Top);
        return w <= 0 || h <= 0 ? 0 : w * h;
    }
}

/// <summary>What <see cref="RectangleFinder"/> found in one thumbnail.</summary>
/// <param name="Margin">
/// Width, in lens pixels, of the band around each rectangle in which the analysis could not see where the edge
/// is. The renderer settles it there per 4×4 block.
/// </param>
public sealed record FoundRectangles(IReadOnlyList<LensRectangle> Items, int Margin)
{
    public static readonly FoundRectangles None = new([], 0);
}

/// <summary>
/// Finds the rectangles for the "rectangles only" option (section 5.6): upright rectangular areas that are
/// bright all over under a dark theme, or dark all over under a light one. Text and other small detail inside
/// an area does not count against it.
/// </summary>
/// <remarks>
/// The rule the user sets with three values: an area of at least <see cref="RectangleSettings.FullArea"/> is
/// changed when it reaches the brightness threshold; an area of <see cref="RectangleSettings.SmallestArea"/>
/// only when it is pure white; sizes in between need a brightness in between; smaller areas are left alone.
/// The finder walks from the threshold up to pure white in <see cref="LensConstants.RectangleLevels"/> steps
/// and, at each step, looks for areas at least that bright and at least as large as the step asks for.
/// "Bright" below stands for "dark" as well: under a light theme everything is mirrored.
/// </remarks>
public static class RectangleFinder
{
    private const byte Other = 0;
    private const byte Bright = 1;
    private const byte Unknown = 2;

    /// <param name="thumbnail">The analysis picture of the lens area.</param>
    /// <param name="settings">The user's values.</param>
    /// <param name="themeBase">Dark looks for bright rectangles, light for dark ones. Keep finds nothing.</param>
    /// <param name="screenPixels">Size of the screen the lens is on, in pixels: areas are shares of this.</param>
    public static FoundRectangles Find(Thumbnail thumbnail, RectangleSettings settings, ThemeBase themeBase, long screenPixels)
    {
        ArgumentNullException.ThrowIfNull(thumbnail);
        ArgumentNullException.ThrowIfNull(settings);
        settings = settings.For(themeBase);
        int margin = thumbnail.SourcePixelsPerSample;
        if (!settings.On || screenPixels <= 0 || thumbnail.Width == 0 || thumbnail.Height == 0)
        {
            return new FoundRectangles([], margin);
        }

        int w = thumbnail.Width;
        int h = thumbnail.Height;
        int n = w * h;
        bool light = themeBase != ThemeBase.Light;

        // How strongly each sample is of the kind looked for, and whether its block is flat. A flat block shows
        // background and nothing else, so its one sampled pixel speaks for it. A block with text or an edge in
        // it says nothing about the background by itself: its pixel may be the ink or the paper.
        float[] strength = new float[n];
        bool[] flat = new bool[n];
        for (int i = 0; i < n; i++)
        {
            float luma = LensConstants.Luma(Srgb.ToEncoded(thumbnail.Pixels[i]));
            strength[i] = light ? luma : 1f - luma;
            flat[i] = thumbnail.Range[i] / 255f <= LensConstants.RectangleFlatRange;
        }

        double samplePixels = (double)thumbnail.SourcePixelsPerSample * thumbnail.SourcePixelsPerSample;
        float threshold = settings.Strength(themeBase);
        const int levels = LensConstants.RectangleLevels;
        var found = new List<LensRectangle>();
        bool[] covered = new bool[n];
        byte[] state = new byte[n];
        byte[] scratch = new byte[n];
        byte[] previousState = new byte[n];
        bool[] mask = new bool[n];
        bool[] grown = new bool[n];
        int[] labels = new int[n];
        var stack = new Stack<int>();
        var areas = new List<(int Label, int Count, int Seed)>();
        bool havePrevious = false;

        // Smallest hole that is not filled: detail smaller than the smallest rectangle is detail, anything
        // larger (a dark picture in a white page) is left out of the rectangle.
        int holeLimit = Math.Max((int)(settings.SmallestArea * screenPixels / samplePixels), n / 200);

        for (int level = 0; level < levels && found.Count < LensConstants.MaxRectangles; level++)
        {
            float step = level / (float)(levels - 1);
            float needed = Smooth.Lerp(threshold, 1f, step) - (LensConstants.RectangleTolerance * step);
            double neededArea = Smooth.Lerp(settings.FullArea, settings.SmallestArea, step) * screenPixels;

            for (int i = 0; i < n; i++)
            {
                state[i] = !flat[i] ? Unknown : strength[i] >= needed ? Bright : Other;
            }

            // Many screens have two or three brightness values in all, so most steps see what the step before
            // saw. Then the areas are the same ones, and only the smaller size asked for is new.
            if (!havePrevious || !state.AsSpan().SequenceEqual(previousState))
            {
                state.CopyTo(previousState, 0);
                havePrevious = true;
                SettleUnknown(state, scratch, w, h);
                for (int i = 0; i < n; i++)
                {
                    mask[i] = state[i] == Bright;
                }

                Close(mask, grown, w, h);
                FillHoles(mask, labels, stack, w, h, holeLimit);

                // Connected areas, largest first.
                Array.Clear(labels);
                areas.Clear();
                for (int i = 0; i < n; i++)
                {
                    if (mask[i] && labels[i] == 0)
                    {
                        int label = areas.Count + 1;
                        areas.Add((label, Flood(mask, labels, stack, w, h, i, label), i));
                    }
                }

                areas.Sort((a, b) => b.Count != a.Count ? b.Count.CompareTo(a.Count) : a.Seed.CompareTo(b.Seed));
            }

            foreach ((int label, int count, _) in areas)
            {
                if (count * samplePixels < neededArea)
                {
                    break;
                }

                if (found.Count >= LensConstants.MaxRectangles)
                {
                    break;
                }

                int already = 0;
                for (int i = 0; i < n; i++)
                {
                    if (labels[i] == label && covered[i])
                    {
                        already++;
                    }
                }

                if (already >= LensConstants.RectangleCover * count)
                {
                    continue;   // found at an earlier step
                }

                List<(int X0, int Y0, int X1, int Y1)> pieces = Pieces(labels, label, w, h, count);
                if (pieces.Count == 0)
                {
                    continue;   // not rectangular
                }

                foreach ((int x0, int y0, int x1, int y1) in pieces)
                {
                    if (found.Count >= LensConstants.MaxRectangles)
                    {
                        break;
                    }

                    found.Add(ToLensPixels(thumbnail, x0, y0, x1, y1, needed));
                    for (int y = y0; y <= y1; y++)
                    {
                        Array.Fill(covered, true, (y * w) + x0, x1 - x0 + 1);
                    }
                }
            }
        }

        return new FoundRectangles(found, margin);
    }

    /// <summary>
    /// Blocks with text or an edge in them take the kind of the flat blocks around them, in three rounds. What
    /// is still undecided after that (the middle of a large picture, say) is not bright.
    /// </summary>
    private static void SettleUnknown(byte[] state, byte[] scratch, int w, int h)
    {
        for (int round = 0; round < 3; round++)
        {
            bool changed = false;
            state.CopyTo(scratch, 0);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w) + x;
                    if (scratch[i] != Unknown)
                    {
                        continue;
                    }

                    int bright = 0;
                    int other = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int yy = y + dy;
                        if (yy < 0 || yy >= h)
                        {
                            continue;
                        }

                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int xx = x + dx;
                            if (xx < 0 || xx >= w)
                            {
                                continue;
                            }

                            byte s = scratch[(yy * w) + xx];
                            if (s == Bright)
                            {
                                bright++;
                            }
                            else if (s == Other)
                            {
                                other++;
                            }
                        }
                    }

                    if (bright != other)
                    {
                        state[i] = bright > other ? Bright : Other;
                        changed = true;
                    }
                }
            }

            if (!changed)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Closes gaps up to two samples wide (a scroll bar thumb, a thin dark rule): grow by one sample, then
    /// shrink by one. Beyond the edge of the lens counts as bright when shrinking, so the edge is not eaten.
    /// </summary>
    private static void Close(bool[] mask, bool[] grown, int w, int h)
    {
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                bool any = false;
                for (int dy = -1; dy <= 1 && !any; dy++)
                {
                    int yy = y + dy;
                    if (yy < 0 || yy >= h)
                    {
                        continue;
                    }

                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = x + dx;
                        if (xx >= 0 && xx < w && mask[(yy * w) + xx])
                        {
                            any = true;
                            break;
                        }
                    }
                }

                grown[(y * w) + x] = any;
            }
        }

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                bool all = true;
                for (int dy = -1; dy <= 1 && all; dy++)
                {
                    int yy = y + dy;
                    if (yy < 0 || yy >= h)
                    {
                        continue;
                    }

                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = x + dx;
                        if (xx >= 0 && xx < w && !grown[(yy * w) + xx])
                        {
                            all = false;
                            break;
                        }
                    }
                }

                mask[(y * w) + x] = all;
            }
        }
    }

    /// <summary>Makes small enclosed gaps part of the area around them. Gaps that reach the edge of the lens are not enclosed.</summary>
    private static void FillHoles(bool[] mask, int[] labels, Stack<int> stack, int w, int h, int holeLimit)
    {
        int n = w * h;
        Array.Clear(labels);
        var members = new List<int>();
        int label = 0;
        for (int start = 0; start < n; start++)
        {
            if (mask[start] || labels[start] != 0)
            {
                continue;
            }

            label++;
            members.Clear();
            bool touchesEdge = false;
            labels[start] = label;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                members.Add(i);
                int x = i % w;
                int y = i / w;
                touchesEdge |= x == 0 || y == 0 || x == w - 1 || y == h - 1;
                Visit(x - 1, y);
                Visit(x + 1, y);
                Visit(x, y - 1);
                Visit(x, y + 1);
            }

            if (!touchesEdge && members.Count <= holeLimit)
            {
                foreach (int i in members)
                {
                    mask[i] = true;
                }
            }

            void Visit(int x, int y)
            {
                if (x < 0 || y < 0 || x >= w || y >= h)
                {
                    return;
                }

                int j = (y * w) + x;
                if (!mask[j] && labels[j] == 0)
                {
                    labels[j] = label;
                    stack.Push(j);
                }
            }
        }
    }

    private static int Flood(bool[] mask, int[] labels, Stack<int> stack, int w, int h, int start, int label)
    {
        int count = 0;
        labels[start] = label;
        stack.Push(start);
        while (stack.Count > 0)
        {
            int i = stack.Pop();
            count++;
            int x = i % w;
            int y = i / w;
            Visit(x - 1, y);
            Visit(x + 1, y);
            Visit(x, y - 1);
            Visit(x, y + 1);
        }

        return count;

        void Visit(int x, int y)
        {
            if (x < 0 || y < 0 || x >= w || y >= h)
            {
                return;
            }

            int j = (y * w) + x;
            if (mask[j] && labels[j] == 0)
            {
                labels[j] = label;
                stack.Push(j);
            }
        }
    }

    /// <summary>
    /// The rectangles an area is made of: the largest rectangle that fits in it, then the largest that fits in
    /// what is left, up to <see cref="LensConstants.RectanglePieces"/>. A window with another window over one
    /// corner is two rectangles; a page around a picture is four. Returns nothing when that many rectangles do
    /// not cover <see cref="LensConstants.RectangleCover"/> of the area: then it is not rectangular.
    /// </summary>
    private static List<(int X0, int Y0, int X1, int Y1)> Pieces(int[] labels, int label, int w, int h, int count)
    {
        var pieces = new List<(int, int, int, int)>();
        bool[] left = new bool[w * h];
        for (int i = 0; i < left.Length; i++)
        {
            left[i] = labels[i] == label;
        }

        int coveredCount = 0;
        int[] heights = new int[w];
        int[] starts = new int[w + 1];
        int[] tall = new int[w + 1];
        for (int piece = 0; piece < LensConstants.RectanglePieces && coveredCount < count; piece++)
        {
            // Largest rectangle of true cells: for each row, the tallest column of true cells ending there,
            // then the largest rectangle under that skyline.
            Array.Clear(heights);
            int best = 0;
            (int X0, int Y0, int X1, int Y1) bestRect = default;
            for (int y = 0; y < h; y++)
            {
                int top = 0;
                for (int x = 0; x <= w; x++)
                {
                    int height = 0;
                    if (x < w)
                    {
                        heights[x] = left[(y * w) + x] ? heights[x] + 1 : 0;
                        height = heights[x];
                    }

                    int start = x;
                    while (top > 0 && tall[top - 1] >= height)
                    {
                        top--;
                        int area = tall[top] * (x - starts[top]);
                        if (area > best)
                        {
                            best = area;
                            bestRect = (starts[top], y - tall[top] + 1, x - 1, y);
                        }

                        start = starts[top];
                    }

                    if (height > 0)
                    {
                        starts[top] = start;
                        tall[top] = height;
                        top++;
                    }
                }
            }

            if (best == 0)
            {
                break;
            }

            pieces.Add(bestRect);
            coveredCount += best;
            for (int y = bestRect.Y0; y <= bestRect.Y1; y++)
            {
                Array.Fill(left, false, (y * w) + bestRect.X0, bestRect.X1 - bestRect.X0 + 1);
            }
        }

        if (coveredCount < LensConstants.RectangleCover * count)
        {
            pieces.Clear();
        }

        return pieces;
    }

    /// <summary>
    /// A rectangle of samples as lens pixels. Each sample speaks for one 4×4 block in the middle of its cell, so
    /// the rectangle runs from the first block known to be bright to the last one; where the bright area really
    /// ends between two samples is settled by the renderer, in the margin. A rectangle that reaches the first or
    /// last sample reaches the edge of the lens.
    /// </summary>
    private static LensRectangle ToLensPixels(Thumbnail t, int x0, int y0, int x1, int y1, float level)
    {
        const int block = LensConstants.BlockSize;
        int stride = t.SourcePixelsPerSample / block;
        int First(int sample) => sample == 0 ? 0 : ((sample * stride) + (stride / 2)) * block;
        int Last(int sample, int samples) => sample == samples - 1 ? LensRectangle.Far : (((sample * stride) + (stride / 2)) * block) + block;
        return new LensRectangle(First(x0), First(y0), Last(x1, t.Width), Last(y1, t.Height), level);
    }
}
