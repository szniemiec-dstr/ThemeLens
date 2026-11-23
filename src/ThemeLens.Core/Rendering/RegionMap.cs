// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Core.Colour;

namespace ThemeLens.Core.Rendering;

/// <summary>
/// Which cells of a grid belong to a light region, which to a dark one, and which are content of whatever is
/// around them. The renderer runs it on 4×4 blocks of the lens, the analysis on its thumbnail; on the GPU each
/// step below is one small render pass, and values are rounded to 8 bits between steps exactly as a render
/// target would.
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>Core: a window in which one kind, light or dark, clearly outnumbers the other (<see cref="LensConstants.Core"/>).</item>
/// <item>Seed: cells a core's window covers, if at least about half of the cell is of the core's kind.</item>
/// <item>Spread: a region grows from its seeds along rows and columns through cells of its kind. This is what
/// makes the gap between two dark buttons part of the light page, however far it is from open page.</item>
/// <item>Edge and local: for every cell, whether a region cell is right beside it, and what the regions close
/// by are, for content that is in none.</item>
/// </list>
/// </remarks>
public sealed class RegionMap
{
    private RegionMap(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>How much each cell belongs to a light region outright.</summary>
    public byte[] HomeLight { get; private init; } = [];

    public byte[] HomeDark { get; private init; } = [];

    /// <summary>How much each cell has a light-region cell right beside it.</summary>
    public byte[] EdgeLight { get; private init; } = [];

    public byte[] EdgeDark { get; private init; } = [];

    /// <summary>The verdict (<see cref="LensConstants.Verdict"/>) on the region cells in the window around each cell.</summary>
    public byte[] LocalLight { get; private init; } = [];

    public byte[] LocalDark { get; private init; } = [];

    /// <param name="light">Per cell, the share of clearly light pixels, 0 to 255.</param>
    /// <param name="dark">Per cell, the share of clearly dark pixels.</param>
    /// <param name="width">Cells per row.</param>
    /// <param name="height">Rows.</param>
    /// <param name="radius">Radius of the core window in cells; the window is 2 × radius + 1 cells wide.</param>
    public static RegionMap Build(byte[] light, byte[] dark, int width, int height, int radius)
    {
        ArgumentNullException.ThrowIfNull(light);
        ArgumentNullException.ThrowIfNull(dark);
        int n = width * height;

        // Window sums are sums of whole bytes, divided once: exact whatever order they are added in, so the
        // GPU and this class agree to the last bit and the sums can come from a summed-area table.
        float windowTotal = ((2 * radius) + 1) * ((2 * radius) + 1) * 255f;

        // Core.
        long[] lightSums = PaddedSums(light, width, height, radius);
        long[] darkSums = PaddedSums(dark, width, height, radius);
        byte[] coreLight = new byte[n];
        byte[] coreDark = new byte[n];
        Parallel.For(0, height, j =>
        {
            for (int i = 0; i < width; i++)
            {
                (float lightCore, float darkCore) = LensConstants.Core(
                    WindowSum(lightSums, width, radius, i, j) / windowTotal,
                    WindowSum(darkSums, width, radius, i, j) / windowTotal);
                coreLight[(j * width) + i] = Srgb.ToByte(lightCore);
                coreDark[(j * width) + i] = Srgb.ToByte(darkCore);
            }
        });

        // Seed. "Through" is how freely a region of each kind may pass through the cell.
        byte[] lightReach = WindowMax(coreLight, width, height, radius);
        byte[] darkReach = WindowMax(coreDark, width, height, radius);
        byte[] throughLight = new byte[n];
        byte[] throughDark = new byte[n];
        byte[] homeLight = new byte[n];
        byte[] homeDark = new byte[n];
        for (int index = 0; index < n; index++)
        {
            float lightThrough = LensConstants.Through(light[index] / 255f);
            float darkThrough = LensConstants.Through(dark[index] / 255f);
            throughLight[index] = Srgb.ToByte(lightThrough);
            throughDark[index] = Srgb.ToByte(darkThrough);
            homeLight[index] = Srgb.ToByte(lightReach[index] / 255f * lightThrough);
            homeDark[index] = Srgb.ToByte(darkReach[index] / 255f * darkThrough);
        }

        // Spread, along rows and along columns in turn.
        for (int pass = 0; pass < LensConstants.SpreadPasses; pass++)
        {
            bool alongRows = pass % 2 == 0;
            homeLight = Spread(homeLight, throughLight, width, height, alongRows);
            homeDark = Spread(homeDark, throughDark, width, height, alongRows);
        }

        // Edge and local.
        long[] homeLightSums = PaddedSums(homeLight, width, height, radius);
        long[] homeDarkSums = PaddedSums(homeDark, width, height, radius);
        byte[] lightAround = WindowMax(homeLight, width, height, 1, skipCentre: true);
        byte[] darkAround = WindowMax(homeDark, width, height, 1, skipCentre: true);
        byte[] localLight = new byte[n];
        byte[] localDark = new byte[n];
        Parallel.For(0, height, j =>
        {
            for (int i = 0; i < width; i++)
            {
                (float lightVerdict, float darkVerdict) = LensConstants.Verdict(
                    WindowSum(homeLightSums, width, radius, i, j) / windowTotal,
                    WindowSum(homeDarkSums, width, radius, i, j) / windowTotal);
                localLight[(j * width) + i] = Srgb.ToByte(lightVerdict);
                localDark[(j * width) + i] = Srgb.ToByte(darkVerdict);
            }
        });

        return new RegionMap(width, height)
        {
            HomeLight = homeLight,
            HomeDark = homeDark,
            EdgeLight = lightAround,
            EdgeDark = darkAround,
            LocalLight = localLight,
            LocalDark = localDark,
        };
    }

    /// <summary>
    /// A summed-area table of the grid with its edge cells repeated <paramref name="radius"/> times outwards,
    /// which is what clamp addressing reads. One row and one column larger than the padded grid.
    /// </summary>
    private static long[] PaddedSums(byte[] values, int width, int height, int radius)
    {
        int paddedWidth = width + (2 * radius);
        int paddedHeight = height + (2 * radius);
        long[] sums = new long[(paddedWidth + 1) * (paddedHeight + 1)];
        for (int y = 0; y < paddedHeight; y++)
        {
            int row = Math.Clamp(y - radius, 0, height - 1) * width;
            long along = 0;
            for (int x = 0; x < paddedWidth; x++)
            {
                along += values[row + Math.Clamp(x - radius, 0, width - 1)];
                sums[((y + 1) * (paddedWidth + 1)) + x + 1] = sums[(y * (paddedWidth + 1)) + x + 1] + along;
            }
        }

        return sums;
    }

    /// <summary>The sum of the window of the given radius around cell (i, j), from <see cref="PaddedSums"/>.</summary>
    private static float WindowSum(long[] sums, int width, int radius, int i, int j)
    {
        int stride = width + (2 * radius) + 1;
        int size = (2 * radius) + 1;
        return sums[((j + size) * stride) + i + size] - sums[(j * stride) + i + size] - sums[((j + size) * stride) + i] + sums[(j * stride) + i];
    }

    /// <summary>
    /// The largest value in the window around each cell, with clamp addressing. A largest value does not care
    /// in which order it is looked for, so rows are done first and columns after.
    /// </summary>
    /// <param name="skipCentre">
    /// True to leave the cell itself out, as the edge test does. At the edge of the grid clamping still reads
    /// the cell in place of the neighbours that are not there, which is what the shader's loads do.
    /// </param>
    private static byte[] WindowMax(byte[] values, int width, int height, int radius, bool skipCentre = false)
    {
        byte[] output = new byte[values.Length];
        if (skipCentre)
        {
            Parallel.For(0, height, j =>
            {
                for (int i = 0; i < width; i++)
                {
                    byte best = 0;
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        int row = Math.Clamp(j + dy, 0, height - 1) * width;
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            if (dx != 0 || dy != 0)
                            {
                                best = Math.Max(best, values[row + Math.Clamp(i + dx, 0, width - 1)]);
                            }
                        }
                    }

                    output[(j * width) + i] = best;
                }
            });
            return output;
        }

        byte[] rows = new byte[values.Length];
        Parallel.For(0, height, j =>
        {
            int row = j * width;
            for (int i = 0; i < width; i++)
            {
                byte best = 0;
                int from = Math.Max(i - radius, 0);
                int to = Math.Min(i + radius, width - 1);
                for (int x = from; x <= to; x++)
                {
                    best = Math.Max(best, values[row + x]);
                }

                rows[row + i] = best;
            }
        });
        Parallel.For(0, height, j =>
        {
            int from = Math.Max(j - radius, 0);
            int to = Math.Min(j + radius, height - 1);
            for (int i = 0; i < width; i++)
            {
                byte best = 0;
                for (int y = from; y <= to; y++)
                {
                    best = Math.Max(best, rows[(y * width) + i]);
                }

                output[(j * width) + i] = best;
            }
        });
        return output;
    }

    /// <summary>
    /// One spread pass for one kind: every cell looks along its row (or column) in both directions, as far as
    /// cells of its kind go, and joins the region if it finds one.
    /// </summary>
    private static byte[] Spread(byte[] home, byte[] through, int width, int height, bool alongRows)
    {
        byte[] output = new byte[home.Length];
        int stepIndex = alongRows ? 1 : width;
        Parallel.For(0, height, j =>
        {
            for (int i = 0; i < width; i++)
            {
                int index = (j * width) + i;
                float own = home[index] / 255f;
                float open = through[index] / 255f;
                float best = 0;
                if (open > own)
                {
                    int position = alongRows ? i : j;
                    int last = (alongRows ? width : height) - 1;
                    for (int direction = -1; direction <= 1; direction += 2)
                    {
                        float path = open;
                        for (int k = 1; k <= LensConstants.SpreadReach; k++)
                        {
                            int at = position + (direction * k);
                            if (at < 0 || at > last || path <= best)
                            {
                                break;
                            }

                            int c = index + (direction * k * stepIndex);
                            best = MathF.Max(best, MathF.Min(path, home[c] / 255f));
                            path = MathF.Min(path, through[c] / 255f);
                        }
                    }
                }

                output[index] = Srgb.ToByte(MathF.Max(own, best));
            }
        });
        return output;
    }
}
