// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.Win32;

namespace ThemeLens.Platform.Windows;

/// <summary>
/// "Start with Windows" (FR-18), per user: one value under the current user's Run key. No administrator rights
/// are needed and nothing outside the user's own registry hive is touched (NFR-05).
/// </summary>
public static class StartWithWindows
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ThemeLens";

    /// <summary>True when the app is set to start when this user signs in.</summary>
    public static bool IsEnabled
    {
        get
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is string command && command.Length > 0;
        }
    }

    /// <summary>The command Windows runs at sign-in, or null when start with Windows is off.</summary>
    public static string? Command
    {
        get
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) as string;
        }
    }

    /// <summary>Turns start with Windows on or off for the current user.</summary>
    /// <param name="enabled">True to start at sign-in.</param>
    /// <param name="executablePath">The program to start. Defaults to the running one.</param>
    /// <param name="arguments">Arguments to start it with, already quoted where needed.</param>
    public static void SetEnabled(bool enabled, string? executablePath = null, string? arguments = null)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        string path = executablePath ?? Environment.ProcessPath
            ?? throw new InvalidOperationException("The path of the running program is not known.");
        string command = string.IsNullOrWhiteSpace(arguments) ? $"\"{path}\"" : $"\"{path}\" {arguments}";
        key.SetValue(ValueName, command, RegistryValueKind.String);
    }
}
