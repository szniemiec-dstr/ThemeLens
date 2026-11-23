// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ThemeLens.Engine;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace ThemeLens.Platform.Windows;

/// <summary>
/// A plain Win32 window with its messages delivered to a virtual method. A window belongs to the thread that
/// created it: every member except <see cref="Handle"/> is for that thread only.
/// </summary>
internal abstract unsafe class NativeWindow
{
    private static readonly ConcurrentDictionary<nint, NativeWindow> Live = new();
    private static readonly Lock ClassGate = new();
    private static readonly HashSet<string> Classes = [];

    [ThreadStatic]
    private static NativeWindow? t_beingCreated;

    private nint _handle;

    /// <summary>The window handle, or zero before creation and after destruction. Safe to read from any thread.</summary>
/// <summary>
    /// Where the pointer was, in screen pixels, when the message now being handled was created. A drag is
    /// worked out from these points and not from where the pointer is by now: when the thread is behind (a slow
    /// frame holds it up), the pointer has moved on, and a drag read from its present place jumps, or carries
    /// on after the button was let go.
    /// </summary>
    protected static System.Drawing.Point MessagePoint()
    {
        uint packed = PInvoke.GetMessagePos();
        return new System.Drawing.Point((short)(packed & 0xFFFF), (short)((packed >> 16) & 0xFFFF));
    }

        public nint Handle => Volatile.Read(ref _handle);

    internal HWND Hwnd => new(Handle);

    protected static HINSTANCE Instance => PInvoke.GetModuleHandle(default(PCWSTR));

    /// <summary>Destroys the window. Call on the thread that created it.</summary>
    public void Destroy()
    {
        HWND hwnd = Hwnd;
        if (!hwnd.IsNull)
        {
            PInvoke.DestroyWindow(hwnd);
        }
    }

    protected static COLORREF ToColorRef(Core.Colour.Rgb8 colour) =>
        new((uint)(colour.R | (colour.G << 8) | (colour.B << 16)));

    protected static int LowWord(nint value) => unchecked((short)(value & 0xFFFF));

    protected static int HighWord(nint value) => unchecked((short)((value >> 16) & 0xFFFF));

    protected void Create(
        string className,
        WNDCLASS_STYLES classStyle,
        string title,
        WINDOW_STYLE style,
        WINDOW_EX_STYLE extendedStyle,
        PixelRect bounds,
        HWND parent = default)
    {
        RegisterClass(className, classStyle);
        t_beingCreated = this;
        try
        {
            HWND hwnd = PInvoke.CreateWindowEx(
                extendedStyle, className, title, style, bounds.X, bounds.Y, bounds.Width, bounds.Height, parent, default, Instance, null);
            if (hwnd.IsNull)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Could not create the '{className}' window.");
            }

            Attach(hwnd);
        }
        finally
        {
            t_beingCreated = null;
        }
    }

    /// <summary>Handles a message. Return false to let Windows handle it.</summary>
    protected abstract bool OnMessage(uint message, WPARAM wParam, LPARAM lParam, out LRESULT result);

    private static void RegisterClass(string className, WNDCLASS_STYLES style)
    {
        lock (ClassGate)
        {
            if (!Classes.Add(className))
            {
                return;
            }

            fixed (char* name = className)
            {
                var windowClass = new WNDCLASSEXW
                {
                    cbSize = (uint)sizeof(WNDCLASSEXW),
                    style = style,
                    lpfnWndProc = &WindowProcedure,
                    hInstance = Instance,
                    hCursor = PInvoke.LoadCursor(default, PInvoke.IDC_ARROW),
                    lpszClassName = name,
                };

                if (PInvoke.RegisterClassEx(in windowClass) == 0)
                {
                    int error = Marshal.GetLastPInvokeError();
                    Classes.Remove(className);
                    throw new Win32Exception(error, $"Could not register the window class '{className}'.");
                }
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT WindowProcedure(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            nint key = (nint)hwnd.Value;
            if (!Live.TryGetValue(key, out NativeWindow? window))
            {
                // Messages sent while CreateWindowEx is still running arrive before it has returned the handle.
                window = t_beingCreated;
                window?.Attach(hwnd);
            }

            if (window is not null)
            {
                bool handled = window.OnMessage(message, wParam, lParam, out LRESULT result);
                if (message == PInvoke.WM_NCDESTROY)
                {
                    Live.TryRemove(key, out _);
                    Volatile.Write(ref window._handle, 0);
                }

                if (handled)
                {
                    return result;
                }
            }
        }
        catch (Exception e)
        {
            // An exception must never travel back into Windows through a window procedure.
            PlatformLog.Warn("A window message handler failed", e);
        }

        return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void Attach(HWND hwnd)
    {
        nint key = (nint)hwnd.Value;
        Volatile.Write(ref _handle, key);
        Live[key] = this;
    }
}
