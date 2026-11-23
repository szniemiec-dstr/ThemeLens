// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.CompilerServices;
using ThemeLens.Platform.Windows;

namespace ThemeLens.Platform.Tests;

/// <summary>Where these tests can run.</summary>
public static class Host
{
    public const string NeedsWindows = "Needs Windows: Direct3D and real windows.";

    public static bool IsWindows => OperatingSystem.IsWindows();
}

/// <summary>A fact that is skipped, not failed, anywhere but on Windows.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        Skip = Host.NeedsWindows;
        SkipUnless = nameof(Host.IsWindows);
        SkipType = typeof(Host);
    }
}

/// <summary>A theory that is skipped, not failed, anywhere but on Windows.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class WindowsTheoryAttribute : TheoryAttribute
{
    public WindowsTheoryAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        Skip = Host.NeedsWindows;
        SkipUnless = nameof(Host.IsWindows);
        SkipType = typeof(Host);
    }
}

/// <summary>
/// One renderer on WARP, the software rasteriser, for a whole test class: compiling the shaders takes a moment,
/// and CI machines have no GPU anyway. Rendering the lens here is what proves the CPU-only path gives the same
/// picture (section 9.4).
/// </summary>
public sealed class SoftwareRendererFixture : IDisposable
{
    private readonly Lazy<D3D11LensRenderer> _renderer = new(static () => new D3D11LensRenderer(forceSoftware: true));

    /// <summary>Created on first use, so that a skipped test never touches Direct3D.</summary>
    public D3D11LensRenderer Renderer => _renderer.Value;

    public void Dispose()
    {
        if (_renderer.IsValueCreated)
        {
            _renderer.Value.Dispose();
        }
    }
}
