// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using ThemeLens.Engine;
using Vortice.Direct3D11;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Win32.Graphics.Gdi;

namespace ThemeLens.Platform.Windows;

/// <summary>
/// The primary capture path (section 7.3): Windows.Graphics.Capture on the monitor under the lens, with the
/// cursor and the yellow capture border turned off.
/// </summary>
/// <remarks>
/// <para>
/// The frame pool is free-threaded. Windows hands each new frame to <see cref="OnFrameArrived"/> on a thread
/// of its own; the handler takes the frame out of the pool at once and keeps only the newest. The pool
/// therefore always has a free buffer, however slowly the lens renders, so the last frame before the screen
/// goes still is never lost. <see cref="CaptureFrameSource.Poll"/> then just picks up what is waiting.
/// </para>
/// <para>
/// A frame wraps the pool's own texture; nothing is copied. Disposing the frame gives the buffer back.
/// </para>
/// </remarks>
public sealed class WgcFrameSource : CaptureFrameSource
{
    /// <summary>One frame with the engine, one waiting here, one free for Windows to fill.</summary>
    private const int BufferCount = 3;

    private const string SessionType = "Windows.Graphics.Capture.GraphicsCaptureSession";

    private readonly D3D11LensRenderer _renderer;
    private readonly Lock _frameGate = new();
    private IDirect3DDevice? _device;
    private int _deviceGeneration = -1;
    private object? _run;
    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private TypedEventHandler<Direct3D11CaptureFramePool, object>? _frameHandler;
    private TypedEventHandler<GraphicsCaptureItem, object>? _closedHandler;
    private SizeInt32 _size;
    private PixelRect _bounds;
    private GpuFrame? _latest;
    private string? _fault;

    public WgcFrameSource(D3D11LensRenderer renderer, IClock clock)
        : base(clock, maximumFailedStarts: 6)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderer = renderer;
    }

    /// <summary>True when this Windows has the capture API at all (Windows 10 version 2004 for capture without the cursor).</summary>
    public static bool IsAvailable
    {
        get
        {
            try
            {
                return OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) && GraphicsCaptureSession.IsSupported();
            }
            catch (Exception e)
            {
                PlatformLog.Warn("Could not ask whether screen capture is supported", e);
                return false;
            }
        }
    }

    /// <summary>
    /// True when the capture border can be turned off, which needs build 20348 or later: in practice
    /// Windows 11 (section 7.7). Without it the lens would be framed in yellow, so the fallback path is used.
    /// </summary>
    public static bool CanCaptureWithoutBorder
    {
        get
        {
            try
            {
                return OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348)
                    && ApiInformation.IsPropertyPresent(SessionType, "IsBorderRequired");
            }
            catch (Exception e)
            {
                PlatformLog.Warn("Could not ask whether the capture border can be turned off", e);
                return false;
            }
        }
    }

    protected override void StartCore(PixelRect area)
    {
        if (!IsAvailable || !CanCaptureWithoutBorder || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348))
        {
            throw new CaptureUnavailableException(PlatformStrings.CaptureUnsupported);
        }

        if (!Monitors.TryFind(area, out HMONITOR monitor, out PixelRect bounds))
        {
            throw new InvalidOperationException("There is no monitor under the lens right now.");
        }

        try
        {
            object run = new();
            GraphicsCaptureItem item = WinRtInterop.CreateItemForMonitor(monitor);
            _item = item;
            SizeInt32 size = item.Size;
            Direct3D11CaptureFramePool pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                DeviceForCapture(), DirectXPixelFormat.B8G8R8A8UIntNormalized, BufferCount, size);
            _pool = pool;
            GraphicsCaptureSession session = pool.CreateCaptureSession(item);
            _session = session;

            // The pointer is drawn by Windows on top of the lens; a second, recoloured one inside it would only confuse.
            session.IsCursorCaptureEnabled = false;
            if (!BorderlessAccessAllowed() && !AcceptBorder)
            {
                // Windows would draw its yellow border round the whole screen. The other way of capturing has
                // none; whoever owns both decides (AutoFrameSource).
                BorderRefused = true;
                throw new CaptureUnavailableException(PlatformStrings.CaptureUnsupported);
            }

            // Asked for in any case. Where it was refused, Windows ignores this and draws the border.
            session.IsBorderRequired = false;

            lock (_frameGate)
            {
                _run = run;
                _size = size;
                _bounds = bounds;
                _fault = null;
            }

            _frameHandler = (_, _) => OnFrameArrived(run);
            _closedHandler = (_, _) => OnItemClosed(run);
            pool.FrameArrived += _frameHandler;
            item.Closed += _closedHandler;
            session.StartCapture();
        }
        catch (Exception e) when (e is UnauthorizedAccessException || e.HResult == HResults.AccessDenied)
        {
            // Refused by Windows privacy settings or by policy (section 7.7): say so plainly.
            throw new CaptureUnavailableException(PlatformStrings.CaptureBlocked, e) { HResult = HResults.AccessDenied };
        }
    }

    /// <summary>True once a start was given up because Windows refused capture without its border.</summary>
    internal bool BorderRefused { get; private set; }

    /// <summary>Set to capture even where Windows insists on drawing its border round the screen.</summary>
    internal bool AcceptBorder { get; set; }

    protected override void StopCore()
    {
        GraphicsCaptureItem? item;
        Direct3D11CaptureFramePool? pool;
        GraphicsCaptureSession? session;
        GpuFrame? latest;
        lock (_frameGate)
        {
            _run = null;
            item = _item;
            pool = _pool;
            session = _session;
            latest = _latest;
            _item = null;
            _pool = null;
            _session = null;
            _latest = null;
            _fault = null;
        }

        // Outside the lock: closing the pool may wait for a handler that is waiting for the lock.
        try
        {
            if (pool is not null && _frameHandler is not null)
            {
                pool.FrameArrived -= _frameHandler;
            }

            if (item is not null && _closedHandler is not null)
            {
                item.Closed -= _closedHandler;
            }

            session?.Dispose();
            pool?.Dispose();
        }
        catch (Exception e)
        {
            PlatformLog.Warn("Stopping screen capture failed", e);
        }

        _frameHandler = null;
        _closedHandler = null;
        latest?.Dispose();
    }

    protected override FramePoll PollCore(out IFrame? frame, out string? fault)
    {
        frame = null;
        fault = null;
        lock (_frameGate)
        {
            if (_run is null)
            {
                return FramePoll.NoChange;
            }

            if (_fault is not null || _renderer.IsDeviceLost)
            {
                fault = _fault ?? "The graphics device was lost.";
                return FramePoll.Fault;
            }

            frame = _latest;
            _latest = null;
        }

        return frame is null ? FramePoll.NoChange : FramePoll.NewFrame;
    }

    protected override void DisposeCore() => _device = null;

    /// <summary>
    /// Asks Windows whether this app may capture without the border, and answers false only on a clear no.
    /// A desktop app gets its answer without a prompt. The wait is short because this can run on the thread
    /// that owns the windows; no answer in time counts as yes, as it did before the answer was looked at.
    /// </summary>
    private static bool BorderlessAccessAllowed()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348))
        {
            return true;
        }

        try
        {
            IAsyncOperation<global::Windows.Security.Authorization.AppCapabilityAccess.AppCapabilityAccessStatus> request =
                GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless);
            Task<global::Windows.Security.Authorization.AppCapabilityAccess.AppCapabilityAccessStatus> answer = request.AsTask();
            if (!answer.Wait(TimeSpan.FromMilliseconds(300)))
            {
                return true;
            }

            if (answer.Result == global::Windows.Security.Authorization.AppCapabilityAccess.AppCapabilityAccessStatus.Allowed)
            {
                return true;
            }

            PlatformLog.Warn($"Capture without a border is not allowed here ({answer.Result}); using the other capture path");
            return false;
        }
        catch (Exception e)
        {
            PlatformLog.Warn("Asking for capture without a border failed", e);
            return true;
        }
    }

    private IDirect3DDevice DeviceForCapture()
    {
        if (_device is null || _deviceGeneration != _renderer.DeviceGeneration)
        {
            _deviceGeneration = _renderer.DeviceGeneration;
            _device = WinRtInterop.CreateDirect3DDevice(_renderer.Device);
        }

        return _device;
    }

    private void OnFrameArrived(object run)
    {
        GpuFrame? replaced = null;
        lock (_frameGate)
        {
            if (!ReferenceEquals(run, _run) || _pool is null)
            {
                return;
            }

            Direct3D11CaptureFrame? captured = null;
            try
            {
                captured = _pool.TryGetNextFrame();
                if (captured is null)
                {
                    return;
                }

                SizeInt32 content = captured.ContentSize;
                if (content.Width != _size.Width || content.Height != _size.Height)
                {
                    // The resolution changed under us. Start again with buffers of the new size.
                    captured.Dispose();
                    _fault = "The size of the screen changed.";
                }
                else
                {
                    Direct3D11CaptureFrame held = captured;
                    ID3D11Texture2D texture = WinRtInterop.GetTexture(held.Surface);
                    replaced = _latest;
                    _latest = new GpuFrame(texture, _bounds, ToEngineTime(held.SystemRelativeTime), () =>
                    {
                        texture.Dispose();
                        held.Dispose();
                    });
                }
            }
            catch (Exception e)
            {
                captured?.Dispose();
                _fault = e.Message;
            }
        }

        replaced?.Dispose();
        FrameArrived?.Invoke();
    }

    private void OnItemClosed(object run)
    {
        lock (_frameGate)
        {
            if (!ReferenceEquals(run, _run))
            {
                return;
            }

            _fault = "The screen being captured went away.";
        }

        FrameArrived?.Invoke();
    }

    /// <summary>A frame's time is on the performance counter; the engine's clock started later. Carry the age across.</summary>
    private TimeSpan ToEngineTime(TimeSpan systemRelativeTime)
    {
        TimeSpan age = Stopwatch.GetElapsedTime(0) - systemRelativeTime;
        if (age < TimeSpan.Zero || age > TimeSpan.FromSeconds(1))
        {
            age = TimeSpan.Zero;
        }

        return Clock.Now - age;
    }
}
