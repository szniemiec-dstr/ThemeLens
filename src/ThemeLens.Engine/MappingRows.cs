// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Core.Analysis;
using ThemeLens.Core.Colour;
using ThemeLens.Core.Mapping;
using ThemeLens.Core.Themes;

namespace ThemeLens.Engine;

/// <summary>
/// One row of "Colours on screen now, and what you see" in the control window (figure 5): a colour found under
/// the lens with its name and hex value, how much of the lens it covers, the role it was given, and the colour
/// it is shown as. Nothing here relies on colour alone.
/// </summary>
public sealed record MappingRow
{
    public required Rgb8 Found { get; init; }

    public required ColourName FoundName { get; init; }

    /// <summary>Share of the lens area, 0..1.</summary>
    public required float Share { get; init; }

    public required Role Role { get; init; }

    public required RegionPolarity Region { get; init; }

    public required Rgb8 Shown { get; init; }

    public required ColourName ShownName { get; init; }

    /// <summary>
    /// Contrast of the shown colour against the shown page, for roles that are read against the page (text,
    /// quiet text, links and status colours). Null for backgrounds.
    /// </summary>
    public float? ContrastAgainstPage { get; init; }

    /// <summary>True when the user chose this role; it stays until "Reset mapping".</summary>
    public bool Pinned { get; init; }

    /// <summary>True when the colour is used as a filled area and so keeps its own lightness (see docs/decisions.md, D4).</summary>
    public bool IsFill { get; init; }

    public string FoundHex => Found.ToHex();

    public string ShownHex => Shown.ToHex();

    /// <summary>Whole per cent, at least 1 for anything that is on screen at all.</summary>
    public int SharePercent => Share <= 0f ? 0 : Math.Max(1, (int)MathF.Round(Share * 100f));
}

public static class MappingRows
{
    /// <summary>The rows for the current mapping, largest colour first.</summary>
    public static IReadOnlyList<MappingRow> From(LensMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        LensTables tables = mapping.Tables;
        float page = tables.Parameters.PageLuminance;
        var rows = new List<MappingRow>(mapping.Roles.Entries.Count);
        foreach (RoleEntry entry in mapping.Roles.Entries)
        {
            Rgb8 shown = tables.Map(entry.Cluster.Colour, entry.Region);
            bool readAgainstPage = tables.Parameters.Direction != 0f && entry.Role is not (Role.Page or Role.Panel);
            rows.Add(new MappingRow
            {
                Found = entry.Cluster.Colour,
                FoundName = ColourName.Of(entry.Cluster.Colour),
                Share = entry.Cluster.Share,
                Role = entry.Role,
                Region = entry.Region,
                Shown = shown,
                ShownName = ColourName.Of(shown),
                ContrastAgainstPage = readAgainstPage ? Wcag.Contrast(Wcag.Luminance(shown), page) : null,
                Pinned = entry.Pinned,
                IsFill = entry.IsFill && entry.Role.IsThemedAccent(),
            });
        }

        return rows;
    }

    /// <summary>
    /// The pins after the user picks <paramref name="role"/> for a row. Choosing a role pins the row; choosing
    /// it again for an already pinned row changes the pin.
    /// </summary>
    public static IReadOnlyList<Pin> Pin(IReadOnlyList<Pin> pins, MappingRow row, Role role)
    {
        ArgumentNullException.ThrowIfNull(pins);
        ArgumentNullException.ThrowIfNull(row);
        Oklab lab = Oklab.FromRgb8(row.Found);
        var result = pins
            .Where(p => p.Region != row.Region || Oklab.Distance(Oklab.FromRgb8(p.Source), lab) > RoleAssigner.PinTolerance)
            .ToList();
        result.Add(new Pin(row.Found, row.Region, role));
        return result;
    }

    /// <summary>The pins after one row's pin is removed.</summary>
    public static IReadOnlyList<Pin> Unpin(IReadOnlyList<Pin> pins, MappingRow row)
    {
        ArgumentNullException.ThrowIfNull(pins);
        ArgumentNullException.ThrowIfNull(row);
        Oklab lab = Oklab.FromRgb8(row.Found);
        return pins
            .Where(p => p.Region != row.Region || Oklab.Distance(Oklab.FromRgb8(p.Source), lab) > RoleAssigner.PinTolerance)
            .ToList();
    }
}
