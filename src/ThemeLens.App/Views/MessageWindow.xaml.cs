// SPDX-License-Identifier: GPL-3.0-or-later
using System.Windows;
using System.Windows.Interop;
using ThemeLens.Platform.Windows;

namespace ThemeLens.App.Views;

/// <summary>Shows one message and a Close button, dark like the rest of the app.</summary>
public partial class MessageWindow : Window
{
    public MessageWindow(string message)
    {
        InitializeComponent();
        MessageText.Text = message;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        DesktopIntegration.UseDarkTitleBar(new WindowInteropHelper(this).Handle);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
