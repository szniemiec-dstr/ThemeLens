// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Engine;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Windows.Win32.Graphics.Gdi;

namespace ThemeLens.Platform.Windows;

/// <summary>
/// A frame source that captures nothing (<c>--source pattern</c>). It stands in for the screen with the
/// animated <see cref="TestPattern"/>, the size of the monitor the lens is on, so the lens, its shaders and the
/// analysis can be worked on without screen capture: on a machine where capture is blocked, in a virtual
/// machine, or under a debugger.
/// </summary>
public sealed class PatternFrameSource : CaptureFrameSource
{
    private static readonly TimeSpan FrameInterval = TimeSpan.FromSeconds(1.0 / 30.0);

    private readonly D3D11LensRenderer _renderer;
    private ID3D11Texture2D? _texture;
    private byte[] _pixels = [];
    private int _width;
    private int _height;
    private PixelRect _bounds;
    private TimeSpan _started;
    private TimeSpan _nextFrame;
    private bool _first;

    public PatternFrameSource(D3D11LensRenderer renderer, IClock clock)
        : base(clock)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderer = renderer;
    }

    protected override void StartCore(PixelRect area)
    {
        _bounds = Monitors.TryFind(area, out HMONITOR _, out PixelRect screen) ? screen : area;
        _width = Math.Max(1, _bounds.Width);
        _height = Math.Max(1, _bounds.Height);
        _pixels = new byte[_width * _height * 4];
        TestPattern.DrawBackground(_pixels, _width, _height);

        var description = new Texture2DDescription
        {
            Width = (uint)_width,
            Height = (uint)_height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None,
        };

        lock (_renderer.Sync)
        {
            unsafe
            {
                fixed (byte* data = _pixels)
                {
                    _texture = _renderer.Device.CreateTexture2D(in description, new SubresourceData(data, (uint)(_width * 4), 0));
                }
            }
        }

        _started = Clock.Now;
        _first = true;
    }

    protected override void StopCore()
    {
        // The engine may still hold a frame of this texture. Each frame has a reference of its own, so the
        // texture lives until the last of them is disposed.
        _texture?.Dispose();
        _texture = null;
        _pixels = [];
    }

    protected override FramePoll PollCore(out IFrame? frame, out string? fault)
    {
        frame = null;
        fault = null;
        if (_texture is null)
        {
            return FramePoll.NoChange;
        }

        if (_renderer.IsDeviceLost)
        {
            fault = "The graphics device was lost.";
            return FramePoll.Fault;
        }

        TimeSpan now = Clock.Now;
        if (!_first && now < _nextFrame)
        {
            return FramePoll.NoChange;
        }

        _nextFrame = now + FrameInterval;
        if (!_first)
        {
            // Only the strip in the middle moves: redraw and upload those rows alone.
            TestPattern.DrawMovingRows(_pixels, _width, _height, (now - _started).TotalSeconds);
            (int top, int bottom) = TestPattern.MovingRows(_height);
            if (bottom > top)
            {
                ReadOnlySpan<byte> rows = _pixels.AsSpan(top * _width * 4, (bottom - top) * _width * 4);
                lock (_renderer.Sync)
                {
                    _renderer.Context.UpdateSubresource(rows, _texture, 0, (uint)(_width * 4), 0, new Box(0, top, 0, _width, bottom, 1));
                }
            }
        }

        _first = false;
        _texture.AddRef();
        var reference = new ID3D11Texture2D(_texture.NativePointer);
        frame = new GpuFrame(reference, _bounds, now, reference.Dispose);
        return FramePoll.NewFrame;
    }
}
