// SPDX-License-Identifier: GPL-3.0-or-later
using System.Windows;
using ThemeLens.App.ViewModels;
using ThemeLens.App.Views;
using ThemeLens.Core.Themes;
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
    private MainViewModel? _viewModel;

    public App(AppOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // The lens starts with the default dark theme and switched off. First run, saved settings and the tray
        // icon come later; they plug in here.
        Theme theme = BuiltInThemes.ById(BuiltInThemes.DefaultDarkId);
        try
        {
            _runtime = new LensRuntime(new LensRuntimeOptions
            {
                DevMode = _options.DevMode,
                Source = _options.Source,
                InitialTheme = theme,
                HandleText = Strings.Format(_options.DevMode ? Strings.HandleTextDev : Strings.HandleText, theme.Name),
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

        _viewModel = new MainViewModel(_runtime, _options, Dispatcher);
        var window = new MainWindow(_viewModel, _runtime);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _viewModel?.Dispose();
        _runtime?.Dispose();
        base.OnExit(e);
    }
}
