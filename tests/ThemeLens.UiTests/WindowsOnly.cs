// SPDX-License-Identifier: GPL-3.0-or-later
namespace ThemeLens.UiTests;

/// <summary>Where these tests can run.</summary>
public static class Host
{
    public const string NeedsWindows = "Needs Windows: the app and a desktop to show it on.";

    public static bool IsWindows => OperatingSystem.IsWindows();
}

/// <summary>A fact that is skipped, not failed, anywhere but on Windows.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute(
        [System.Runtime.CompilerServices.CallerFilePath] string? sourceFilePath = null,
        [System.Runtime.CompilerServices.CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        Skip = Host.NeedsWindows;
        SkipUnless = nameof(Host.IsWindows);
        SkipType = typeof(Host);
    }
}
