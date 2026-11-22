// SPDX-License-Identifier: GPL-3.0-or-later
namespace ThemeLens.Core.Themes;

/// <summary>
/// The part a colour plays on screen (section 5.2). A theme defines an output colour per role; the engine
/// decides at run time which on-screen colour plays which role.
/// </summary>
public enum Role
{
    /// <summary>The largest background area.</summary>
    Page,

    /// <summary>Secondary backgrounds: toolbars, cards, table stripes.</summary>
    Panel,

    /// <summary>Main reading text.</summary>
    Text,

    /// <summary>Secondary text, borders, icons.</summary>
    QuietText,

    /// <summary>Interactive accent, usually blue in the source.</summary>
    Link,

    /// <summary>Status colour, usually green in the source.</summary>
    Good,

    /// <summary>Status colour, usually yellow or orange in the source.</summary>
    Warning,

    /// <summary>Status colour, usually red in the source.</summary>
    Bad,

    /// <summary>Any remaining strong colour; keeps its hue and takes the theme's brightness and saturation limits.</summary>
    OtherAccent,
}

public static class RoleExtensions
{
    public static bool IsNeutral(this Role role) => role is Role.Page or Role.Panel or Role.Text or Role.QuietText;

    /// <summary>True for the roles that pull a source colour onto a specific theme colour.</summary>
    public static bool IsThemedAccent(this Role role) => role is Role.Link or Role.Good or Role.Warning or Role.Bad;
}
