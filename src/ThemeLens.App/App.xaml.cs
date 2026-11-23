// SPDX-License-Identifier: GPL-3.0-or-later
using System.Windows;
using ThemeLens.App.ViewModels;
using ThemeLens.App.Views;
using ThemeLens.Core.Themes;
using ThemeLens.Engine;
using ThemeLens.Platform.Windows;

namespace ThemeLens.App;

/// <summary>
/// Wires everything together at start-up (figure 6): the lens runtime from the platform layer, the view model
/// on top of it, and the control window on top of that. Closing the control window ends the app, and ending
/// the app removes the lens.
/// </summary>
public partial class App : Application
{
    private readonly AppOptions _options;
    private LensRuntime? _runtime;
    private LensSession? _session;
    private MainViewModel? _viewModel;
    private TrayIcon? _tray;

    public App(AppOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _runtime = new LensRuntime(new LensRuntimeOptions
            {
                DevMode = _options.DevMode,
                Source = _options.Source,
            });
        }
        catch (Exception failure)
        {
            // Without Direct3D or a monitor there is no lens to control. Say so, in a dark window, and stop.
            var message = new MessageWindow(Strings.Format(Strings.StartFailed, failure.Message));
            MainWindow = message;
            message.Show();
            return;
        }

        // What the app remembers (FR-18): the theme with the user's own values for it, where the lens was on
        // this arrangement of monitors, and whether it was on. The session puts the theme on the lens; the lens
        // itself is put where it was here, because the runtime owns its place on screen.
        LensRuntime runtime = _runtime;
        _session = new LensSession(
            runtime.Controller,
            new ThemeCatalogue(AppPaths.ThemesDirectory),
            new SettingsStore(AppPaths.SettingsFile),
            () => runtime.Screens)
        {
            EditModeSwitch = runtime.SetEditMode,
            EverythingOffSwitch = runtime.EverythingOff,
        };
        runtime.SetPlacement(_session.Placement);
        runtime.Controller.SetArea(runtime.LensBounds);

        _viewModel = new MainViewModel(runtime, _session, _options, Dispatcher);
        var window = new MainWindow(_viewModel, runtime);
        MainWindow = window;
        window.Show();
        _tray = new TrayIcon(_viewModel, window.ShowFromTray, Shutdown);

        if (_session.Settings.LensWasOn)
        {
            // The lens was on when the app was last closed: it comes back by itself.
            _ = Task.Run(runtime.Controller.TurnOn);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _viewModel?.Dispose();
        _session?.Dispose();
        _runtime?.Dispose();
        base.OnExit(e);
    }
}
