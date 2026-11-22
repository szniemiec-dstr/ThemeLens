// SPDX-License-Identifier: GPL-3.0-or-later
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace ThemeLens.Platform.Windows;

/// <summary>An intermediate picture of the lens pipeline: a texture a pass can draw into and a later pass can read.</summary>
internal sealed class RenderTexture : IDisposable
{
    public RenderTexture(ID3D11Device device, int width, int height, Format format)
    {
        Width = width;
        Height = height;
        Texture = device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None,
        });
        Target = device.CreateRenderTargetView(Texture);
        Resource = device.CreateShaderResourceView(Texture);
    }

    public int Width { get; }

    public int Height { get; }

    public ID3D11Texture2D Texture { get; }

    public ID3D11RenderTargetView Target { get; }

    public ID3D11ShaderResourceView Resource { get; }

    public void Dispose()
    {
        Resource.Dispose();
        Target.Dispose();
        Texture.Dispose();
    }
}
