// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Core.Analysis;
using ThemeLens.Core.Colour;
using ThemeLens.Core.Imaging;
using ThemeLens.Core.Mapping;

namespace ThemeLens.Engine;

/// <summary>A monotonic clock. Tests use a fake one; the app uses the system's high-resolution counter.</summary>
public interface IClock
{
    TimeSpan Now { get; }
}

public sealed class SystemClock : IClock
{
    private readonly System.Diagnostics.Stopwatch _stopwatch = System.Diagnostics.Stopwatch.StartNew();

    public TimeSpan Now => _stopwatch.Elapsed;
}

/// <summary>A rectangle in physical screen pixels.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Contains(int x, int y) => x >= X && y >= Y && x < Right && y < Bottom;

    public long IntersectionArea(PixelRect other)
    {
        long w = Math.Min(Right, other.Right) - Math.Max(X, other.X);
        long h = Math.Min(Bottom, other.Bottom) - Math.Max(Y, other.Y);
        return w <= 0 || h <= 0 ? 0 : w * h;
    }
}

/// <summary>
/// One captured picture of what is under the lens. The engine never looks inside: on Windows this is a GPU
/// texture, in tests an image in memory. Screen content stays in these objects and goes nowhere else (NFR-02).
/// </summary>
public interface IFrame : IDisposable
{
    /// <summary>When the screen content of this frame was produced, on the engine's clock.</summary>
    TimeSpan CapturedAt { get; }
}

/// <summary>A frame held in ordinary memory: the test pattern, and every frame in the tests.</summary>
public sealed class ImageFrame(ImageRgb8 image, TimeSpan capturedAt) : IFrame
{
    public ImageRgb8 Image { get; } = image;

    public TimeSpan CapturedAt { get; } = capturedAt;

    public void Dispose()
    {
    }
}

public enum FramePoll
{
    /// <summary>Nothing on screen changed since the last poll. This is normal for a static screen.</summary>
    NoChange,

    /// <summary>A new frame is available.</summary>
    NewFrame,

    /// <summary>The source can no longer deliver frames it trusts.</summary>
    Fault,
}

/// <summary>Thrown by <see cref="IFrameSource.Start"/> when the screen cannot be captured at all (FR-20).</summary>
public sealed class CaptureUnavailableException : Exception
{
    public CaptureUnavailableException(string message)
        : base(message)
    {
    }

    public CaptureUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Screen capture (section 7.3). Two implementations sit behind this on Windows, chosen at run time.</summary>
public interface IFrameSource : IDisposable
{
    /// <summary>Starts capturing the screen that contains <paramref name="area"/>.</summary>
    /// <exception cref="CaptureUnavailableException">Capture is blocked by policy or not supported.</exception>
    void Start(PixelRect area);

    void Stop();

    /// <summary>
    /// Returns the newest frame, if there is one. The caller owns the frame and disposes it.
    /// <paramref name="fault"/> says why when the result is <see cref="FramePoll.Fault"/>.
    /// </summary>
    FramePoll Poll(out IFrame? frame, out string? fault);
}

/// <summary>The lens window and its renderer, as the engine sees them.</summary>
public interface ILensSurface
{
    /// <summary>Time between two refreshes of the display the lens is on.</summary>
    TimeSpan RefreshInterval { get; }

    /// <summary>True when rendering runs on the CPU (no usable GPU).</summary>
    bool IsSoftwareRendered { get; }

    /// <summary>Fills the whole lens with one colour and shows it. Must be cheap and must not depend on capture.</summary>
    void ShowFill(Rgb8 colour);

    /// <summary>Recolours a frame with the given look and shows it. A slow frame is still a complete frame.</summary>
    void ShowFrame(IFrame frame, LensTables look);

    /// <summary>Takes the lens body off the screen, fading over <paramref name="fade"/> (zero for at once).</summary>
    void Hide(TimeSpan fade);

    /// <summary>
    /// The analysis picture of the frame shown most recently, or null when it is not ready yet.
    /// Reading it must not block the frame loop.
    /// </summary>
    Thumbnail? TryReadThumbnail();

    /// <summary>
    /// Shows a plain fill from any thread, without waiting for the frame loop. The watchdog calls this when the
    /// loop has stalled, so it must work even while <see cref="ShowFrame"/> is stuck.
    /// </summary>
    void ShowEmergencyFill(Rgb8 colour);
}
