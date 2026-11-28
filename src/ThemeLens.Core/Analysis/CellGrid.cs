// SPDX-License-Identifier: GPL-3.0-or-later
using System.Numerics;

namespace ThemeLens.Core.Analysis;

/// <summary>
/// Small operations on grids of byte cells with a border of one cell all round, so that no loop has to ask
/// whether a neighbour exists. Cell (x, y) of a grid <c>w</c> wide lives at index <c>(y + 1) * (w + 2) + x + 1</c>.
/// </summary>
internal static class CellGrid
{
    /// <summary>Each cell becomes the largest of itself and its eight neighbours. The border counts as <paramref name="border"/>.</summary>
    public static void Max3x3(byte[] cells, byte[] scratch, int paddedWidth, int paddedHeight, byte border)
    {
        SetBorder(cells, paddedWidth, paddedHeight, border);
        Combine(cells, scratch, paddedWidth, paddedHeight, 1, max: true);
        SetBorder(scratch, paddedWidth, paddedHeight, border);
        Combine(scratch, cells, paddedWidth, paddedHeight, paddedWidth, max: true);
    }

    /// <summary>Each cell becomes the smallest of itself and its eight neighbours. The border counts as <paramref name="border"/>.</summary>
    public static void Min3x3(byte[] cells, byte[] scratch, int paddedWidth, int paddedHeight, byte border)
    {
        SetBorder(cells, paddedWidth, paddedHeight, border);
        Combine(cells, scratch, paddedWidth, paddedHeight, 1, max: false);
        SetBorder(scratch, paddedWidth, paddedHeight, border);
        Combine(scratch, cells, paddedWidth, paddedHeight, paddedWidth, max: false);
    }

    public static void SetBorder(byte[] cells, int paddedWidth, int paddedHeight, byte value)
    {
        cells.AsSpan(0, paddedWidth).Fill(value);
        cells.AsSpan((paddedHeight - 1) * paddedWidth, paddedWidth).Fill(value);
        for (int y = 1; y < paddedHeight - 1; y++)
        {
            cells[y * paddedWidth] = value;
            cells[(y * paddedWidth) + paddedWidth - 1] = value;
        }
    }

    /// <summary>
    /// <paramref name="to"/>[i] = max or min of <paramref name="from"/> at i − step, i and i + step, for every
    /// cell that is not on the first or last row. Border cells of <paramref name="to"/> come out as nonsense
    /// and are set by the caller.
    /// </summary>
    private static void Combine(byte[] from, byte[] to, int paddedWidth, int paddedHeight, int step, bool max)
    {
        int start = paddedWidth;
        int end = (paddedHeight - 1) * paddedWidth;
        int i = start;
        int wide = Vector<byte>.Count;
        if (Vector.IsHardwareAccelerated)
        {
            for (; i <= end - wide; i += wide)
            {
                var a = new Vector<byte>(from, i - step);
                var b = new Vector<byte>(from, i);
                var c = new Vector<byte>(from, i + step);
                (max ? Vector.Max(Vector.Max(a, b), c) : Vector.Min(Vector.Min(a, b), c)).CopyTo(to, i);
            }
        }

        for (; i < end; i++)
        {
            byte a = from[i - step];
            byte b = from[i];
            byte c = from[i + step];
            to[i] = max ? Math.Max(Math.Max(a, b), c) : Math.Min(Math.Min(a, b), c);
        }
    }
}

/// <summary>
/// The connected areas of a padded grid of cells, found row by row: a row is cut into runs of cells of the
/// wanted value, and a run joins the runs it touches in the row above. Much less work than visiting every cell
/// and its four neighbours.
/// </summary>
internal sealed class CellRuns
{
    private int[] _parent = new int[1024];
    private int[] _rowStart = [];

    public int Count { get; private set; }

    public int[] Y { get; private set; } = new int[1024];

    public int[] X0 { get; private set; } = new int[1024];

    public int[] X1 { get; private set; } = new int[1024];

    /// <summary>Finds the areas of cells equal to <paramref name="value"/>. Two cells are in one area when they share a side.</summary>
    public void Find(byte[] cells, int width, int height, byte value)
    {
        int paddedWidth = width + 2;
        Count = 0;
        if (_rowStart.Length < height + 1)
        {
            _rowStart = new int[height + 1];
        }

        for (int y = 0; y < height; y++)
        {
            _rowStart[y] = Count;
            ReadOnlySpan<byte> row = cells.AsSpan(((y + 1) * paddedWidth) + 1, width);
            int x = 0;
            while (x < width)
            {
                int first = row[x..].IndexOf(value);
                if (first < 0)
                {
                    break;
                }

                int x0 = x + first;
                int length = row[x0..].IndexOfAnyExcept(value);
                int x1 = length < 0 ? width - 1 : x0 + length - 1;
                Add(y, x0, x1);
                x = x1 + 1;
            }

            if (y > 0)
            {
                // Both rows hold their runs from left to right, so one walk along the two finds every touch.
                int above = _rowStart[y - 1];
                int aboveEnd = _rowStart[y];
                int here = aboveEnd;
                while (above < aboveEnd && here < Count)
                {
                    if (X1[above] < X0[here])
                    {
                        above++;
                    }
                    else if (X1[here] < X0[above])
                    {
                        here++;
                    }
                    else
                    {
                        Join(above, here);
                        if (X1[above] < X1[here])
                        {
                            above++;
                        }
                        else
                        {
                            here++;
                        }
                    }
                }
            }
        }

        _rowStart[height] = Count;
    }

    /// <summary>The run that stands for the area <paramref name="run"/> belongs to.</summary>
    public int Root(int run)
    {
        while (_parent[run] != run)
        {
            _parent[run] = _parent[_parent[run]];
            run = _parent[run];
        }

        return run;
    }

    private void Join(int a, int b)
    {
        a = Root(a);
        b = Root(b);
        if (a != b)
        {
            // The earlier run stands for the area, so an area's number does not depend on the order of joins.
            if (a < b)
            {
                _parent[b] = a;
            }
            else
            {
                _parent[a] = b;
            }
        }
    }

    private void Add(int y, int x0, int x1)
    {
        if (Count == Y.Length)
        {
            int size = Count * 2;
            Y = Grown(Y, size);
            X0 = Grown(X0, size);
            X1 = Grown(X1, size);
            _parent = Grown(_parent, size);
        }

        Y[Count] = y;
        X0[Count] = x0;
        X1[Count] = x1;
        _parent[Count] = Count;
        Count++;
    }

    private int[] Grown(int[] old, int size)
    {
        int[] next = new int[size];
        old.AsSpan(0, Count).CopyTo(next);
        return next;
    }
}
