// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Core.Colour;
using ThemeLens.Core.Rendering;
using ThemeLens.Core.Themes;

namespace ThemeLens.Core.Analysis;

/// <summary>
/// One rectangle the lens changes while the "rectangles only" option is on, in cells of the picture it was
/// found in. Right and bottom are the first cells outside it.
/// </summary>
/// <param name="Level">
/// How strongly a pixel on the rim of the rectangle has to be of the rectangle's kind to belong to it; see
/// <see cref="LensConstants.RectangleStrength"/>.
/// </param>
/// <param name="Weight">1 normally; between 1 and 0 while a rectangle that is no longer found fades out.</param>
public readonly record struct LensRectangle(int Left, int Top, int Right, int Bottom, float Level, float Weight = 1f)
{
    public long Area => (long)Math.Max(0, Right - Left) * Math.Max(0, Bottom - Top);

    public long IntersectionArea(LensRectangle other)
    {
        long w = Math.Min(Right, other.Right) - Math.Max(Left, other.Left);
        long h = Math.Min(Bottom, other.Bottom) - Math.Max(Top, other.Top);
        return w <= 0 || h <= 0 ? 0 : w * h;
    }
}

/// <summary>What <see cref="RectangleFinder"/> found in one picture.</summary>
/// <param name="Items">The rectangles, in cells.</param>
/// <param name="Width">Width of the picture, in cells.</param>
/// <param name="Height">Height of the picture, in cells.</param>
/// <param name="Pitch">Lens pixels per cell: 4 normally, 8 on very large lenses.</param>
/// <param name="Ask">
/// One byte per cell, not zero where an area of the other kind lies inside a rectangle (a dark button on a white
/// page), and one cell around it. There the renderer asks the region logic for each pixel, so that the button
/// and what is written on it stay as they are. Everywhere else inside a rectangle every pixel is changed.
/// </param>
public sealed record FoundRectangles(IReadOnlyList<LensRectangle> Items, int Width, int Height, int Pitch, byte[] Ask)
{
    public static readonly FoundRectangles None = new([], 0, 0, LensConstants.BlockSize, []);

    /// <summary>The rectangles as the renderer takes them.</summary>
    public RectangleMask ToMask() => RectangleMask.Build(Width, Height, Pitch, Items, Ask);
}

/// <summary>
/// Finds the rectangles for the "rectangles only" option (section 5.6): upright rectangular areas that are
/// bright all over, or dark all over when dark rectangles are looked for. Text and other small detail inside
/// an area does not count against it.
/// </summary>
/// <remarks>
/// The rule the user sets with three values: an area at least as large as a square of
/// <see cref="RectangleSettings.FullSide"/> is changed when it reaches the brightness threshold; one as large as
/// a square of <see cref="RectangleSettings.SmallestSide"/> only when it is pure white; sizes in between need a
/// brightness in between; smaller areas are left alone. The finder walks from the threshold up to pure white in
/// <see cref="LensConstants.RectangleLevels"/> steps and, at each step, looks for areas at least that bright and
/// at least as large as the step asks for. "Bright" below stands for "dark" as well: for dark rectangles
/// everything is mirrored.
/// </remarks>
public static class RectangleFinder
{
    /// <param name="picture">
    /// The lens area, one sample per cell. Small rectangles can only be found in the detailed picture
    /// (<see cref="Thumbnail.Detail"/>), which has a sample for every 4×4 block.
    /// </param>
    /// <param name="values">The user's values.</param>
    /// <param name="target">Bright rectangles or dark ones.</param>
    public static FoundRectangles Find(Thumbnail picture, RectangleSettings values, RectangleTarget target)
    {
        ArgumentNullException.ThrowIfNull(picture);
        ArgumentNullException.ThrowIfNull(values);
        values = values.For(target);
        int w = picture.Width;
        int h = picture.Height;
        int n = w * h;
        int pitch = picture.SourcePixelsPerSample;
        var found = new List<LensRectangle>();
        byte[] ask = new byte[n];
        if (n == 0)
        {
            return new FoundRectangles(found, w, h, pitch, ask);
        }

        // Every grid below has a border of one cell all round; see CellGrid. The grids are kept from one call
        // to the next: an analysis runs several times a second, and each grid is the size of the picture.
        int pw = w + 2;
        int ph = h + 2;
        Workspace work = _workspace ??= new Workspace();
        work.Resize(pw * ph, n);
        float[] strength = work.Strength;
        byte[] ragged = work.Ragged;
        Measure(picture, target == RectangleTarget.Bright, strength, ragged, work.Known, pw);

        // Which strengths occur at all, in 256 bins: most steps find nothing between them and the step before.
        int[] occurs = work.Occurs;
        Array.Clear(occurs);
        for (int y = 1; y <= h; y++)
        {
            for (int i = (y * pw) + 1, end = (y * pw) + w; i <= end; i++)
            {
                occurs[Bin(strength[i])]++;
            }
        }

        float threshold = values.Strength(target);
        const int levels = LensConstants.RectangleLevels;
        bool[] covered = work.Covered;
        Array.Clear(covered, 0, n);
        byte[] mask = work.Mask;
        byte[] scratch = work.Scratch;
        byte[] holes = work.Holes;
        int[] labels = work.Labels;
        CellRuns runs = work.Runs;
        List<Area> areas = work.Areas;
        areas.Clear();
        float previous = float.NaN;

        for (int level = 0; level < levels && found.Count < LensConstants.MaxRectangles; level++)
        {
            float step = level / (float)(levels - 1);
            float needed = Smooth.Lerp(threshold, 1f, step) - (LensConstants.RectangleTolerance * step);

            // Sizes run along the bar by the side of the square, so that is what is taken in between. A size is
            // measured to within one cell, and the rule is read in the rectangle's favour by that much.
            float side = Smooth.Lerp(values.FullSide, values.SmallestSide, step);
            float neededCells = Cells(MathF.Max(side - pitch, pitch), pitch);

            // Many screens have two or three brightness values in all, so most steps see what the step before
            // saw. Then the areas are the same ones, and only the smaller size asked for is new.
            if (float.IsNaN(previous) || AnyBetween(occurs, previous, needed))
            {
                FindAreas(strength, ragged, needed, mask, scratch, holes, labels, runs, areas, w, h);
            }

            previous = needed;
            foreach (Area area in areas)
            {
                if (area.Size < neededCells || found.Count >= LensConstants.MaxRectangles)
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

                bool taken = false;
                foreach ((int x0, int y0, int x1, int y1) in Pieces(labels, area, w))
                {
                    if (found.Count >= LensConstants.MaxRectangles)
                    {
                        break;
                    }

                    taken = true;
                    found.Add(new LensRectangle(x0, y0, x1 + 1, y1 + 1, needed));
                    for (int y = y0; y <= y1; y++)
                    {
                        Array.Fill(covered, true, (y * w) + x0, x1 - x0 + 1);
                    }
                }

                if (taken && area.HasHoles)
                {
                    // Where something of the other kind lies in the area, the renderer asks the regions.
                    for (int y = area.Y0; y <= area.Y1; y++)
                    {
                        int padded = ((y + 1) * pw) + area.X0 + 1;
                        for (int i = (y * w) + area.X0, end = (y * w) + area.X1; i <= end; i++, padded++)
                        {
                            if (labels[i] == area.Label && holes[padded] != 0)
                            {
                                ask[i] = 1;
                            }
                        }
                    }
                }
            }
        }

        return new FoundRectangles(found, w, h, pitch, ask);
    }

    /// <summary>The area of a square with the given side, in cells.</summary>
    private static float Cells(float side, int pitch) => side * side / (pitch * pitch);

    private static int Bin(float strength) => Math.Clamp((int)(strength * 256f), 0, 255);

    /// <summary>True when some cell may have a strength between the two, so that the two steps do not see the same picture.</summary>
    private static bool AnyBetween(int[] occurs, float low, float high)
    {
        for (int bin = Bin(MathF.Min(low, high)), last = Bin(MathF.Max(low, high)); bin <= last; bin++)
        {
            if (occurs[bin] != 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// How strongly each cell is of the kind looked for, and which cells are ragged. A flat block shows
    /// background and nothing else, so its one sampled pixel speaks for it. A ragged block, with text or an edge
    /// in it, says nothing about the background by itself: its pixel may be the ink or the paper. It takes the
    /// average of the flat blocks around it, in three rounds; a block that is still undecided after that (the
    /// middle of a large picture) counts as not of the kind. A block that lies across the edge of a rectangle
    /// ends up half way between inside and outside, and so outside.
    /// </summary>
    private static void Measure(Thumbnail picture, bool bright, float[] strength, byte[] ragged, byte[] known, int pw)
    {
        int w = picture.Width;
        int h = picture.Height;
        const int flatRange = (int)(LensConstants.RectangleFlatRange * 255f);
        int cells = pw * (h + 2);
        Array.Fill(strength, -1f, 0, cells);
        Array.Clear(ragged, 0, cells);
        Array.Clear(known, 0, cells);
        var pending = new List<int>();
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int source = (y * w) + x;
                int i = ((y + 1) * pw) + x + 1;
                if (picture.Range[source] > flatRange)
                {
                    ragged[i] = 1;
                    pending.Add(i);
                    continue;
                }

                float luma = LensConstants.Luma(Srgb.ToEncoded(picture.Pixels[source]));
                strength[i] = bright ? luma : 1f - luma;
                known[i] = 1;
            }
        }

        ReadOnlySpan<int> around = [-pw - 1, -pw, -pw + 1, -1, 1, pw - 1, pw, pw + 1];
        var settled = new List<(int Cell, float Strength)>();
        for (int round = 0; round < 3 && pending.Count > 0; round++)
        {
            // All cells of a round decide from what was known before the round.
            settled.Clear();
            int kept = 0;
            for (int k = 0; k < pending.Count; k++)
            {
                int i = pending[k];
                float sum = 0f;
                int count = 0;
                foreach (int offset in around)
                {
                    if (known[i + offset] != 0)
                    {
                        sum += strength[i + offset];
                        count++;
                    }
                }

                if (count > 0)
                {
                    settled.Add((i, sum / count));
                }
                else
                {
                    pending[kept++] = i;
                }
            }

            pending.RemoveRange(kept, pending.Count - kept);
            foreach ((int cell, float value) in settled)
            {
                strength[cell] = value;
                known[cell] = 1;
            }
        }

        foreach (int i in pending)
        {
            strength[i] = 0f;
        }
    }

    /// <summary>
    /// The connected areas of cells at least <paramref name="needed"/> strong, largest first, after gaps up to
    /// two cells wide are closed (a scroll bar thumb, a dark rule) and enclosed holes are filled: an area is
    /// judged by its outline. A white page with dark buttons, or with a dark picture in it, is one rectangle.
    /// <paramref name="holes"/> gets the cells of the filled holes and one cell around them, so that the
    /// renderer can leave what is dark inside the page alone. <paramref name="labels"/> gets each cell's area.
    /// </summary>
    private static void FindAreas(
        float[] strength, byte[] ragged, float needed, byte[] mask, byte[] scratch, byte[] holes, int[] labels, CellRuns runs, List<Area> areas, int w, int h)
    {
        int pw = w + 2;
        int ph = h + 2;
        areas.Clear();
        bool any = false;
        int cells = pw * ph;
        for (int i = 0; i < cells; i++)
        {
            bool strong = strength[i] >= needed;
            mask[i] = strong ? (byte)1 : (byte)0;
            any |= strong;
        }

        if (!any)
        {
            return;
        }

        // Close: grow by one cell, then shrink by one. Beyond the edge of the lens counts as bright when
        // shrinking, so the edge is not eaten.
        CellGrid.Max3x3(mask, scratch, pw, ph, border: 0);
        CellGrid.Min3x3(mask, scratch, pw, ph, border: 1);
        CellGrid.SetBorder(mask, pw, ph, 0);

        // Fill enclosed holes. A gap that reaches the edge of the lens is not enclosed.
        runs.Find(mask, w, h, value: 0);
        bool[] open = new bool[runs.Count];
        for (int r = 0; r < runs.Count; r++)
        {
            open[runs.Root(r)] |= runs.Y[r] == 0 || runs.Y[r] == h - 1 || runs.X0[r] == 0 || runs.X1[r] == w - 1;
        }

        Array.Clear(holes, 0, cells);
        bool anyHole = false;
        for (int r = 0; r < runs.Count; r++)
        {
            if (!open[runs.Root(r)])
            {
                int first = ((runs.Y[r] + 1) * pw) + runs.X0[r] + 1;
                int length = runs.X1[r] - runs.X0[r] + 1;
                mask.AsSpan(first, length).Fill(1);
                holes.AsSpan(first, length).Fill(1);
                anyHole = true;
            }
        }

        if (anyHole)
        {
            CellGrid.Max3x3(holes, scratch, pw, ph, border: 0);
            CellGrid.SetBorder(holes, pw, ph, 0);
        }

        // The areas themselves.
        runs.Find(mask, w, h, value: 1);
        Array.Clear(labels, 0, w * h);
        var byRoot = new Area?[runs.Count];
        for (int r = 0; r < runs.Count; r++)
        {
            int root = runs.Root(r);
            Area area = byRoot[root] ??= new Area { Label = root + 1, X0 = int.MaxValue, Y0 = int.MaxValue };
            int y = runs.Y[r];
            int x0 = runs.X0[r];
            int x1 = runs.X1[r];
            area.Count += x1 - x0 + 1;
            area.X0 = Math.Min(area.X0, x0);
            area.X1 = Math.Max(area.X1, x1);
            area.Y0 = Math.Min(area.Y0, y);
            area.Y1 = Math.Max(area.Y1, y);
            labels.AsSpan((y * w) + x0, x1 - x0 + 1).Fill(root + 1);

            // A ragged cell beside the area lies across its edge: on average half of it belongs to the area.
            // Without this a 10 by 10 pixel square, which may hold one whole 4×4 block, would count as 16 pixels.
            int first = ((y + 1) * pw) + x0 + 1;
            int last = first + x1 - x0;
            int rim = (ragged[first - 1] & (1 - mask[first - 1])) + (ragged[last + 1] & (1 - mask[last + 1]));
            for (int i = first; i <= last; i++)
            {
                rim += (ragged[i - pw] & (1 - mask[i - pw])) + (ragged[i + pw] & (1 - mask[i + pw]));
            }

            area.Rim += rim;
            area.HasHoles |= anyHole && holes.AsSpan(first, last - first + 1).IndexOfAnyExcept((byte)0) >= 0;
        }

        foreach (Area? area in byRoot)
        {
            if (area is not null)
            {
                area.Size = area.Count + (0.5f * area.Rim);
                areas.Add(area);
            }
        }

        areas.Sort(static (a, b) => b.Size != a.Size ? b.Size.CompareTo(a.Size) : a.Label.CompareTo(b.Label));
    }

    [ThreadStatic]
    private static Workspace? _workspace;

    /// <summary>The grids one call works on, kept for the next call on the same thread.</summary>
    private sealed class Workspace
    {
        public float[] Strength { get; private set; } = [];

        public byte[] Ragged { get; private set; } = [];

        public byte[] Known { get; private set; } = [];

        public byte[] Mask { get; private set; } = [];

        public byte[] Scratch { get; private set; } = [];

        public byte[] Holes { get; private set; } = [];

        public int[] Labels { get; private set; } = [];

        public bool[] Covered { get; private set; } = [];

        public int[] Occurs { get; } = new int[256];

        public CellRuns Runs { get; } = new();

        public List<Area> Areas { get; } = [];

        /// <summary>Makes every grid exactly large enough: the vector loops of CellGrid run to the end of a grid.</summary>
        public void Resize(int padded, int cells)
        {
            if (Strength.Length != padded)
            {
                Strength = new float[padded];
                Ragged = new byte[padded];
                Known = new byte[padded];
                Mask = new byte[padded];
                Scratch = new byte[padded];
                Holes = new byte[padded];
            }

            if (Labels.Length != cells)
            {
                Labels = new int[cells];
                Covered = new bool[cells];
            }
        }
    }

    /// <summary>One connected bright area of the picture, with the box of cells around it.</summary>
    private sealed class Area
    {
        public int Label;
        public int Count;
        public int X0;
        public int Y0;
        public int X1;
        public int Y1;

        /// <summary>Ragged cells beside the area: cells that lie across its edge.</summary>
        public int Rim;

        /// <summary>True when something of the other kind lies inside the area.</summary>
        public bool HasHoles;

        /// <summary>The area in cells, with the cells that lie across its edge counted in part.</summary>
        public float Size;

        /// <summary>True once the area has been taken or turned down; it is not looked at again while the picture of this step lasts.</summary>
        public bool Settled;
    }

    /// <summary>
    /// The rectangles an area is made of: the largest rectangle that fits in it, then the largest that fits in
    /// what is left, up to <see cref="LensConstants.RectanglePieces"/>. A window with another window over one
    /// corner is two rectangles. Returns nothing when that many rectangles do
    /// not cover <see cref="LensConstants.RectangleCover"/> of the area: then it is not rectangular.
    /// </summary>
    private static List<(int X0, int Y0, int X1, int Y1)> Pieces(int[] labels, Area area, int w)
    {
        var pieces = new List<(int, int, int, int)>(1);
        int bw = area.X1 - area.X0 + 1;
        int bh = area.Y1 - area.Y0 + 1;
        if (area.Count == bw * bh)
        {
            pieces.Add((area.X0, area.Y0, area.X1, area.Y1));
            return pieces;
        }

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
}
