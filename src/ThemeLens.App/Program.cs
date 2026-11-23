// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Platform.Windows;

namespace ThemeLens.App;

/// <summary>
/// The entry point. It exists, instead of the one WPF would generate, to settle DPI awareness before the first
/// window: the manifest already asks for per-monitor v2, but a start through <c>dotnet ThemeLens.App.dll</c>
/// (the debugger does that) never reads the manifest.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] arguments)
    {
        DesktopIntegration.EnsurePerMonitorDpiAwareness();
        var app = new App(AppOptions.Parse(arguments));
        app.InitializeComponent();
        return app.Run();
    }
}
