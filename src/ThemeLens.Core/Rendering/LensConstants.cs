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
    /// A pixel counts as "light" when its luminance is above this band and as "dark" when it is below the dark
    /// band. Mid-tones in between are neither: a blue button or a green pill is not a light page and not a dark
    /// one, and follows whatever surrounds it.
    /// </summary>
    public const float LightVoteLow = 0.36f;
    public const float LightVoteHigh = 0.40f;
    public const float DarkVoteLow = 0.09f;
    public const float DarkVoteHigh = 0.11f;

    /// <summary>
    /// For the whole-lens figure only: a mid-tone votes with this weight, light or dark by which side of the
    /// band it is on. The band sits where designers switch from light text to dark text on a background. It
    /// matters where nothing clearly light or dark is in sight, such as text on a mid-grey page.
    /// </summary>
    public const float WeakVote = 0.02f;
    public const float WeakVoteLow = 0.14f;
    public const float WeakVoteHigh = 0.24f;

    /// <summary>
    /// For the whole-lens figure only: a 4×4 block votes in full when its luma range is below the first value
    /// and not at all above the second.
    /// </summary>
    public const float FlatRangeLow = 0.04f;
    public const float FlatRangeHigh = 0.10f;

    /// <summary>
    /// A region has a core: a window of 20 by 20 pixels (five quarter-size texels each way) in which one kind,
    /// light or dark, clearly outnumbers the other. This is the radius of that window in texels.
    /// </summary>
    /// <remarks>
    /// The window is what tells a region from content. A dark band 16 pixels thick fills 16/20 of it and is a
    /// region: a chip, a button, a toolbar, a code block, with whatever is written on it. A dark icon of 16
    /// pixels, a thick rule or a letter never fills it and is content of the page around it. Because the shares
    /// are counted in pixels, the answer does not change as a shape moves across the 4-pixel grid.
    /// </remarks>
    public const int CoreRadius = 2;

    /// <summary>
    /// One kind makes a core when its share of the light and dark pixels in the window passes this band. The
    /// band lies between 15/20 and 16/20, so that no whole number of pixels falls inside it.
    /// </summary>
    public const float CoreShareLow = 0.76f;
    public const float CoreShareHigh = 0.79f;

    /// <summary>A window says nothing unless this much of it is clearly light or clearly dark (a photo or a blue banner is neither).</summary>
    public const float CoreVotesLow = 0.35f;
    public const float CoreVotesHigh = 0.65f;

    /// <summary>
    /// A region takes in a 4×4 block, and may spread through it, when about half of the block or more is of
    /// the region's kind. Text on a dark panel leaves most of each block dark, so the panel keeps its text.
    /// </summary>
    public const float ThroughLow = 0.35f;
    public const float ThroughHigh = 0.65f;

    /// <summary>
    /// How often a region spreads from the blocks it has (along rows, then columns, and so on), and how many
    /// blocks it may travel in one go. Four turns reach round two corners; 255 blocks is the whole of most lenses.
    /// </summary>
    public const int SpreadPasses = 4;
    public const int SpreadReach = 255;

    /// <summary>When this share of a neighbourhood belongs to regions, its verdict is trusted fully; with less, a wider neighbourhood is asked.</summary>
    public const float VoteConfidence = 0.08f;

    /// <summary>Outside any region, a pixel is in light surroundings when more of the regions nearby are light than dark.</summary>
    public const float PolarityLow = 0.47f;
    public const float PolarityHigh = 0.53f;

    /// <summary>How much a pixel's own lightness shifts that decision where light and dark regions are equally near.</summary>
    public const float OwnVoteBias = 0.05f;

    /// <summary>The area flag rises from 0 to 1 over this share of the theme's maximum area contrast.</summary>
    public const float AreaFlagStart = 0.8f;

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

    /// <summary>1 for a clearly light pixel, 0 otherwise.</summary>
    public static float StrongLight(float luminance) => Smooth.Step(LightVoteLow, LightVoteHigh, luminance);

    /// <summary>1 for a clearly dark pixel, 0 otherwise.</summary>
    public static float StrongDark(float luminance) => 1f - Smooth.Step(DarkVoteLow, DarkVoteHigh, luminance);

    /// <summary>The vote a pixel casts for "this lens shows a light background", for the whole-lens figure.</summary>
    public static float LightVote(float luminance) =>
        MathF.Max(StrongLight(luminance), WeakVote * Smooth.Step(WeakVoteLow, WeakVoteHigh, luminance));

    /// <summary>The vote a pixel casts for "this lens shows a dark background", for the whole-lens figure.</summary>
    public static float DarkVote(float luminance) =>
        MathF.Max(StrongDark(luminance), WeakVote * (1f - Smooth.Step(WeakVoteLow, WeakVoteHigh, luminance)));

    /// <summary>How much of its vote a 4×4 block may cast in the whole-lens figure, from the luma range inside it.</summary>
    public static float Flatness(float range) => 1f - Smooth.Step(FlatRangeLow, FlatRangeHigh, range);

    /// <summary>
    /// Is there a core here? Takes the shares of clearly light and clearly dark pixels in the window and gives
    /// back how much of a light core and how much of a dark core it is.
    /// </summary>
    public static (float Light, float Dark) Core(float lightShare, float darkShare)
    {
        float votes = lightShare + darkShare;
        float light = lightShare / MathF.Max(votes, 1e-4f);
        float sure = Smooth.Step(CoreVotesLow, CoreVotesHigh, votes);
        return (Smooth.Step(CoreShareLow, CoreShareHigh, light) * sure, Smooth.Step(CoreShareLow, CoreShareHigh, 1f - light) * sure);
    }

    /// <summary>How freely a region may pass through a block of which this share is of the region's kind.</summary>
    public static float Through(float ownShare) => Smooth.Step(ThroughLow, ThroughHigh, ownShare);

    /// <summary>
    /// Refines a light fraction with the verdict of a closer neighbourhood: the surer that neighbourhood is,
    /// the more its own verdict replaces <paramref name="wider"/>.
    /// </summary>
    public static float RefineLightFraction(float wider, float lightVotes, float darkVotes)
    {
        float votes = lightVotes + darkVotes;
        float own = lightVotes / MathF.Max(votes, 1e-4f);
        return Smooth.Lerp(wider, own, Smooth.Saturate(votes));
    }

    /// <summary>
    /// Turns the votes of one neighbourhood cell into its verdict: the two results add up to how sure the cell
    /// is (1 once <see cref="VoteConfidence"/> of it has voted) and share that out between light and dark.
    /// </summary>
    /// <remarks>
    /// Cells are compared by verdict, not by how many votes they happened to collect, so that a region with
    /// few members in one place counts as much as a solid one beside it.
    /// </remarks>
    public static (float Light, float Dark) Verdict(float lightVotes, float darkVotes)
    {
        float votes = lightVotes + darkVotes;
        float light = lightVotes / MathF.Max(votes, 1e-4f);
        float sure = Smooth.Saturate(votes / VoteConfidence);
        return (light * sure, (1f - light) * sure);
    }

    /// <summary>
    /// For a pixel outside any region: the share of light among the regions around it. Asked first is the
    /// window right around its block, then the coarse and the wide neighbourhood, and where no region is in
    /// sight at all, the figure for the whole lens.
    /// </summary>
    public static float LightFraction(
        float wholeLens, float wideLight, float wideDark, float coarseLight, float coarseDark, float localLight, float localDark)
    {
        float wide = RefineLightFraction(wholeLens, wideLight, wideDark);
        float coarse = RefineLightFraction(wide, coarseLight, coarseDark);
        return RefineLightFraction(coarse, localLight, localDark);
    }

    /// <summary>
    /// 1 where a pixel's surroundings are light, 0 where they are dark. <paramref name="lightFraction"/> is
    /// from <see cref="LightFraction"/>; <paramref name="luminance"/> is the pixel's own, which settles a tie.
    /// </summary>
    public static float Surroundings(float lightFraction, float luminance) =>
        Smooth.Step(PolarityLow, PolarityHigh, lightFraction + (OwnVoteBias * (StrongLight(luminance) - StrongDark(luminance))));

    /// <summary>
    /// The table a pixel is recoloured with: 1 for the light-region table, 0 for the dark-region one.
    /// </summary>
    /// <remarks>
    /// A pixel whose block belongs to one region takes that region's table, whatever its own colour: text on a
    /// dark panel is part of the dark panel. Where the block also belongs to, or touches, a region of the other
    /// kind, it is on the edge between the two, and each pixel joins the region of its own kind. A block that
    /// only touches a region gives it its pixels of that kind. Everything else is content and takes the table
    /// of its <paramref name="surroundings"/>.
    /// </remarks>
    public static float Polarity(float surroundings, float homeLight, float homeDark, float edgeLight, float edgeDark, float luminance)
    {
        float isLight = StrongLight(luminance);
        float isDark = StrongDark(luminance);
        float towardsDark = MathF.Max(homeDark * Smooth.Lerp(1f, isDark, MathF.Max(homeLight, edgeLight)), edgeDark * isDark);
        float towardsLight = MathF.Max(homeLight * Smooth.Lerp(1f, isLight, MathF.Max(homeDark, edgeDark)), edgeLight * isLight);
        return Smooth.Lerp(Smooth.Lerp(surroundings, 0f, towardsDark), 1f, towardsLight);
    }

    public static float SoftIndicator(float range) =>
        Smooth.Step(SoftRangeLow, SoftRangeMid, range) * (1f - Smooth.Step(SoftRangeHigh, SoftRangeMax, range));

    /// <summary>Rec. 709 luma of an encoded colour. Cheap, and good enough for edge and detail measures.</summary>
    public static float Luma(System.Numerics.Vector3 encoded) => System.Numerics.Vector3.Dot(encoded, Wcag.LuminanceWeights);
}
