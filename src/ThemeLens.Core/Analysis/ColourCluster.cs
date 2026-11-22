// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Core.Colour;

namespace ThemeLens.Core.Analysis;

/// <summary>Whether a part of the screen is light with dark content, or dark with light content.</summary>
public enum RegionPolarity
{
    Light,
    Dark,
}

/// <summary>One group of similar colours found under the lens, with the features the role rules use (section 6.1).</summary>
public sealed record ColourCluster
{
    /// <summary>The colour that stands for the cluster: its most common exact colour, or its mean when no single colour dominates.</summary>
    public required Rgb8 Colour { get; init; }

    public required Oklab Lab { get; init; }

    public required float Luminance { get; init; }

    /// <summary>Share of the lens area, 0..1.</summary>
    public required float Share { get; init; }

    /// <summary>Share of this cluster's pixels that lie in light regions of the screen.</summary>
    public required float LightRegionShare { get; init; }

    /// <summary>Share of this cluster's pixels that sit next to a different colour. Text and lines are near 1, flat areas near 0.</summary>
    public required float Thinness { get; init; }

    /// <summary>Share of the lens border that this cluster covers.</summary>
    public required float EdgeTouch { get; init; }

    public float Chroma => Lab.Chroma;

    public float HueDegrees => Lab.HueDegrees;

    public RegionPolarity Region => LightRegionShare >= 0.5f ? RegionPolarity.Light : RegionPolarity.Dark;
}

/// <summary>The result of one analysis of the lens area.</summary>
public sealed record SceneAnalysis
{
    public static readonly SceneAnalysis Empty = new()
    {
        Clusters = [],
        MeanLuminance = 0.5f,
        MedianLightness = 0.5f,
        LightFraction = 0.5f,
    };

    /// <summary>At most <see cref="ClusterFinder.MaxClusters"/> clusters, largest first.</summary>
    public required IReadOnlyList<ColourCluster> Clusters { get; init; }

    /// <summary>Mean WCAG luminance of the lens area. A large jump means the scene changed.</summary>
    public required float MeanLuminance { get; init; }

    /// <summary>Median of the cube root of luminance; the pivot for contrast on the keep base.</summary>
    public required float MedianLightness { get; init; }

    /// <summary>Share of light votes among all background votes in the lens area: above one half, the screen is mostly light.</summary>
    public required float LightFraction { get; init; }
}
