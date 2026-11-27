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
/// is, or what lies between its samples. The renderer settles it there per 4×4 block.
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
        // Two samples: the width of the gaps the finder closes. Whatever bright thing lies that close to a
        // rectangle was seen as part of it, so the renderer may treat it as part of it too.
        int margin = 2 * thumbnail.SourcePixelsPerSample;
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
        var areas = new List<Area>();
        int[] unknown = new int[n];
        int unknownCount = 0;
        for (int i = 0; i < n; i++)
        {
            if (!flat[i])
            {
                unknown[unknownCount++] = i;
            }
        }

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
                SettleUnknown(state, scratch, unknown.AsSpan(0, unknownCount), w, h);
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
                        areas.Add(Flood(mask, labels, stack, w, h, i, areas.Count + 1));
                    }
                }

                areas.Sort((a, b) => b.Count != a.Count ? b.Count.CompareTo(a.Count) : a.Label.CompareTo(b.Label));
            }

            foreach (Area area in areas)
            {
                if (area.Count * samplePixels < neededArea || found.Count >= LensConstants.MaxRectangles)
                {
                    break;
                }

                if (area.Settled)
                {
                    continue;   // taken, or looked at and found not to be rectangular
                }

                area.Settled = true;
                int already = 0;
                for (int y = area.Y0; y <= area.Y1; y++)
                {
                    for (int i = (y * w) + area.X0, end = (y * w) + area.X1; i <= end; i++)
                    {
                        if (labels[i] == area.Label && covered[i])
                        {
                            already++;
                        }
                    }
                }

                if (already >= LensConstants.RectangleCover * area.Count)
                {
                    continue;   // found at an earlier step
                }

                foreach ((int x0, int y0, int x1, int y1) in Pieces(labels, area, w))
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
    private static void SettleUnknown(byte[] state, byte[] scratch, ReadOnlySpan<int> unknown, int w, int h)
    {
        for (int round = 0; round < 3; round++)
        {
            bool changed = false;
            foreach (int i in unknown)
            {
                scratch[i] = state[i];
                if (state[i] != Unknown)
                {
                    continue;
                }

                int x = i % w;
                int y = i / w;
                int bright = 0;
                int other = 0;
                for (int yy = Math.Max(y - 1, 0), yEnd = Math.Min(y + 1, h - 1); yy <= yEnd; yy++)
                {
                    for (int j = (yy * w) + Math.Max(x - 1, 0), end = (yy * w) + Math.Min(x + 1, w - 1); j <= end; j++)
                    {
                        byte s = state[j];
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
                    scratch[i] = bright > other ? Bright : Other;
                    changed = true;
                }
            }

            // All samples of a round decide from the state before the round.
            foreach (int i in unknown)
            {
                state[i] = scratch[i];
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
        // Grow: along rows into the scratch buffer, along columns back into the mask.
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                grown[row + x] = mask[row + x] || (x > 0 && mask[row + x - 1]) || (x < w - 1 && mask[row + x + 1]);
            }
        }

        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                mask[row + x] = grown[row + x] || (y > 0 && grown[row + x - w]) || (y < h - 1 && grown[row + x + w]);
            }
        }

        // Shrink, the same way.
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                grown[row + x] = mask[row + x] && (x == 0 || mask[row + x - 1]) && (x == w - 1 || mask[row + x + 1]);
            }
        }

        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                mask[row + x] = grown[row + x] && (y == 0 || grown[row + x - w]) && (y == h - 1 || grown[row + x + w]);
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

    private static Area Flood(bool[] mask, int[] labels, Stack<int> stack, int w, int h, int start, int label)
    {
        var area = new Area { Label = label, X0 = int.MaxValue, Y0 = int.MaxValue };
        labels[start] = label;
        stack.Push(start);
        while (stack.Count > 0)
        {
            int i = stack.Pop();
            int x = i % w;
            int y = i / w;
            area.Count++;
            area.X0 = Math.Min(area.X0, x);
            area.X1 = Math.Max(area.X1, x);
            area.Y0 = Math.Min(area.Y0, y);
            area.Y1 = Math.Max(area.Y1, y);
            Visit(x - 1, y);
            Visit(x + 1, y);
            Visit(x, y - 1);
            Visit(x, y + 1);
        }

        return area;

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

    /// <summary>One connected bright area of the thumbnail, with the box of samples around it.</summary>
    private sealed class Area
    {
        public int Label;
        public int Count;
        public int X0;
        public int Y0;
        public int X1;
        public int Y1;

        /// <summary>True once the area has been taken or turned down; it is not looked at again while the picture of this step lasts.</summary>
        public bool Settled;
    }

    /// <summary>
    /// The rectangles an area is made of: the largest rectangle that fits in it, then the largest that fits in
    /// what is left, up to <see cref="LensConstants.RectanglePieces"/>. A window with another window over one
    /// corner is two rectangles; a page around a picture is four. Returns nothing when that many rectangles do
    /// not cover <see cref="LensConstants.RectangleCover"/> of the area: then it is not rectangular.
    /// </summary>
    private static List<(int X0, int Y0, int X1, int Y1)> Pieces(int[] labels, Area area, int w)
    {
        var pieces = new List<(int, int, int, int)>();
        int bw = area.X1 - area.X0 + 1;
        int bh = area.Y1 - area.Y0 + 1;
        bool[] left = new bool[bw * bh];
        for (int y = 0; y < bh; y++)
        {
            for (int x = 0; x < bw; x++)
            {
                left[(y * bw) + x] = labels[((y + area.Y0) * w) + x + area.X0] == area.Label;
            }
        }

        int coveredCount = 0;
        int[] heights = new int[bw];
        int[] starts = new int[bw + 1];
        int[] tall = new int[bw + 1];
        for (int piece = 0; piece < LensConstants.RectanglePieces && coveredCount < area.Count; piece++)
        {
            // Largest rectangle of true cells: for each row, the tallest column of true cells ending there,
            // then the largest rectangle under that skyline.
            Array.Clear(heights);
            int best = 0;
            (int X0, int Y0, int X1, int Y1) bestRect = default;
            for (int y = 0; y < bh; y++)
            {
                int top = 0;
                for (int x = 0; x <= bw; x++)
                {
                    int height = 0;
                    if (x < bw)
                    {
                        heights[x] = left[(y * bw) + x] ? heights[x] + 1 : 0;
                        height = heights[x];
                    }

                    int start = x;
                    while (top > 0 && tall[top - 1] >= height)
                    {
                        top--;
                        int size = tall[top] * (x - starts[top]);
                        if (size > best)
                        {
                            best = size;
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

            pieces.Add((bestRect.X0 + area.X0, bestRect.Y0 + area.Y0, bestRect.X1 + area.X0, bestRect.Y1 + area.Y0));
            coveredCount += best;
            for (int y = bestRect.Y0; y <= bestRect.Y1; y++)
            {
                Array.Fill(left, false, (y * bw) + bestRect.X0, bestRect.X1 - bestRect.X0 + 1);
            }
        }

        if (coveredCount < LensConstants.RectangleCover * area.Count)
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
