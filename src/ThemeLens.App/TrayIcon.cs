// SPDX-License-Identifier: GPL-3.0-or-later
using System.ComponentModel;
using System.Windows;
using Microsoft.Win32;
using ThemeLens.App.ViewModels;
using Forms = System.Windows.Forms;

namespace ThemeLens.App;

/// <summary>
/// The icon in the notification area (section 4.6): it shows at a glance whether the lens is on, off or
/// holding the background, and its menu repeats the most common actions. The three pictures differ in shape,
/// not only in colour (see docs/icons.html).
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly MainViewModel _viewModel;
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _lens;
    private readonly Forms.ToolStripMenuItem _fill;
    private readonly Dictionary<string, System.Drawing.Icon> _pictures = [];
    private readonly string _taskbar;

    public TrayIcon(MainViewModel viewModel, Action showWindow, Action quit)
    {
        _viewModel = viewModel;
        _taskbar = TaskbarIsLight() ? "light" : "dark";

        _lens = new Forms.ToolStripMenuItem(Strings.LensSwitchName, null, (_, _) => _viewModel.IsLensOn = !_viewModel.IsLensOn);
        _fill = new Forms.ToolStripMenuItem(Strings.FillScreen, null, (_, _) => _viewModel.FillsScreen = !_viewModel.FillsScreen);
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_lens);
        menu.Items.Add(_fill);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem(Strings.ShortcutControlWindow, null, (_, _) => showWindow()));
        menu.Items.Add(new Forms.ToolStripMenuItem(Strings.TrayQuit, null, (_, _) => quit()));

        _icon = new Forms.NotifyIcon { ContextMenuStrip = menu };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                showWindow();
            }
        };
        _viewModel.PropertyChanged += OnViewModelChanged;
        Show();
        _icon.Visible = true;
    }

    public void Dispose()
    {
        _viewModel.PropertyChanged -= OnViewModelChanged;
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        foreach (System.Drawing.Icon picture in _pictures.Values)
        {
            picture.Dispose();
        }
    }

    /// <summary>Windows keeps the taskbar's light or dark setting here; without it the taskbar is dark.</summary>
    private static bool TaskbarIsLight()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            return false;
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsLensOn) or nameof(MainViewModel.StatusKind)
            or nameof(MainViewModel.StatusText) or nameof(MainViewModel.FillsScreen))
        {
            Show();
        }
    }

    private void Show()
    {
        string state = !_viewModel.IsLensOn ? "off"
            : _viewModel.StatusKind.StartsWith("HoldingBackground", StringComparison.Ordinal) ? "hold"
            : "on";
        _icon.Icon = Picture(state);

        // Windows cuts a tooltip off at 127 characters.
        string tip = Strings.Format(Strings.TrayTip, _viewModel.StatusText);
        _icon.Text = tip.Length > 120 ? tip[..120] : tip;
        _lens.Checked = _viewModel.IsLensOn;
        _fill.Checked = _viewModel.FillsScreen;
    }

    private System.Drawing.Icon Picture(string state)
    {
        if (!_pictures.TryGetValue(state, out System.Drawing.Icon? picture))
        {
            var address = new Uri($"Resources/tray-{state}-{_taskbar}.ico", UriKind.Relative);
            using System.IO.Stream stream = Application.GetResourceStream(address)?.Stream
                ?? throw new InvalidOperationException($"The icon '{address}' is missing from the app.");
            picture = new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
            _pictures[state] = picture;
        }

        return picture;
    }
}
