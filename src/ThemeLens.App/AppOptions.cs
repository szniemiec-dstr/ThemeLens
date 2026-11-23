// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Platform.Windows;

namespace ThemeLens.App;

/// <summary>What the command line asked for.</summary>
/// <param name="DevMode">
/// <c>--dev</c>: the lens is not topmost and covers at most a quarter of the screen, so stopping at a
/// breakpoint cannot leave a frozen rectangle over the editor (section 10.2).
/// </param>
/// <param name="Source"><c>--source pattern</c> shows the built-in test pattern instead of capturing the screen.</param>
/// <param name="Unknown">The first argument that was not understood, if any.</param>
public sealed record AppOptions(bool DevMode, FrameSourceKind Source, string? Unknown)
{
    public static AppOptions Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        bool devMode = false;
        FrameSourceKind source = FrameSourceKind.Auto;
        string? unknown = null;
        for (int i = 0; i < arguments.Count; i++)
        {
            string argument = arguments[i];
            if (string.Equals(argument, "--dev", StringComparison.OrdinalIgnoreCase))
            {
                devMode = true;
            }
            else if (string.Equals(argument, "--source", StringComparison.OrdinalIgnoreCase) && i + 1 < arguments.Count)
            {
                string value = arguments[++i];
                if (string.Equals(value, "pattern", StringComparison.OrdinalIgnoreCase))
                {
                    source = FrameSourceKind.Pattern;
                }
                else if (string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase))
                {
                    source = FrameSourceKind.Auto;
                }
                else
                {
                    unknown ??= argument + " " + value;
                }
            }
            else
            {
                unknown ??= argument;
            }
        }

        return new AppOptions(devMode, source, unknown);
    }
}
