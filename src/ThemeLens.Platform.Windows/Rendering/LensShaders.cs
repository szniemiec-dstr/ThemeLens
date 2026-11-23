// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Concurrent;
using SharpGen.Runtime;
using Vortice.D3DCompiler;
using Vortice.Direct3D;

namespace ThemeLens.Platform.Windows;

/// <summary>The pixel shaders of <c>lens.hlsl</c>, one per pass. The entry point of each is "Ps" plus its name here.</summary>
internal enum LensPass
{
    Fine,
    Coarse,
    Wide,
    RecolourFinal,
    RecolourMapped,
    Shrink,
    Thick,
    BlurRows,
    Finish,
    Copy,
}

/// <summary>
/// Compiles <c>lens.hlsl</c>, which is embedded in this assembly, with the compiler that ships with Windows
/// (d3dcompiler_47.dll). Nothing is needed at build time, so the project builds on any OS.
/// </summary>
internal static class LensShaders
{
    public const string ResourceName = "ThemeLens.Shaders.lens.hlsl";
    public const string VertexEntryPoint = "VsFullscreen";

    private const string VertexProfile = "vs_5_0";
    private const string PixelProfile = "ps_5_0";

    // IEEE strictness keeps the arithmetic in the order it is written, which is the order the reference renderer uses.
    private const ShaderFlags Flags = ShaderFlags.OptimizationLevel2 | ShaderFlags.IeeeStrictness;

    public static string EntryPoint(LensPass pass) => "Ps" + pass;

    public static byte[] LoadSource()
    {
        using Stream stream = typeof(LensShaders).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The shader source '{ResourceName}' is missing from the assembly.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        byte[] bytes = memory.ToArray();

        // The compiler takes bytes, not text: drop a byte-order mark if an editor added one.
        return bytes is [0xEF, 0xBB, 0xBF, ..] ? bytes[3..] : bytes;
    }

    /// <summary>Compiles the vertex shader and every pixel shader. Entry points compile side by side; the compiler is thread-safe.</summary>
    public static (byte[] Vertex, IReadOnlyDictionary<LensPass, byte[]> Pixel) CompileAll()
    {
        byte[] source = LoadSource();
        byte[] vertex = [];
        var pixel = new ConcurrentDictionary<LensPass, byte[]>();
        var work = new List<Action> { () => vertex = Compile(source, VertexEntryPoint, VertexProfile) };
        foreach (LensPass pass in Enum.GetValues<LensPass>())
        {
            work.Add(() => pixel[pass] = Compile(source, EntryPoint(pass), PixelProfile));
        }

        try
        {
            Parallel.Invoke([.. work]);
        }
        catch (AggregateException e) when (e.InnerExceptions.Count > 0)
        {
            throw e.InnerExceptions[0];
        }

        return (vertex, pixel);
    }

    public static byte[] Compile(byte[] source, string entryPoint, string profile)
    {
        Result result = Compiler.Compile(
            source.AsSpan(), null!, null!, entryPoint, "lens.hlsl", profile, Flags, out Blob? code, out Blob? errors);
        try
        {
            if (result.Failure || code is null)
            {
                // The message holds compiler diagnostics about our own shader source, never screen content.
                string detail = errors?.AsString() ?? result.ToString();
                throw new InvalidOperationException($"lens.hlsl: '{entryPoint}' did not compile for {profile}. {detail}");
            }

            return code.AsBytes();
        }
        finally
        {
            code?.Dispose();
            errors?.Dispose();
        }
    }
}
