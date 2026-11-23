// SPDX-License-Identifier: GPL-3.0-or-later
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Interop;
using ThemeLens.App.ViewModels;
using ThemeLens.Platform.Windows;

namespace ThemeLens.App.Views;

/// <summary>The control window. Everything it shows comes from <see cref="MainViewModel"/>.</summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly LensRuntime _runtime;

    public MainWindow(MainViewModel viewModel, LensRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(runtime);
        _viewModel = viewModel;
        _runtime = runtime;
        InitializeComponent();

        // The window grows with what it shows, up to the height of the screen's work area; beyond that it scrolls.
        MaxHeight = SystemParameters.WorkArea.Height;
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelChanged;
        viewModel.ShowWindowRequested += BringToFront;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        nint handle = new WindowInteropHelper(this).Handle;

        // Out of capture, so the lens never recolours this window, and above the lens whenever the lens comes forward.
        _runtime.AttachControlWindow(handle);
        DesktopIntegration.UseDarkTitleBar(handle);
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelChanged;
        _viewModel.ShowWindowRequested -= BringToFront;
        base.OnClosed(e);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.StatusKind))
        {
            // A change of state ("Live", "Holding background") is announced to screen readers (section 4.8);
            // the delay ticking from 9 to 10 ms is not.
            UIElementAutomationPeer.FromElement(StatusLine)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    /// <summary>Shows the window and brings it to the front, from the notification-area icon.</summary>
    public void ShowFromTray() => BringToFront();

    private void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Show();
        Activate();
    }
}
