// SPDX-License-Identifier: GPL-3.0-or-later
using SharpGen.Runtime;
using Vortice.DirectComposition;
using Vortice.DXGI;

namespace ThemeLens.Platform.Windows;

/// <summary>
/// Puts a swap chain on a window that has no redirection bitmap (section 7.3): one DirectComposition visual
/// whose content is the swap chain, as the root of the window's visual tree. The visual's opacity is what
/// fades the lens out when it is turned off.
/// </summary>
/// <remarks>
/// The second-generation device (Windows 8.1 and later) is used where it is on offer, because only its visuals
/// have an opacity. If Windows hands out the first generation only, the lens still shows; it just disappears
/// without a fade.
/// </remarks>
internal sealed class LensComposition : IDisposable
{
    private readonly IDCompositionDesktopDevice? _desktopDevice;
    private readonly IDCompositionDevice? _firstDevice;
    private readonly IDCompositionTarget _target;
    private readonly IDCompositionVisual _visual;
    private readonly IDCompositionVisual3? _opacity;

    public LensComposition(IDXGIDevice dxgiDevice, nint window, IDXGISwapChain1 swapChain)
    {
        // The helper only hands out the first-version interface; the desktop device is the same object behind it.
        IDCompositionDevice first = DComp.DCompositionCreateDevice2<IDCompositionDevice>(dxgiDevice);
        IDCompositionDesktopDevice? desktop = first.QueryInterfaceOrNull<IDCompositionDesktopDevice>();
        IDCompositionTarget? target = null;
        IDCompositionVisual? visual = null;
        try
        {
            if (desktop is not null)
            {
                first.Dispose();
                _desktopDevice = desktop;
                desktop.CreateTargetForHwnd(window, true, out target).CheckError();
                visual = desktop.CreateVisual();
            }
            else
            {
                _firstDevice = first;
                first.CreateTargetForHwnd(window, true, out target).CheckError();
                visual = first.CreateVisual();
            }

            visual.SetContent(swapChain).CheckError();
            target.SetRoot(visual).CheckError();
            Commit().CheckError();
            _target = target;
            _visual = visual;
            _opacity = visual.QueryInterfaceOrNull<IDCompositionVisual3>();
        }
        catch
        {
            visual?.Dispose();
            target?.Dispose();
            _desktopDevice?.Dispose();
            _firstDevice?.Dispose();
            throw;
        }
    }

    /// <summary>True when the lens picture can be faded.</summary>
    public bool CanFade => _opacity is not null;

    /// <summary>Sets the opacity of the lens picture, 0 to 1, and shows it at once.</summary>
    public void SetOpacity(float opacity)
    {
        if (_opacity is null)
        {
            return;
        }

        _opacity.SetOpacity(Math.Clamp(opacity, 0f, 1f));
        Commit();
    }

    public void Dispose()
    {
        // Take the visual tree off the window first, so a new one can be attached to it later.
        _target.SetRoot(null!);
        Commit();
        _opacity?.Dispose();
        _visual.Dispose();
        _target.Dispose();
        _desktopDevice?.Dispose();
        _firstDevice?.Dispose();
    }

    private Result Commit() => _desktopDevice?.Commit() ?? _firstDevice!.Commit();
}
