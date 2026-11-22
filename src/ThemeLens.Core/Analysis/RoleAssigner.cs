// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Core.Colour;
using ThemeLens.Core.Themes;

namespace ThemeLens.Core.Analysis;

/// <summary>
/// A mapping the user corrected in the control window. It wins over the automatic choice until
/// "Reset mapping" and never changes on its own (section 6.2, FR-09).
/// </summary>
public sealed record Pin(Rgb8 Source, RegionPolarity Region, Role Role);

/// <summary>One row of the control window's table: a colour found on screen and the role it plays.</summary>
public sealed record RoleEntry
{
    public required ColourCluster Cluster { get; init; }

    public required Role Role { get; init; }

    public RegionPolarity Region => Cluster.Region;

    /// <summary>
    /// True when this cluster fixes a point of the mapping: its colour is shown exactly as the theme's colour
    /// for the role. Other clusters with the same role follow the curve between the anchors.
    /// </summary>
    public required bool IsAnchor { get; init; }

    public bool Pinned { get; init; }

    /// <summary>
    /// True when the colour is used as a filled area (a button, a bar, a pill) and not as text or line work.
    /// A fill takes the theme's hue for its role but keeps the lightness the general rule gives it, so that it
    /// stays a background and the label on it stays readable.
    /// </summary>
    public bool IsFill => Cluster.Thinness < RoleAssigner.ThinEnoughForForeground;
}

/// <summary>Which on-screen colour plays which role, separately for the light and the dark regions of the screen.</summary>
public sealed class RoleMap
{
    public static readonly RoleMap Empty = new([]);

    public RoleMap(IReadOnlyList<RoleEntry> entries)
    {
        Entries = entries;
    }

    /// <summary>Largest cluster first.</summary>
    public IReadOnlyList<RoleEntry> Entries { get; }

    public RoleEntry? Anchor(RegionPolarity region, Role role)
    {
        foreach (RoleEntry e in Entries)
        {
            if (e.IsAnchor && e.Role == role && e.Region == region)
            {
                return e;
            }
        }

        return null;
    }

    public IEnumerable<RoleEntry> Anchors(RegionPolarity region) =>
        Entries.Where(e => e.IsAnchor && e.Region == region);
}

/// <summary>Gives each cluster a role (section 6.2).</summary>
public static class RoleAssigner
{
    /// <summary>Below this chroma a colour is treated as a neutral: a background or ordinary text.</summary>
    public const float NeutralChroma = 0.07f;

    /// <summary>A pin applies to a cluster when their colours are this close in OKLab.</summary>
    public const float PinTolerance = 0.05f;

    private const float MinTextContrast = 2.5f;
    private const float MaxPanelAnchorContrast = 1.8f;

    /// <summary>From this thinness a cluster is line work or text; below it, a filled area.</summary>
    public const float ThinEnoughForForeground = 0.45f;

    public static RoleMap Assign(SceneAnalysis analysis, IReadOnlyList<Pin>? pins = null)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        pins ??= [];
        var entries = new List<RoleEntry>();
        foreach (RegionPolarity region in (ReadOnlySpan<RegionPolarity>)[RegionPolarity.Light, RegionPolarity.Dark])
        {
            AssignRegion(analysis.Clusters.Where(c => c.Region == region).ToList(), region, pins, entries);
        }

        entries.Sort(static (a, b) =>
        {
            int byShare = b.Cluster.Share.CompareTo(a.Cluster.Share);
            return byShare != 0 ? byShare : string.CompareOrdinal(a.Cluster.Colour.ToHex(), b.Cluster.Colour.ToHex());
        });
        return new RoleMap(entries);
    }

    /// <summary>The hue family of a strongly coloured cluster: blue, green, yellow-orange, red, or none of them.</summary>
    public static Role HueFamily(float hueDegrees) => hueDegrees switch
    {
        >= 345f or < 48f => Role.Bad,
        < 115f => Role.Warning,
        < 185f => Role.Good,
        >= 195f and < 300f => Role.Link,
        _ => Role.OtherAccent,
    };

    /// <summary>
    /// True when a thin coloured cluster is just the blend between a larger coloured cluster and a larger neutral
    /// one: the colour of anti-aliased edges where the two meet.
    /// </summary>
    private static bool IsEdgeBlend(ColourCluster c, List<ColourCluster> all)
    {
        if (c.Thinness < 0.8f || c.Chroma < NeutralChroma * 0.5f)
        {
            return false;
        }

        foreach (ColourCluster coloured in all)
        {
            // The other end of the blend is a purer version of the same colour. With thin text it can cover less
            // area than its own edges do, so it only has to be present, not larger.
            if (ReferenceEquals(coloured, c) || coloured.Share < c.Share * 0.15f || coloured.Chroma <= c.Chroma * 1.1f)
            {
                continue;
            }

            foreach (ColourCluster neutral in all)
            {
                // The neutral partner has to be a background: an edge blends a shape with what it sits on.
                if (neutral.Share < c.Share || neutral.Chroma >= NeutralChroma || neutral.Thinness >= 0.5f)
                {
                    continue;
                }

                // Where does c fall on the straight line from the coloured cluster to the neutral one?
                float dl = neutral.Lab.L - coloured.Lab.L;
                float da = neutral.Lab.A - coloured.Lab.A;
                float db = neutral.Lab.B - coloured.Lab.B;
                float length = (dl * dl) + (da * da) + (db * db);
                if (length < 1e-4f)
                {
                    continue;
                }

                float t = (((c.Lab.L - coloured.Lab.L) * dl) + ((c.Lab.A - coloured.Lab.A) * da) + ((c.Lab.B - coloured.Lab.B) * db)) / length;
                if (t is < 0.1f or > 0.9f)
                {
                    continue;
                }

                var onLine = new Oklab(coloured.Lab.L + (dl * t), coloured.Lab.A + (da * t), coloured.Lab.B + (db * t));
                if (Oklab.Distance(onLine, c.Lab) <= 0.04f)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void AssignRegion(List<ColourCluster> clusters, RegionPolarity region, IReadOnlyList<Pin> pins, List<RoleEntry> output)
    {
        var role = new Dictionary<ColourCluster, Role>();
        var anchors = new Dictionary<Role, ColourCluster>();
        var pinned = new HashSet<ColourCluster>();

        // Pins first. A pin for a colour that is not on screen right now still holds its place in the table.
        foreach (Pin pin in pins.Where(p => p.Region == region))
        {
            Oklab lab = Oklab.FromRgb8(pin.Source);
            ColourCluster? match = clusters
                .Where(c => !pinned.Contains(c) && Oklab.Distance(c.Lab, lab) <= PinTolerance)
                .OrderBy(c => Oklab.Distance(c.Lab, lab))
                .FirstOrDefault();
            if (match is null)
            {
                match = new ColourCluster
                {
                    Colour = pin.Source,
                    Lab = lab,
                    Luminance = Wcag.Luminance(pin.Source),
                    Share = 0f,
                    LightRegionShare = region == RegionPolarity.Light ? 1f : 0f,
                    Thinness = 0f,
                    EdgeTouch = 0f,
                };
                clusters.Add(match);
            }

            pinned.Add(match);
            role[match] = pin.Role;
            if (pin.Role != Role.OtherAccent)
            {
                anchors.TryAdd(pin.Role, match);
            }
        }

        bool lightRegion = region == RegionPolarity.Light;
        List<ColourCluster> free = clusters.Where(c => !pinned.Contains(c)).ToList();

        // Page: largest share, low chroma, touches the edges.
        if (!anchors.TryGetValue(Role.Page, out ColourCluster? page))
        {
            page = free
                .Where(c => c.Chroma < NeutralChroma)
                .OrderByDescending(c => c.Share * (1f + c.EdgeTouch))
                .FirstOrDefault();
            if (page is not null)
            {
                role[page] = Role.Page;
                anchors[Role.Page] = page;
                free.Remove(page);
            }
        }

        // Text: highest contrast against Page among thin clusters, on the far side of the page.
        if (!anchors.TryGetValue(Role.Text, out ColourCluster? text) && page is not null)
        {
            text = free
                .Where(c => c.Chroma < NeutralChroma
                    && (lightRegion ? c.Luminance < page.Luminance : c.Luminance > page.Luminance)
                    && Wcag.Contrast(c.Luminance, page.Luminance) >= MinTextContrast)
                .OrderByDescending(c => Wcag.Contrast(c.Luminance, page.Luminance) * (c.Thinness >= 0.5f ? 1f : 0.5f))
                .FirstOrDefault();
            if (text is not null)
            {
                role[text] = Role.Text;
                anchors[Role.Text] = text;
                free.Remove(text);
            }
        }

        // Panel and Quiet text: remaining low-chroma clusters, split by which side of the midpoint between
        // Page and Text they fall on.
        if (page is not null)
        {
            float pageJ = MathF.Cbrt(page.Luminance);
            float textJ = text is not null ? MathF.Cbrt(text.Luminance) : (lightRegion ? 0.25f : 0.9f);
            float mid = (pageJ + textJ) / 2f;
            foreach (ColourCluster c in free.Where(c => c.Chroma < NeutralChroma).ToList())
            {
                float j = MathF.Cbrt(c.Luminance);
                bool pageSide = lightRegion ? j >= mid : j <= mid;
                Role r = pageSide ? Role.Panel : Role.QuietText;
                role[c] = r;
                free.Remove(c);

                bool canAnchor = r == Role.Panel
                    ? Wcag.Contrast(c.Luminance, page.Luminance) <= MaxPanelAnchorContrast
                    : c.Thinness >= 0.4f;
                if (canAnchor)
                {
                    anchors.TryAdd(r, c);
                }
            }
        }

        // Link, Good, Warning, Bad: strongly coloured clusters by hue family. A family can have one colour used
        // as line work or text and one used as a fill (link text and a button in the same blue family, say);
        // any further colour of that family is an Other accent and keeps its own hue.
        var accentAnchors = new HashSet<ColourCluster>();
        var taken = new HashSet<(Role Family, bool Fill)>();
        foreach (ColourCluster c in pinned)
        {
            if (role[c].IsThemedAccent())
            {
                taken.Add((role[c], c.Thinness < ThinEnoughForForeground));
            }
        }

        var edges = new HashSet<ColourCluster>();
        foreach (ColourCluster c in free.OrderByDescending(c => c.Share).ToList())
        {
            if (IsEdgeBlend(c, clusters))
            {
                // The anti-aliased rim of a coloured shape. It follows the general rule, which keeps the rim
                // between the shape and its background; as an accent of its own it would glow.
                role[c] = Role.OtherAccent;
                edges.Add(c);
                continue;
            }

            Role family = c.Chroma >= NeutralChroma ? HueFamily(c.HueDegrees) : Role.OtherAccent;
            if (family != Role.OtherAccent && taken.Add((family, c.Thinness < ThinEnoughForForeground)))
            {
                role[c] = family;
                accentAnchors.Add(c);
            }
            else
            {
                role[c] = Role.OtherAccent;
            }
        }

        foreach (ColourCluster c in clusters)
        {
            Role r = role.GetValueOrDefault(c, Role.OtherAccent);
            output.Add(new RoleEntry
            {
                Cluster = c,
                Role = r,
                // Other accents are anchors too: they hold their own hue against the pull of a neighbouring accent.
                IsAnchor = r == Role.OtherAccent
                    ? c.Chroma >= NeutralChroma && !edges.Contains(c)
                    : accentAnchors.Contains(c) || (anchors.TryGetValue(r, out ColourCluster? a) && ReferenceEquals(a, c)),
                Pinned = pinned.Contains(c),
            });
        }
    }
}
