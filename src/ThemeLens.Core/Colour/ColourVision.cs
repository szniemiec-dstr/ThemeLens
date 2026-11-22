// SPDX-License-Identifier: GPL-3.0-or-later
using System.Numerics;

namespace ThemeLens.Core.Colour;

public enum ColourVisionDeficiency
{
    Protanopia,
    Deuteranopia,
    Tritanopia,
}

/// <summary>
/// Simulation of dichromatic colour vision with the matrices of Machado, Oliveira and Fernandes (2009),
/// "A physiologically-based model for simulation of color vision deficiency", at severity 1.0.
/// The matrices apply to linear sRGB. They are used by tests and by the theme preview; the lens itself never
/// simulates a deficiency.
/// </summary>
public static class ColourVision
{
    public static Vector3 Simulate(Vector3 linear, ColourVisionDeficiency deficiency)
    {
        Vector3 r = deficiency switch
        {
            ColourVisionDeficiency.Protanopia => new Vector3(
                Vector3.Dot(linear, new Vector3(0.152286f, 1.052583f, -0.204868f)),
                Vector3.Dot(linear, new Vector3(0.114503f, 0.786281f, 0.099216f)),
                Vector3.Dot(linear, new Vector3(-0.003882f, -0.048116f, 1.051998f))),
            ColourVisionDeficiency.Deuteranopia => new Vector3(
                Vector3.Dot(linear, new Vector3(0.367322f, 0.860646f, -0.227968f)),
                Vector3.Dot(linear, new Vector3(0.280085f, 0.672501f, 0.047413f)),
                Vector3.Dot(linear, new Vector3(-0.011820f, 0.042940f, 0.968881f))),
            ColourVisionDeficiency.Tritanopia => new Vector3(
                Vector3.Dot(linear, new Vector3(1.255528f, -0.076749f, -0.178779f)),
                Vector3.Dot(linear, new Vector3(-0.078411f, 0.930809f, 0.147602f)),
                Vector3.Dot(linear, new Vector3(0.004733f, 0.691367f, 0.303900f))),
            _ => throw new ArgumentOutOfRangeException(nameof(deficiency)),
        };

        return Vector3.Clamp(r, Vector3.Zero, Vector3.One);
    }

    public static Rgb8 Simulate(Rgb8 colour, ColourVisionDeficiency deficiency) =>
        Srgb.FromLinear(Simulate(Srgb.ToLinear(colour), deficiency));
}
