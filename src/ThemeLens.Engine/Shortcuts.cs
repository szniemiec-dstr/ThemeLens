// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics.CodeAnalysis;

namespace ThemeLens.Engine;

/// <summary>The actions that have a global shortcut (section 4.7).</summary>
public enum ShortcutAction
{
    ToggleLens,
    EditMode,
    FillScreen,
    NextTheme,
    OpenControlWindow,
    EverythingOff,
}

[Flags]
public enum ShortcutModifiers
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>A key together with the modifiers held, such as Win+Alt+L.</summary>
public readonly record struct Shortcut(ShortcutModifiers Modifiers, string Key)
{
    /// <summary>
    /// Ctrl+Alt is the same as AltGr on Polish and many other European keyboards, so a Ctrl+Alt shortcut would
    /// block typing a letter (Ctrl+Alt+L is "ł"). No default may use it (FR-16), and the app warns about it.
    /// </summary>
    public bool UsesCtrlAlt => Modifiers.HasFlag(ShortcutModifiers.Ctrl) && Modifiers.HasFlag(ShortcutModifiers.Alt);

    /// <summary>A global shortcut needs at least one of Ctrl, Alt or Win, or it would swallow ordinary typing.</summary>
    public bool IsUsable => Key.Length > 0 && (Modifiers & (ShortcutModifiers.Ctrl | ShortcutModifiers.Alt | ShortcutModifiers.Win)) != 0;

    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(ShortcutModifiers.Win))
        {
            parts.Add("Win");
        }

        if (Modifiers.HasFlag(ShortcutModifiers.Ctrl))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(ShortcutModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(ShortcutModifiers.Shift))
        {
            parts.Add("Shift");
        }

        parts.Add(Key);
        return string.Join("+", parts);
    }

    /// <summary>Parses text such as "Win+Alt+L" or "ctrl + shift + F9". The key is a letter, a digit or F1 to F24.</summary>
    public static bool TryParse([NotNullWhen(true)] string? text, out Shortcut shortcut)
    {
        shortcut = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        ShortcutModifiers modifiers = ShortcutModifiers.None;
        string? key = null;
        foreach (string part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToUpperInvariant())
            {
                case "WIN" or "WINDOWS":
                    modifiers |= ShortcutModifiers.Win;
                    break;
                case "CTRL" or "CONTROL":
                    modifiers |= ShortcutModifiers.Ctrl;
                    break;
                case "ALT":
                    modifiers |= ShortcutModifiers.Alt;
                    break;
                case "SHIFT":
                    modifiers |= ShortcutModifiers.Shift;
                    break;
                default:
                    if (key is not null || !IsKey(part))
                    {
                        return false;
                    }

                    key = part.ToUpperInvariant();
                    break;
            }
        }

        if (key is null)
        {
            return false;
        }

        shortcut = new Shortcut(modifiers, key);
        return true;
    }

    private static bool IsKey(string part)
    {
        if (part.Length == 1)
        {
            return part[0] is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9');
        }

        return part.Length is 2 or 3
            && part[0] is 'F' or 'f'
            && int.TryParse(part.AsSpan(1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n)
            && n is >= 1 and <= 24;
    }
}

public static class Shortcuts
{
    private const ShortcutModifiers WinAlt = ShortcutModifiers.Win | ShortcutModifiers.Alt;

    /// <summary>The proposed defaults of section 4.7. All use Win+Alt.</summary>
    public static IReadOnlyDictionary<ShortcutAction, Shortcut> Defaults { get; } = new Dictionary<ShortcutAction, Shortcut>
    {
        [ShortcutAction.ToggleLens] = new(WinAlt, "L"),
        [ShortcutAction.EditMode] = new(WinAlt, "E"),
        [ShortcutAction.FillScreen] = new(WinAlt, "F"),
        [ShortcutAction.NextTheme] = new(WinAlt, "T"),
        [ShortcutAction.OpenControlWindow] = new(WinAlt, "C"),
        [ShortcutAction.EverythingOff] = new(WinAlt, "X"),
    };

    public static IReadOnlyList<ShortcutAction> Actions { get; } = Enum.GetValues<ShortcutAction>();

    /// <summary>
    /// The shortcut for each action after the user's changes: a missing entry uses the default, an empty entry
    /// means the user removed the shortcut, and text that cannot be read falls back to the default.
    /// </summary>
    public static IReadOnlyDictionary<ShortcutAction, Shortcut?> Resolve(IReadOnlyDictionary<string, string> userChoices)
    {
        ArgumentNullException.ThrowIfNull(userChoices);
        var result = new Dictionary<ShortcutAction, Shortcut?>();
        foreach (ShortcutAction action in Actions)
        {
            if (!userChoices.TryGetValue(action.ToString(), out string? text))
            {
                result[action] = Defaults[action];
            }
            else if (string.IsNullOrWhiteSpace(text))
            {
                result[action] = null;
            }
            else
            {
                result[action] = Shortcut.TryParse(text, out Shortcut parsed) && parsed.IsUsable ? parsed : Defaults[action];
            }
        }

        return result;
    }
}
