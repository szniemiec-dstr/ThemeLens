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

    /// <summary>
    /// When the wide neighbourhood agrees with itself this strongly (0 is evenly split, 1 is unanimous), its
    /// verdict stands, whatever the coarse neighbourhood says. A small solid shape, such as a dark icon or a
    /// thick rule on a light page, is then content to be recoloured and not a little dark region of its own.
    /// Where the wide neighbourhood is split, two regions meet nearby and the coarse one draws the line.
    /// </summary>
    public const float UnanimousLow = 0.35f;
    public const float UnanimousHigh = 0.65f;

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
    /// The guard asks two things of an area: that it is thick, and that there is enough of it.
    /// Thick: a place starts to count as "thick" when this share of the 16 by 16 pixel window around it is
    /// flagged, and is fully thick at the second value. Text, lines and a band of 12 pixels never are; a band
    /// of 17 pixels is.
    /// </summary>
    public const float GuardLow = 0.80f;
    public const float GuardHigh = 0.95f;

    /// <summary>
    /// Weights of the window that measures thickness, along each axis, in quarter-size texels (4 pixels each):
    /// three whole texels and half of one on either side make 16 pixels, centred.
    /// </summary>
    public static readonly float[] ThickWindow = [0.5f, 1f, 1f, 1f, 0.5f];

    /// <summary>The guard also acts within this distance of a thick place, which covers the rim of an area.</summary>
    public const float GuardReach = 8f;

    /// <summary>
    /// Enough of it: the guard acts fully when the thick core of the shapes nearby adds up to this many pixels,
    /// which a solid square of about 32 pixels has, and a long band of 17 pixels easily has. A checkbox, a status
    /// dot or a small badge stays as it is. The guard starts to act at <see cref="GuardCoreStart"/> of this.
    /// </summary>
    public const float GuardCoreArea = 400f;
    public const float GuardCoreStart = 0.375f;

    /// <summary>
    /// On a small lens the core area that counts as "enough" shrinks with the lens, to this share of its pixels,
    /// so that the guard still catches an area before it reaches half a per cent of the lens (FR-10).
    /// </summary>
    public const float GuardCoreShare = 0.0016f;

    /// <summary>The thick core that counts as "enough of it" for a lens of this size, in pixels.</summary>
    public static float GuardCoreEnough(int lensWidth, int lensHeight) =>
        MathF.Min(GuardCoreArea, GuardCoreShare * lensWidth * lensHeight);

    /// <summary>Half the distance between the four taps that add up the thick core nearby.</summary>
    public const float GuardCoreOffset = WideScale / 2f;

    /// <summary>The four taps together weigh the thick core over an area of this many pixels.</summary>
    public const float GuardCoreWindow = 4f * WideScale * WideScale;

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
        return Smooth.Lerp(wider, own, Smooth.Saturate(votes));
    }

    /// <summary>
    /// Turns the raw votes of one neighbourhood cell into its verdict: the two results add up to how sure the
    /// cell is (1 once <see cref="VoteConfidence"/> of it has voted) and share that out between light and dark.
    /// </summary>
    /// <remarks>
    /// Cells are compared by verdict, not by how many votes they happened to collect. A light region full of
    /// text casts far fewer votes than a clean dark one, and counting raw votes would let the dark side win a
    /// few pixels beyond the true boundary, which shows as a bright line along it.
    /// </remarks>
    public static (float Light, float Dark) Verdict(float lightVotes, float darkVotes)
    {
        float votes = lightVotes + darkVotes;
        float light = lightVotes / MathF.Max(votes, 1e-4f);
        float sure = Smooth.Saturate(votes / VoteConfidence);
        return (light * sure, (1f - light) * sure);
    }

    /// <summary>
    /// The share of light votes that decides a pixel's region, from the verdicts of the wide and the coarse
    /// neighbourhood (see <see cref="Verdict"/>) and the figure for the whole lens (used where nobody nearby voted).
    /// </summary>
    public static float LightFraction(float wholeLens, float wideLight, float wideDark, float coarseLight, float coarseDark)
    {
        float wide = RefineLightFraction(wholeLens, wideLight, wideDark);
        float coarse = RefineLightFraction(wide, coarseLight, coarseDark);
        float unanimity = MathF.Abs((2f * wide) - 1f);
        float wideStands = Smooth.Step(UnanimousLow, UnanimousHigh, unanimity) * Smooth.Saturate(wideLight + wideDark);
        return Smooth.Lerp(coarse, wide, wideStands);
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
