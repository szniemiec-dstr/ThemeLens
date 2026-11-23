// SPDX-License-Identifier: GPL-3.0-or-later
//
// The lens on the GPU.
//
// ThemeLens.Core.Rendering.ReferenceRenderer is the definition of what the lens shows. This file repeats it
// pass for pass, in the same order and with the same arithmetic, and the platform tests hold the two to the
// same pixels. If you change one, change the other.
//
//   PsFine            source            -> quarter size    Neighbourhood.Build, fine level (two targets)
//   PsCoarse          quarter           -> sixteenth       Neighbourhood.Build, coarse level
//   PsWide            sixteenth         -> sixty-fourth    Neighbourhood.Build, wide level
//   PsRecolourFinal   source            -> lens            ReferenceRenderer pass 2 when nothing else is switched on
//   PsRecolourMapped  source            -> "mapped"        ReferenceRenderer pass 2, with the area flag in alpha
//   PsShrink          any RGBA          -> a quarter of it ReferenceRenderer.Shrink (pass 3, run up to four times)
//   PsThick           quarter of mapped -> "thick"         ReferenceRenderer.Thick, for the area guard
//   PsBlurRows        mapped            -> row blur        ReferenceRenderer.BlurRows
//   PsFinish          mapped and copies -> lens            ReferenceRenderer pass 4
//
// Intermediate targets are R8G8B8A8_UNORM. Writing a float to one rounds it exactly as Srgb.ToByte does, which
// is the rounding the reference applies between passes. The row blur is the one exception: the reference keeps
// it in floats, so its target is R32_FLOAT.
//
// A captured frame is B8G8R8A8_UNORM. Load() hands back the channels by name whatever their order in memory,
// so .rgb below is red, green, blue; only the CPU code that fills and reads textures has to care about BGRA.
//
// Numbers that live in LensConstants arrive through the Rules buffer, so they cannot drift. The few literals
// below name the member they repeat.
//
// Two ways of reading between texels are used. Where the value read goes straight into a colour, the sampler
// does it (Sample). Where it is compared with a threshold (which region is this, is there enough of an area),
// Between() spells the arithmetic out: a sampler rounds the position between two texels to 1/256, and that is
// enough to tip a pixel to the other side.

static const int Block = 4;         // LensConstants.BlockSize
static const int CoarseScale = 16;  // LensConstants.CoarseScale
static const int WideScale = 64;    // LensConstants.WideScale
static const float LutSize = 33.0;  // Lut3D.Size
static const float Flare = 0.05;    // Wcag.Flare
static const float3 LuminanceWeights = float3(0.2126, 0.7152, 0.0722);  // Wcag.LuminanceWeights
static const float ThickWindow[5] = { 0.5, 1.0, 1.0, 1.0, 0.5 };        // LensConstants.ThickWindow

// One-to-one with FrameParameters, plus where the lens sits in the captured frame. Filled once per frame.
cbuffer Frame : register(b0)
{
    float3 PageEncoded;             // FrameParameters.PageEncoded
    float Direction;                // FrameParameters.Direction
    float3 TextEncoded;             // FrameParameters.TextEncoded
    float PageLuminance;            // FrameParameters.PageLuminance
    float MaxAreaContrast;          // FrameParameters.MaxAreaContrast
    float GuardStrength;            // FrameParameters.GuardStrength
    float GuardSoftGate;            // FrameParameters.GuardSoftGate
    float MaxLuminance;             // FrameParameters.MaxLuminance
    float WholeLensLightFraction;   // FrameParameters.WholeLensLightFraction
    float LocalContrast;            // FrameParameters.LocalContrast
    float Sharpen;                  // FrameParameters.Sharpen
    float StrongSharpen;            // FrameParameters.StrongSharpen
    int2 SourceOrigin;              // top-left corner of the lens inside the captured frame
    int2 SourceLimit;               // last texel of the captured frame
    int2 LensSize;                  // the lens, in pixels
    int2 FineSize;                  // quarter-size level: ceil(LensSize / 4)
    int2 CoarseSize;                // sixteenth-size level: ceil(FineSize / 4)
    int2 WideSize;                  // sixty-fourth-size level: ceil(CoarseSize / 4)
    float OneTable;                 // 1 when the light and dark tables are the same table
    float CoreEnough;               // LensConstants.GuardCoreEnough for this lens
    float2 FrameUnused;
};

// LensConstants. Filled once.
cbuffer Rules : register(b1)
{
    float LightVoteLow;             // LensConstants.LightVoteLow
    float LightVoteHigh;            // LensConstants.LightVoteHigh
    float DarkVoteLow;              // LensConstants.DarkVoteLow
    float DarkVoteHigh;             // LensConstants.DarkVoteHigh
    float WeakVote;                 // LensConstants.WeakVote
    float WeakVoteLow;              // LensConstants.WeakVoteLow
    float WeakVoteHigh;             // LensConstants.WeakVoteHigh
    float VoteConfidence;           // LensConstants.VoteConfidence
    float FlatRangeLow;             // LensConstants.FlatRangeLow
    float FlatRangeHigh;            // LensConstants.FlatRangeHigh
    float PolarityLow;              // LensConstants.PolarityLow
    float PolarityHigh;             // LensConstants.PolarityHigh
    float OwnVoteBias;              // LensConstants.OwnVoteBias
    float AreaFlagStart;            // LensConstants.AreaFlagStart
    float GuardLow;                 // LensConstants.GuardLow
    float GuardHigh;                // LensConstants.GuardHigh
    float UnanimousLow;             // LensConstants.UnanimousLow
    float GuardReach;               // LensConstants.GuardReach
    float GuardDetailGain;          // LensConstants.GuardDetailGain
    float SoftGateLow;              // LensConstants.SoftGateLow
    float SoftGateHigh;             // LensConstants.SoftGateHigh
    float SoftRangeLow;             // LensConstants.SoftRangeLow
    float SoftRangeMid;             // LensConstants.SoftRangeMid
    float SoftRangeHigh;            // LensConstants.SoftRangeHigh
    float SoftRangeMax;             // LensConstants.SoftRangeMax
    float LocalContrastGain;        // LensConstants.LocalContrastGain
    float SharpenGain;              // LensConstants.SharpenGain
    float StrongSharpenGain;        // LensConstants.StrongSharpenGain
    float HaloAllowance;            // LensConstants.HaloAllowance
    float StrongKernel4;            // LensConstants.StrongKernel[4]
    float UnanimousHigh;            // LensConstants.UnanimousHigh
    float GuardCoreStart;           // LensConstants.GuardCoreStart
    float4 StrongKernel0123;        // LensConstants.StrongKernel[0..3]
    float GuardCoreOffset;          // LensConstants.GuardCoreOffset
    float GuardCoreWindow;          // LensConstants.GuardCoreWindow
    float2 RulesUnused;
};

Texture2D<float4> Source : register(t0);        // the captured frame
Texture2D<float4> Fine : register(t1);          // light votes, dark votes, luma range
Texture2D<float4> Coarse : register(t2);        // light verdict, dark verdict, soft share
Texture2D<float4> Wide : register(t3);          // light verdict, dark verdict
Texture3D<float4> LutLight : register(t4);      // LensTables.Light
Texture3D<float4> LutDark : register(t5);       // LensTables.Dark
Texture2D<float4> Mapped : register(t6);        // recoloured picture, area flag in alpha
Texture2D<float4> MappedFine : register(t7);    // Mapped at a quarter of its size
Texture2D<float4> MappedCoarse : register(t8);  // Mapped at a sixteenth of its size
Texture2D<float> BlurredRows : register(t9);    // luma of Mapped, blurred along rows
Texture2D<float4> ShrinkSource : register(t10); // whatever PsShrink is asked to shrink
Texture2D<float4> Thick : register(t11);        // where flagged pixels form something thick, quarter size
Texture2D<float4> ThickWide : register(t12);    // Thick at a sixteenth of its size

SamplerState LinearClamp : register(s0);        // bilinear, clamp addressing: Bilinear.Sample and Lut3D.Sample

// ---------------------------------------------------------------------------------------------------------
// Helpers. Each one is the HLSL spelling of the C# function named beside it.
// ---------------------------------------------------------------------------------------------------------

// Srgb.Decode
float SrgbDecode(float encoded)
{
    return encoded <= 0.04045 ? encoded / 12.92 : pow(abs((encoded + 0.055) / 1.055), 2.4);
}

float3 SrgbDecode3(float3 encoded)
{
    return float3(SrgbDecode(encoded.r), SrgbDecode(encoded.g), SrgbDecode(encoded.b));
}

// Srgb.Encode
float SrgbEncode(float lin)
{
    return lin <= 0.0031308 ? lin * 12.92 : (1.055 * pow(abs(lin), 1.0 / 2.4)) - 0.055;
}

float3 SrgbEncode3(float3 lin)
{
    return float3(SrgbEncode(lin.r), SrgbEncode(lin.g), SrgbEncode(lin.b));
}

// Wcag.Luminance of an encoded colour
float Luminance(float3 encoded)
{
    return dot(SrgbDecode3(encoded), LuminanceWeights);
}

// LensConstants.Luma
float Luma(float3 encoded)
{
    return dot(encoded, LuminanceWeights);
}

// LensConstants.StrongLight
float StrongLight(float luminance)
{
    return smoothstep(LightVoteLow, LightVoteHigh, luminance);
}

// LensConstants.StrongDark
float StrongDark(float luminance)
{
    return 1.0 - smoothstep(DarkVoteLow, DarkVoteHigh, luminance);
}

// LensConstants.LightVote
float LightVote(float luminance)
{
    return max(StrongLight(luminance), WeakVote * smoothstep(WeakVoteLow, WeakVoteHigh, luminance));
}

// LensConstants.DarkVote
float DarkVote(float luminance)
{
    return max(StrongDark(luminance), WeakVote * (1.0 - smoothstep(WeakVoteLow, WeakVoteHigh, luminance)));
}

// LensConstants.Flatness
float Flatness(float range)
{
    return 1.0 - smoothstep(FlatRangeLow, FlatRangeHigh, range);
}

// LensConstants.SoftIndicator
float SoftIndicator(float range)
{
    return smoothstep(SoftRangeLow, SoftRangeMid, range) * (1.0 - smoothstep(SoftRangeHigh, SoftRangeMax, range));
}

// LensConstants.RefineLightFraction
float RefineLightFraction(float wider, float lightVotes, float darkVotes)
{
    float votes = lightVotes + darkVotes;
    float own = lightVotes / max(votes, 1e-4);
    return lerp(wider, own, saturate(votes));
}

// LensConstants.Verdict
float2 Verdict(float lightVotes, float darkVotes)
{
    float votes = lightVotes + darkVotes;
    float light = lightVotes / max(votes, 1e-4);
    float sure = saturate(votes / VoteConfidence);
    return float2(light * sure, (1.0 - light) * sure);
}

// LensConstants.LightFraction
float LightFraction(float wholeLens, float2 wideVerdict, float2 coarseVerdict)
{
    float wide = RefineLightFraction(wholeLens, wideVerdict.r, wideVerdict.g);
    float coarse = RefineLightFraction(wide, coarseVerdict.r, coarseVerdict.g);
    float unanimity = abs((2.0 * wide) - 1.0);
    float wideStands = smoothstep(UnanimousLow, UnanimousHigh, unanimity) * saturate(wideVerdict.r + wideVerdict.g);
    return lerp(coarse, wide, wideStands);
}

// LensConstants.Polarity
float Polarity(float lightFraction, float luminance)
{
    return smoothstep(PolarityLow, PolarityHigh, lightFraction + (OwnVoteBias * (StrongLight(luminance) - StrongDark(luminance))));
}

// LensConstants.StrongKernel[abs(k)]
float KernelWeight(int k)
{
    int i = abs(k);
    return i == 0 ? StrongKernel0123.x
        : i == 1 ? StrongKernel0123.y
        : i == 2 ? StrongKernel0123.z
        : i == 3 ? StrongKernel0123.w
        : StrongKernel4;
}

// ImageRgb8.Clamped: a pixel of the lens area, by lens coordinates. The second clamp only matters while the
// lens hangs over the edge of the captured frame, which the reference never sees.
float3 LoadSource(int2 xy)
{
    int2 p = clamp(xy, int2(0, 0), LensSize - 1) + SourceOrigin;
    p = clamp(p, int2(0, 0), SourceLimit);
    return Source.Load(int3(p, 0)).rgb;
}

// ReferenceRenderer.Texel
float3 LoadMapped(int2 xy)
{
    return Mapped.Load(int3(clamp(xy, int2(0, 0), LensSize - 1), 0)).rgb;
}

// Texture coordinates for Bilinear.Sample: the centre of lens pixel (x, y) in a texture whose texels each
// cover `scale` lens pixels. `centre` is (x + 0.5, y + 0.5), which is what SV_Position holds. The sampler then
// works out u = (x + 0.5) / scale - 0.5 in texel space by itself.
float2 ShrunkUv(float2 centre, int scale, int2 size)
{
    return centre / (float(scale) * float2(size));
}

// Bilinear.Sample, spelled out with four texels and the reference's own arithmetic. `centre` is as for ShrunkUv.
float4 Between(Texture2D<float4> tex, float2 centre, int scale, int2 size)
{
    float2 uv = (centre / float(scale)) - 0.5;
    float2 whole = floor(uv);
    float2 f = uv - whole;
    int2 last = size - 1;
    int2 a = clamp(int2(whole), int2(0, 0), last);
    int2 b = clamp(int2(whole) + 1, int2(0, 0), last);
    float4 p00 = tex.Load(int3(a.x, a.y, 0));
    float4 p10 = tex.Load(int3(b.x, a.y, 0));
    float4 p01 = tex.Load(int3(a.x, b.y, 0));
    float4 p11 = tex.Load(int3(b.x, b.y, 0));
    float4 top = p00 + ((p10 - p00) * f.x);
    float4 bottom = p01 + ((p11 - p01) * f.x);
    return top + ((bottom - top) * f.y);
}

// Lut3D.Sample: a 33-cubed table read between texel centres.
float3 SampleLut(Texture3D<float4> lut, float3 encoded)
{
    float3 uvw = (saturate(encoded) * ((LutSize - 1.0) / LutSize)) + (0.5 / LutSize);
    return lut.SampleLevel(LinearClamp, uvw, 0).rgb;
}

// FrameParameters.GuardOn
bool GuardOn()
{
    return GuardStrength > 0.0 && Direction != 0.0;
}

// ReferenceRenderer.AreaFlag
float AreaFlag(float3 encoded)
{
    float flag = 0.0;
    if (GuardOn())
    {
        float y = Luminance(encoded);
        float contrast = Direction > 0.0
            ? (y + Flare) / (PageLuminance + Flare)
            : (PageLuminance + Flare) / (y + Flare);
        flag = smoothstep(AreaFlagStart * MaxAreaContrast, MaxAreaContrast, contrast);
    }

    return flag;
}

// FinishMath.Clamp
float3 FinishClamp(float3 encoded)
{
    encoded = saturate(encoded);
    if (Direction > 0.0)
    {
        encoded = max(encoded, PageEncoded);
    }
    else if (Direction < 0.0)
    {
        encoded = min(encoded, PageEncoded);
    }

    if (MaxLuminance < 1.0)
    {
        float3 lin = SrgbDecode3(encoded);
        float y = dot(lin, LuminanceWeights);
        if (y > MaxLuminance)
        {
            encoded = SrgbEncode3(lin * (MaxLuminance / y));
        }
    }

    return encoded;
}

// ---------------------------------------------------------------------------------------------------------
// Vertex shader: one triangle that covers the whole target. No vertex buffer.
// ---------------------------------------------------------------------------------------------------------

float4 VsFullscreen(uint id : SV_VertexID) : SV_Position
{
    float2 corner = float2((id << 1) & 2, id & 2);
    return float4((corner * float2(2.0, -2.0)) + float2(-1.0, 1.0), 0.0, 1.0);
}

// ---------------------------------------------------------------------------------------------------------
// Pass 1: neighbourhood (Neighbourhood.Build).
// ---------------------------------------------------------------------------------------------------------

struct FineOutput
{
    float4 Votes : SV_Target0;      // FineLight, FineDark, FineRange, unused
    float4 Centre : SV_Target1;     // FinePoint (rgb), FineRange: everything the analysis thumbnail needs
};

FineOutput PsFine(float4 position : SV_Position)
{
    int2 corner = int2(position.xy) * Block;
    float light = 0.0;
    float dark = 0.0;
    float lo = 3.0e38;
    float hi = -3.0e38;
    for (int dy = 0; dy < Block; dy++)
    {
        for (int dx = 0; dx < Block; dx++)
        {
            float3 p = LoadSource(corner + int2(dx, dy));
            float luminance = Luminance(p);
            light += LightVote(luminance);
            dark += DarkVote(luminance);
            float luma = Luma(p);
            lo = min(lo, luma);
            hi = max(hi, luma);
        }
    }

    // Only flat blocks vote on what the background is.
    float range = hi - lo;
    float flatness = Flatness(range);
    FineOutput result;
    result.Votes = float4(flatness * light / float(Block * Block), flatness * dark / float(Block * Block), range, 0.0);
    result.Centre = float4(LoadSource(corner + (Block / 2)), range);
    return result;
}

float4 PsCoarse(float4 position : SV_Position) : SV_Target
{
    int2 corner = int2(position.xy) * Block;
    float light = 0.0;
    float dark = 0.0;
    float soft = 0.0;
    for (int dy = 0; dy < Block; dy++)
    {
        for (int dx = 0; dx < Block; dx++)
        {
            float4 f = Fine.Load(int3(min(corner + int2(dx, dy), FineSize - 1), 0));
            light += f.r;
            dark += f.g;
            soft += SoftIndicator(f.b);
        }
    }

    // Cells are compared by verdict, not by how many votes each happened to collect.
    float count = float(Block * Block);
    return float4(Verdict(light / count, dark / count), soft / count, 0.0);
}

float4 PsWide(float4 position : SV_Position) : SV_Target
{
    int2 corner = int2(position.xy) * Block;
    float light = 0.0;
    float dark = 0.0;
    for (int dy = 0; dy < Block; dy++)
    {
        for (int dx = 0; dx < Block; dx++)
        {
            float4 c = Coarse.Load(int3(min(corner + int2(dx, dy), CoarseSize - 1), 0));
            light += c.r;
            dark += c.g;
        }
    }

    float count = float(Block * Block);
    return float4(Verdict(light / count, dark / count), 0.0, 0.0);
}

// ---------------------------------------------------------------------------------------------------------
// Pass 2: recolour. One table lookup per pixel; the polarity check chooses between the two tables.
// ---------------------------------------------------------------------------------------------------------

// Neighbourhood.LightFractionAt
float LightFractionAt(float2 centre)
{
    float2 wideVerdict = Between(Wide, centre, WideScale, WideSize).rg;
    float2 coarseVerdict = Between(Coarse, centre, CoarseScale, CoarseSize).rg;
    return LightFraction(WholeLensLightFraction, wideVerdict, coarseVerdict);
}

float3 Recolour(float4 position)
{
    float3 encoded = LoadSource(int2(position.xy));
    float3 mapped = SampleLut(LutLight, encoded);
    if (OneTable < 0.5)
    {
        float polarity = Polarity(LightFractionAt(position.xy), Luminance(encoded));
        mapped = lerp(SampleLut(LutDark, encoded), mapped, polarity);
    }

    return mapped;
}

// Nothing else is switched on: this is the final picture.
float4 PsRecolourFinal(float4 position : SV_Position) : SV_Target
{
    return float4(FinishClamp(Recolour(position)), 1.0);
}

// The finish pass follows: keep the unclamped colour and flag pixels that an area may not consist of.
float4 PsRecolourMapped(float4 position : SV_Position) : SV_Target
{
    float3 o = Recolour(position);
    return float4(o, AreaFlag(o));
}

// ---------------------------------------------------------------------------------------------------------
// Pass 3: shrunken copies of the recoloured picture (ReferenceRenderer.Shrink), and the row blur.
// ---------------------------------------------------------------------------------------------------------

float4 PsShrink(float4 position : SV_Position) : SV_Target
{
    uint width;
    uint height;
    ShrinkSource.GetDimensions(width, height);
    int2 last = int2(width, height) - 1;
    int2 corner = int2(position.xy) * Block;

    // Sum whole bytes, as the reference does, so that a mean that lands exactly between two bytes rounds the same way.
    float4 sum = 0.0;
    for (int dy = 0; dy < Block; dy++)
    {
        for (int dx = 0; dx < Block; dx++)
        {
            sum += round(ShrinkSource.Load(int3(min(corner + int2(dx, dy), last), 0)) * 255.0);
        }
    }

    return sum / float(Block * Block) / 255.0;
}

// ReferenceRenderer.Thick: how thick the flagged pixels around each quarter-size texel are, from a window of
// 16 by 16 pixels. Written to all four channels, so that PsShrink can shrink it like any other picture.
float4 PsThick(float4 position : SV_Position) : SV_Target
{
    int2 xy = int2(position.xy);
    float sum = 0.0;
    float total = 0.0;
    for (int dy = -2; dy <= 2; dy++)
    {
        for (int dx = -2; dx <= 2; dx++)
        {
            float weight = ThickWindow[dx + 2] * ThickWindow[dy + 2];
            sum += weight * MappedFine.Load(int3(clamp(xy + int2(dx, dy), int2(0, 0), FineSize - 1), 0)).a;
            total += weight;
        }
    }

    float thick = smoothstep(GuardLow, GuardHigh, sum / total);
    return float4(thick, thick, thick, thick);
}

// ReferenceRenderer.BlurRows: the horizontal half of the strong-sharpen blur.
float PsBlurRows(float4 position : SV_Position) : SV_Target
{
    int2 xy = int2(position.xy);
    float sum = 0.0;
    for (int k = -4; k <= 4; k++)
    {
        sum += KernelWeight(k) * Luma(LoadMapped(xy + int2(k, 0)));
    }

    return sum;
}

// ---------------------------------------------------------------------------------------------------------
// Pass 4: finish. Area guard, local contrast, sharpen, then clamp to the theme's limits.
// ---------------------------------------------------------------------------------------------------------

// One tap of the thick map, for the pixel whose centre is given.
float ThickAt(float2 centre)
{
    return Thick.SampleLevel(LinearClamp, ShrunkUv(centre, Block, FineSize), 0).r;
}

float4 PsFinish(float4 position : SV_Position) : SV_Target
{
    int2 xy = int2(position.xy);
    float2 centre = position.xy;
    float3 o = LoadMapped(xy);
    float luma = Luma(o);
    float3 colour = o;
    float guard = 0.0;

    if (GuardOn())
    {
        // Is this pixel in or right beside something thick? Read here and at four places nearby, which covers
        // the rim of an area, where the thickness window is only half inside it.
        float reach = GuardReach;
        float isThick = ThickAt(centre);
        isThick = max(isThick, ThickAt(centre + float2(-reach, -reach)));
        isThick = max(isThick, ThickAt(centre + float2(reach, -reach)));
        isThick = max(isThick, ThickAt(centre + float2(-reach, reach)));
        isThick = max(isThick, ThickAt(centre + float2(reach, reach)));

        // And is there enough of it nearby to call it an area? Four taps of the smallest copy add up the thick
        // core over a wide window that slides with the pixel.
        float spread = GuardCoreOffset;
        float core = 0.25 * GuardCoreWindow * (
            Between(ThickWide, centre + float2(-spread, -spread), WideScale, WideSize).r
            + Between(ThickWide, centre + float2(spread, -spread), WideScale, WideSize).r
            + Between(ThickWide, centre + float2(-spread, spread), WideScale, WideSize).r
            + Between(ThickWide, centre + float2(spread, spread), WideScale, WideSize).r);

        guard = GuardStrength * isThick * smoothstep(GuardCoreStart * CoreEnough, CoreEnough, core);
        if (GuardSoftGate > 0.0)
        {
            // Neighbourhood.SoftFractionAt
            float softShare = Between(Coarse, centre, CoarseScale, CoarseSize).b;
            guard *= 1.0 - (GuardSoftGate * smoothstep(SoftGateLow, SoftGateHigh, softShare));
        }

        // The area becomes the page colour; whatever stands out from its surroundings is redrawn towards the text colour.
        float3 nearby = MappedFine.SampleLevel(LinearClamp, ShrunkUv(centre, Block, FineSize), 0).rgb;
        float detailRange = max(abs(Luma(TextEncoded) - Luma(PageEncoded)), 0.1);    // 0.1: floor in ReferenceRenderer.Render
        float detail = saturate(GuardDetailGain * abs(luma - Luma(nearby)) / detailRange);
        float3 redrawn = lerp(PageEncoded, TextEncoded, detail);
        colour = lerp(o, redrawn, guard);
    }

    float delta = 0.0;
    if (LocalContrast > 0.0)
    {
        float3 blurred = MappedCoarse.SampleLevel(LinearClamp, ShrunkUv(centre, CoarseScale, CoarseSize), 0).rgb;
        delta += LocalContrastGain * LocalContrast * (luma - Luma(blurred));
    }

    if (Sharpen > 0.0 || StrongSharpen > 0.0)
    {
        float north = Luma(LoadMapped(xy + int2(0, -1)));
        float south = Luma(LoadMapped(xy + int2(0, 1)));
        float west = Luma(LoadMapped(xy + int2(-1, 0)));
        float east = Luma(LoadMapped(xy + int2(1, 0)));
        float lo = min(luma, min(min(north, south), min(west, east)));
        float hi = max(luma, max(max(north, south), max(west, east)));

        if (Sharpen > 0.0)
        {
            // Five samples, on lightness only. Strength falls where the edge is already crisp.
            float crisp = smoothstep(0.35, 0.85, hi - lo);                           // 0.35, 0.85: ReferenceRenderer.Render
            float strength = SharpenGain * Sharpen * lerp(1.0, 0.4, crisp);          // 1, 0.4: ReferenceRenderer.Render
            delta += strength * (luma - ((north + south + west + east) * 0.25));
        }

        if (StrongSharpen > 0.0)
        {
            // The vertical half of the blur, over the rows PsBlurRows prepared.
            float blurred = 0.0;
            for (int k = -4; k <= 4; k++)
            {
                blurred += KernelWeight(k) * BlurredRows.Load(int3(xy.x, clamp(xy.y + k, 0, LensSize.y - 1), 0));
            }

            // Halo limiting: the result may leave the local range only by a small allowance.
            float raw = luma + (StrongSharpenGain * StrongSharpen * (luma - blurred));
            float limited = clamp(raw, lo - HaloAllowance, hi + HaloAllowance);
            delta += limited - luma;
        }
    }

    // Contrast and sharpen add to lightness only, and stay out of guarded areas.
    colour += delta * (1.0 - guard);

    // Clamp last: a sharpening halo can never exceed the brightness limit.
    return float4(FinishClamp(colour), 1.0);
}

// ---------------------------------------------------------------------------------------------------------
// Not part of the reference.
// ---------------------------------------------------------------------------------------------------------

// FrameParameters.Identity: the lens area exactly as it was captured.
float4 PsCopy(float4 position : SV_Position) : SV_Target
{
    return float4(LoadSource(int2(position.xy)), 1.0);
}
