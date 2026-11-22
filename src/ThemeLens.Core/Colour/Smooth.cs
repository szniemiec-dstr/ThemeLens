// SPDX-License-Identifier: GPL-3.0-or-later
namespace ThemeLens.Core.Colour;

/// <summary>Scalar helpers that have direct HLSL equivalents, so the reference renderer reads like the shader.</summary>
public static class Smooth
{
    public static float Saturate(float x) => Math.Clamp(x, 0f, 1f);

    public static float Lerp(float a, float b, float t) => a + ((b - a) * t);

    /// <summary>HLSL <c>smoothstep</c>: 0 below <paramref name="edge0"/>, 1 above <paramref name="edge1"/>.</summary>
    public static float Step(float edge0, float edge1, float x)
    {
        float t = Saturate((x - edge0) / (edge1 - edge0));
        return t * t * (3f - (2f * t));
    }
}
