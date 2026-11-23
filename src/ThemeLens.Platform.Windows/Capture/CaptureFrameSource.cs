// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Engine;

namespace ThemeLens.Platform.Windows;

/// <summary>
/// What the frame sources of this layer share: how a failure to start is treated.
/// </summary>
/// <remarks>
/// <para>
/// A source can fail to start for a reason that will not go away (capture is blocked by policy, or this
/// Windows cannot do it) or for one that will (the display is still settling after a mode change, the secure
/// desktop is up). The first kind is a <see cref="CaptureUnavailableException"/>: the engine turns the lens
/// off and says why (FR-20). The second kind must not turn the lens off. The source then reports itself
/// started, the lens holds the background, and after a short pause <see cref="Poll"/> returns
/// <see cref="FramePoll.Fault"/>, which makes the engine start it again (FR-19).
/// </para>
/// <para>
/// A source that keeps failing that way is given up on after a few seconds, so the lens can never sit on the
/// screen as a plain rectangle for good.
/// </para>
/// </remarks>
public abstract class CaptureFrameSource : IFrameSource
{
    /// <summary>How long to hold the background before trying to start again.</summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);

    private readonly int _maximumFailedStarts;
    private string? _pendingFault;
    private TimeSpan _retryAt;
    private int _failedStarts;
    private bool _disposed;

    /// <param name="clock">The engine's clock.</param>
    /// <param name="maximumFailedStarts">How many starts in a row may fail before the source gives up.</param>
    protected CaptureFrameSource(IClock clock, int maximumFailedStarts = 8)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Clock = clock;
        _maximumFailedStarts = maximumFailedStarts;
    }

    /// <summary>
    /// Called, on whatever thread the source learns it, when a new frame is waiting. The frame loop uses it to
    /// wake up at once instead of at its next tick. Sources that have no such signal never call it.
    /// </summary>
    public Action? FrameArrived { get; set; }

    protected IClock Clock { get; }

    /// <summary>Serialises <see cref="Start"/>, <see cref="Stop"/> and <see cref="Poll"/>.</summary>
    protected Lock Gate { get; } = new();

    public void Start(PixelRect area)
    {
        lock (Gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            StopCore();
            _pendingFault = null;
            try
            {
                StartCore(area);
            }
            catch (CaptureUnavailableException)
            {
                StopCore();
                _failedStarts = 0;
                throw;
            }
            catch (Exception e)
            {
                StopCore();
                PlatformLog.Warn($"{GetType().Name} could not start", e);
                if (CountsAgainstStarting(e) && ++_failedStarts > _maximumFailedStarts)
                {
                    _failedStarts = 0;
                    throw new CaptureUnavailableException(PlatformStrings.Format(PlatformStrings.CaptureFailed, e.Message), e);
                }

                _pendingFault = e.Message;
                _retryAt = Clock.Now + RetryDelay;
            }
        }
    }

    public void Stop()
    {
        lock (Gate)
        {
            _pendingFault = null;
            StopCore();
        }
    }

    public FramePoll Poll(out IFrame? frame, out string? fault)
    {
        frame = null;
        fault = null;
        lock (Gate)
        {
            if (_disposed)
            {
                return FramePoll.NoChange;
            }

            if (_pendingFault is not null)
            {
                if (Clock.Now < _retryAt)
                {
                    return FramePoll.NoChange;
                }

                fault = _pendingFault;
                return FramePoll.Fault;
            }

            FramePoll result;
            try
            {
                result = PollCore(out frame, out fault);
            }
            catch (Exception e)
            {
                PlatformLog.Warn($"{GetType().Name} failed while polling", e);
                frame?.Dispose();
                frame = null;
                fault = e.Message;
                result = FramePoll.Fault;
            }

            if (result == FramePoll.NewFrame)
            {
                _failedStarts = 0;
            }

            return result;
        }
    }

    public void Dispose()
    {
        lock (Gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            StopCore();
            DisposeCore();
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Starts capturing the screen that contains <paramref name="area"/>. Throw
    /// <see cref="CaptureUnavailableException"/> when capture cannot work here at all; any other exception is
    /// taken as a passing problem and the start is tried again.
    /// </summary>
    protected abstract void StartCore(PixelRect area);

    /// <summary>
    /// Whether a failed start uses up one of the attempts this source gets before it is given up. A failure
    /// that is known to end by itself, however long it lasts, does not.
    /// </summary>
    protected virtual bool CountsAgainstStarting(Exception failure) => true;

    /// <summary>Stops capturing and frees what <see cref="StartCore"/> made. Must be safe to call when not started.</summary>
    protected abstract void StopCore();

    protected abstract FramePoll PollCore(out IFrame? frame, out string? fault);

    protected virtual void DisposeCore()
    {
    }
}
