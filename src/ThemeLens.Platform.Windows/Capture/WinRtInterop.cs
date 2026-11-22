// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.WinRT;
using Windows.Win32.System.WinRT.Graphics.Capture;
using WinRT;
using DxgiDevice = Vortice.DXGI.IDXGIDevice;
using NativeDxgiDevice = Windows.Win32.Graphics.Dxgi.IDXGIDevice;
using NativeDxgiInterfaceAccess = Windows.Win32.System.WinRT.Direct3D11.IDirect3DDxgiInterfaceAccess;
using NativeInspectable = Windows.Win32.System.WinRT.IInspectable;

namespace ThemeLens.Platform.Windows;

/// <summary>
/// The three bridges between Direct3D 11 and Windows.Graphics.Capture that have no managed projection:
/// a capture item for a monitor, a WinRT device around a DXGI device, and the texture behind a WinRT surface.
/// The COM declarations come from CsWin32; the objects cross over as raw interface pointers.
/// </summary>
internal static unsafe class WinRtInterop
{
    private const string CaptureItemClass = "Windows.Graphics.Capture.GraphicsCaptureItem";

    /// <summary>The interface id of IGraphicsCaptureItem, the only one CreateForMonitor accepts.</summary>
    private static readonly Guid CaptureItemInterface = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    /// <summary>A capture item for a whole monitor, without the picker (IGraphicsCaptureItemInterop::CreateForMonitor).</summary>
    public static GraphicsCaptureItem CreateItemForMonitor(HMONITOR monitor)
    {
        PInvoke.WindowsCreateString(CaptureItemClass, (uint)CaptureItemClass.Length, out HSTRING classId).ThrowOnFailure();
        try
        {
            Guid interopInterface = IGraphicsCaptureItemInterop.IID_Guid;
            void* factory = null;
            PInvoke.RoGetActivationFactory(classId, &interopInterface, &factory).ThrowOnFailure();
            var interop = (IGraphicsCaptureItemInterop*)factory;
            try
            {
                Guid itemInterface = CaptureItemInterface;
                void* item = null;
                interop->CreateForMonitor(monitor, &itemInterface, &item);
                try
                {
                    return GraphicsCaptureItem.FromAbi((nint)item);
                }
                finally
                {
                    Marshal.Release((nint)item);
                }
            }
            finally
            {
                interop->Release();
            }
        }
        finally
        {
            PInvoke.WindowsDeleteString(classId);
        }
    }

    /// <summary>The WinRT face of a Direct3D device, which the capture frame pool wants (CreateDirect3D11DeviceFromDXGIDevice).</summary>
    public static IDirect3DDevice CreateDirect3DDevice(ID3D11Device device)
    {
        using DxgiDevice dxgiDevice = device.QueryInterface<DxgiDevice>();
        NativeInspectable* inspectable = null;
        PInvoke.CreateDirect3D11DeviceFromDXGIDevice((NativeDxgiDevice*)dxgiDevice.NativePointer, &inspectable).ThrowOnFailure();
        try
        {
            return MarshalInspectable<IDirect3DDevice>.FromAbi((nint)inspectable);
        }
        finally
        {
            inspectable->Release();
        }
    }

    /// <summary>The Direct3D texture behind a captured surface (IDirect3DDxgiInterfaceAccess::GetInterface). The caller disposes it.</summary>
    public static ID3D11Texture2D GetTexture(IDirect3DSurface surface)
    {
        nint unknown = MarshalInterface<IDirect3DSurface>.FromManaged(surface);
        try
        {
            Guid accessInterface = NativeDxgiInterfaceAccess.IID_Guid;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in accessInterface, out nint accessPointer));
            var access = (NativeDxgiInterfaceAccess*)accessPointer;
            try
            {
                Guid textureInterface = typeof(ID3D11Texture2D).GUID;
                void* texture = null;
                access->GetInterface(&textureInterface, &texture);
                return new ID3D11Texture2D((nint)texture);
            }
            finally
            {
                access->Release();
            }
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }
}
