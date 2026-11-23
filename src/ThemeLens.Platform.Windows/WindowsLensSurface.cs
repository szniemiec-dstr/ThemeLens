// SPDX-License-Identifier: GPL-3.0-or-later
using SharpGen.Runtime;
using ThemeLens.Core.Analysis;
using ThemeLens.Core.Colour;
using ThemeLens.Core.Imaging;
using ThemeLens.Core.Mapping;
using ThemeLens.Engine;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace ThemeLens.Platform.Windows;

/// <summary>
/// The lens as the engine sees it (<see cref="ILensSurface"/>): the lens window, a flip-model swap chain
/// attached to it through DirectComposition, and the renderer that fills it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Threads.</b> The engine calls <see cref="ShowFill"/>, <see cref="ShowFrame"/> and <see cref="Hide"/> from
/// the frame loop and from whichever thread issues a command; it never overlaps them. Drawing happens on the
/// calling thread. Everything that concerns the window itself is posted to the window's thread and never waited
/// for, so a busy window cannot stall a frame and a slow frame cannot freeze the window.
/// <see cref="ShowEmergencyFill"/> touches none of this: it takes no lock and uses a separate window on a
/// separate thread, so it works while the frame loop is stuck in here.
/// </para>
/// <para>
/// <b>Background first.</b> The swap chain is as large as the monitor, not as the lens, and whatever part of it
/// lies outside the current picture holds the page colour. When the lens grows, the newly uncovered part shows
/// that colour until the next frame arrives (section 4.5); it never shows through to the unthemed screen. The
/// picture is on the swap chain before the window is asked to appear, so the first thing ever visible is the
/// page colour (FR-12). If Direct3D cannot draw at all, the plain fill window takes over (FR-11).
/// </para>
/// </remarks>
public sealed class WindowsLensSurface : ILensSurface, IDisposable
{
    private const int BufferCount = 2;
    private const Format BackBufferFormat = Format.B8G8R8A8_UNorm;

    private readonly D3D11LensRenderer _renderer;
    private readonly WindowThread _windows;
    private readonly bool _ownsWindowThread;
    private readonly EmergencyFill _emergency;
    private readonly LensWindow _window;
    private readonly Lock _sync;

    private IDXGISwapChain1? _swapChain;
    private ID3D11Texture2D? _backBuffer;
    private ID3D11RenderTargetView? _backBufferView;
    private LensComposition? _composition;
    private int _capacityWidth;
    private int _capacityHeight;
    private PixelRect _bounds;
    private PixelRect _screen;
    private Rgb8 _background = Rgb8.Black;
    private int _marginsToClear = BufferCount;
    private ImageRgb8? _uploadedImage;
    private ID3D11Texture2D? _uploadedTexture;
    private bool _disposed;

    // Read without the lock, by the emergency fill and by the frame loop's pacing.
    private Placed _placed;
    private long _refreshTicks = TimeSpan.FromSeconds(1.0 / 60.0).Ticks;
    private long _presented;
    private int _shown;
    private int _emergencyShown;

    /// <summary>Creates the lens, hidden, with a window thread of its own.</summary>
    /// <param name="renderer">The renderer whose device draws the lens. Not disposed with the surface.</param>
    /// <param name="lens">Where the lens starts, in physical pixels.</param>
    /// <param name="screen">The full bounds of the monitor it is on.</param>
    /// <param name="devMode">True to keep the lens out of the topmost band (section 10.2).</param>
    public WindowsLensSurface(D3D11LensRenderer renderer, PixelRect lens, PixelRect screen, bool devMode = false)
        : this(renderer, new WindowThread("ThemeLens windows"), ownsWindowThread: true, lens, screen, devMode)
    {
    }

    internal WindowsLensSurface(
        D3D11LensRenderer renderer, WindowThread windows, bool ownsWindowThread, PixelRect lens, PixelRect screen, bool devMode)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderer = renderer;
        _sync = renderer.Sync;
        _windows = windows;
        _ownsWindowThread = ownsWindowThread;
        _bounds = lens;
        _screen = screen;
        _placed = new Placed(lens);

        LensWindow? window = null;
        _windows.Invoke(() => window = new LensWindow(devMode, lens));
        _window = window!;
        _window.ApplyOpacity = SetOpacity;
        _window.RaisedAgain = () => Raised?.Invoke();
        _emergency = new EmergencyFill(devMode);
        try
        {
            lock (_sync)
            {
                Volatile.Write(ref _refreshTicks, Monitors.RefreshIntervalFor(lens).Ticks);
                CreateSwapChain();
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>The lens window was raised to the top. Raised on the window thread; other windows that belong above the lens follow it there.</summary>
    internal event Action? Raised;

    public TimeSpan RefreshInterval => TimeSpan.FromTicks(Volatile.Read(ref _refreshTicks));

    public bool IsSoftwareRendered => _renderer.IsSoftwareRendered;

    /// <summary>How many pictures have been handed to the display so far. The frame-rate read-out counts these.</summary>
    public long PresentedFrames => Interlocked.Read(ref _presented);

    /// <summary>The handle of the lens window.</summary>
    public nint WindowHandle => _window.Handle;

    internal LensWindow Window => _window;

    public void ShowFill(Rgb8 colour)
    {
        bool drawn = false;
        lock (_sync)
        {
            _background = colour;
            _marginsToClear = BufferCount;
            try
            {
                if (CanDraw())
                {
                    _renderer.Fill(_backBufferView!, colour);
                    drawn = Present(syncInterval: 0);
                }
            }
            catch (SharpGenException e)
            {
                Failed("Filling the lens", e);
            }
        }

        Shown(drawn, colour);
    }

    public void ShowFrame(IFrame frame, LensTables look)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(look);
        bool drawn = false;
        Rgb8 background;
        lock (_sync)
        {
            // On the keep base there is no page colour; black is the safe thing to have beside the picture.
            FrameParameters parameters = look.Parameters;
            background = parameters.Direction != 0f ? parameters.PageColour : Rgb8.Black;
            if (background != _background)
            {
                _background = background;
                _marginsToClear = BufferCount;
            }

            try
            {
                if (CanDraw() && TryResolve(frame, out ID3D11Texture2D? texture, out PixelRect frameBounds))
                {
                    int width = Math.Min(_bounds.Width, _capacityWidth);
                    int height = Math.Min(_bounds.Height, _capacityHeight);
                    bool underLens = _bounds.IntersectionArea(frameBounds) > 0;
                    if (!underLens || (_marginsToClear > 0 && (width < _capacityWidth || height < _capacityHeight)))
                    {
                        _renderer.Fill(_backBufferView!, background);
                    }

                    if (_marginsToClear > 0)
                    {
                        _marginsToClear--;
                    }

                    if (underLens)
                    {
                        var region = new PixelRect(_bounds.X - frameBounds.X, _bounds.Y - frameBounds.Y, width, height);
                        _renderer.Render(texture, region, look, _backBufferView!);
                    }
                    else
                    {
                        // The frame is of another monitor (the lens has just moved): the background, not a smear.
                        _marginsToClear = BufferCount;
                    }

                    // Wait for the display only while the picture is on it; a hidden lens has no refresh to wait for.
                    drawn = Present(syncInterval: Volatile.Read(ref _shown) != 0 ? 1u : 0u);
                }
            }
            catch (SharpGenException e)
            {
                Failed("Drawing the lens", e);
            }
        }

        Shown(drawn, background);
    }

    public void Hide(TimeSpan fade)
    {
        Volatile.Write(ref _shown, 0);
        _windows.Post(() => _window.Hide(fade));
        HideEmergencyFill();
    }

    public Thumbnail? TryReadThumbnail() => _renderer.TryReadThumbnail();

    public void ShowEmergencyFill(Rgb8 colour)
    {
        // No lock here, on purpose: the frame loop may be stuck while holding it.
        PixelRect bounds = Volatile.Read(ref _placed).Bounds;
        Volatile.Write(ref _emergencyShown, 1);
        _emergency.Show(bounds, colour);
    }

    /// <summary>Moves or resizes the lens. The picture follows with the next frame; until then new parts show the page colour.</summary>
    /// <param name="lens">The rectangle of the lens, in physical pixels.</param>
    /// <param name="screen">The full bounds of the monitor the lens is on.</param>
    public void SetBounds(PixelRect lens, PixelRect screen)
    {
        if (lens.IsEmpty)
        {
            return;
        }

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            bool screenChanged = screen != _screen;
            _bounds = lens;
            _screen = screen;
            Volatile.Write(ref _placed, new Placed(lens));
            _marginsToClear = BufferCount;
            if (screenChanged)
            {
                Volatile.Write(ref _refreshTicks, Monitors.RefreshIntervalFor(lens).Ticks);
            }

            (int width, int height) = Capacity(lens, screen);
            if (_swapChain is not null && !_renderer.IsDeviceLost && (width != _capacityWidth || height != _capacityHeight))
            {
                try
                {
                    // Before the window can grow past the old buffers: new ones, already in the page colour.
                    ResizeSwapChain(width, height);
                    _renderer.Fill(_backBufferView!, _background);
                    Present(syncInterval: 0);
                }
                catch (SharpGenException e)
                {
                    Failed("Resizing the lens", e);
                }
            }
        }

        _windows.Post(() => _window.Move(lens));
        if (Volatile.Read(ref _emergencyShown) != 0)
        {
            _emergency.Show(lens, _background);
        }
    }

    /// <summary>Switches the lens window between locked (click-through) and edit mode.</summary>
    public void SetEditMode(bool edit) => _windows.Post(() => _window.SetEditMode(edit));

    /// <summary>True when the graphics device was lost, or an earlier attempt to replace it did not get all the way.</summary>
    public bool NeedsRecovery
    {
        get
        {
            lock (_sync)
            {
                return !_disposed && (_renderer.IsDeviceLost || _swapChain is null);
            }
        }
    }

    /// <summary>
    /// Replaces a lost graphics device and everything made from it. The lens shows nothing useful until the
    /// engine has put up the background again, so the caller follows this with
    /// <see cref="LensController.NotifyDisplayChanged"/>. Safe to call again if it throws.
    /// </summary>
    public void RecoverDevice()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            ReleaseGraphics();
            if (_renderer.IsDeviceLost)
            {
                _renderer.RecreateDevice();
            }

            CreateSwapChain();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ReleaseGraphics();
        }

        try
        {
            _windows.Invoke(_window.Destroy);
        }
        catch (ObjectDisposedException)
        {
        }

        _emergency.Dispose();
        if (_ownsWindowThread)
        {
            _windows.Dispose();
        }
    }

    private static (int Width, int Height) Capacity(PixelRect lens, PixelRect screen) =>
        (Math.Max(1, Math.Max(lens.Width, screen.Width)), Math.Max(1, Math.Max(lens.Height, screen.Height)));

    private bool CanDraw() => !_disposed && !_renderer.IsDeviceLost && _swapChain is not null && _backBufferView is not null;

    private bool Present(uint syncInterval)
    {
        Result result = _swapChain!.Present(syncInterval, PresentFlags.None);
        if (result.Failure)
        {
            _renderer.NoteFailure(result);
            PlatformLog.Warn($"Present failed with 0x{result.Code:X8}.");
            return false;
        }

        Interlocked.Increment(ref _presented);
        return true;
    }

    /// <summary>After drawing: let the window appear if it has not yet, or cover for a picture that could not be drawn.</summary>
    private void Shown(bool drawn, Rgb8 background)
    {
        if (!drawn)
        {
            // Direct3D cannot draw right now. The plain window puts the colour up instead (FR-11).
            ShowEmergencyFill(background);
            return;
        }

        if (Interlocked.Exchange(ref _shown, 1) == 0)
        {
            _windows.Post(() =>
            {
                if (Volatile.Read(ref _shown) != 0)
                {
                    _window.Show();
                    Raised?.Invoke();
                }
            });
        }

        HideEmergencyFill();
    }

    private void HideEmergencyFill()
    {
        if (Interlocked.Exchange(ref _emergencyShown, 0) != 0)
        {
            _emergency.Hide();
        }
    }

    private void Failed(string what, SharpGenException exception)
    {
        _renderer.NoteFailure(exception.ResultCode);
        PlatformLog.Warn(what + " failed", exception);
    }

    private void SetOpacity(float opacity)
    {
        lock (_sync)
        {
            try
            {
                _composition?.SetOpacity(opacity);
            }
            catch (SharpGenException e)
            {
                Failed("Fading the lens", e);
            }
        }
    }

    private bool TryResolve(IFrame frame, out ID3D11Texture2D texture, out PixelRect frameBounds)
    {
        switch (frame)
        {
            case GpuFrame gpu:
                texture = gpu.Texture;
                frameBounds = gpu.ScreenBounds;
                return texture.NativePointer != 0;

            case ImageFrame image:
                // A picture held in memory, as the tests and tools use: it is taken to lie exactly under the lens.
                if (!ReferenceEquals(image.Image, _uploadedImage) || _uploadedTexture is null)
                {
                    _uploadedTexture?.Dispose();
                    _uploadedTexture = _renderer.CreateFrameTexture(image.Image);
                    _uploadedImage = image.Image;
                }

                texture = _uploadedTexture;
                frameBounds = new PixelRect(_bounds.X, _bounds.Y, image.Image.Width, image.Image.Height);
                return true;

            default:
                texture = null!;
                frameBounds = default;
                return false;
        }
    }

    private void CreateSwapChain()
    {
        (int width, int height) = Capacity(_bounds, _screen);
        using IDXGIDevice dxgiDevice = _renderer.Device.QueryInterface<IDXGIDevice>();
        using IDXGIAdapter adapter = dxgiDevice.GetAdapter();
        using IDXGIFactory2 factory = adapter.GetParent<IDXGIFactory2>();
        var description = new SwapChainDescription1
        {
            Width = (uint)width,
            Height = (uint)height,
            Format = BackBufferFormat,
            Stereo = false,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = BufferCount,

            // A swap chain for composition must be flip-model and stretch-scaled. Sequential, not discard:
            // the part of each buffer outside the lens picture is cleared once and then has to stay as it is.
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipSequential,
            AlphaMode = AlphaMode.Ignore,
            Flags = SwapChainFlags.None,
        };

        _swapChain = factory.CreateSwapChainForComposition(_renderer.Device, description);
        _capacityWidth = width;
        _capacityHeight = height;
        AcquireBackBuffer();
        _marginsToClear = BufferCount;

        // Not shown yet, but never undefined: the first buffer starts in the background colour.
        _renderer.Fill(_backBufferView!, _background);
        _composition = new LensComposition(dxgiDevice, _window.Handle, _swapChain);
    }

    private void ResizeSwapChain(int width, int height)
    {
        _backBufferView?.Dispose();
        _backBuffer?.Dispose();
        _backBufferView = null;
        _backBuffer = null;
        _renderer.Context.ClearState();
        _renderer.Context.Flush();
        _swapChain!.ResizeBuffers(BufferCount, (uint)width, (uint)height, Format.Unknown, SwapChainFlags.None).CheckError();
        _capacityWidth = width;
        _capacityHeight = height;
        AcquireBackBuffer();
        _marginsToClear = BufferCount;
    }

    private void AcquireBackBuffer()
    {
        _backBuffer = _swapChain!.GetBuffer<ID3D11Texture2D>(0);
        _backBufferView = _renderer.Device.CreateRenderTargetView(_backBuffer);
    }

    private void ReleaseGraphics()
    {
        _uploadedTexture?.Dispose();
        _uploadedTexture = null;
        _uploadedImage = null;
        try
        {
            _composition?.Dispose();
        }
        catch (SharpGenException e)
        {
            PlatformLog.Warn("Releasing the composition tree failed", e);
        }

        _composition = null;
        _backBufferView?.Dispose();
        _backBuffer?.Dispose();
        _swapChain?.Dispose();
        _backBufferView = null;
        _backBuffer = null;
        _swapChain = null;
    }

    private sealed record Placed(PixelRect Bounds);
}
