// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Core.Colour;

namespace ThemeLens.Core.Rendering;

/// <summary>
/// Numbers shared by the reference renderer, the analysis and the GPU shader. The shader receives them through
/// its constant buffer or repeats them as literals; <c>lens.hlsl</c> names the constant next to each literal.
/// </summary>
public static class LensConstants
{
    /// <summary>Each neighbourhood level shrinks the previous one by this factor in both directions.</summary>
    public const int BlockSize = 4;

    /// <summary>Source pixels per texel of the coarse neighbourhood level (two shrinks).</summary>
    public const int CoarseScale = BlockSize * BlockSize;

    /// <summary>Source pixels per texel of the wide neighbourhood level (three shrinks).</summary>
    public const int WideScale = CoarseScale * BlockSize;

    /// <summary>
    /// A pixel votes "light" when its luminance is above this band and "dark" when it is below the dark band.
    /// Mid-tones in between only get a weak vote: a blue button or a green pill is neither a light page nor a
    /// dark one, and follows whatever surrounds it.
    /// </summary>
    /// <remarks>
    /// The bands are narrow on purpose. A flat colour inside a band would vote half-heartedly, and then the
    /// middle of a large shape (which only hears its own vote) could come out differently from its rim (which
    /// also hears the page).
    /// </remarks>
    public const float LightVoteLow = 0.36f;
    public const float LightVoteHigh = 0.40f;
    public const float DarkVoteLow = 0.09f;
    public const float DarkVoteHigh = 0.11f;

    /// <summary>
    /// A mid-tone votes with this weight, light or dark by which side of the band it is on. The band sits where
    /// designers switch from light text to dark text on a background. A weak vote only matters where nothing
    /// clearly light or dark is nearby, such as text on a mid-grey page.
    /// </summary>
    public const float WeakVote = 0.02f;
    public const float WeakVoteLow = 0.14f;
    public const float WeakVoteHigh = 0.24f;

    /// <summary>
    /// Only flat areas vote: a 4×4 block casts its votes in full when its luma range is below the first value
    /// and not at all above the second. Text and edges say nothing about what the background is.
    /// </summary>
    public const float FlatRangeLow = 0.04f;
    public const float FlatRangeHigh = 0.10f;

    /// <summary>When this share of a neighbourhood has voted, its verdict is trusted fully; with fewer votes a wider neighbourhood is asked.</summary>
    public const float VoteConfidence = 0.08f;

    /// <summary>A region is light when more of the nearby votes are light than dark.</summary>
    public const float PolarityLow = 0.47f;
    public const float PolarityHigh = 0.53f;

    /// <summary>
    /// How much a pixel's own vote shifts the region decision. Where a light and a dark region meet, this makes
    /// each pixel join the region of its own kind, so the boundary gets no glowing band.
    /// </summary>
    public const float OwnVoteBias = 0.05f;

    /// <summary>The area flag rises from 0 to 1 over this share of the theme's maximum area contrast.</summary>
    public const float AreaFlagStart = 0.8f;

    /// <summary>
    /// The guard looks at a window of about 16 by 16 pixels around each pixel. It starts to act when this share
    /// of the window is flagged and acts fully at the second value, so anything that contains a solid square of
    /// about 17 pixels is treated as an area, and text and lines are left alone.
    /// </summary>
    public const float GuardLow = 0.65f;
    public const float GuardHigh = 0.85f;

    /// <summary>Half the guard window, in lens pixels: the four taps that make up the window sit this far from its centre.</summary>
    public const float GuardWindowOffset = 4f;

    /// <summary>The guard also acts within this distance of a flagged window, which covers the rim of an area.</summary>
    public const float GuardReach = 8f;

    /// <summary>Thin detail keeps its full strength when an area is pulled back to the page colour.</summary>
    public const float GuardDetailGain = 1.6f;

    /// <summary>Balanced mode lets an area through when this share of it has soft variation, as photos do.</summary>
    public const float SoftGateLow = 0.30f;
    public const float SoftGateHigh = 0.60f;

    /// <summary>A 4×4 block counts as "soft" when its luma range lies inside this band: not flat, not a hard edge.</summary>
    public const float SoftRangeLow = 0.012f;
    public const float SoftRangeMid = 0.04f;
    public const float SoftRangeHigh = 0.25f;
    public const float SoftRangeMax = 0.45f;

    /// <summary>Strength of local contrast at 100%.</summary>
    public const float LocalContrastGain = 0.9f;

    /// <summary>Strength of standard sharpening at 100%.</summary>
    public const float SharpenGain = 1.4f;

    /// <summary>Strength of strong sharpening at 100%.</summary>
    public const float StrongSharpenGain = 2.2f;

    /// <summary>Strong sharpening may overshoot the local range by this much (encoded luma) before it is limited.</summary>
    public const float HaloAllowance = 0.04f;

    /// <summary>Gaussian weights of the strong-sharpen kernel, centre first (9 taps, sigma about 2).</summary>
    public static readonly float[] StrongKernel = [0.2042f, 0.1802f, 0.1238f, 0.0663f, 0.0276f];

    private static float StrongLight(float luminance) => Smooth.Step(LightVoteLow, LightVoteHigh, luminance);

    private static float StrongDark(float luminance) => 1f - Smooth.Step(DarkVoteLow, DarkVoteHigh, luminance);

    /// <summary>The vote a pixel casts for "this is a light background".</summary>
    public static float LightVote(float luminance) =>
        MathF.Max(StrongLight(luminance), WeakVote * Smooth.Step(WeakVoteLow, WeakVoteHigh, luminance));

    /// <summary>The vote a pixel casts for "this is a dark background".</summary>
    public static float DarkVote(float luminance) =>
        MathF.Max(StrongDark(luminance), WeakVote * (1f - Smooth.Step(WeakVoteLow, WeakVoteHigh, luminance)));

    /// <summary>How much of its vote a 4×4 block may cast, from the luma range inside it.</summary>
    public static float Flatness(float range) => 1f - Smooth.Step(FlatRangeLow, FlatRangeHigh, range);

    /// <summary>
    /// Refines a light fraction with the votes of a closer neighbourhood: the more of that neighbourhood voted,
    /// the more its own verdict replaces <paramref name="wider"/>.
    /// </summary>
    public static float RefineLightFraction(float wider, float lightVotes, float darkVotes)
    {
        float votes = lightVotes + darkVotes;
        float own = lightVotes / MathF.Max(votes, 1e-4f);
        return Smooth.Lerp(wider, own, Smooth.Saturate(votes / VoteConfidence));
    }

    /// <summary>
    /// 1 where the source region is light, 0 where it is dark. <paramref name="lightFraction"/> is the share of
    /// light votes nearby; <paramref name="luminance"/> is this pixel's own, which settles a tie.
    /// </summary>
    public static float Polarity(float lightFraction, float luminance) =>
        Smooth.Step(PolarityLow, PolarityHigh, lightFraction + (OwnVoteBias * (StrongLight(luminance) - StrongDark(luminance))));

    public static float SoftIndicator(float range) =>
        Smooth.Step(SoftRangeLow, SoftRangeMid, range) * (1f - Smooth.Step(SoftRangeHigh, SoftRangeMax, range));

    /// <summary>Rec. 709 luma of an encoded colour. Cheap, and good enough for edge and detail measures.</summary>
    public static float Luma(System.Numerics.Vector3 encoded) => System.Numerics.Vector3.Dot(encoded, Wcag.LuminanceWeights);
}
