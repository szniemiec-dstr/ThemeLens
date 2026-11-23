// SPDX-License-Identifier: GPL-3.0-or-later
namespace ThemeLens.Core.Colour;

/// <summary>How light, dark or muted a colour is, as a word.</summary>
public enum ColourTone
{
    Plain,
    Pale,
    Soft,
    Dark,
}

/// <summary>The family a colour belongs to, as a word. The first eight are neutrals.</summary>
public enum ColourFamily
{
    White,
    OffWhite,
    LightGrey,
    Grey,
    DarkGrey,
    NearBlack,
    Charcoal,
    Black,
    Red,
    Orange,
    Yellow,
    Green,
    Teal,
    Blue,
    Purple,
    Pink,
}

/// <summary>
/// A plain-language name for a colour, such as "Pale blue" or "Near black". The control window shows one next
/// to every swatch, together with the hex value, so that it never relies on colour alone (section 4.1).
/// </summary>
/// <remarks>
/// The name is returned as two enum values so the app can look both words up in its resource files
/// (NFR-08). <see cref="ToEnglish"/> gives the default wording.
/// </remarks>
public readonly record struct ColourName(ColourTone Tone, ColourFamily Family)
{
    public bool IsNeutral => Family <= ColourFamily.Black;

    public static ColourName Of(Rgb8 colour)
    {
        Oklab lab = Oklab.FromRgb8(colour);
        float chroma = lab.Chroma;
        float hue = lab.HueDegrees;

        if (chroma < 0.03f)
        {
            // A light neutral with a warm tint reads as off-white, not as grey.
            bool warm = chroma >= 0.012f && hue is >= 40f and <= 120f;
            ColourFamily neutral = lab.L switch
            {
                >= 0.99f => ColourFamily.White,
                >= 0.88f => ColourFamily.LightGrey,
                >= 0.75f => warm ? ColourFamily.OffWhite : ColourFamily.LightGrey,
                >= 0.55f => ColourFamily.Grey,
                >= 0.35f => ColourFamily.DarkGrey,
                >= 0.20f => ColourFamily.NearBlack,
                >= 0.08f => ColourFamily.Charcoal,
                _ => ColourFamily.Black,
            };
            return new ColourName(ColourTone.Plain, neutral);
        }

        ColourFamily family = hue switch
        {
            >= 345f or < 40f => lab.L > 0.72f && chroma < 0.12f ? ColourFamily.Pink : ColourFamily.Red,
            < 80f => ColourFamily.Orange,
            < 115f => ColourFamily.Yellow,
            < 170f => ColourFamily.Green,
            < 215f => ColourFamily.Teal,
            < 285f => ColourFamily.Blue,
            < 325f => ColourFamily.Purple,
            _ => ColourFamily.Pink,
        };

        ColourTone tone =
            lab.L < 0.45f ? ColourTone.Dark
            : lab.L > 0.70f && chroma < 0.075f ? ColourTone.Pale
            : chroma < 0.11f ? ColourTone.Soft
            : ColourTone.Plain;
        return new ColourName(tone, family);
    }

    public string ToEnglish()
    {
        string family = Family switch
        {
            ColourFamily.OffWhite => "off-white",
            ColourFamily.LightGrey => "light grey",
            ColourFamily.DarkGrey => "dark grey",
            ColourFamily.NearBlack => "near black",
            _ => Family.ToString().ToLowerInvariant(),
        };
        string text = Tone == ColourTone.Plain ? family : Tone.ToString().ToLowerInvariant() + " " + family;
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    public override string ToString() => ToEnglish();
}
