// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Engine;
using Vortice.Direct3D11;

namespace ThemeLens.Platform.Windows;

/// <summary>
/// A captured picture of one monitor, as a texture on the GPU. This is as far as screen content ever travels:
/// from here it goes through the lens shaders to the lens window and nowhere else (NFR-02).
/// </summary>
public sealed class GpuFrame : IFrame
{
    private Action? _release;

    /// <param name="texture">The picture, B8G8R8A8. It stays valid until this frame is disposed.</param>
    /// <param name="screenBounds">The part of the desktop the texture shows, in physical pixels.</param>
    /// <param name="capturedAt">When the screen content was produced, on the engine's clock.</param>
    /// <param name="release">Called once when the frame is disposed, to give the texture back to its source.</param>
    public GpuFrame(ID3D11Texture2D texture, PixelRect screenBounds, TimeSpan capturedAt, Action? release = null)
    {
        ArgumentNullException.ThrowIfNull(texture);
        Texture = texture;
        ScreenBounds = screenBounds;
        CapturedAt = capturedAt;
        _release = release;
    }

    public ID3D11Texture2D Texture { get; }

    public PixelRect ScreenBounds { get; }

    public TimeSpan CapturedAt { get; }

    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}
