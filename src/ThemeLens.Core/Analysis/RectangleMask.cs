// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Core.Rendering;

namespace ThemeLens.Core.Analysis;

/// <summary>
/// The rectangles of the "rectangles only" option as the renderer takes them: one cell per 4×4 block of the
/// lens (per 8×8 on very large lenses), four bytes per cell, in the layout of an R8G8B8A8 texture.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Byte 0, weight: 0 where no rectangle is near; otherwise how much the rectangle counts (255 normally, less while it fades out).</item>
/// <item>Byte 1, level: how strongly a pixel of a rim cell has to be of the rectangle's kind to belong to it.</item>
/// <item>Byte 2, part: 255 inside a rectangle, where every pixel belongs; 128 inside a rectangle where an
/// area of the other kind lies in it (a dark button on a white page), and the region logic decides for each
/// pixel; 0 on its rim.</item>
/// </list>
/// The rim is the outermost ring of cells of a rectangle and the ring of cells around it. The finder knows
/// where a rectangle is to the cell, not to the pixel; in the rim the renderer decides pixel by pixel. That is
/// what makes the edge of a rectangle exact, and what keeps the dark pixels beside a bright rectangle dark.
/// </remarks>
public sealed class RectangleMask
{
    public const int BytesPerCell = 4;

    /// <summary>The value of the part byte inside a rectangle.</summary>
    public const byte Inside = 255;

    /// <summary>The value of the part byte inside a rectangle, where an area of the other kind lies in it.</summary>
    public const byte AskRegions = 128;

    public static readonly RectangleMask Empty = new(0, 0, LensConstants.BlockSize, []);

    private RectangleMask(int width, int height, int pitch, byte[] cells)
    {
        Width = width;
        Height = height;
        Pitch = pitch;
        Cells = cells;
    }

    /// <summary>Width in cells.</summary>
    public int Width { get; }

    /// <summary>Height in cells.</summary>
    public int Height { get; }

    /// <summary>Lens pixels per cell.</summary>
    public int Pitch { get; }

    public byte[] Cells { get; }

    public bool IsEmpty => Width == 0 || Height == 0;

    /// <param name="ask">
    /// One byte per cell, not zero where an area of the other kind lies inside a rectangle; see
    /// <see cref="FoundRectangles.Ask"/>. Null or empty when there is none.
    /// </param>
    public static RectangleMask Build(int width, int height, int pitch, IReadOnlyList<LensRectangle> rectangles, byte[]? ask = null)
    {
        ArgumentNullException.ThrowIfNull(rectangles);
        if (ask is not null && ask.Length != width * height)
        {
            ask = null;
        }

        if (width <= 0 || height <= 0 || pitch <= 0)
        {
            return Empty;
        }

        // Grids with a border of one cell all round; see CellGrid.
        int pw = width + 2;
        int ph = height + 2;
        byte[] weight = new byte[pw * ph];
        byte[] level = new byte[pw * ph];
        Array.Fill(level, (byte)255);
        foreach (LensRectangle r in rectangles)
        {
            byte w = (byte)Math.Clamp((int)MathF.Round(r.Weight * 255f), 0, 255);
            if (w == 0)
            {
                continue;
            }

            byte l = (byte)Math.Clamp((int)MathF.Round(r.Level * 255f), 0, 255);
            int x0 = Math.Clamp(r.Left, 0, width);
            int x1 = Math.Clamp(r.Right, x0, width);
            int y0 = Math.Clamp(r.Top, 0, height);
            int y1 = Math.Clamp(r.Bottom, y0, height);
            for (int y = y0; y < y1; y++)
            {
                for (int i = ((y + 1) * pw) + x0 + 1, end = ((y + 1) * pw) + x1 + 1; i < end; i++)
                {
                    // Where rectangles overlap, the one that counts for more wins; between equals, the one
                    // that is easier to belong to.
                    if (w > weight[i] || (w == weight[i] && l < level[i]))
                    {
                        weight[i] = w;
                        level[i] = l;
                    }
                }
            }
        }

        // Inside: the cell and all eight around it belong to rectangles. Beyond the edge of the lens counts as
        // belonging, so a rectangle that reaches the edge is "inside" right up to it.
        byte[] scratch = new byte[pw * ph];
        byte[] inside = new byte[pw * ph];
        for (int i = 0; i < inside.Length; i++)
        {
            inside[i] = weight[i] > 0 ? (byte)1 : (byte)0;
        }

        CellGrid.Min3x3(inside, scratch, pw, ph, border: 1);

        // The ring around: cells with a rectangle next to them take its weight and its level.
        byte[] nearWeight = (byte[])weight.Clone();
        byte[] nearLevel = (byte[])level.Clone();
        CellGrid.Max3x3(nearWeight, scratch, pw, ph, border: 0);
        CellGrid.Min3x3(nearLevel, scratch, pw, ph, border: 255);

        byte[] cells = new byte[width * height * BytesPerCell];
        for (int y = 0; y < height; y++)
        {
            int c = y * width * BytesPerCell;
            int cell = y * width;
            for (int i = ((y + 1) * pw) + 1, end = ((y + 1) * pw) + width; i <= end; i++, c += BytesPerCell, cell++)
            {
                if (weight[i] > 0)
                {
                    cells[c] = weight[i];
                    cells[c + 1] = level[i];
                    cells[c + 2] = inside[i] == 0 ? (byte)0 : ask is not null && ask[cell] != 0 ? AskRegions : Inside;
                }
                else if (nearWeight[i] > 0)
                {
                    cells[c] = nearWeight[i];
                    cells[c + 1] = nearLevel[i];
                }
            }
        }

        return new RectangleMask(width, height, pitch, cells);
    }

    /// <summary>
    /// How much the rectangles give the pixel (<paramref name="x"/>, <paramref name="y"/>), 0..1.
    /// <paramref name="strength"/> is the pixel's own; see <see cref="LensConstants.RectangleStrength"/>.
    /// <paramref name="askRegions"/> comes back true where the caller has to leave out pixels that lie in a
    /// region of the other kind.
    /// </summary>
    public float Selected(int x, int y, float strength, out bool askRegions)
    {
        askRegions = false;
        if (IsEmpty)
        {
            return 0f;
        }

        int c = ((Math.Min(y / Pitch, Height - 1) * Width) + Math.Min(x / Pitch, Width - 1)) * BytesPerCell;
        float weight = Cells[c] / 255f;
        byte part = Cells[c + 2];
        if (part > 191)
        {
            return weight;
        }

        if (part > 63)
        {
            askRegions = true;
            return weight;
        }

        return strength >= (Cells[c + 1] / 255f) - LensConstants.RectangleHalfStep ? weight : 0f;
    }
}
