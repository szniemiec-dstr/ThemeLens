// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Engine;

namespace ThemeLens.Platform.Windows;

/// <summary>Where the lens gets its picture of the screen from.</summary>
public enum FrameSourceKind
{
    /// <summary>Capture the screen: Windows.Graphics.Capture where it can run without a border, else Desktop Duplication.</summary>
    Auto,

    /// <summary>Capture nothing; show the animated test pattern (<c>--source pattern</c>).</summary>
    Pattern,
}

/// <summary>Chooses the frame source at run time (section 7.3).</summary>
public static class FrameSources
{
    /// <summary>
    /// Creates the source for <paramref name="kind"/>. For <see cref="FrameSourceKind.Auto"/> the choice
    /// between the two capture paths is made when capture starts, and made again if the first one turns out
    /// not to work. When neither does, starting throws <see cref="CaptureUnavailableException"/> with a text
    /// for the user that says why (FR-20).
    /// </summary>
    public static IFrameSource Create(FrameSourceKind kind, D3D11LensRenderer renderer, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(clock);
        return kind == FrameSourceKind.Pattern
            ? new PatternFrameSource(renderer, clock)
            : new AutoFrameSource(new WgcFrameSource(renderer, clock), new DuplicationFrameSource(renderer, clock));
    }

    /// <summary>Asks a source to call <paramref name="frameArrived"/> when a new frame is waiting, if it can tell.</summary>
    internal static void Notify(IFrameSource source, Action frameArrived)
    {
        switch (source)
        {
            case CaptureFrameSource capture:
                capture.FrameArrived = frameArrived;
                break;
            case AutoFrameSource auto:
                auto.SetFrameArrived(frameArrived);
                break;
        }
    }
}

/// <summary>
/// The primary capture path with the fallback behind it. Each start tries Windows.Graphics.Capture first, until
/// it has once said it cannot work on this Windows; from then on Desktop Duplication is used. A capture that
/// Windows refuses is not retried another way: the lens stays off and the user is told why.
/// </summary>
internal sealed class AutoFrameSource : IFrameSource
{
    private readonly Lock _gate = new();
    private readonly CaptureFrameSource _primary;
    private readonly CaptureFrameSource _fallback;
    private CaptureFrameSource? _active;
    private bool _primaryRuledOut;

    public AutoFrameSource(CaptureFrameSource primary, CaptureFrameSource fallback)
    {
        _primary = primary;
        _fallback = fallback;
    }

    public void SetFrameArrived(Action frameArrived)
    {
        _primary.FrameArrived = frameArrived;
        _fallback.FrameArrived = frameArrived;
    }

    public void Start(PixelRect area)
    {
        lock (_gate)
        {
            _active = null;
            if (!_primaryRuledOut)
            {
                try
                {
                    _primary.Start(area);
                    _active = _primary;
                    return;
                }
                catch (CaptureUnavailableException e) when (e.HResult == HResults.AccessDenied)
                {
                    // Windows or the organisation said no. That is an answer, not an obstacle: the other
                    // capture path is not there to get around it (FR-20).
                    throw;
                }
                catch (CaptureUnavailableException) when (_primary is WgcFrameSource { BorderRefused: true, AcceptBorder: false } bordered)
                {
                    // Windows will not let this app capture without its yellow border round the screen. The
                    // other way of capturing has no border, so it is preferred where it starts at once. Where
                    // it does not, a lens with a border round the screen is better than no lens.
                    if (StartsAtOnce(_fallback, area))
                    {
                        _primaryRuledOut = true;
                        _active = _fallback;
                        return;
                    }

                    bordered.AcceptBorder = true;
                    _primary.Start(area);
                    _active = _primary;
                    return;
                }
                catch (CaptureUnavailableException)
                {
                    // Not possible on this Windows, or it just does not work here.
                    _primaryRuledOut = true;
                }
            }

            _fallback.Start(area);
            _active = _fallback;
        }
    }

    private static bool StartsAtOnce(CaptureFrameSource source, PixelRect area)
    {
        try
        {
            source.Start(area);
            if (!source.IsWaitingToRetry)
            {
                return true;
            }

            source.Stop();
            return false;
        }
        catch (CaptureUnavailableException)
        {
            return false;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _active?.Stop();
            _active = null;
        }
    }

    public FramePoll Poll(out IFrame? frame, out string? fault)
    {
        CaptureFrameSource? active;
        lock (_gate)
        {
            active = _active;
        }

        if (active is null)
        {
            frame = null;
            fault = null;
            return FramePoll.NoChange;
        }

        return active.Poll(out frame, out fault);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _active = null;
            _primary.Dispose();
            _fallback.Dispose();
        }
    }
}
