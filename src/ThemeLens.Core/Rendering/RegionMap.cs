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
        float count = ((2 * radius) + 1) * ((2 * radius) + 1);

        // Core.
        byte[] coreLight = new byte[n];
        byte[] coreDark = new byte[n];
        Parallel.For(0, height, j =>
        {
            for (int i = 0; i < width; i++)
            {
                float lightSum = 0;
                float darkSum = 0;
                for (int dy = -radius; dy <= radius; dy++)
                {
                    int row = Math.Clamp(j + dy, 0, height - 1) * width;
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int c = row + Math.Clamp(i + dx, 0, width - 1);
                        lightSum += light[c] / 255f;
                        darkSum += dark[c] / 255f;
                    }
                }

                (float lightCore, float darkCore) = LensConstants.Core(lightSum / count, darkSum / count);
                coreLight[(j * width) + i] = Srgb.ToByte(lightCore);
                coreDark[(j * width) + i] = Srgb.ToByte(darkCore);
            }
        });

        // Seed. "Through" is how freely a region of each kind may pass through the cell.
        byte[] throughLight = new byte[n];
        byte[] throughDark = new byte[n];
        byte[] homeLight = new byte[n];
        byte[] homeDark = new byte[n];
        Parallel.For(0, height, j =>
        {
            for (int i = 0; i < width; i++)
            {
                float lightReach = 0;
                float darkReach = 0;
                for (int dy = -radius; dy <= radius; dy++)
                {
                    int row = Math.Clamp(j + dy, 0, height - 1) * width;
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int c = row + Math.Clamp(i + dx, 0, width - 1);
                        lightReach = MathF.Max(lightReach, coreLight[c] / 255f);
                        darkReach = MathF.Max(darkReach, coreDark[c] / 255f);
                    }
                }

                int index = (j * width) + i;
                float lightThrough = LensConstants.Through(light[index] / 255f);
                float darkThrough = LensConstants.Through(dark[index] / 255f);
                throughLight[index] = Srgb.ToByte(lightThrough);
                throughDark[index] = Srgb.ToByte(darkThrough);
                homeLight[index] = Srgb.ToByte(lightReach * lightThrough);
                homeDark[index] = Srgb.ToByte(darkReach * darkThrough);
            }
        });

        // Spread, along rows and along columns in turn.
        for (int pass = 0; pass < LensConstants.SpreadPasses; pass++)
        {
            bool alongRows = pass % 2 == 0;
            homeLight = Spread(homeLight, throughLight, width, height, alongRows);
            homeDark = Spread(homeDark, throughDark, width, height, alongRows);
        }

        // Edge and local.
        byte[] edgeLight = new byte[n];
        byte[] edgeDark = new byte[n];
        byte[] localLight = new byte[n];
        byte[] localDark = new byte[n];
        Parallel.For(0, height, j =>
        {
            for (int i = 0; i < width; i++)
            {
                float lightBeside = 0;
                float darkBeside = 0;
                float lightSum = 0;
                float darkSum = 0;
                for (int dy = -radius; dy <= radius; dy++)
                {
                    int row = Math.Clamp(j + dy, 0, height - 1) * width;
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int c = row + Math.Clamp(i + dx, 0, width - 1);
                        float lightHome = homeLight[c] / 255f;
                        float darkHome = homeDark[c] / 255f;
                        lightSum += lightHome;
                        darkSum += darkHome;
                        if (Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1 && (dx != 0 || dy != 0))
                        {
                            lightBeside = MathF.Max(lightBeside, lightHome);
                            darkBeside = MathF.Max(darkBeside, darkHome);
                        }
                    }
                }

                int index = (j * width) + i;
                edgeLight[index] = Srgb.ToByte(lightBeside);
                edgeDark[index] = Srgb.ToByte(darkBeside);
                (float lightVerdict, float darkVerdict) = LensConstants.Verdict(lightSum / count, darkSum / count);
                localLight[index] = Srgb.ToByte(lightVerdict);
                localDark[index] = Srgb.ToByte(darkVerdict);
            }
        });

        return new RegionMap(width, height)
        {
            HomeLight = homeLight,
            HomeDark = homeDark,
            EdgeLight = edgeLight,
            EdgeDark = edgeDark,
            LocalLight = localLight,
            LocalDark = localDark,
        };
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
