// SPDX-License-Identifier: GPL-3.0-or-later
using Vortice.DirectComposition;
using Vortice.DXGI;

namespace ThemeLens.Platform.Windows;

/// <summary>
/// Puts a swap chain on a window that has no redirection bitmap (section 7.3): one DirectComposition visual
/// whose content is the swap chain, as the root of the window's visual tree. The visual's opacity is what
/// fades the lens out when it is turned off.
/// </summary>
internal sealed class LensComposition : IDisposable
{
    private readonly IDCompositionDesktopDevice _device;
    private readonly IDCompositionTarget _target;
    private readonly IDCompositionVisual2 _visual;
    private readonly IDCompositionVisual3? _opacity;

    public LensComposition(IDXGIDevice dxgiDevice, nint window, IDXGISwapChain1 swapChain)
    {
        // The helper only hands out the first-version interface; the desktop device is the same object.
        using (IDCompositionDevice first = DComp.DCompositionCreateDevice2<IDCompositionDevice>(dxgiDevice))
        {
            _device = first.QueryInterface<IDCompositionDesktopDevice>();
        }

        try
        {
            _device.CreateTargetForHwnd(window, true, out _target).CheckError();
            _visual = _device.CreateVisual();
            _visual.SetContent(swapChain).CheckError();
            _target.SetRoot(_visual).CheckError();
            _device.Commit().CheckError();
            _opacity = _visual.QueryInterfaceOrNull<IDCompositionVisual3>();
        }
        catch
        {
            _visual?.Dispose();
            _target?.Dispose();
            _device.Dispose();
            throw;
        }
    }

    /// <summary>True when the lens picture can be faded. It can on every Windows this app supports.</summary>
    public bool CanFade => _opacity is not null;

    /// <summary>Sets the opacity of the lens picture, 0 to 1, and shows it at once.</summary>
    public void SetOpacity(float opacity)
    {
        if (_opacity is null)
        {
            return;
        }

        _opacity.SetOpacity(Math.Clamp(opacity, 0f, 1f));
        _device.Commit();
    }

    public void Dispose()
    {
        // Take the visual tree off the window first, so a new one can be attached to it later.
        _target.SetRoot(null!);
        _device.Commit();
        _opacity?.Dispose();
        _visual.Dispose();
        _target.Dispose();
        _device.Dispose();
    }
}
