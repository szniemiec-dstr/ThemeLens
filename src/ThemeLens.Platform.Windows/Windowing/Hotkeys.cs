// SPDX-License-Identifier: GPL-3.0-or-later
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace ThemeLens.Platform.Windows;

/// <summary>The actions that have a global shortcut (section 4.7).</summary>
public enum HotkeyAction
{
    /// <summary>Lens on or off.</summary>
    ToggleLens,

    /// <summary>Edit mode: move and resize.</summary>
    ToggleEditMode,

    /// <summary>Fill this screen, or restore.</summary>
    ToggleFillScreen,

    NextTheme,

    OpenControlWindow,

    /// <summary>Turn everything off now (FR-17).</summary>
    EverythingOff,
}

/// <summary>A shortcut: Win+Alt plus one letter key.</summary>
public sealed record HotkeyBinding(HotkeyAction Action, char Key);

/// <summary>
/// A shortcut after start-up. <see cref="IsRegistered"/> is false when Windows or another app already owns
/// the combination; the control window then shows it as "not set" for the user to choose another.
/// </summary>
public sealed record HotkeyRegistration(HotkeyAction Action, char Key, bool IsRegistered);

/// <summary>
/// Global shortcuts. The defaults use Win+Alt and never Ctrl+Alt: on Polish and many other European keyboards
/// Ctrl+Alt is AltGr, so Ctrl+Alt+L would block typing "ł".
/// </summary>
public static class Hotkeys
{
    /// <summary>The proposed defaults of section 4.7.</summary>
    public static IReadOnlyList<HotkeyBinding> Defaults { get; } =
    [
        new(HotkeyAction.ToggleLens, 'L'),
        new(HotkeyAction.ToggleEditMode, 'E'),
        new(HotkeyAction.ToggleFillScreen, 'F'),
        new(HotkeyAction.NextTheme, 'T'),
        new(HotkeyAction.OpenControlWindow, 'C'),
        new(HotkeyAction.EverythingOff, 'X'),
    ];

    /// <summary>
    /// Registers each shortcut against a window; its thread then receives WM_HOTKEY with the action as the id.
    /// Must run on that window's thread. Holding the keys down does not repeat the action.
    /// </summary>
    internal static IReadOnlyList<HotkeyRegistration> Register(HWND window, IEnumerable<HotkeyBinding> bindings)
    {
        const HOT_KEY_MODIFIERS modifiers =
            HOT_KEY_MODIFIERS.MOD_WIN | HOT_KEY_MODIFIERS.MOD_ALT | HOT_KEY_MODIFIERS.MOD_NOREPEAT;
        var results = new List<HotkeyRegistration>();
        foreach (HotkeyBinding binding in bindings)
        {
            char key = char.ToUpperInvariant(binding.Key);

            // For letters and digits the virtual-key code is the character itself.
            bool registered = char.IsAsciiLetterOrDigit(key)
                && PInvoke.RegisterHotKey(window, IdOf(binding.Action), modifiers, key);
            results.Add(new HotkeyRegistration(binding.Action, key, registered));
        }

        return results;
    }

    internal static void Unregister(HWND window, IEnumerable<HotkeyRegistration> registrations)
    {
        foreach (HotkeyRegistration registration in registrations)
        {
            if (registration.IsRegistered)
            {
                PInvoke.UnregisterHotKey(window, IdOf(registration.Action));
            }
        }
    }

    internal static int IdOf(HotkeyAction action) => (int)action + 1;

    internal static HotkeyAction? ActionOf(nuint id) =>
        id >= 1 && id <= (nuint)Enum.GetValues<HotkeyAction>().Length ? (HotkeyAction)((int)id - 1) : null;
}
