// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace ThemeLens.Platform.Windows;

/// <summary>
/// A thread with a message loop that owns windows. Other threads hand it work with <see cref="Post"/>, which
/// never waits, so the frame loop cannot be held up by a window and a window cannot be held up by the frame loop.
/// </summary>
internal sealed class WindowThread : IDisposable
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _started = new();
    private readonly ConcurrentQueue<Action> _work = new();
    private DispatchWindow? _dispatch;
    private Exception? _startFailure;
    private volatile bool _stopped;

    public WindowThread(string name)
    {
        _thread = new Thread(Run) { Name = name, IsBackground = true };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _started.Wait();
        if (_startFailure is not null)
        {
            _stopped = true;
            ExceptionDispatchInfo.Capture(_startFailure).Throw();
        }
    }

    public bool IsCurrent => Thread.CurrentThread == _thread;

    /// <summary>Queues work for the window thread and returns at once.</summary>
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_stopped)
        {
            return;
        }

        _work.Enqueue(action);
        nint target = _dispatch?.Handle ?? 0;
        if (target != 0)
        {
            PInvoke.PostMessage(new HWND(target), DispatchWindow.RunMessage, default, default);
        }
    }

    /// <summary>
    /// Runs work on the window thread and waits for it. Never call this while holding a lock the window thread
    /// may want.
    /// </summary>
    public void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (IsCurrent)
        {
            action();
            return;
        }

        ObjectDisposedException.ThrowIf(_stopped, this);
        using var done = new ManualResetEventSlim();
        Exception? failure = null;
        Post(() =>
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                failure = e;
            }
            finally
            {
                done.Set();
            }
        });
        done.Wait();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    public void Dispose()
    {
        if (_stopped)
        {
            return;
        }

        Post(static () => PInvoke.PostQuitMessage(0));
        _stopped = true;
        if (!IsCurrent)
        {
            _thread.Join(TimeSpan.FromSeconds(2));
        }

        _started.Dispose();
    }

    private void Run()
    {
        try
        {
            // Windows made on this thread work in physical pixels whatever the process manifest says.
            PInvoke.SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
            _dispatch = new DispatchWindow(this);
        }
        catch (Exception e)
        {
            _startFailure = e;
            _started.Set();
            return;
        }

        _started.Set();
        Drain();
        while (true)
        {
            BOOL got = PInvoke.GetMessage(out MSG message, default, 0, 0);
            if (got.Value <= 0)
            {
                break;
            }

            PInvoke.TranslateMessage(in message);
            PInvoke.DispatchMessage(in message);
        }

        _dispatch.Destroy();
    }

    private void Drain()
    {
        while (_work.TryDequeue(out Action? action))
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                PlatformLog.Warn("Work posted to a window thread failed", e);
            }
        }
    }

    /// <summary>A message-only window: posting to it wakes the loop even while a modal loop is running.</summary>
    private sealed class DispatchWindow : NativeWindow
    {
        public const uint RunMessage = PInvoke.WM_APP + 1;

        private readonly WindowThread _owner;

        public DispatchWindow(WindowThread owner)
        {
            _owner = owner;
            Create("ThemeLens.Dispatch", 0, string.Empty, 0, 0, default, HWND.HWND_MESSAGE);
        }

        protected override bool OnMessage(uint message, WPARAM wParam, LPARAM lParam, out LRESULT result)
        {
            result = default;
            if (message != RunMessage)
            {
                return false;
            }

            _owner.Drain();
            return true;
        }
    }
}
