// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using SharpGen.Runtime;
using ThemeLens.Engine;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace ThemeLens.Platform.Windows;

/// <summary>
/// The fallback capture path (section 7.3): DXGI Desktop Duplication of the monitor under the lens. It is used
/// where Windows.Graphics.Capture cannot turn its border off, which is anything older than Windows 11.
/// </summary>
/// <remarks>
/// Duplication lends out the desktop picture for a moment and wants it back before it gives the next one, while
/// the engine keeps a frame for as long as it likes. So each new picture is copied on the GPU into a texture
/// from a small pool, and the frame wraps that copy. The pointer is not part of a duplicated picture, and
/// windows excluded from capture are left out of it just as they are from the primary path.
/// </remarks>
public sealed class DuplicationFrameSource : CaptureFrameSource
{
    private const int InvalidArgument = unchecked((int)0x80070057);

    private readonly D3D11LensRenderer _renderer;
    private readonly Lock _poolGate = new();
    private readonly Stack<ID3D11Texture2D> _free = new();
    private IDXGIOutputDuplication? _duplication;
    private PixelRect _bounds;
    private int _run;

    public DuplicationFrameSource(D3D11LensRenderer renderer, IClock clock)
        : base(clock, maximumFailedStarts: 120)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderer = renderer;
    }

    /// <summary>
    /// While the lock screen or a consent prompt is up, Windows refuses to duplicate the desktop. That ends when
    /// the user comes back, however long it takes, so it is waited out and not counted as a failure.
    /// </summary>
    protected override bool CountsAgainstStarting(Exception failure) =>
        failure is not SharpGenException { ResultCode.Code: HResults.AccessDenied or HResults.DxgiAccessLost };

    protected override void StartCore(PixelRect area)
    {
        ID3D11Device device = _renderer.Device;
        using IDXGIDevice dxgiDevice = device.QueryInterface<IDXGIDevice>();
        using IDXGIAdapter adapter = dxgiDevice.GetAdapter();

        IDXGIOutput? best = null;
        long bestOverlap = 0;
        PixelRect bestBounds = default;
        ModeRotation rotation = ModeRotation.Unspecified;
        for (uint index = 0; adapter.EnumOutputs(index, out IDXGIOutput? output).Success && output is not null; index++)
        {
            OutputDescription description = output.Description;
            var bounds = new PixelRect(
                description.DesktopCoordinates.Left,
                description.DesktopCoordinates.Top,
                description.DesktopCoordinates.Right - description.DesktopCoordinates.Left,
                description.DesktopCoordinates.Bottom - description.DesktopCoordinates.Top);
            long overlap = area.IntersectionArea(bounds);
            if (description.AttachedToDesktop && overlap > bestOverlap)
            {
                best?.Dispose();
                best = output;
                bestOverlap = overlap;
                bestBounds = bounds;
                rotation = description.Rotation;
            }
            else
            {
                output.Dispose();
            }
        }

        if (best is null)
        {
            // The monitor hangs off another graphics adapter, or this device has no outputs at all (software rendering).
            throw new CaptureUnavailableException(PlatformStrings.CaptureUnsupported);
        }

        using (best)
        {
            if (rotation is not (ModeRotation.Identity or ModeRotation.Unspecified))
            {
                // A duplicated picture comes unrotated; turning it is not implemented.
                throw new CaptureUnavailableException(PlatformStrings.CaptureRotated);
            }

            using IDXGIOutput1 output1 = best.QueryInterface<IDXGIOutput1>();
            try
            {
                _duplication = output1.DuplicateOutput(device);
            }
            catch (SharpGenException e) when (e.ResultCode.Code is HResults.DxgiNotCurrentlyAvailable or InvalidArgument)
            {
                throw new CaptureUnavailableException(PlatformStrings.CaptureUnsupported, e);
            }
        }

        _bounds = bestBounds;
        lock (_poolGate)
        {
            _run++;
        }
    }

    protected override void StopCore()
    {
        _duplication?.Dispose();
        _duplication = null;
        lock (_poolGate)
        {
            // Copies still out with the engine notice the new run number when they come back, and are let go then.
            _run++;
            while (_free.TryPop(out ID3D11Texture2D? texture))
            {
                texture.Dispose();
            }
        }
    }

    protected override FramePoll PollCore(out IFrame? frame, out string? fault)
    {
        frame = null;
        fault = null;
        if (_duplication is null)
        {
            return FramePoll.NoChange;
        }

        if (_renderer.IsDeviceLost)
        {
            fault = "The graphics device was lost.";
            return FramePoll.Fault;
        }

        Result result = _duplication.AcquireNextFrame(0, out OutduplFrameInfo info, out IDXGIResource? resource);
        if (result.Code == HResults.DxgiWaitTimeout)
        {
            resource?.Dispose();
            return FramePoll.NoChange;
        }

        if (result.Failure)
        {
            // Access lost (a mode change, the secure desktop) or the device went away: start again.
            resource?.Dispose();
            _renderer.NoteFailure(result);
            fault = result.ToString();
            return FramePoll.Fault;
        }

        try
        {
            if (resource is null || info.LastPresentTime == 0)
            {
                // Only the pointer moved; the picture is the one we already have.
                return FramePoll.NoChange;
            }

            using ID3D11Texture2D desktop = resource.QueryInterface<ID3D11Texture2D>();
            int run;
            ID3D11Texture2D copy;
            lock (_poolGate)
            {
                run = _run;
                copy = _free.TryPop(out ID3D11Texture2D? pooled) ? pooled : CreateCopyTarget(desktop.Description);
            }

            lock (_renderer.Sync)
            {
                _renderer.Context.CopyResource(copy, desktop);
            }

            TimeSpan age = Stopwatch.GetElapsedTime(info.LastPresentTime);
            if (age < TimeSpan.Zero || age > TimeSpan.FromSeconds(1))
            {
                age = TimeSpan.Zero;
            }

            frame = new GpuFrame(copy, _bounds, Clock.Now - age, () => Return(copy, run));
            return FramePoll.NewFrame;
        }
        finally
        {
            resource?.Dispose();
            _duplication.ReleaseFrame();
        }
    }

    private ID3D11Texture2D CreateCopyTarget(Texture2DDescription desktop) =>
        _renderer.Device.CreateTexture2D(new Texture2DDescription
        {
            Width = desktop.Width,
            Height = desktop.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = desktop.Format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None,
        });

    private void Return(ID3D11Texture2D texture, int run)
    {
        lock (_poolGate)
        {
            if (run == _run)
            {
                _free.Push(texture);
                return;
            }
        }

        texture.Dispose();
    }
}
