// SPDX-License-Identifier: GPL-3.0-or-later
using System.Numerics;
using System.Runtime.InteropServices;
using ThemeLens.Core.Rendering;

namespace ThemeLens.Platform.Windows;

/// <summary>
/// The <c>Frame</c> constant buffer of <c>lens.hlsl</c>, field for field. HLSL packs a buffer into rows of four
/// floats and never lets a vector straddle two rows; every group below is exactly one row, so the plain
/// sequential layout of this struct is the layout the shader reads.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct FrameConstants
{
    public const int SizeInBytes = 8 * 16;

    // Row 0
    public Vector3 PageEncoded;
    public float Direction;

    // Row 1
    public Vector3 TextEncoded;
    public float PageLuminance;

    // Row 2
    public float MaxAreaContrast;
    public float GuardStrength;
    public float GuardSoftGate;
    public float MaxLuminance;

    // Row 3
    public float WholeLensLightFraction;
    public float LocalContrast;
    public float Sharpen;
    public float StrongSharpen;

    // Row 4
    public int SourceOriginX;
    public int SourceOriginY;
    public int SourceLimitX;
    public int SourceLimitY;

    // Row 5
    public int LensWidth;
    public int LensHeight;
    public int FineWidth;
    public int FineHeight;

    // Row 6
    public int CoarseWidth;
    public int CoarseHeight;
    public int WideWidth;
    public int WideHeight;

    // Row 7
    public float OneTable;
    public float Unused0;
    public float Unused1;
    public float Unused2;
}

/// <summary>The <c>Rules</c> constant buffer of <c>lens.hlsl</c>: every number the shader takes from <see cref="LensConstants"/>.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct RuleConstants
{
    public const int SizeInBytes = 9 * 16;

    // Row 0
    public float LightVoteLow;
    public float LightVoteHigh;
    public float DarkVoteLow;
    public float DarkVoteHigh;

    // Row 1
    public float WeakVote;
    public float WeakVoteLow;
    public float WeakVoteHigh;
    public float VoteConfidence;

    // Row 2
    public float FlatRangeLow;
    public float FlatRangeHigh;
    public float PolarityLow;
    public float PolarityHigh;

    // Row 3
    public float OwnVoteBias;
    public float AreaFlagStart;
    public float GuardLow;
    public float GuardHigh;

    // Row 4
    public float GuardWindowOffset;
    public float GuardReach;
    public float GuardDetailGain;
    public float SoftGateLow;

    // Row 5
    public float SoftGateHigh;
    public float SoftRangeLow;
    public float SoftRangeMid;
    public float SoftRangeHigh;

    // Row 6
    public float SoftRangeMax;
    public float LocalContrastGain;
    public float SharpenGain;
    public float StrongSharpenGain;

    // Row 7
    public float HaloAllowance;
    public float StrongKernel4;
    public float Unused0;
    public float Unused1;

    // Row 8
    public Vector4 StrongKernel0123;

    public static RuleConstants FromLensConstants()
    {
        // lens.hlsl repeats these four as literals, because its loops need them at compile time.
        if (LensConstants.BlockSize != 4 || LensConstants.CoarseScale != 16 || LensConstants.WideScale != 64
            || LensConstants.StrongKernel.Length != 5)
        {
            throw new InvalidOperationException(
                "lens.hlsl assumes 4×4 blocks and a nine-tap kernel. LensConstants changed; change the shader with it.");
        }

        return new RuleConstants
        {
            LightVoteLow = LensConstants.LightVoteLow,
            LightVoteHigh = LensConstants.LightVoteHigh,
            DarkVoteLow = LensConstants.DarkVoteLow,
            DarkVoteHigh = LensConstants.DarkVoteHigh,
            WeakVote = LensConstants.WeakVote,
            WeakVoteLow = LensConstants.WeakVoteLow,
            WeakVoteHigh = LensConstants.WeakVoteHigh,
            VoteConfidence = LensConstants.VoteConfidence,
            FlatRangeLow = LensConstants.FlatRangeLow,
            FlatRangeHigh = LensConstants.FlatRangeHigh,
            PolarityLow = LensConstants.PolarityLow,
            PolarityHigh = LensConstants.PolarityHigh,
            OwnVoteBias = LensConstants.OwnVoteBias,
            AreaFlagStart = LensConstants.AreaFlagStart,
            GuardLow = LensConstants.GuardLow,
            GuardHigh = LensConstants.GuardHigh,
            GuardWindowOffset = LensConstants.GuardWindowOffset,
            GuardReach = LensConstants.GuardReach,
            GuardDetailGain = LensConstants.GuardDetailGain,
            SoftGateLow = LensConstants.SoftGateLow,
            SoftGateHigh = LensConstants.SoftGateHigh,
            SoftRangeLow = LensConstants.SoftRangeLow,
            SoftRangeMid = LensConstants.SoftRangeMid,
            SoftRangeHigh = LensConstants.SoftRangeHigh,
            SoftRangeMax = LensConstants.SoftRangeMax,
            LocalContrastGain = LensConstants.LocalContrastGain,
            SharpenGain = LensConstants.SharpenGain,
            StrongSharpenGain = LensConstants.StrongSharpenGain,
            HaloAllowance = LensConstants.HaloAllowance,
            StrongKernel4 = LensConstants.StrongKernel[4],
            StrongKernel0123 = new Vector4(
                LensConstants.StrongKernel[0],
                LensConstants.StrongKernel[1],
                LensConstants.StrongKernel[2],
                LensConstants.StrongKernel[3]),
        };
    }
}
